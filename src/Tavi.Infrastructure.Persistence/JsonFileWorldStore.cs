using System.Text.Json;
using Tavi.Application.World;
using Tavi.Domain.World;
using RuntimeWorld = Tavi.Domain.World.World;

namespace Tavi.Infrastructure.Persistence;

/// <summary>
/// 使用版本化 JSON 文件持久化世界快照，并通过原子替换维护一个备份。
/// </summary>
public sealed class JsonFileWorldStore : IWorldStore, IDisposable
{
    private readonly string _saveDirectory;
    private readonly JsonSerializerOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// 创建使用指定存档目录的 JSON 世界存储。
    /// </summary>
    public JsonFileWorldStore(string saveDirectory)
    {
        if (string.IsNullOrWhiteSpace(saveDirectory))
            throw new ArgumentException("存档目录不能为空。", nameof(saveDirectory));
        _saveDirectory = Path.GetFullPath(saveDirectory);
        _options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };
    }

    /// <summary>
    /// 获取存档文件所在目录。
    /// </summary>
    public string SaveDirectory => _saveDirectory;

    /// <summary>
    /// 读取指定存档槽；主文件无效时尝试读取备份，二者均不存在时返回 null。
    /// </summary>
    public async Task<WorldSnapshot?> LoadAsync(string slot, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string normalizedSlot = ValidateSlot(slot);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string savePath = GetSavePath(normalizedSlot);
            string backupPath = GetBackupPath(normalizedSlot);
            if (!File.Exists(savePath) && !File.Exists(backupPath))
                return null;
            Exception? primaryException = null;
            if (File.Exists(savePath))
            {
                try
                {
                    return await ReadSnapshotAsync(savePath, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    primaryException = exception;
                }
            }
            if (File.Exists(backupPath))
            {
                try
                {
                    return await ReadSnapshotAsync(backupPath, cancellationToken);
                }
                catch (Exception backupException) when (backupException is not OperationCanceledException)
                {
                    throw new WorldStoreException($"存档槽“{normalizedSlot}”的主文件和备份均无法读取。", new AggregateException(primaryException ?? new FileNotFoundException("主存档不存在。"), backupException));
                }
            }
            throw new WorldStoreException($"存档槽“{normalizedSlot}”无法读取。", primaryException ?? new InvalidDataException("存档内容无效。"));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 将世界快照原子写入指定存档槽，并保留上一个有效版本作为备份。
    /// </summary>
    public async Task SaveAsync(string slot, WorldSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(snapshot);
        string normalizedSlot = ValidateSlot(slot);
        await _gate.WaitAsync(cancellationToken);
        string? tempPath = null;
        try
        {
            Directory.CreateDirectory(_saveDirectory);
            string savePath = GetSavePath(normalizedSlot);
            string backupPath = GetBackupPath(normalizedSlot);
            tempPath = Path.Combine(_saveDirectory, $"{normalizedSlot}.{Guid.NewGuid():N}.tmp");
            var document = new SaveDocumentV1 { SavedAtUtc = DateTimeOffset.UtcNow, World = WorldSaveMapper.FromDomain(snapshot) };
            await WriteDocumentAsync(tempPath, document, cancellationToken);
            if (!File.Exists(savePath))
                File.Move(tempPath, savePath);
            else if (await IsValidSaveAsync(savePath, cancellationToken))
                File.Replace(tempPath, savePath, backupPath, true);
            else
                File.Replace(tempPath, savePath, null, true);
            tempPath = null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not WorldStoreException)
        {
            throw new WorldStoreException($"无法保存存档槽“{normalizedSlot}”。", exception);
        }
        finally
        {
            if (tempPath is not null)
                TryDelete(tempPath);
            _gate.Release();
        }
    }

    /// <summary>
    /// 释放文件操作同步资源。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _gate.Dispose();
    }

    private async Task<WorldSnapshot> ReadSnapshotAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        SaveDocumentV1 document = await JsonSerializer.DeserializeAsync<SaveDocumentV1>(stream, _options, cancellationToken) ?? throw new InvalidDataException("存档内容为空。");
        if (document.Version != SaveDocumentV1.CurrentVersion)
            throw new InvalidDataException($"不支持存档版本 {document.Version}，当前版本为 {SaveDocumentV1.CurrentVersion}。");
        if (document.World is null)
            throw new InvalidDataException("存档缺少 world 数据。");
        WorldSnapshot snapshot = WorldSaveMapper.ToDomain(document.World);
        _ = RuntimeWorld.Create(snapshot);
        return snapshot;
    }

    private async Task WriteDocumentAsync(string path, SaveDocumentV1 document, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, document, _options, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(true);
    }

    private async Task<bool> IsValidSaveAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            _ = await ReadSnapshotAsync(path, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private string GetSavePath(string slot)
    {
        return Path.Combine(_saveDirectory, $"{slot}.save.json");
    }

    private string GetBackupPath(string slot)
    {
        return Path.Combine(_saveDirectory, $"{slot}.save.bak.json");
    }

    private static string ValidateSlot(string slot)
    {
        if (string.IsNullOrWhiteSpace(slot) || slot != slot.Trim() || slot is "." or ".." || slot.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Path.GetFileName(slot) != slot)
            throw new ArgumentException("存档槽名称无效。", nameof(slot));
        return slot;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
