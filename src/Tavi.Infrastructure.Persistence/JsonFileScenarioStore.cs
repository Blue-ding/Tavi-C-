using System.Text.Json;
using Tavi.Application.Evolution;
using Tavi.Domain.Scenario;

namespace Tavi.Infrastructure.Persistence;

/// <summary>使用独立版本化 JSON 文件持久化 Scenario，并通过原子替换保留一个备份。</summary>
public sealed class JsonFileScenarioStore : IScenarioStore, IDisposable
{
    private readonly string _saveDirectory;
    private readonly JsonSerializerOptions _options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    /// <summary>创建使用指定目录的 Scenario JSON Store。</summary>
    public JsonFileScenarioStore(string saveDirectory)
    {
        if (string.IsNullOrWhiteSpace(saveDirectory))
            throw new ArgumentException("存档目录不能为空。", nameof(saveDirectory));
        _saveDirectory = Path.GetFullPath(saveDirectory);
    }

    /// <summary>获取 Scenario 存档目录。</summary>
    public string SaveDirectory => _saveDirectory;

    /// <inheritdoc />
    public async Task<ScenarioSnapshot?> LoadAsync(string slot, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string normalized = ValidateSlot(slot);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string savePath = GetSavePath(normalized);
            string backupPath = GetBackupPath(normalized);
            if (!File.Exists(savePath) && !File.Exists(backupPath))
                return null;
            Exception? primary = null;
            if (File.Exists(savePath))
            {
                try
                {
                    return await ReadAsync(savePath, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    primary = exception;
                }
            }
            if (File.Exists(backupPath))
            {
                try
                {
                    return await ReadAsync(backupPath, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    throw StoreError(StorageErrorCodes.ScenarioReadFailed, nameof(LoadAsync), normalized, "主文件和备份均无法读取。", new AggregateException(primary ?? new FileNotFoundException("主存档不存在。"), exception));
                }
            }
            throw StoreError(StorageErrorCodes.ScenarioReadFailed, nameof(LoadAsync), normalized, "存档无法读取。", primary);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(string slot, ScenarioSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(snapshot);
        _ = Scenario.Create(snapshot);
        string normalized = ValidateSlot(slot);
        await _gate.WaitAsync(cancellationToken);
        string? tempPath = null;
        try
        {
            Directory.CreateDirectory(_saveDirectory);
            string savePath = GetSavePath(normalized);
            string backupPath = GetBackupPath(normalized);
            tempPath = Path.Combine(_saveDirectory, $"{normalized}.scenario.{Guid.NewGuid():N}.tmp");
            var document = new ScenarioSaveDocumentV1 { SavedAtUtc = DateTimeOffset.UtcNow, Scenario = ScenarioSaveMapper.FromDomain(snapshot) };
            await WriteAsync(tempPath, document, cancellationToken);
            if (!File.Exists(savePath))
                File.Move(tempPath, savePath);
            else if (await IsValidAsync(savePath, cancellationToken))
                File.Replace(tempPath, savePath, backupPath, true);
            else
                File.Replace(tempPath, savePath, null, true);
            tempPath = null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not ScenarioStoreException)
        {
            throw StoreError(StorageErrorCodes.ScenarioWriteFailed, nameof(SaveAsync), normalized, "无法写入 Scenario 存档。", exception);
        }
        finally
        {
            if (tempPath is not null)
                TryDelete(tempPath);
            _gate.Release();
        }
    }

    /// <summary>释放文件操作同步资源。</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _gate.Dispose();
    }

    private async Task<ScenarioSnapshot> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using JsonDocument json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("version", out JsonElement value) || !value.TryGetInt32(out int version) || version != ScenarioSaveDocumentV1.CurrentVersion)
            throw new InvalidDataException($"Scenario 存档版本无效，当前只支持 {ScenarioSaveDocumentV1.CurrentVersion}。");
        ScenarioSaveDocumentV1 document = json.RootElement.Deserialize<ScenarioSaveDocumentV1>(_options) ?? throw new InvalidDataException("Scenario 存档内容为空。");
        return ScenarioSaveMapper.ToDomain(document.Scenario ?? throw new InvalidDataException("Scenario 存档缺少 scenario 数据。"));
    }

    private async Task WriteAsync(string path, ScenarioSaveDocumentV1 document, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, document, _options, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(true);
    }

    private async Task<bool> IsValidAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            _ = await ReadAsync(path, cancellationToken);
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

    private string GetSavePath(string slot) => Path.Combine(_saveDirectory, $"{slot}.scenario.json");
    private string GetBackupPath(string slot) => Path.Combine(_saveDirectory, $"{slot}.scenario.bak.json");

    private static string ValidateSlot(string slot)
    {
        if (string.IsNullOrWhiteSpace(slot) || slot != slot.Trim() || slot is "." or ".." || slot.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Path.GetFileName(slot) != slot)
            throw new ArgumentException("Scenario 存档槽名称无效。", nameof(slot));
        return slot;
    }

    private static ScenarioStoreException StoreError(string code, string operation, string slot, string message, Exception? innerException = null) => new(code, operation, $"存档槽“{slot}”{message}", new Dictionary<string, string> { ["Slot"] = slot }, innerException);

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

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
