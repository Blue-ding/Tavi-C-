using Tavi.Application.LanguageModel;

namespace Tavi.Infrastructure.OpenAI;

/// <summary>定义可持久化到本地文件的 OpenAI 连接配置，其中包含本机使用的 API Key。</summary>
public sealed record OpenAIConfiguration
{
    /// <summary>获取当前配置文件格式版本。</summary>
    public const int CurrentVersion = 1;

    /// <summary>获取配置文件格式版本。</summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>获取 OpenAI 或兼容服务的基础地址。</summary>
    public Uri Endpoint { get; init; } = new("https://api.openai.com/v1");

    /// <summary>获取服务端模型名称。</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>获取仅供本机使用的 API Key。</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>获取使用的 OpenAI API 类型。</summary>
    public OpenAIClientType ClientType { get; init; } = OpenAIClientType.Chat;

    /// <summary>获取服务端是否支持 <c>tool_choice=required</c>。</summary>
    public bool SupportsRequiredToolChoice { get; init; } = true;

    /// <summary>获取兼容端点的 <c>enable_thinking</c> 扩展值；<see langword="null"/> 表示不发送该字段。</summary>
    public bool? EnableThinking { get; init; }

    /// <summary>验证格式版本和全部 OpenAI 连接参数。</summary>
    public void Validate()
    {
        if (Version != CurrentVersion)
            throw LanguageModelConfigurationException.Invalid($"不支持 OpenAI 配置版本 {Version}，当前版本为 {CurrentVersion}。");
        ToOptions().Validate();
    }

    /// <summary>创建用于构造 OpenAI 客户端的运行时选项。</summary>
    public OpenAILanguageModelOptions ToOptions() => new()
    {
        Endpoint = Endpoint,
        Model = Model,
        ApiKey = ApiKey,
        ClientType = ClientType,
        SupportsRequiredToolChoice = SupportsRequiredToolChoice,
        EnableThinking = EnableThinking
    };
}
