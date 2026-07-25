using System.Text.Json;
using Tavi.Application.Extensions;
using Tavi.Extensibility;

namespace Tavi.Infrastructure.Persistence;

/// <summary>使用原子替换写入版本化 Extension 设置 JSON；该文件只保存启用状态和非敏感行为参数。</summary>
public sealed class JsonFileExtensionSettingsStore : IExtensionSettingsStore, IDisposable
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };
    private bool _disposed;

    /// <summary>创建使用指定绝对或相对文件路径的 Store。</summary>
    public JsonFileExtensionSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = System.IO.Path.GetFullPath(path);
    }

    /// <summary>获取设置文件绝对路径。</summary>
    public string Path => _path;

    /// <inheritdoc />
    public async Task<ExtensionSettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path))
                return null;
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            Document document = await JsonSerializer.DeserializeAsync<Document>(stream, _options, cancellationToken) ?? throw new InvalidDataException("Extension 设置文件为空。");
            if (document.Version != 1 || document.Modules is null)
                throw new InvalidDataException($"Extension 设置版本 {document.Version} 无效。");
            return new ExtensionSettings { Version = document.Version, Modules = document.Modules.Select(module => new ExtensionModuleSettings { Module = new ModuleId(module.Id), Enabled = module.Enabled, Parameters = module.Parameters ?? new Dictionary<string, string>() }).ToArray() };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(ExtensionSettings settings, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken);
        string? temporaryPath = null;
        try
        {
            string directory = System.IO.Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("Extension 设置路径缺少父目录。");
            Directory.CreateDirectory(directory);
            temporaryPath = System.IO.Path.Combine(directory, $"{System.IO.Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
            var document = new Document { Version = settings.Version, Modules = settings.Modules.Select(module => new ModuleDocument { Id = module.Module.Value, Enabled = module.Enabled, Parameters = new Dictionary<string, string>(module.Parameters, StringComparer.Ordinal) }).ToList() };
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, _options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
            }
            File.Move(temporaryPath, _path, true);
            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
                TryDelete(temporaryPath);
            _gate.Release();
        }
    }

    /// <summary>释放文件同步资源。</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _gate.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

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

    private sealed class Document
    {
        public int Version { get; set; } = 1;
        public List<ModuleDocument> Modules { get; set; } = [];
    }

    private sealed class ModuleDocument
    {
        public string Id { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.Ordinal);
    }
}
