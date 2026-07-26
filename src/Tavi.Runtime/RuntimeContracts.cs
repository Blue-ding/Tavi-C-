using Tavi.Application.Guidance;
using Tavi.Application.LanguageModel;
using Tavi.Extensibility;
using Tavi.Infrastructure.OpenAI;
using System.Text.Json;

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

/// <summary>描述 Extension Runtime 中一个已安装 Module 的活动与期望状态。</summary>
public sealed record ExtensionModuleRuntimeSnapshot(
    ModuleManifest Manifest,
    bool ActiveEnabled,
    bool DesiredEnabled,
    JsonElement SettingsSchema,
    JsonElement ActiveSettings,
    JsonElement DesiredSettings);

/// <summary>描述可安全交给 Host 的完整 Extension 配置状态。</summary>
public sealed record ExtensionRuntimeSnapshot(
    long Revision,
    bool RestartRequired,
    string Message,
    IReadOnlyList<ExtensionModuleRuntimeSnapshot> Modules);

/// <summary>表示完整候选配置中的一个 Module。</summary>
public sealed record ExtensionModuleRuntimeUpdate(
    ModuleId Module,
    bool Enabled,
    JsonElement Settings);

/// <summary>请求以乐观并发条件替换完整 Extension 期望配置。</summary>
public sealed record ExtensionRuntimeUpdate(
    long ExpectedRevision,
    IReadOnlyList<ExtensionModuleRuntimeUpdate> Modules);

/// <summary>表示 Extension Runtime 的稳定配置、并发或持久化失败。</summary>
public sealed class ExtensionRuntimeException : TaviException
{
    public ExtensionRuntimeException(
        string errorCode,
        TaviErrorCategory category,
        string message,
        string operation,
        bool isTransient = false,
        Exception? innerException = null)
        : base(errorCode, category, message, operation, isTransient, innerException: innerException)
    {
    }
}
