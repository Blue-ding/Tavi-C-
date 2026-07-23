using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tavi.Infrastructure.OpenAI;

/// <summary>将包含本机 API Key 的版本化 OpenAI 配置保存为 JSON 文件。</summary>
public sealed class JsonFileOpenAIConfigurationStore : IOpenAIConfigurationStore, IDisposable
{
    private readonly string _path;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    /// <summary>创建使用指定 JSON 文件的 OpenAI 配置存储。</summary>
    public JsonFileOpenAIConfigurationStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("OpenAI 配置文件路径不能为空。", nameof(path));
        _path = System.IO.Path.GetFullPath(path);
        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
        };
    }

    /// <summary>获取配置文件的绝对路径。</summary>
    public string Path => _path;

    /// <inheritdoc />
    public async Task<OpenAIConfiguration?> LoadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path))
                return null;
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            OpenAIConfiguration configuration = await JsonSerializer.DeserializeAsync<OpenAIConfiguration>(stream, _serializerOptions, cancellationToken) ?? throw new InvalidDataException("OpenAI 配置文件为空。");
            configuration.Validate();
            return configuration;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OpenAIConfigurationStoreException)
        {
            throw new OpenAIConfigurationStoreException(OpenAIConfigurationStoreErrorCodes.ReadFailed, "Load", "无法读取 OpenAI 配置。", exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(OpenAIConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        await _gate.WaitAsync(cancellationToken);
        string? temporaryPath = null;
        try
        {
            string directory = System.IO.Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("OpenAI 配置文件缺少父目录。");
            Directory.CreateDirectory(directory);
            temporaryPath = System.IO.Path.Combine(directory, $"{System.IO.Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, configuration, _serializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
            }
            File.Move(temporaryPath, _path, true);
            temporaryPath = null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OpenAIConfigurationStoreException)
        {
            throw new OpenAIConfigurationStoreException(OpenAIConfigurationStoreErrorCodes.WriteFailed, "Save", "无法保存 OpenAI 配置。", exception);
        }
        finally
        {
            if (temporaryPath is not null)
                TryDelete(temporaryPath);
            _gate.Release();
        }
    }

    /// <summary>释放文件访问同步资源。</summary>
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
}
