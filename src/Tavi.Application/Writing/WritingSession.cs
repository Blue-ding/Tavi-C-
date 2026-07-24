using Tavi.Domain.Story;

namespace Tavi.Application.Writing;

/// <summary>持有唯一活动手稿，并统一控制段落暂存、提交、历史、原子保存、自动保存和归档。</summary>
public sealed class WritingSession : IWritingService
{
    private readonly IManuscriptStore _store;
    private readonly TimeSpan? _autoSaveDelay;
    private readonly object _sync = new();
    private readonly object _debounceSync = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeSource = new();
    private readonly List<WritingStagedChange> _staged = [];
    private readonly Stack<HistoryEntry> _undo = new();
    private readonly Stack<HistoryEntry> _redo = new();
    private readonly List<Task> _autoSaveTasks = [];
    private CancellationTokenSource? _debounceSource;
    private Manuscript? _current;
    private Guid _savedStateId;
    private long _stagingRevision;
    private bool _initialized;
    private bool _transitioning;
    private bool _disposed;

    /// <summary>创建使用指定持久化端口的 Writing 会话；自动保存延迟为空时禁用自动保存。</summary>
    public WritingSession(IManuscriptStore store, TimeSpan? autoSaveDelay = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        if (autoSaveDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(autoSaveDelay), "自动保存延迟不能为负数。");
        _autoSaveDelay = autoSaveDelay;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        lock (_sync)
        {
            if (_initialized)
                throw new InvalidOperationException("WritingSession 已经初始化。");
            _initialized = true;
            _transitioning = true;
        }
        try
        {
            Manuscript? active = await _store.LoadActiveAsync(cancellationToken);
            lock (_sync)
            {
                _current = active;
                _savedStateId = active?.StateId ?? Guid.Empty;
                _transitioning = false;
            }
        }
        catch
        {
            lock (_sync)
            {
                _initialized = false;
                _transitioning = false;
            }
            throw;
        }
    }

    /// <inheritdoc />
    public WritingSnapshot GetSnapshot()
    {
        ThrowIfDisposed();
        lock (_sync)
        {
            EnsureInitializedLocked();
            return CreateSnapshotLocked();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ManuscriptSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        IReadOnlyList<Manuscript> archived = await _store.ListArchivedAsync(cancellationToken);
        Manuscript? active;
        lock (_sync)
        {
            EnsureInitializedLocked();
            active = _current is null ? null : CreateProjectionLocked();
        }
        return (active is null ? archived : archived.Append(active)).OrderByDescending(manuscript => manuscript.UpdatedAtUtc).Select(ToSummary).ToArray();
    }

    /// <inheritdoc />
    public async Task<Manuscript> GetAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
    {
        if (manuscriptId == Guid.Empty)
            throw new ArgumentException("手稿标识不能为空。", nameof(manuscriptId));
        ThrowIfDisposed();
        lock (_sync)
        {
            EnsureInitializedLocked();
            if (_current?.Id == manuscriptId)
                return CreateProjectionLocked();
        }
        return await _store.LoadArchivedAsync(manuscriptId, cancellationToken) ?? throw WritingException.NotFound(manuscriptId);
    }

    /// <inheritdoc />
    public async Task<WritingSnapshot> CreateAsync(string title, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ThrowIfDisposed();
        Manuscript created;
        lock (_sync)
        {
            EnsureReadyLocked();
            if (_current is not null)
                throw WritingException.ActiveExists(_current.Id);
            _transitioning = true;
            created = Manuscript.Create(title);
            _current = created;
            _savedStateId = Guid.Empty;
        }
        try
        {
            await _store.SaveActiveAsync(created, cancellationToken);
            lock (_sync)
            {
                _savedStateId = created.StateId;
                _transitioning = false;
                return CreateSnapshotLocked();
            }
        }
        catch
        {
            lock (_sync)
            {
                _current = null;
                _savedStateId = Guid.Empty;
                _transitioning = false;
            }
            throw;
        }
    }

    /// <inheritdoc />
    public Guid Stage(ManuscriptOperation operation, WritingChangeSource source = WritingChangeSource.Player)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!Enum.IsDefined(source))
            throw new ArgumentOutOfRangeException(nameof(source));
        ThrowIfDisposed();
        lock (_sync)
        {
            EnsureEditableLocked("Stage");
            _ = ManuscriptEditor.Apply(CreateProjectionLocked(), [operation], true);
            var change = new WritingStagedChange { Id = Guid.NewGuid(), Source = source, Operation = operation };
            _staged.Add(change);
            _stagingRevision++;
            return change.Id;
        }
    }

    /// <inheritdoc />
    public bool DeleteStaged(Guid changeId)
    {
        ThrowIfDisposed();
        lock (_sync)
        {
            EnsureEditableLocked("DeleteStaged");
            int index = _staged.FindIndex(change => change.Id == changeId);
            if (index < 0)
                return false;
            _staged.RemoveAt(index);
            _stagingRevision++;
            return true;
        }
    }

    /// <inheritdoc />
    public WritingCommitResult CommitStaged(IEnumerable<Guid> changeIds, Guid expectedStateId)
    {
        ArgumentNullException.ThrowIfNull(changeIds);
        Guid[] selectedIds = changeIds.Distinct().ToArray();
        ThrowIfDisposed();
        lock (_sync)
        {
            EnsureEditableLocked("CommitStaged");
            Manuscript current = RequireCurrentLocked("CommitStaged");
            EnsureStateLocked(current, expectedStateId);
            if (selectedIds.Length == 0)
                throw WritingException.StagingInvalid("至少需要选择一项暂存修改。");
            WritingStagedChange[] selected = selectedIds.Select(id => _staged.FirstOrDefault(change => change.Id == id) ?? throw WritingException.StagingInvalid($"不存在暂存项 {id}。")).ToArray();
            Manuscript candidate = ManuscriptEditor.Apply(current, selected.Select(change => change.Operation));
            WritingStagedChange[] remaining = _staged.Where(change => !selectedIds.Contains(change.Id)).ToArray();
            _ = ManuscriptEditor.Apply(candidate, remaining.Select(change => change.Operation), true);
            WritingCommitResult result = CommitLocked(selected.Select(change => change.Operation));
            _staged.RemoveAll(change => selectedIds.Contains(change.Id));
            _stagingRevision++;
            return result;
        }
    }

    /// <inheritdoc />
    public WritingCommitResult Apply(ManuscriptOperation operation, Guid expectedStateId, WritingChangeSource source = WritingChangeSource.Player)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!Enum.IsDefined(source))
            throw new ArgumentOutOfRangeException(nameof(source));
        ThrowIfDisposed();
        lock (_sync)
        {
            EnsureEditableLocked("Apply");
            Manuscript current = RequireCurrentLocked("Apply");
            EnsureStateLocked(current, expectedStateId);
            return CommitLocked([operation]);
        }
    }

    /// <inheritdoc />
    public WritingCommitResult Undo(Guid expectedStateId)
    {
        ThrowIfDisposed();
        lock (_sync)
        {
            EnsureEditableLocked("Undo");
            Manuscript current = RequireCurrentLocked("Undo");
            EnsureStateLocked(current, expectedStateId);
            if (_staged.Count > 0)
                throw WritingException.StagingInvalid("存在尚未提交的修改，不能撤销已提交历史。");
            if (!_undo.TryPop(out HistoryEntry? entry))
                throw new InvalidOperationException("没有可撤销的手稿操作。");
            Manuscript restored = ManuscriptEditor.RebaseContent(entry.Before);
            _current = restored;
            _redo.Push(entry);
            ScheduleAutoSave();
            return new WritingCommitResult(Guid.NewGuid(), current.StateId, restored.StateId, true);
        }
    }

    /// <inheritdoc />
    public WritingCommitResult Redo(Guid expectedStateId)
    {
        ThrowIfDisposed();
        lock (_sync)
        {
            EnsureEditableLocked("Redo");
            Manuscript current = RequireCurrentLocked("Redo");
            EnsureStateLocked(current, expectedStateId);
            if (_staged.Count > 0)
                throw WritingException.StagingInvalid("存在尚未提交的修改，不能重做已提交历史。");
            if (!_redo.TryPop(out HistoryEntry? entry))
                throw new InvalidOperationException("没有可重做的手稿操作。");
            Manuscript restored = ManuscriptEditor.RebaseContent(entry.After);
            _current = restored;
            _undo.Push(entry);
            ScheduleAutoSave();
            return new WritingCommitResult(Guid.NewGuid(), current.StateId, restored.StateId, true);
        }
    }

    /// <inheritdoc />
    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        CancelPendingAutoSave();
        return SaveCoreAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Manuscript> ArchiveAsync(Guid expectedStateId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        CancelPendingAutoSave();
        Manuscript archived;
        lock (_sync)
        {
            EnsureEditableLocked("Archive");
            Manuscript current = RequireCurrentLocked("Archive");
            EnsureStateLocked(current, expectedStateId);
            if (_staged.Count > 0)
            {
                _ = CommitLocked(_staged.Select(change => change.Operation));
                _staged.Clear();
                _stagingRevision++;
                current = RequireCurrentLocked("Archive");
            }
            _transitioning = true;
            archived = ManuscriptEditor.Apply(current, [], false, ManuscriptStatus.Archived);
        }
        try
        {
            await _saveGate.WaitAsync(cancellationToken);
            try
            {
                await _store.ArchiveAsync(archived, cancellationToken);
            }
            finally
            {
                _saveGate.Release();
            }
            lock (_sync)
            {
                _current = null;
                _savedStateId = Guid.Empty;
                _undo.Clear();
                _redo.Clear();
                _transitioning = false;
            }
            return archived;
        }
        catch
        {
            lock (_sync)
                _transitioning = false;
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Manuscript> RenameArchivedAsync(Guid manuscriptId, string title, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ThrowIfDisposed();
        Manuscript archived = await _store.LoadArchivedAsync(manuscriptId, cancellationToken) ?? throw WritingException.NotFound(manuscriptId);
        Manuscript renamed = new(archived.Id, Guid.NewGuid(), title, archived.Paragraphs, ManuscriptStatus.Archived, archived.CreatedAtUtc, DateTimeOffset.UtcNow);
        await _store.SaveArchivedAsync(renamed, cancellationToken);
        return renamed;
    }

    /// <inheritdoc />
    public async Task DeleteArchivedAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!await _store.DeleteArchivedAsync(manuscriptId, cancellationToken))
            throw WritingException.NotFound(manuscriptId);
    }

    /// <summary>取消后台保存、刷新未保存活动手稿并释放同步资源。</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        await CancelAndDrainAutoSaveAsync();
        try
        {
            if (_initialized && _current is not null)
                await SaveCoreAsync(CancellationToken.None);
        }
        finally
        {
            _disposed = true;
            _lifetimeSource.Cancel();
            _lifetimeSource.Dispose();
            _saveGate.Dispose();
        }
    }

    /// <summary>获取最近一次后台自动保存异常；下一次保存成功后清空。</summary>
    public Exception? LastAutoSaveException { get; private set; }

    private WritingCommitResult CommitLocked(IEnumerable<ManuscriptOperation> operations)
    {
        Manuscript before = RequireCurrentLocked("Commit");
        Manuscript after = ManuscriptEditor.Apply(before, operations);
        if (ReferenceEquals(before, after))
            return new WritingCommitResult(Guid.Empty, before.StateId, before.StateId, false);
        _current = after;
        _undo.Push(new HistoryEntry(before, after));
        _redo.Clear();
        ScheduleAutoSave();
        return new WritingCommitResult(Guid.NewGuid(), before.StateId, after.StateId, true);
    }

    private async Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        Manuscript snapshot;
        lock (_sync)
        {
            EnsureEditableLocked("Save");
            snapshot = RequireCurrentLocked("Save");
        }
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            await _store.SaveActiveAsync(snapshot, cancellationToken);
            lock (_sync)
            {
                _savedStateId = snapshot.StateId;
                LastAutoSaveException = null;
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private void ScheduleAutoSave()
    {
        if (_autoSaveDelay is null || _disposed)
            return;
        CancellationTokenSource source;
        lock (_debounceSync)
        {
            _debounceSource?.Cancel();
            source = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeSource.Token);
            _debounceSource = source;
            Task task = RunAutoSaveAsync(source);
            _autoSaveTasks.Add(task);
        }
    }

    private async Task RunAutoSaveAsync(CancellationTokenSource source)
    {
        await Task.Yield();
        try
        {
            await Task.Delay(_autoSaveDelay!.Value, source.Token);
            await SaveCoreAsync(source.Token);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        catch (Exception exception)
        {
            LastAutoSaveException = exception;
        }
        finally
        {
            lock (_debounceSync)
            {
                if (ReferenceEquals(_debounceSource, source))
                    _debounceSource = null;
            }
            source.Dispose();
        }
    }

    private void CancelPendingAutoSave()
    {
        lock (_debounceSync)
        {
            _debounceSource?.Cancel();
            _debounceSource = null;
        }
    }

    private async Task CancelAndDrainAutoSaveAsync()
    {
        Task[] tasks;
        lock (_debounceSync)
        {
            _debounceSource?.Cancel();
            _debounceSource = null;
            tasks = _autoSaveTasks.ToArray();
            _autoSaveTasks.Clear();
        }
        if (tasks.Length > 0)
            await Task.WhenAll(tasks);
    }

    private WritingSnapshot CreateSnapshotLocked()
    {
        Manuscript? projection = _current is null ? null : CreateProjectionLocked();
        return new WritingSnapshot { Manuscript = projection, StagingRevision = _stagingRevision, IsDirty = _current is not null && _current.StateId != _savedStateId, CanUndo = _undo.Count > 0 && _staged.Count == 0, CanRedo = _redo.Count > 0 && _staged.Count == 0, StagedChanges = _staged.ToArray(), LastAutoSaveException = LastAutoSaveException };
    }

    private Manuscript CreateProjectionLocked() => _staged.Count == 0 ? RequireCurrentLocked("Read") : ManuscriptEditor.Apply(RequireCurrentLocked("Read"), _staged.Select(change => change.Operation), true);
    private static ManuscriptSummary ToSummary(Manuscript manuscript) => new(manuscript.Id, manuscript.Title, manuscript.Status, manuscript.Paragraphs.Count, CreatePreview(manuscript), manuscript.CreatedAtUtc, manuscript.UpdatedAtUtc);
    private static string CreatePreview(Manuscript manuscript)
    {
        string preview = string.Join(" ", manuscript.Paragraphs.Select(paragraph => paragraph.Text.Trim()).Where(text => text.Length > 0)).Trim();
        return preview.Length > 160 ? $"{preview[..160]}…" : preview;
    }
    private Manuscript RequireCurrentLocked(string operation) => _current ?? throw WritingException.NoActive(operation);
    private static void EnsureStateLocked(Manuscript current, Guid expectedStateId)
    {
        if (current.StateId != expectedStateId)
            throw WritingException.Conflict(expectedStateId, current.StateId);
    }
    private void EnsureInitializedLocked()
    {
        if (!_initialized)
            throw new InvalidOperationException("WritingSession 尚未初始化。");
    }
    private void EnsureReadyLocked()
    {
        EnsureInitializedLocked();
        if (_transitioning)
            throw new InvalidOperationException("WritingSession 正在执行状态转换。");
    }
    private void EnsureEditableLocked(string operation)
    {
        EnsureReadyLocked();
        Manuscript current = RequireCurrentLocked(operation);
        if (current.Status != ManuscriptStatus.Editing)
            throw WritingException.ArchivedImmutable(current.Id);
    }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    private sealed record HistoryEntry(Manuscript Before, Manuscript After);
}
