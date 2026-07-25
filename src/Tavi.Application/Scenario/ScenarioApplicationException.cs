namespace Tavi.Application.Scenario;

/// <summary>定义 Module 和 Scenario 应用会话稳定错误码。</summary>
public static class ScenarioApplicationErrorCodes
{
    /// <summary>Module 声明文件或注册内容无效。</summary>
    public const string InvalidModule = "TAVI.SCENARIO.MODULE.INVALID";

    /// <summary>Scenario 未满足已加载 Module 的语义约束。</summary>
    public const string SemanticViolation = "TAVI.SCENARIO.SEMANTICS.VIOLATION";

    /// <summary>请求基于的 Scenario StateId 已经过期。</summary>
    public const string StateConflict = "TAVI.SCENARIO.STATE.CONFLICT";

    /// <summary>请求的 Scenario 能力不存在或未注册。</summary>
    public const string CapabilityUnavailable = "TAVI.SCENARIO.CAPABILITY.UNAVAILABLE";

    /// <summary>ScenarioSession 当前状态不允许请求的操作。</summary>
    public const string InvalidSessionState = "TAVI.SCENARIO.SESSION.INVALID_STATE";
}

/// <summary>表示 Module 加载、语义校验或 Scenario 会话操作失败。</summary>
public sealed class ScenarioApplicationException : TaviException
{
    /// <summary>创建带有稳定错误码和结构化操作上下文的 Scenario 应用异常。</summary>
    public ScenarioApplicationException(string errorCode, TaviErrorCategory category, string operation, string message, IReadOnlyDictionary<string, string>? details = null, Exception? innerException = null)
        : base(errorCode, category, message, operation, details: details, innerException: innerException)
    {
    }
}
