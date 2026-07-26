using System.Text.Json;
using Tavi.Application.Writing;
using Tavi.Domain.Story;

namespace Tavi.Infrastructure.Persistence;

/// <summary>使用独立 active 文件和逐篇 archive 文件持久化手稿，并以临时文件原子替换防止读取半写快照。</summary>
public sealed class JsonFileManuscriptStore : IManuscriptStore, IDisposable
{
    private const int CurrentVersion = 2;
    private readonly string _directory;
    private readonly string _archiveDirectory;
    private readonly JsonSerializerOptions _options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    /// <summary>创建使用指定专用目录的 JSON 手稿存储。</summary>
    public JsonFileManuscriptStore(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("手稿存储目录不能为空。", nameof(directory));
        _directory = Path.GetFullPath(directory);
        _archiveDirectory = Path.Combine(_directory, "archive");
    }

    /// <summary>获取手稿存储的绝对根目录。</summary>
    public string DirectoryPath => _directory;

    /// <inheritdoc />
    public async Task<Manuscript?> LoadActiveAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string path = ActivePath;
            if (!File.Exists(path))
                return null;
            Manuscript active = await ReadAsync(path, cancellationToken);
            string archivedPath = GetArchivedPath(active.Id);
            if (File.Exists(archivedPath))
            {
                TryDelete(path);
                return null;
            }
            if (active.Status != ManuscriptStatus.Editing)
                throw new InvalidDataException("active.json 中的手稿不是 Editing 状态。");
            return active;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not ManuscriptStoreException)
        {
            throw ReadFailure("LoadActive", "无法读取活动手稿。", null, exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Manuscript>> ListArchivedAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!Directory.Exists(_archiveDirectory))
                return [];
            var results = new List<Manuscript>();
            foreach (string path in Directory.EnumerateFiles(_archiveDirectory, "*.manuscript.json", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Manuscript manuscript = await ReadAsync(path, cancellationToken);
                if (manuscript.Status != ManuscriptStatus.Archived)
                    throw new InvalidDataException($"归档文件 {Path.GetFileName(path)} 不是 Archived 状态。");
                results.Add(manuscript);
            }
            return results.OrderByDescending(manuscript => manuscript.UpdatedAtUtc).ToArray();
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not ManuscriptStoreException)
        {
            throw ReadFailure("ListArchived", "无法读取手稿归档库。", null, exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Manuscript?> LoadArchivedAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
    {
        ValidateId(manuscriptId);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string path = GetArchivedPath(manuscriptId);
            return File.Exists(path) ? await ReadAsync(path, cancellationToken) : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not ManuscriptStoreException)
        {
            throw ReadFailure("LoadArchived", "无法读取归档手稿。", manuscriptId, exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveActiveAsync(Manuscript manuscript, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manuscript);
        if (manuscript.Status != ManuscriptStatus.Editing)
            throw new ArgumentException("活动存储只接受 Editing 手稿。", nameof(manuscript));
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(ActivePath))
            {
                Manuscript existing = await ReadAsync(ActivePath, cancellationToken);
                if (existing.Id != manuscript.Id)
                    throw new InvalidOperationException($"活动手稿 {existing.Id} 尚未归档，不能保存另一篇活动手稿。");
            }
            await WriteAtomicallyAsync(ActivePath, manuscript, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not ManuscriptStoreException)
        {
            throw WriteFailure("SaveActive", "无法保存活动手稿。", manuscript.Id, exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task ArchiveAsync(Manuscript manuscript, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manuscript);
        if (manuscript.Status != ManuscriptStatus.Archived)
            throw new ArgumentException("归档存储只接受 Archived 手稿。", nameof(manuscript));
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(ActivePath))
                throw new InvalidOperationException("活动手稿不存在，不能完成归档。");
            Manuscript active = await ReadAsync(ActivePath, cancellationToken);
            if (active.Id != manuscript.Id)
                throw new InvalidOperationException("归档手稿与当前活动手稿不匹配。");
            await WriteAtomicallyAsync(GetArchivedPath(manuscript.Id), manuscript, cancellationToken);
            File.Delete(ActivePath);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not ManuscriptStoreException)
        {
            throw WriteFailure("Archive", "无法归档活动手稿。", manuscript.Id, exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveArchivedAsync(Manuscript manuscript, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manuscript);
        if (manuscript.Status != ManuscriptStatus.Archived)
            throw new ArgumentException("归档存储只接受 Archived 手稿。", nameof(manuscript));
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string path = GetArchivedPath(manuscript.Id);
            if (!File.Exists(path))
                throw new FileNotFoundException("归档手稿不存在。", path);
            await WriteAtomicallyAsync(path, manuscript, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not ManuscriptStoreException)
        {
            throw WriteFailure("RenameArchived", "无法更新归档手稿。", manuscript.Id, exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteArchivedAsync(Guid manuscriptId, CancellationToken cancellationToken = default)
    {
        ValidateId(manuscriptId);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string path = GetArchivedPath(manuscriptId);
            if (!File.Exists(path))
                return false;
            File.Delete(path);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not ManuscriptStoreException)
        {
            throw WriteFailure("DeleteArchived", "无法删除归档手稿。", manuscriptId, exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>释放文件访问同步资源；存储本身不持有打开的手稿文件。</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _gate.Dispose();
    }

    private string ActivePath => Path.Combine(_directory, "active.json");
    private string GetArchivedPath(Guid id) => Path.Combine(_archiveDirectory, $"{id:N}.manuscript.json");

    private async Task<Manuscript> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        ManuscriptDocument document = await JsonSerializer.DeserializeAsync<ManuscriptDocument>(stream, _options, cancellationToken) ?? throw new InvalidDataException("手稿文件内容为空。");
        if (document.Version is < 1 or > CurrentVersion)
            throw new InvalidDataException($"不支持手稿版本 {document.Version}。");
        if (document.Paragraphs is null)
            throw new InvalidDataException("手稿缺少 paragraphs 数据。");
        return new Manuscript(
            document.Id,
            document.StateId,
            document.Title ?? "",
            document.Paragraphs.Select(paragraph => new ManuscriptParagraph(paragraph.Id, paragraph.Text ?? "")),
            document.Status,
            document.CreatedAtUtc,
            document.UpdatedAtUtc,
            (document.BeatPublications ?? []).Select(value => new ManuscriptBeatPublication(value.PerformanceId, value.BeatId, value.ParagraphIds ?? [])));
    }

    private async Task WriteAtomicallyAsync(string path, Manuscript manuscript, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tempPath = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var document = new ManuscriptDocument
            {
                Id = manuscript.Id,
                StateId = manuscript.StateId,
                Title = manuscript.Title,
                Status = manuscript.Status,
                CreatedAtUtc = manuscript.CreatedAtUtc,
                UpdatedAtUtc = manuscript.UpdatedAtUtc,
                Paragraphs = manuscript.Paragraphs.Select(paragraph => new ParagraphDocument { Id = paragraph.Id, Text = paragraph.Text }).ToArray(),
                BeatPublications = manuscript.BeatPublications.Select(value => new BeatPublicationDocument { PerformanceId = value.PerformanceId, BeatId = value.BeatId, ParagraphIds = value.ParagraphIds.ToArray() }).ToArray()
            };
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, _options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
            }
            if (File.Exists(path))
                File.Move(tempPath, path, true);
            else
                File.Move(tempPath, path);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static void ValidateId(Guid id)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("手稿标识不能为空。", nameof(id));
    }
    private static ManuscriptStoreException ReadFailure(string operation, string message, Guid? id, Exception inner) => new(StorageErrorCodes.ManuscriptReadFailed, operation, message, Details(id), inner);
    private static ManuscriptStoreException WriteFailure(string operation, string message, Guid? id, Exception inner) => new(StorageErrorCodes.ManuscriptWriteFailed, operation, message, Details(id), inner);
    private static IReadOnlyDictionary<string, string>? Details(Guid? id) => id is null ? null : new Dictionary<string, string> { ["ManuscriptId"] = id.Value.ToString() };
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { }
    }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class ManuscriptDocument
    {
        public int Version { get; init; } = CurrentVersion;
        public Guid Id { get; init; }
        public Guid StateId { get; init; }
        public string? Title { get; init; }
        public ManuscriptStatus Status { get; init; }
        public DateTimeOffset CreatedAtUtc { get; init; }
        public DateTimeOffset UpdatedAtUtc { get; init; }
        public ParagraphDocument[]? Paragraphs { get; init; }
        public BeatPublicationDocument[]? BeatPublications { get; init; }
    }

    private sealed class ParagraphDocument
    {
        public Guid Id { get; init; }
        public string? Text { get; init; }
    }

    private sealed class BeatPublicationDocument
    {
        public Guid PerformanceId { get; init; }
        public Guid BeatId { get; init; }
        public Guid[]? ParagraphIds { get; init; }
    }
}
