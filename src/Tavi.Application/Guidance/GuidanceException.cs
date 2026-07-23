namespace Tavi.Application.Guidance;

/// <summary>定义遵循 <c>TAVI.&lt;AREA&gt;.&lt;SUBJECT&gt;.&lt;REASON&gt;</c> 约定的 Guidance 稳定错误码。</summary>
public static class GuidanceErrorCodes
{
    /// <summary>指定请求的 Guidance 会话不存在。</summary>
    public const string SessionNotFound = "TAVI.GUIDANCE.SESSION.NOT_FOUND";

    /// <summary>指定 Guidance 会话当前状态不允许请求的操作。</summary>
    public const string InvalidSessionState = "TAVI.GUIDANCE.SESSION.INVALID_STATE";

    /// <summary>指定 Guidance 模型生成失败。</summary>
    public const string GenerationFailed = "TAVI.GUIDANCE.GENERATION.FAILED";

    /// <summary>指定 Guidance 提案提交发生非业务分支失败。</summary>
    public const string CommitFailed = "TAVI.GUIDANCE.COMMIT.FAILED";

    /// <summary>指定 Guidance 内部出现无法归入其他代码的失败。</summary>
    public const string InternalFailure = "TAVI.GUIDANCE.INTERNAL.FAILURE";
}

/// <summary>表示 Guidance 应用流程失败，并携带稳定代码、操作、会话、瞬态标志及结构化详情。</summary>
public sealed class GuidanceException : TaviException
{
    /// <summary>创建包含完整 Guidance 错误上下文的异常。</summary>
    public GuidanceException(string errorCode, string operation, string message, Guid? sessionId = null, bool isTransient = false, IReadOnlyDictionary<string, string>? details = null, Exception? innerException = null)
        : base(errorCode, GetCategory(errorCode), message, operation, isTransient, details, innerException)
    {
        SessionId = sessionId;
    }

    /// <summary>获取相关 Guidance 会话标识；错误不属于特定会话时为 <see langword="null"/>。</summary>
    public Guid? SessionId { get; }

    private static TaviErrorCategory GetCategory(string errorCode)
    {
        return errorCode switch
        {
            GuidanceErrorCodes.SessionNotFound => TaviErrorCategory.NotFound,
            GuidanceErrorCodes.InvalidSessionState => TaviErrorCategory.InvalidState,
            GuidanceErrorCodes.GenerationFailed => TaviErrorCategory.ExternalService,
            GuidanceErrorCodes.CommitFailed or GuidanceErrorCodes.InternalFailure => TaviErrorCategory.InternalFailure,
            _ => throw new ArgumentException("不支持的 Guidance 错误码。", nameof(errorCode))
        };
    }
}
