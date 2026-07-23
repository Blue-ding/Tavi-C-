using System.Text.Json;
using System.Text.Json.Serialization;
using Tavi.Application.LanguageModel;
using Tavi.Infrastructure.OpenAI;
using Xunit;

namespace Tavi.Infrastructure.OpenAI.Tests;

/// <summary>使用本机 OpenAI 配置文件验证真实兼容服务。</summary>
public sealed class OpenAILanguageModelIntegrationTests
{
    /// <summary>验证 Chat API 的真实工具循环和 JSON 输出。</summary>
    [OpenAIIntegrationFact]
    public async Task ChatCompletesToolLoopAndProducesJson()
    {
        OpenAIConfiguration configuration = LocalOpenAIConfiguration.Load();
        var client = new OpenAILanguageModelClient((configuration with { ClientType = OpenAIClientType.Chat }).ToOptions());
        var runner = new LanguageModelRunner(client, IntegrationSettings());
        var request = new LanguageModelRunRequest
        {
            Conversation = new LanguageModelConversation
            {
                Messages = [ModelMessage.User("必须调用 echo 工具一次，然后只用 JSON 返回工具结果中的 ok 布尔值。")]
            },
            Tools = [new EchoTool()],
            ToolCallMode = ToolCallMode.Auto,
            OutputValidator = new JsonOutputValidator<JsonResult>()
        };

        LanguageModelRunResult result = await runner.Start(request).Completion;
        JsonResult? output = JsonSerializer.Deserialize<JsonResult>(result.Output);

        Assert.NotNull(output);
        Assert.True(output.Ok);
        Assert.Equal(1, result.ToolRounds);
    }

    /// <summary>验证 Responses API，或确认兼容端点的 404 被稳定映射。</summary>
    [OpenAIIntegrationFact]
    public async Task ResponsesCompletesSimpleRequest()
    {
        OpenAIConfiguration configuration = LocalOpenAIConfiguration.Load();
        var client = new OpenAILanguageModelClient((configuration with { ClientType = OpenAIClientType.Responses }).ToOptions());
        var runner = new LanguageModelRunner(client, IntegrationSettings());
        var request = new LanguageModelRunRequest
        {
            Conversation = new LanguageModelConversation
            {
                Messages = [ModelMessage.User("仅回复 pong。")]
            },
            ToolCallMode = ToolCallMode.None
        };

        try
        {
            LanguageModelRunResult result = await runner.Start(request).Completion;
            Assert.Contains("pong", result.Output, StringComparison.OrdinalIgnoreCase);
        }
        catch (LanguageModelProviderException exception) when (exception.LanguageModelDetails.HttpStatusCode == 404)
        {
            Assert.Equal(LanguageModelErrorCodes.InvalidRequest, exception.ErrorCode);
            Assert.Equal("OpenAI", exception.LanguageModelDetails.Provider);
        }
    }

    /// <summary>验证不支持强制工具选择时在网络调用前拒绝请求。</summary>
    [Fact]
    public void UnsupportedRequiredToolChoiceFailsBeforeNetworkCall()
    {
        var client = new OpenAILanguageModelClient(new OpenAILanguageModelOptions
        {
            Endpoint = new Uri("https://example.invalid/v1"),
            Model = "test",
            ApiKey = "not-a-real-key",
            SupportsRequiredToolChoice = false
        });
        var runner = new LanguageModelRunner(client, IntegrationSettings());
        var request = new LanguageModelRunRequest
        {
            Conversation = new LanguageModelConversation { Messages = [ModelMessage.User("test")] },
            Tools = [new EchoTool()],
            ToolCallMode = ToolCallMode.Required
        };

        LanguageModelConfigurationException exception = Assert.Throws<LanguageModelConfigurationException>(() => runner.Start(request));

        Assert.Equal(LanguageModelErrorCodes.UnsupportedCapability, exception.ErrorCode);
        Assert.Equal("RequiredToolChoice", exception.LanguageModelDetails.Operation);
    }

    private static LanguageModelSettings IntegrationSettings() =>
        new()
        {
            OverallTimeout = TimeSpan.FromSeconds(90),
            MaxToolRounds = 3,
            MaxOutputRepairAttempts = 1
        };

    private sealed record EchoArguments : IToolArgument
    {
        public string Value { get; init; } = string.Empty;
    }

    private sealed class EchoTool : Tool<EchoArguments>
    {
        public override string name => "echo";
        public override string description => "返回固定的 JSON 结果。";

        protected override Task<string> Execute(EchoArguments arguments, CancellationToken cancellationToken) =>
            Task.FromResult("""{"ok":true}""");
    }

    private sealed record JsonResult
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; init; }
    }
}

/// <summary>仅在显式启用且本机凭证文件存在时运行在线测试。</summary>
public sealed class OpenAIIntegrationFactAttribute : FactAttribute
{
    /// <summary>创建只在显式启用且凭证存在时运行的在线测试标记。</summary>
    public OpenAIIntegrationFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("TAVI_RUN_OPENAI_INTEGRATION_TESTS"), "1", StringComparison.Ordinal))
            Skip = "设置 TAVI_RUN_OPENAI_INTEGRATION_TESTS=1 后运行在线测试。";
        else if (!File.Exists(LocalOpenAIConfiguration.FindPath()))
            Skip = "未找到本机 OpenAI 配置 openai.json。";
    }
}

/// <summary>只在测试进程内加载本机 OpenAI 配置，且从不输出配置值。</summary>
internal static class LocalOpenAIConfiguration
{
    internal static OpenAIConfiguration Load()
    {
        using var store = new JsonFileOpenAIConfigurationStore(FindPath());
        return store.LoadAsync().GetAwaiter().GetResult() ?? throw new FileNotFoundException("未找到本机 OpenAI 配置。", store.Path);
    }

    internal static string FindPath()
    {
        string? configuredPath = Environment.GetEnvironmentVariable("TAVI_OPENAI_CONFIGURATION_PATH");
        return string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tavi", "Settings", "openai.json")
            : Path.GetFullPath(configuredPath);
    }
}
