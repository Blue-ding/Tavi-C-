using System.Collections.ObjectModel;

namespace Tavi.Application.Guidance;

/// <summary>提供稳定的 Guidance 异常代码。</summary>
public static class GuidanceErrorCodes
{
    /// <summary>指定请求的 Guidance 会话不存在。</summary>
    public const string SessionNotFound = "guidance.session_not_found";

    /// <summary>指定 Guidance 会话当前状态不允许请求的操作。</summary>
    public const string InvalidSessionState = "guidance.invalid_session_state";

    /// <summary>指定 Guidance 模型生成失败。</summary>
    public const string GenerationFailed = "guidance.generation_failed";

    /// <summary>指定 Guidance 提案提交发生非业务分支失败。</summary>
    public const string CommitFailed = "guidance.commit_failed";

    /// <summary>指定 Guidance 内部出现无法归入其他代码的失败。</summary>
    public const string InternalFailure = "guidance.internal_failure";
}

/// <summary>表示 Guidance 应用流程失败，并携带稳定代码、操作、会话、瞬态标志及结构化详情。</summary>
public sealed class GuidanceException : Exception
{
    /// <summary>创建包含完整 Guidance 错误上下文的异常。</summary>
    public GuidanceException(string errorCode, string operation, string message, Guid? sessionId = null, bool isTransient = false, IReadOnlyDictionary<string, string>? details = null, Exception? innerException = null) : base(message, innerException)
    {
        ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? throw new ArgumentException("Guidance 错误代码不能为空。", nameof(errorCode)) : errorCode;
        Operation = string.IsNullOrWhiteSpace(operation) ? throw new ArgumentException("Guidance 操作名称不能为空。", nameof(operation)) : operation;
        SessionId = sessionId;
        IsTransient = isTransient;
        Details = new ReadOnlyDictionary<string, string>(details is null ? new Dictionary<string, string>() : new Dictionary<string, string>(details, StringComparer.Ordinal));
    }

    /// <summary>获取稳定的 Guidance 错误代码。</summary>
    public string ErrorCode { get; }

    /// <summary>获取发生错误的 Guidance 操作。</summary>
    public string Operation { get; }

    /// <summary>获取相关 Guidance 会话标识；错误不属于特定会话时为 null。</summary>
    public Guid? SessionId { get; }

    /// <summary>获取调用方稍后重试是否可能成功。</summary>
    public bool IsTransient { get; }

    /// <summary>获取不依赖异常消息解析的结构化错误详情。</summary>
    public IReadOnlyDictionary<string, string> Details { get; }
}
