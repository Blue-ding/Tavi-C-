namespace Tavi.Application.LanguageModel;

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

    public static LanguageModelConfigurationException Invalid(string message) =>
        new(LanguageModelErrorCodes.InvalidConfiguration, LanguageModelErrorCategory.Configuration, message);

    public static LanguageModelConfigurationException Unsupported(
        string provider,
        string capability) =>
        new(
            LanguageModelErrorCodes.UnsupportedCapability,
            LanguageModelErrorCategory.UnsupportedCapability,
            $"语言模型适配器“{provider}”不支持所需能力“{capability}”。",
            new LanguageModelErrorDetails { Provider = provider, Operation = capability });
}

public sealed class LanguageModelProviderException : LanguageModelException
{
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

public sealed class LanguageModelProtocolException : LanguageModelException
{
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

public sealed class ModelOutputValidationException : LanguageModelException
{
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

public sealed class ModelToolException : LanguageModelException
{
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

public sealed class ModelRoundLimitException : LanguageModelException
{
    public ModelRoundLimitException(int maximum, Guid runId)
        : base(
            LanguageModelErrorCodes.RoundLimit,
            LanguageModelErrorCategory.RoundLimit,
            $"工具调用超过最大轮数 {maximum}。",
            details: new LanguageModelErrorDetails { RunId = runId, Operation = "ToolLoop" })
    {
    }
}

public sealed class LanguageModelTimeoutException : LanguageModelException
{
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
