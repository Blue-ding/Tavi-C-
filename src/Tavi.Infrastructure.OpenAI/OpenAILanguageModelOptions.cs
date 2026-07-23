namespace Tavi.Infrastructure.OpenAI;

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
    public Uri Endpoint { get; init; } = new("https://api.openai.com/v1");
    public string Model { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public OpenAIClientType ClientType { get; init; } = OpenAIClientType.Chat;

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
