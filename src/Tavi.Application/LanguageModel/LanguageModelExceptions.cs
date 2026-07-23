namespace Tavi.Application.LanguageModel;

/// <summary>定义供业务判断和统计使用的稳定错误分类。</summary>
public enum LanguageModelErrorCategory
{
    Configuration,
    InvalidRequest,
    UnsupportedCapability,
    Authentication,
    Authorization,
    RateLimit,
    Transport,
    ServiceUnavailable,
    ProviderProtocol,
    ContextLength,
    ContentFilter,
    OutputValidation,
    ToolNotFound,
    ToolExecution,
    RoundLimit,
    Timeout
}

/// <summary>保存经过脱敏的供应商、请求、运行和工具诊断信息。</summary>
public sealed record LanguageModelErrorDetails
{
    public string? Provider { get; init; }
    public string? ProviderErrorCode { get; init; }
    public int? HttpStatusCode { get; init; }
    public string? RequestId { get; init; }
    public TimeSpan? RetryAfter { get; init; }
    public string? Operation { get; init; }
    public Guid? RunId { get; init; }
    public string? ToolName { get; init; }
    public string? ToolCallId { get; init; }
}

/// <summary>定义跨供应商保持稳定的语言模型错误码。</summary>
public static class LanguageModelErrorCodes
{
    public const string InvalidConfiguration = "TAVI.LM.CONFIG.INVALID";
    public const string UnsupportedCapability = "TAVI.LM.CONFIG.UNSUPPORTED_CAPABILITY";
    public const string InvalidRequest = "TAVI.LM.REQUEST.INVALID";
    public const string Authentication = "TAVI.LM.PROVIDER.AUTHENTICATION";
    public const string Authorization = "TAVI.LM.PROVIDER.AUTHORIZATION";
    public const string RateLimited = "TAVI.LM.PROVIDER.RATE_LIMITED";
    public const string Transport = "TAVI.LM.PROVIDER.TRANSPORT";
    public const string ServiceUnavailable = "TAVI.LM.PROVIDER.SERVICE_UNAVAILABLE";
    public const string InvalidProviderResponse = "TAVI.LM.PROTOCOL.INVALID_RESPONSE";
    public const string ContextLength = "TAVI.LM.OUTPUT.CONTEXT_LENGTH";
    public const string ContentFiltered = "TAVI.LM.OUTPUT.CONTENT_FILTERED";
    public const string OutputValidationFailed = "TAVI.LM.OUTPUT.VALIDATION_FAILED";
    public const string ToolNotFound = "TAVI.LM.TOOL.NOT_FOUND";
    public const string ToolExecutionFailed = "TAVI.LM.TOOL.EXECUTION_FAILED";
    public const string RoundLimit = "TAVI.LM.RUN.ROUND_LIMIT";
    public const string Timeout = "TAVI.LM.RUN.TIMEOUT";
}

/// <summary>
/// 语言模型运行失败的稳定异常边界。供应商异常保存在 InnerException 中。
/// </summary>
public class LanguageModelException : Exception
{
    /// <summary>创建带稳定错误码和脱敏诊断信息的语言模型异常。</summary>
    public LanguageModelException(
        string errorCode,
        LanguageModelErrorCategory category,
        string message,
        bool isTransient = false,
        LanguageModelErrorDetails? details = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
            throw new ArgumentException("错误码不能为空。", nameof(errorCode));
        ErrorCode = errorCode;
        Category = category;
        IsTransient = isTransient;
        Details = details ?? new LanguageModelErrorDetails();
    }

    public string ErrorCode { get; }
    public LanguageModelErrorCategory Category { get; }
    public bool IsTransient { get; }
    public LanguageModelErrorDetails Details { get; }
}

/// <summary>表示业务设置与适配器能力无效或不兼容。</summary>
public sealed class LanguageModelConfigurationException : LanguageModelException
{
    private LanguageModelConfigurationException(
        string errorCode,
        LanguageModelErrorCategory category,
        string message,
        LanguageModelErrorDetails? details = null)
        : base(errorCode, category, message, details: details)
    {
    }

    /// <summary>创建无效配置异常。</summary>
    public static LanguageModelConfigurationException Invalid(string message) =>
        new(LanguageModelErrorCodes.InvalidConfiguration, LanguageModelErrorCategory.Configuration, message);

    /// <summary>创建适配器缺少必需能力的异常。</summary>
    public static LanguageModelConfigurationException Unsupported(
        string provider,
        string capability) =>
        new(
            LanguageModelErrorCodes.UnsupportedCapability,
            LanguageModelErrorCategory.UnsupportedCapability,
            $"语言模型适配器“{provider}”不支持所需能力“{capability}”。",
            new LanguageModelErrorDetails { Provider = provider, Operation = capability });
}

/// <summary>表示供应商请求失败，并保留原始异常和服务端诊断信息。</summary>
public sealed class LanguageModelProviderException : LanguageModelException
{
    /// <summary>创建供应商异常。</summary>
    public LanguageModelProviderException(
        string errorCode,
        LanguageModelErrorCategory category,
        string message,
        bool isTransient,
        LanguageModelErrorDetails details,
        Exception? innerException = null)
        : base(errorCode, category, message, isTransient, details, innerException)
    {
    }
}

/// <summary>表示适配器收到无法映射或违反约定的供应商响应。</summary>
public sealed class LanguageModelProtocolException : LanguageModelException
{
    /// <summary>创建协议异常。</summary>
    public LanguageModelProtocolException(
        string message,
        LanguageModelErrorDetails? details = null,
        Exception? innerException = null)
        : base(
            LanguageModelErrorCodes.InvalidProviderResponse,
            LanguageModelErrorCategory.ProviderProtocol,
            message,
            details: details,
            innerException: innerException)
    {
    }
}

/// <summary>表示模型输出耗尽修复次数后仍未通过本地校验。</summary>
public sealed class ModelOutputValidationException : LanguageModelException
{
    /// <summary>创建输出校验异常。</summary>
    public ModelOutputValidationException(
        string message,
        LanguageModelErrorDetails details)
        : base(
            LanguageModelErrorCodes.OutputValidationFailed,
            LanguageModelErrorCategory.OutputValidation,
            message,
            details: details)
    {
    }
}

/// <summary>表示工具不存在或工具执行失败。</summary>
public sealed class ModelToolException : LanguageModelException
{
    /// <summary>创建工具异常。</summary>
    public ModelToolException(
        string errorCode,
        LanguageModelErrorCategory category,
        string message,
        LanguageModelErrorDetails details,
        Exception? innerException = null)
        : base(errorCode, category, message, details: details, innerException: innerException)
    {
    }
}

/// <summary>表示工具循环达到业务配置的最大轮次。</summary>
public sealed class ModelRoundLimitException : LanguageModelException
{
    /// <summary>创建工具轮次上限异常。</summary>
    public ModelRoundLimitException(int maximum, Guid runId)
        : base(
            LanguageModelErrorCodes.RoundLimit,
            LanguageModelErrorCategory.RoundLimit,
            $"工具调用超过最大轮数 {maximum}。",
            details: new LanguageModelErrorDetails { RunId = runId, Operation = "ToolLoop" })
    {
    }
}

/// <summary>表示一次完整模型运行超过 Application 总超时。</summary>
public sealed class LanguageModelTimeoutException : LanguageModelException
{
    /// <summary>创建模型运行超时异常。</summary>
    public LanguageModelTimeoutException(TimeSpan timeout, Guid runId, Exception innerException)
        : base(
            LanguageModelErrorCodes.Timeout,
            LanguageModelErrorCategory.Timeout,
            $"语言模型运行超过总超时时间 {timeout}。",
            true,
            new LanguageModelErrorDetails { RunId = runId, Operation = "Run" },
            innerException)
    {
    }
}
