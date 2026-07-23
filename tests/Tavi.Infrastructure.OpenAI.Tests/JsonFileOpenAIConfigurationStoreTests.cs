using Tavi.Infrastructure.OpenAI;
using Xunit;

namespace Tavi.Infrastructure.OpenAI.Tests;

/// <summary>验证包含本机 API Key 的 OpenAI JSON 配置持久化契约。</summary>
public sealed class JsonFileOpenAIConfigurationStoreTests
{
    /// <summary>验证完整配置能够以约定的版本化 JSON 格式往返。</summary>
    [Fact]
    public async Task ConfigurationCanRoundTrip()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonFileOpenAIConfigurationStore(Path.Combine(directory.Path, "openai.json"));
        var configuration = new OpenAIConfiguration
        {
            Endpoint = new Uri("https://example.com/v1"),
            Model = "example-model",
            ApiKey = "local-secret",
            ClientType = OpenAIClientType.Responses,
            SupportsRequiredToolChoice = false,
            EnableThinking = false
        };

        await store.SaveAsync(configuration);
        OpenAIConfiguration? restored = await store.LoadAsync();
        string file = await File.ReadAllTextAsync(store.Path);

        Assert.Equal(configuration, restored);
        Assert.Contains("\"api_key\": \"local-secret\"", file);
        Assert.Contains("\"client_type\": \"responses\"", file);
    }

    /// <summary>验证配置文件不存在时返回未配置状态。</summary>
    [Fact]
    public async Task MissingConfigurationReturnsNull()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonFileOpenAIConfigurationStore(Path.Combine(directory.Path, "openai.json"));

        OpenAIConfiguration? configuration = await store.LoadAsync();

        Assert.Null(configuration);
    }

    /// <summary>验证无效版本在存储边界转换为稳定且不泄露密钥的异常。</summary>
    [Fact]
    public async Task InvalidConfigurationIsReportedByStoreBoundary()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "openai.json");
        await File.WriteAllTextAsync(path, """{"version":999,"endpoint":"https://example.com/v1","model":"model","api_key":"must-not-leak"}""");
        using var store = new JsonFileOpenAIConfigurationStore(path);

        OpenAIConfigurationStoreException exception = await Assert.ThrowsAsync<OpenAIConfigurationStoreException>(() => store.LoadAsync());

        Assert.Equal(OpenAIConfigurationStoreErrorCodes.ReadFailed, exception.ErrorCode);
        Assert.Equal(TaviErrorCategory.Storage, exception.Category);
        Assert.Equal("Load", exception.Operation);
        Assert.DoesNotContain("must-not-leak", exception.ToString(), StringComparison.Ordinal);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Tavi.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
