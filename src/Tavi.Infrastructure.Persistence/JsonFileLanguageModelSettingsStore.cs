using System.Text.Json;
using System.Text.Json.Serialization;
using Tavi.Application.LanguageModel;

namespace Tavi.Infrastructure.Persistence;

/// <summary>
/// 将 Application 语言模型设置保存为版本化 JSON 文件。
/// 此存储不包含 API Key 等适配器秘密。
/// </summary>
public sealed class JsonFileLanguageModelSettingsStore :
    ILanguageModelSettingsStore,
    IDisposable
{
    private readonly string _path;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    /// <summary>创建使用指定 JSON 文件的设置存储。</summary>
    public JsonFileLanguageModelSettingsStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("设置文件路径不能为空。", nameof(path));
        _path = System.IO.Path.GetFullPath(path);
        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
        };
    }

    /// <summary>获取设置文件的绝对路径。</summary>
    public string Path => _path;

    /// <inheritdoc />
    public async Task<LanguageModelSettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path))
                return null;
            await using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            LanguageModelSettings settings =
                await JsonSerializer.DeserializeAsync<LanguageModelSettings>(
                    stream,
                    _serializerOptions,
                    cancellationToken)
                ?? throw new InvalidDataException("语言模型设置文件为空。");
            settings.Validate();
            return settings;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not LanguageModelSettingsStoreException)
        {
            throw new LanguageModelSettingsStoreException("无法读取语言模型设置。", exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(LanguageModelSettings settings, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        await _gate.WaitAsync(cancellationToken);
        string? temporaryPath = null;
        try
        {
            string directory = System.IO.Path.GetDirectoryName(_path)
                               ?? throw new InvalidOperationException("设置文件缺少父目录。");
            Directory.CreateDirectory(directory);
            temporaryPath = System.IO.Path.Combine(
                directory,
                $"{System.IO.Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settings,
                    _serializerOptions,
                    cancellationToken);
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
        catch (Exception exception) when (exception is not LanguageModelSettingsStoreException)
        {
            throw new LanguageModelSettingsStoreException("无法保存语言模型设置。", exception);
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

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
}

/// <summary>表示语言模型设置文件无法读取或写入。</summary>
public sealed class LanguageModelSettingsStoreException : Exception
{
    /// <summary>创建设置存储异常。</summary>
    public LanguageModelSettingsStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
