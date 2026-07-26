using Tavi.Domain.Story;
using Tavi.Domain.Performance;
using Tavi.Utilities.Concurrency;

namespace Tavi.Application.Writing;

/// <summary>持有唯一活动手稿，并统一控制段落暂存、提交、历史、原子保存、自动保存和归档。</summary>
public sealed class WritingSession : IWritingWorkspace, IWritingSessionLifecycle, IBeatPublisher
{
    private readonly IManuscriptStore _store;
    private readonly TimeSpan? _autoSaveDelay;
    private readonly object _initializationSync = new();
    private readonly object _debounceSync = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeSource = new();
    private readonly VersionedWorkspace<WritingWorkspaceState, WritingOperationBatch, WritingHistoryEntry> _workspace;
    private readonly List<WritingStagedChange> _staged = [];
    private readonly List<Task> _autoSaveTasks = [];
    private CancellationTokenSource? _debounceSource;
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
        _workspace = new VersionedWorkspace<WritingWorkspaceState, WritingOperationBatch, WritingHistoryEntry>(new WritingConcurrencyModel());
        Queries = new WritingQueries(this);
        Commands = new WritingCommands(this);
    }

    /// <inheritdoc />
    public WritingQueries Queries { get; }

    /// <inheritdoc />
    public WritingCommands Commands { get; }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        lock (_initializationSync)
        {
            if (_initialized)
                throw new InvalidOperationException("WritingSession 已经初始化。");
            _initialized = true;
            _transitioning = true;
        }
        try
        {
            Manuscript? active = await _store.LoadActiveAsync(cancellationToken);
            _workspace.Initialize(new WritingWorkspaceState(active));
            _workspace.ExecuteExclusive(() =>
            {
                _savedStateId = active?.StateId ?? Guid.Empty;
                _transitioning = false;
                return true;
            });
        }
        catch
        {
            lock (_initializationSync)
            {
                _initialized = false;
                _transitioning = false;
            }
            throw;
        }
    }

    /// <inheritdoc />
    internal WritingSnapshot GetSnapshot()
    {
        ThrowIfDisposed();
        EnsureInitialized();
        return _workspace.ExecuteExclusive(CreateSnapshotLocked);
    }

    /// <inheritdoc />
    internal async Task<IReadOnlyList<ManuscriptSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        IReadOnlyList<Manuscript> archived = await _store.ListArchivedAsync(cancellationToken);
        Manuscript? active;
        EnsureInitialized();
        active = _workspace.ExecuteExclusive(() =>
        {
            Manuscript? current = CurrentLocked();
            return current is null ? null : CreateProjectionLocked();
        });
        return (active is null ? archived : archived.Append(active)).OrderByDescending(manuscript => manuscript.UpdatedAtUtc).Select(ToSummary).ToArray();
    }

    /// <inheritdoc />
    internal async Task<Manuscript> GetAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
    {
        if (manuscriptId == Guid.Empty)
            throw new ArgumentException("手稿标识不能为空。", nameof(manuscriptId));
        ThrowIfDisposed();
        EnsureInitialized();
        Manuscript? current = _workspace.ExecuteExclusive(() =>
        {
            Manuscript? value = CurrentLocked();
            return value?.Id == manuscriptId ? CreateProjectionLocked() : null;
        });
        if (current is not null)
            return current;
        return await _store.LoadArchivedAsync(manuscriptId, cancellationToken) ?? throw WritingException.NotFound(manuscriptId);
    }

    /// <inheritdoc />
    internal async Task<WritingSnapshot> CreateAsync(string title, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ThrowIfDisposed();
        EnsureInitialized();
        Manuscript created = _workspace.ExecuteExclusive(() =>
        {
            EnsureReadyLocked();
            Manuscript? current = CurrentLocked();
            if (current is not null)
                throw WritingException.ActiveExists(current.Id);
            _transitioning = true;
            Manuscript created = Manuscript.Create(title);
            _workspace.Reset(new WritingWorkspaceState(created));
            _savedStateId = Guid.Empty;
            return created;
        });
        try
        {
            await _store.SaveActiveAsync(created, cancellationToken);
            return _workspace.ExecuteExclusive(() =>
            {
                _savedStateId = created.StateId;
                _transitioning = false;
                return CreateSnapshotLocked();
            });
        }
        catch
        {
            _workspace.ExecuteExclusive(() =>
            {
                _workspace.Reset(new WritingWorkspaceState(null));
                _savedStateId = Guid.Empty;
                _transitioning = false;
                return true;
            });
            throw;
        }
    }

    /// <inheritdoc />
    internal Guid Stage(ManuscriptOperation operation, WritingChangeSource source = WritingChangeSource.Player)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!Enum.IsDefined(source))
            throw new ArgumentOutOfRangeException(nameof(source));
        ThrowIfDisposed();
        EnsureInitialized();
        return _workspace.ExecuteExclusive(() =>
        {
            EnsureEditableLocked("Stage");
            _ = ManuscriptEditor.Apply(CreateProjectionLocked(), [operation], true);
            var change = new WritingStagedChange { Id = Guid.NewGuid(), Source = source, Operation = operation };
            _staged.Add(change);
            _stagingRevision++;
            return change.Id;
        });
    }

    /// <inheritdoc />
    internal bool DeleteStaged(Guid changeId)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        return _workspace.ExecuteExclusive(() =>
        {
            EnsureEditableLocked("DeleteStaged");
            int index = _staged.FindIndex(change => change.Id == changeId);
            if (index < 0)
                return false;
            _staged.RemoveAt(index);
            _stagingRevision++;
            return true;
        });
    }

    /// <inheritdoc />
    internal WritingCommitResult CommitStaged(IEnumerable<Guid> changeIds, Guid expectedStateId)
    {
        ArgumentNullException.ThrowIfNull(changeIds);
        Guid[] selectedIds = changeIds.Distinct().ToArray();
        ThrowIfDisposed();
        EnsureInitialized();
        return _workspace.ExecuteExclusive(() =>
        {
            EnsureEditableLocked("CommitStaged");
            Manuscript current = RequireCurrentLocked("CommitStaged");
            if (selectedIds.Length == 0)
                throw WritingException.StagingInvalid("至少需要选择一项暂存修改。");
            WritingStagedChange[] selected = selectedIds.Select(id => _staged.FirstOrDefault(change => change.Id == id) ?? throw WritingException.StagingInvalid($"不存在暂存项 {id}。")).ToArray();
            Manuscript candidate = ManuscriptEditor.Apply(current, selected.Select(change => change.Operation));
            WritingStagedChange[] remaining = _staged.Where(change => !selectedIds.Contains(change.Id)).ToArray();
            _ = ManuscriptEditor.Apply(candidate, remaining.Select(change => change.Operation), true);
            WritingCommitResult result = CommitLocked(selected.Select(change => change.Operation), expectedStateId);
            _staged.RemoveAll(change => selectedIds.Contains(change.Id));
            _stagingRevision++;
            return result;
        });
    }

    /// <inheritdoc />
    internal WritingCommitResult Apply(ManuscriptOperation operation, Guid expectedStateId, WritingChangeSource source = WritingChangeSource.Player)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!Enum.IsDefined(source))
            throw new ArgumentOutOfRangeException(nameof(source));
        ThrowIfDisposed();
        EnsureInitialized();
        return _workspace.ExecuteExclusive(() =>
        {
            EnsureEditableLocked("Apply");
            return CommitLocked([operation], expectedStateId);
        });
    }

    /// <inheritdoc />
    BeatPublicationResult IBeatPublisher.PublishBeat(Guid performanceId, Guid beatId, IReadOnlyList<BeatParagraph> paragraphs, Guid expectedStateId)
        => PublishBeat(performanceId, beatId, paragraphs, expectedStateId);

    internal BeatPublicationResult PublishBeat(Guid performanceId, Guid beatId, IReadOnlyList<BeatParagraph> paragraphs, Guid expectedStateId)
    {
        if (performanceId == Guid.Empty || beatId == Guid.Empty)
            throw new ArgumentException("Performance 与 Beat 标识不能为空。");
        ArgumentNullException.ThrowIfNull(paragraphs);
        ThrowIfDisposed();
        EnsureInitialized();
        return _workspace.ExecuteExclusive(() =>
        {
            EnsureEditableLocked(nameof(PublishBeat));
            Manuscript current = RequireCurrentLocked(nameof(PublishBeat));
            ManuscriptBeatPublication? existing = current.BeatPublications.SingleOrDefault(value => value.PerformanceId == performanceId && value.BeatId == beatId);
            if (existing is not null)
            {
                Guid[] requestedIds = paragraphs.Select(value => value.Id).ToArray();
                if (!existing.ParagraphIds.SequenceEqual(requestedIds))
                    throw WritingException.StagingInvalid($"Beat {beatId} 已使用不同段落集合发布。");
                return new BeatPublicationResult(current.Id, current.StateId, true);
            }
            var operation = new PublishBeatOperation(performanceId, beatId, paragraphs.Select(value => new ManuscriptParagraph(value.Id, value.Text)).ToArray());
            Manuscript candidate = ManuscriptEditor.Apply(current, [operation]);
            _ = ManuscriptEditor.Apply(candidate, _staged.Select(value => value.Operation), true);
            WritingCommitResult committed = CommitLocked([operation], expectedStateId);
            Manuscript updated = RequireCurrentLocked(nameof(PublishBeat));
            // Beat 发布跨越 Performance 与 Writing；建立不可撤销检查点，避免单边 Undo 破坏已发布状态。
            _workspace.Reset(new WritingWorkspaceState(updated));
            return new BeatPublicationResult(updated.Id, committed.StateId, false);
        });
    }

    /// <inheritdoc />
    internal WritingCommitResult Undo(Guid expectedStateId)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        WritingCommitResult result = _workspace.ExecuteExclusive(() =>
        {
            EnsureEditableLocked("Undo");
            if (_staged.Count > 0)
                throw WritingException.StagingInvalid("存在尚未提交的修改，不能撤销已提交历史。");
            if (!_workspace.CanUndo)
                throw new InvalidOperationException("没有可撤销的手稿操作。");
            return ExecuteWorkspace(() => _workspace.Undo(expectedStateId));
        });
        if (result.Changed)
            ScheduleAutoSave();
        return result;
    }

    /// <inheritdoc />
    internal WritingCommitResult Redo(Guid expectedStateId)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        WritingCommitResult result = _workspace.ExecuteExclusive(() =>
        {
            EnsureEditableLocked("Redo");
            if (_staged.Count > 0)
                throw WritingException.StagingInvalid("存在尚未提交的修改，不能重做已提交历史。");
            if (!_workspace.CanRedo)
                throw new InvalidOperationException("没有可重做的手稿操作。");
            return ExecuteWorkspace(() => _workspace.Redo(expectedStateId));
        });
        if (result.Changed)
            ScheduleAutoSave();
        return result;
    }

    /// <inheritdoc />
    internal Task SaveAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        CancelPendingAutoSave();
        return SaveCoreAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await CancelAndDrainAutoSaveAsync();
        if (_initialized && _workspace.IsInitialized &&
            _workspace.ExecuteExclusive(() =>
            {
                Manuscript? current = CurrentLocked();
                return current is not null && current.StateId != _savedStateId;
            }))
        {
            await SaveCoreAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    internal async Task<Manuscript> ArchiveAsync(Guid expectedStateId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        CancelPendingAutoSave();
        EnsureInitialized();
        Manuscript archived = _workspace.ExecuteExclusive(() =>
        {
            EnsureEditableLocked("Archive");
            Manuscript current = RequireCurrentLocked("Archive");
            if (current.StateId != expectedStateId)
                throw WritingException.Conflict(expectedStateId, current.StateId);
            if (_staged.Count > 0)
            {
                _ = CommitLocked(_staged.Select(change => change.Operation), expectedStateId);
                _staged.Clear();
                _stagingRevision++;
                current = RequireCurrentLocked("Archive");
            }
            _transitioning = true;
            return ManuscriptEditor.Apply(current, [], false, ManuscriptStatus.Archived);
        });
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
            _workspace.ExecuteExclusive(() =>
            {
                _workspace.Reset(new WritingWorkspaceState(null));
                _savedStateId = Guid.Empty;
                _transitioning = false;
                return true;
            });
            return archived;
        }
        catch
        {
            _workspace.ExecuteExclusive(() =>
            {
                _transitioning = false;
                return true;
            });
            throw;
        }
    }

    /// <inheritdoc />
    internal async Task<Manuscript> RenameArchivedAsync(Guid manuscriptId, string title, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ThrowIfDisposed();
        Manuscript archived = await _store.LoadArchivedAsync(manuscriptId, cancellationToken) ?? throw WritingException.NotFound(manuscriptId);
        Manuscript renamed = new(archived.Id, Guid.NewGuid(), title, archived.Paragraphs, ManuscriptStatus.Archived, archived.CreatedAtUtc, DateTimeOffset.UtcNow, archived.BeatPublications);
        await _store.SaveArchivedAsync(renamed, cancellationToken);
        return renamed;
    }

    /// <inheritdoc />
    internal async Task DeleteArchivedAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
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
            if (_initialized && _workspace.IsInitialized && _workspace.Read(state => state.Current is not null))
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

    private WritingCommitResult CommitLocked(IEnumerable<ManuscriptOperation> operations, Guid expectedStateId)
    {
        WritingCommitResult result = ExecuteWorkspace(() => _workspace.Commit(new WritingOperationBatch(operations), expectedStateId));
        if (result.Changed)
            ScheduleAutoSave();
        return result;
    }

    private async Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        Manuscript snapshot;
        EnsureInitialized();
        snapshot = _workspace.ExecuteExclusive(() =>
        {
            EnsureEditableLocked("Save");
            return RequireCurrentLocked("Save");
        });
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            await _store.SaveActiveAsync(snapshot, cancellationToken);
            _workspace.ExecuteExclusive(() =>
            {
                _savedStateId = snapshot.StateId;
                LastAutoSaveException = null;
                return true;
            });
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
        Manuscript? current = CurrentLocked();
        Manuscript? projection = current is null ? null : CreateProjectionLocked();
        return new WritingSnapshot { Manuscript = projection, StagingRevision = _stagingRevision, IsDirty = current is not null && current.StateId != _savedStateId, CanUndo = _workspace.CanUndo && _staged.Count == 0, CanRedo = _workspace.CanRedo && _staged.Count == 0, StagedChanges = _staged.ToArray(), LastAutoSaveException = LastAutoSaveException };
    }

    private Manuscript CreateProjectionLocked() => _staged.Count == 0 ? RequireCurrentLocked("Read") : ManuscriptEditor.Apply(RequireCurrentLocked("Read"), _staged.Select(change => change.Operation), true);
    private static ManuscriptSummary ToSummary(Manuscript manuscript) => new(manuscript.Id, manuscript.Title, manuscript.Status, manuscript.Paragraphs.Count, CreatePreview(manuscript), manuscript.CreatedAtUtc, manuscript.UpdatedAtUtc);
    private static string CreatePreview(Manuscript manuscript)
    {
        string preview = string.Join(" ", manuscript.Paragraphs.Select(paragraph => paragraph.Text.Trim()).Where(text => text.Length > 0)).Trim();
        return preview.Length > 160 ? $"{preview[..160]}…" : preview;
    }
    private WritingCommitResult ExecuteWorkspace(Func<VersionedCommitResult<WritingHistoryEntry>> action)
    {
        try
        {
            VersionedCommitResult<WritingHistoryEntry> result = action();
            return new WritingCommitResult(result.CommitId, result.PreviousStateId, result.StateId, result.Changed);
        }
        catch (OptimisticConcurrencyConflictException exception)
        {
            throw WritingException.Conflict(exception.ExpectedStateId, exception.ActualStateId);
        }
    }

    private Manuscript? CurrentLocked() => _workspace.Read(state => state.Current);
    private Manuscript RequireCurrentLocked(string operation) => CurrentLocked() ?? throw WritingException.NoActive(operation);

    private void EnsureInitialized()
    {
        bool initialized;
        lock (_initializationSync)
            initialized = _initialized;
        if (!initialized || !_workspace.IsInitialized)
            throw new InvalidOperationException("WritingSession 尚未初始化。");
    }

    private void EnsureReadyLocked()
    {
        EnsureInitialized();
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

}
