namespace Tavi.Infrastructure.OpenAI;

/// <summary>定义使用 OpenAI Chat Completions 或 Responses API。</summary>
public enum OpenAIClientType
{
    Chat,
    Responses
}

/// <summary>
/// OpenAI 适配器专有配置。API Key 不应写入 Application 设置存档。
/// </summary>
public sealed record OpenAILanguageModelOptions
{
    /// <summary>获取 OpenAI 或兼容服务的基础地址。</summary>
    public Uri Endpoint { get; init; } = new("https://api.openai.com/v1");
    /// <summary>获取服务端模型名称。</summary>
    public string Model { get; init; } = string.Empty;
    /// <summary>获取仅由启动层提供且不得持久化到 Application 设置的 API Key。</summary>
    public string ApiKey { get; init; } = string.Empty;
    /// <summary>获取使用的 OpenAI API 类型。</summary>
    public OpenAIClientType ClientType { get; init; } = OpenAIClientType.Chat;
    /// <summary>获取服务端是否支持 tool_choice=required；兼容端点不支持时应显式设为 false。</summary>
    public bool SupportsRequiredToolChoice { get; init; } = true;
    /// <summary>获取兼容端点的 enable_thinking 扩展值；null 表示不发送该供应商字段。</summary>
    public bool? EnableThinking { get; init; }

    /// <summary>验证 Endpoint、模型、凭证和客户端类型。</summary>
    public void Validate()
    {
        if (Endpoint is null || !Endpoint.IsAbsoluteUri)
            throw new ArgumentException("OpenAI Endpoint 必须是绝对 URI。", nameof(Endpoint));
        if (string.IsNullOrWhiteSpace(Model))
            throw new ArgumentException("OpenAI 模型名称不能为空。", nameof(Model));
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new ArgumentException("OpenAI API Key 不能为空。", nameof(ApiKey));
        if (!Enum.IsDefined(ClientType))
            throw new ArgumentOutOfRangeException(nameof(ClientType));
    }
}
