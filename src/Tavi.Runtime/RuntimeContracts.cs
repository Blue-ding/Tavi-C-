using Tavi.Application.Guidance;
using Tavi.Application.LanguageModel;
using Tavi.Infrastructure.OpenAI;

namespace Tavi.Runtime;

/// <summary>描述 World Runtime 向进程内 Adapter 发布的状态变化。</summary>
public sealed record WorldRuntimeEvent(
    string Type,
    Guid StateId,
    bool IsDirty,
    Guid? CommitId,
    string? Operation,
    string? Error);

/// <summary>描述 Guidance Runtime 当前是否可用。</summary>
public sealed record GuidanceAvailability(
    bool Available,
    string? Provider,
    string Message);

/// <summary>描述已启动的 Guidance 操作及启动后的会话快照。</summary>
public sealed record GuidanceRuntimeOperation(
    Guid OperationId,
    Guid SessionId,
    GuidanceOperationState State,
    GuidanceSnapshot Snapshot);

/// <summary>描述 Guidance 提交结果及提交后的会话快照。</summary>
public sealed record GuidanceRuntimeCommit(
    GuidanceCommitResult Result,
    GuidanceSnapshot Snapshot);

/// <summary>描述 Guidance Runtime 向进程内 Adapter 发布的事件。</summary>
public sealed record GuidanceRuntimeEvent(
    string Type,
    Guid SessionId,
    Guid? OperationId,
    string? Text,
    GuidanceSnapshot? Snapshot,
    string? Error);

/// <summary>不暴露密钥正文的 OpenAI Runtime 配置快照。</summary>
public sealed record OpenAIConfigurationSnapshot(
    Uri Endpoint,
    string Model,
    OpenAIClientType ClientType,
    bool SupportsRequiredToolChoice,
    bool? EnableThinking,
    bool HasApiKey);

/// <summary>请求 Runtime 更新本机 OpenAI 配置；空密钥表示保留现值。</summary>
public sealed record OpenAIConfigurationUpdate(
    Uri Endpoint,
    string Model,
    string? ApiKey,
    OpenAIClientType ClientType,
    bool SupportsRequiredToolChoice,
    bool? EnableThinking);
