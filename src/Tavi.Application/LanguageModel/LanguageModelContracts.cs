using System.Collections.ObjectModel;

namespace Tavi.Application.LanguageModel;

/// <summary>
/// 描述语言模型适配器已经实现的能力。
/// </summary>
public sealed record LanguageModelCapabilities
{
    /// <summary>获取适配器及协议名称。</summary>
    public required string Provider { get; init; }
    /// <summary>获取是否支持函数工具调用。</summary>
    public bool SupportsToolCalls { get; init; }
    /// <summary>获取是否支持强制至少调用一个工具。</summary>
    public bool SupportsRequiredToolChoice { get; init; }
    /// <summary>获取是否允许模型在一轮返回多个工具调用。</summary>
    public bool SupportsParallelToolCalls { get; init; }
    /// <summary>获取是否支持原生 JSON object 输出。</summary>
    public bool SupportsNativeJsonOutput { get; init; }
    /// <summary>获取是否支持原生 JSON Schema 输出。</summary>
    public bool SupportsJsonSchema { get; init; }
    /// <summary>获取当前适配器是否已经实现流式读取。</summary>
    public bool SupportsStreaming { get; init; }
}

/// <summary>
/// 创建供应商会话并公开适配器能力。
/// </summary>
public interface ILanguageModelClient
{
    /// <summary>获取适配器已经实现的能力。</summary>
    LanguageModelCapabilities Capabilities { get; }

    /// <summary>创建一次独占且不要求并发安全的模型会话。</summary>
    ILanguageModelSession CreateSession(LanguageModelSessionOptions options);
}

/// <summary>
/// 表示一次独占的供应商会话。实现不需要保证并发调用安全。
/// </summary>
public interface ILanguageModelSession : IAsyncDisposable
{
    /// <summary>提交相对上一轮新增的消息并完成一次模型调用。</summary>
    Task<ModelTurnResult> CompleteAsync(IReadOnlyList<ModelMessage> messages, CancellationToken cancellationToken = default);
}

/// <summary>定义可以逐段返回文本的模型会话。</summary>
public interface IStreamingLanguageModelSession : ILanguageModelSession
{
    /// <summary>流式提交新增消息；序列结束前必须返回包含 CompletedTurn 的更新。</summary>
    IAsyncEnumerable<ModelStreamUpdate> CompleteStreamingAsync(IReadOnlyList<ModelMessage> messages, CancellationToken cancellationToken = default);
}

/// <summary>表示一段流式文本增量以及可选的最终轮次结果。</summary>
public sealed record ModelStreamUpdate(
    string TextDelta,
    ModelTurnResult? CompletedTurn = null);

/// <summary>定义规范消息在会话中的角色。</summary>
public enum ModelMessageRole
{
    System,
    User,
    Assistant,
    Tool
}

/// <summary>
/// 供应商无关的规范消息。工具消息通过 ToolCallId 与先前调用关联。
/// </summary>
public sealed record ModelMessage
{
    public required ModelMessageRole Role { get; init; }
    public string? Text { get; init; }
    public IReadOnlyList<ModelToolCall> ToolCalls { get; init; } = [];
    public string? ToolCallId { get; init; }

    /// <summary>创建系统消息。</summary>
    public static ModelMessage System(string text) =>
        new() { Role = ModelMessageRole.System, Text = text ?? throw new ArgumentNullException(nameof(text)) };

    /// <summary>创建用户消息。</summary>
    public static ModelMessage User(string text) =>
        new() { Role = ModelMessageRole.User, Text = text ?? throw new ArgumentNullException(nameof(text)) };

    /// <summary>创建助手文本消息。</summary>
    public static ModelMessage Assistant(string text) =>
        new() { Role = ModelMessageRole.Assistant, Text = text ?? throw new ArgumentNullException(nameof(text)) };

    /// <summary>创建助手工具调用消息。</summary>
    public static ModelMessage Assistant(IReadOnlyList<ModelToolCall> toolCalls)
    {
        ArgumentNullException.ThrowIfNull(toolCalls);
        return new ModelMessage { Role = ModelMessageRole.Assistant, ToolCalls = toolCalls };
    }

    /// <summary>创建与指定调用关联的工具结果消息。</summary>
    public static ModelMessage Tool(string callId, string result)
    {
        if (string.IsNullOrWhiteSpace(callId))
            throw new ArgumentException("工具调用 ID 不能为空。", nameof(callId));
        return new ModelMessage
        {
            Role = ModelMessageRole.Tool,
            ToolCallId = callId,
            Text = result ?? throw new ArgumentNullException(nameof(result))
        };
    }
}

/// <summary>
/// 可持久化、可回放的规范会话历史。
/// </summary>
public sealed record LanguageModelConversation
{
    public IReadOnlyList<ModelMessage> Messages { get; init; } = [];

    /// <summary>返回追加消息后的新会话，不修改当前实例。</summary>
    public LanguageModelConversation Append(params ModelMessage[] messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return this with { Messages = new ReadOnlyCollection<ModelMessage>(Messages.Concat(messages).ToArray()) };
    }
}

/// <summary>定义暴露给模型的工具名称、描述和参数 JSON Schema。</summary>
public sealed record ModelToolDefinition(
    string Name,
    string Description,
    BinaryData ParameterSchema);

/// <summary>表示模型请求执行的一次工具调用。</summary>
public sealed record ModelToolCall(
    string CallId,
    string Name,
    BinaryData Arguments);

/// <summary>定义单轮模型调用的结束原因。</summary>
public enum ModelFinishReason
{
    Completed,
    ToolCalls,
    LengthLimit,
    ContentFiltered
}

/// <summary>表示一次模型调用的规范化结果。</summary>
public sealed record ModelTurnResult
{
    public string? Text { get; init; }
    public IReadOnlyList<ModelToolCall> ToolCalls { get; init; } = [];
    public required ModelFinishReason FinishReason { get; init; }

    /// <summary>创建正常完成的文本结果。</summary>
    public static ModelTurnResult Completed(string text) =>
        new() { Text = text, FinishReason = ModelFinishReason.Completed };

    /// <summary>创建需要执行工具的结果。</summary>
    public static ModelTurnResult CallingTools(IReadOnlyList<ModelToolCall> calls) =>
        new() { ToolCalls = calls, FinishReason = ModelFinishReason.ToolCalls };
}

/// <summary>描述供应商原生 JSON 输出格式；Schema 为空时仅要求合法 JSON。</summary>
public sealed record ModelJsonFormat(
    string Name,
    BinaryData? Schema = null,
    bool Strict = true);

/// <summary>描述一次供应商会话使用的工具、输出格式和兼容能力选择。</summary>
public sealed record LanguageModelSessionOptions
{
    public IReadOnlyList<ModelToolDefinition> Tools { get; init; } = [];
    public ToolCallMode ToolCallMode { get; init; }
    public ModelJsonFormat? JsonFormat { get; init; }
    public bool UseNativeJsonOutput { get; init; }
    public bool UseStreaming { get; init; }
}
