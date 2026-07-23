using System.Text.Json;
using System.Text.Json.Serialization;
using Tavi.Application.LanguageModel;
using Tavi.Infrastructure.OpenAI;
using Xunit;

namespace Tavi.Infrastructure.OpenAI.Tests;

/// <summary>使用本机忽略的 SelfCongif.md 验证真实 OpenAI 兼容服务。</summary>
public sealed class OpenAILanguageModelIntegrationTests
{
    /// <summary>验证 Chat API 的真实工具循环和 JSON 输出。</summary>
    [OpenAIIntegrationFact]
    public async Task ChatCompletesToolLoopAndProducesJson()
    {
        LocalOpenAIConfiguration configuration = LocalOpenAIConfiguration.Load();
        var client = new OpenAILanguageModelClient(configuration.ToOptions(OpenAIClientType.Chat));
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
        LocalOpenAIConfiguration configuration = LocalOpenAIConfiguration.Load();
        var client = new OpenAILanguageModelClient(configuration.ToOptions(OpenAIClientType.Responses));
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
        catch (LanguageModelProviderException exception) when (exception.Details.HttpStatusCode == 404)
        {
            Assert.Equal(LanguageModelErrorCodes.InvalidRequest, exception.ErrorCode);
            Assert.Equal("OpenAI", exception.Details.Provider);
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
        Assert.Equal("RequiredToolChoice", exception.Details.Operation);
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
            Skip = "未找到被 Git 忽略的 SelfCongif.md。";
    }
}

/// <summary>只在测试进程内加载本机凭证，且从不输出配置值。</summary>
internal sealed record LocalOpenAIConfiguration(Uri Uri, string Model, string ApiKey)
{
    internal static LocalOpenAIConfiguration Load()
    {
        string path = FindPath();
        string fragment = File.ReadAllText(path);
        using JsonDocument document = JsonDocument.Parse($"{{{fragment}}}", new JsonDocumentOptions { AllowTrailingCommas = true });
        JsonElement root = document.RootElement;
        string uri = GetRequired(root, "Uri");
        string model = GetRequired(root, "Model");
        string apiKey = GetRequired(root, "APIKey");
        return new LocalOpenAIConfiguration(new Uri(uri, UriKind.Absolute), model, apiKey);
    }

    internal OpenAILanguageModelOptions ToOptions(OpenAIClientType clientType) =>
        new()
        {
            Endpoint = Uri,
            Model = Model,
            ApiKey = ApiKey,
            ClientType = clientType,
            SupportsRequiredToolChoice = false,
            EnableThinking = false
        };

    internal static string FindPath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tavi.sln")))
            directory = directory.Parent;
        if (directory is null)
            throw new FileNotFoundException("无法定位 Tavi.sln。");
        return Path.Combine(directory.FullName, "src", "Tavi.Infrastructure.OpenAI", "SelfCongif.md");
    }

    private static string GetRequired(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"SelfCongif.md 缺少字符串字段 {name}。");
        string? value = property.GetString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"SelfCongif.md 字段 {name} 不能为空。")
            : value;
    }
}
