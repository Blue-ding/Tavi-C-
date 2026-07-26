using System.Text.Json;
using Tavi.Application.Performance;
using Tavi.Domain.Performance;

namespace Tavi.Infrastructure.Persistence;

/// <summary>使用一个活动文件和按 PerformanceId 归档的版本化 JSON Store。</summary>
public sealed class JsonFilePerformanceStore : IPerformanceStore, IDisposable
{
    private readonly string _directory;
    private readonly JsonSerializerOptions _options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public JsonFilePerformanceStore(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Performance 存储目录不能为空。", nameof(directory));
        _directory = Path.GetFullPath(directory);
    }

    public async Task<PerformanceSnapshot?> LoadActiveAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return File.Exists(ActivePath) ? await ReadAsync(ActivePath, cancellationToken) : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not PerformanceStoreException)
        {
            throw Error(StorageErrorCodes.PerformanceReadFailed, nameof(LoadActiveAsync), "无法读取活动 Performance。", exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveActiveAsync(PerformanceSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(snapshot);
        _ = Tavi.Domain.Performance.Performance.Create(snapshot);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(ActivePath))
            {
                PerformanceSnapshot current = await ReadAsync(ActivePath, cancellationToken);
                if (current.PerformanceId != snapshot.PerformanceId)
                    throw Error(StorageErrorCodes.PerformanceWriteFailed, nameof(SaveActiveAsync), $"活动席位已被 Performance {current.PerformanceId} 占用。");
            }
            await WriteAtomicAsync(ActivePath, snapshot, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not PerformanceStoreException)
        {
            throw Error(StorageErrorCodes.PerformanceWriteFailed, nameof(SaveActiveAsync), "无法保存活动 Performance。", exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ArchiveAsync(PerformanceSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Status == PerformanceStatus.Active)
            throw new ArgumentException("活动 Performance 不能归档。", nameof(snapshot));
        _ = Tavi.Domain.Performance.Performance.Create(snapshot);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(ArchiveDirectory);
            await WriteAtomicAsync(Path.Combine(ArchiveDirectory, $"{snapshot.PerformanceId:N}.performance.json"), snapshot, cancellationToken);
            if (File.Exists(ActivePath))
            {
                PerformanceSnapshot current = await ReadAsync(ActivePath, cancellationToken);
                if (current.PerformanceId == snapshot.PerformanceId)
                    File.Delete(ActivePath);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not PerformanceStoreException)
        {
            throw Error(StorageErrorCodes.PerformanceWriteFailed, nameof(ArchiveAsync), "无法归档 Performance。", exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<PerformanceSnapshot>> ListArchivedAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!Directory.Exists(ArchiveDirectory))
                return [];
            var values = new List<PerformanceSnapshot>();
            foreach (string path in Directory.EnumerateFiles(ArchiveDirectory, "*.performance.json").OrderBy(value => value, StringComparer.Ordinal))
                values.Add(await ReadAsync(path, cancellationToken));
            return values;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PerformanceSnapshot?> LoadArchivedAsync(Guid performanceId, CancellationToken cancellationToken = default)
    {
        if (performanceId == Guid.Empty)
            throw new ArgumentException("Performance 标识不能为空。", nameof(performanceId));
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string path = Path.Combine(ArchiveDirectory, $"{performanceId:N}.performance.json");
            return File.Exists(path) ? await ReadAsync(path, cancellationToken) : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _gate.Dispose();
    }

    private string ActivePath => Path.Combine(_directory, "active.performance.json");
    private string ArchiveDirectory => Path.Combine(_directory, "archive");

    private async Task<PerformanceSnapshot> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        PerformanceSaveDocumentV1 document = await JsonSerializer.DeserializeAsync<PerformanceSaveDocumentV1>(stream, _options, cancellationToken)
            ?? throw new InvalidDataException("Performance 存档内容为空。");
        if (document.Version != PerformanceSaveDocumentV1.CurrentVersion)
            throw new InvalidDataException($"不支持 Performance 存档版本 {document.Version}。");
        return PerformanceSaveMapper.ToDomain(document.Performance);
    }

    private async Task WriteAtomicAsync(string path, PerformanceSnapshot snapshot, CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Performance 文件缺少目录。");
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var document = new PerformanceSaveDocumentV1 { SavedAtUtc = DateTimeOffset.UtcNow, Performance = PerformanceSaveMapper.FromDomain(snapshot) };
                await JsonSerializer.SerializeAsync(stream, document, _options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static PerformanceStoreException Error(string code, string operation, string message, Exception? inner = null)
        => new(code, operation, message, innerException: inner);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
