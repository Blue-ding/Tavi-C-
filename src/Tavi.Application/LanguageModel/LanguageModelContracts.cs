using System.Collections.ObjectModel;

namespace Tavi.Application.LanguageModel;

/// <summary>
/// 描述语言模型适配器已经实现的能力。
/// </summary>
public sealed record LanguageModelCapabilities
{
    public required string Provider { get; init; }
    public bool SupportsToolCalls { get; init; }
    public bool SupportsParallelToolCalls { get; init; }
    public bool SupportsNativeJsonOutput { get; init; }
    public bool SupportsJsonSchema { get; init; }
    public bool SupportsStreaming { get; init; }
}

/// <summary>
/// 创建供应商会话并公开适配器能力。
/// </summary>
public interface ILanguageModelClient
{
    LanguageModelCapabilities Capabilities { get; }

    ILanguageModelSession CreateSession(LanguageModelSessionOptions options);
}

/// <summary>
/// 表示一次独占的供应商会话。实现不需要保证并发调用安全。
/// </summary>
public interface ILanguageModelSession : IAsyncDisposable
{
    Task<ModelTurnResult> CompleteAsync(
        IReadOnlyList<ModelMessage> messages,
        CancellationToken cancellationToken = default);
}

public interface IStreamingLanguageModelSession : ILanguageModelSession
{
    IAsyncEnumerable<ModelStreamUpdate> CompleteStreamingAsync(
        IReadOnlyList<ModelMessage> messages,
        CancellationToken cancellationToken = default);
}

public sealed record ModelStreamUpdate(
    string TextDelta,
    ModelTurnResult? CompletedTurn = null);

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

    public static ModelMessage System(string text) =>
        new() { Role = ModelMessageRole.System, Text = text ?? throw new ArgumentNullException(nameof(text)) };

    public static ModelMessage User(string text) =>
        new() { Role = ModelMessageRole.User, Text = text ?? throw new ArgumentNullException(nameof(text)) };

    public static ModelMessage Assistant(string text) =>
        new() { Role = ModelMessageRole.Assistant, Text = text ?? throw new ArgumentNullException(nameof(text)) };

    public static ModelMessage Assistant(IReadOnlyList<ModelToolCall> toolCalls)
    {
        ArgumentNullException.ThrowIfNull(toolCalls);
        return new ModelMessage { Role = ModelMessageRole.Assistant, ToolCalls = toolCalls };
    }

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

    public LanguageModelConversation Append(params ModelMessage[] messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return this with { Messages = new ReadOnlyCollection<ModelMessage>(Messages.Concat(messages).ToArray()) };
    }
}

public sealed record ModelToolDefinition(
    string Name,
    string Description,
    BinaryData ParameterSchema);

public sealed record ModelToolCall(
    string CallId,
    string Name,
    BinaryData Arguments);

public enum ModelFinishReason
{
    Completed,
    ToolCalls,
    LengthLimit,
    ContentFiltered
}

public sealed record ModelTurnResult
{
    public string? Text { get; init; }
    public IReadOnlyList<ModelToolCall> ToolCalls { get; init; } = [];
    public required ModelFinishReason FinishReason { get; init; }

    public static ModelTurnResult Completed(string text) =>
        new() { Text = text, FinishReason = ModelFinishReason.Completed };

    public static ModelTurnResult CallingTools(IReadOnlyList<ModelToolCall> calls) =>
        new() { ToolCalls = calls, FinishReason = ModelFinishReason.ToolCalls };
}

public sealed record ModelJsonFormat(
    string Name,
    BinaryData? Schema = null,
    bool Strict = true);

public sealed record LanguageModelSessionOptions
{
    public IReadOnlyList<ModelToolDefinition> Tools { get; init; } = [];
    public ToolCallMode ToolCallMode { get; init; }
    public ModelJsonFormat? JsonFormat { get; init; }
    public bool UseNativeJsonOutput { get; init; }
    public bool UseStreaming { get; init; }
}
