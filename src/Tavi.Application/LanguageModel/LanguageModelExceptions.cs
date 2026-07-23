using System.Globalization;

namespace Tavi.Application.LanguageModel;

/// <summary>保存经过脱敏的供应商、请求、运行和工具诊断信息。</summary>
public sealed record LanguageModelErrorDetails
{
    /// <summary>获取语言模型供应商名称。</summary>
    public string? Provider { get; init; }

    /// <summary>获取供应商返回的原始错误码。</summary>
    public string? ProviderErrorCode { get; init; }

    /// <summary>获取供应商响应的 HTTP 状态码。</summary>
    public int? HttpStatusCode { get; init; }

    /// <summary>获取供应商返回的请求标识。</summary>
    public string? RequestId { get; init; }

    /// <summary>获取供应商建议的重试等待时间。</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>获取发生失败的语言模型操作。</summary>
    public string? Operation { get; init; }

    /// <summary>获取相关语言模型运行标识。</summary>
    public Guid? RunId { get; init; }

    /// <summary>获取相关工具名称。</summary>
    public string? ToolName { get; init; }

    /// <summary>获取相关工具调用标识。</summary>
    public string? ToolCallId { get; init; }

    internal IReadOnlyDictionary<string, string> ToDiagnosticDetails()
    {
        var details = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(details, nameof(Provider), Provider);
        Add(details, nameof(ProviderErrorCode), ProviderErrorCode);
        Add(details, nameof(HttpStatusCode), HttpStatusCode?.ToString(CultureInfo.InvariantCulture));
        Add(details, nameof(RequestId), RequestId);
        Add(details, nameof(RetryAfter), RetryAfter?.ToString("c", CultureInfo.InvariantCulture));
        Add(details, nameof(Operation), Operation);
        Add(details, nameof(RunId), RunId?.ToString());
        Add(details, nameof(ToolName), ToolName);
        Add(details, nameof(ToolCallId), ToolCallId);
        return details;
    }

    private static void Add(IDictionary<string, string> details, string key, string? value)
    {
        if (value is not null)
            details.Add(key, value);
    }
}

/// <summary>定义跨供应商保持稳定并遵循 <c>TAVI.&lt;AREA&gt;.&lt;SUBJECT&gt;.&lt;REASON&gt;</c> 约定的语言模型错误码。</summary>
public static class LanguageModelErrorCodes
{
    /// <summary>语言模型配置无效。</summary>
    public const string InvalidConfiguration = "TAVI.LM.CONFIG.INVALID";

    /// <summary>语言模型适配器不支持请求的能力。</summary>
    public const string UnsupportedCapability = "TAVI.LM.CONFIG.UNSUPPORTED_CAPABILITY";

    /// <summary>语言模型请求无效。</summary>
    public const string InvalidRequest = "TAVI.LM.REQUEST.INVALID";

    /// <summary>语言模型供应商认证失败。</summary>
    public const string Authentication = "TAVI.LM.PROVIDER.AUTHENTICATION";

    /// <summary>语言模型供应商拒绝授权。</summary>
    public const string Authorization = "TAVI.LM.PROVIDER.AUTHORIZATION";

    /// <summary>语言模型供应商限制请求速率。</summary>
    public const string RateLimited = "TAVI.LM.PROVIDER.RATE_LIMITED";

    /// <summary>语言模型供应商通信失败。</summary>
    public const string Transport = "TAVI.LM.PROVIDER.TRANSPORT";

    /// <summary>语言模型供应商暂时不可用。</summary>
    public const string ServiceUnavailable = "TAVI.LM.PROVIDER.SERVICE_UNAVAILABLE";

    /// <summary>语言模型供应商响应违反协议。</summary>
    public const string InvalidProviderResponse = "TAVI.LM.PROTOCOL.INVALID_RESPONSE";

    /// <summary>语言模型上下文超过供应商限制。</summary>
    public const string ContextLength = "TAVI.LM.OUTPUT.CONTEXT_LENGTH";

    /// <summary>语言模型输出被内容策略过滤。</summary>
    public const string ContentFiltered = "TAVI.LM.OUTPUT.CONTENT_FILTERED";

    /// <summary>语言模型输出未通过本地校验。</summary>
    public const string OutputValidationFailed = "TAVI.LM.OUTPUT.VALIDATION_FAILED";

    /// <summary>模型请求调用了未注册的工具。</summary>
    public const string ToolNotFound = "TAVI.LM.TOOL.NOT_FOUND";

    /// <summary>模型工具执行失败。</summary>
    public const string ToolExecutionFailed = "TAVI.LM.TOOL.EXECUTION_FAILED";

    /// <summary>工具调用达到配置的最大轮数。</summary>
    public const string RoundLimit = "TAVI.LM.RUN.ROUND_LIMIT";

    /// <summary>语言模型运行超过总超时时间。</summary>
    public const string Timeout = "TAVI.LM.RUN.TIMEOUT";
}

/// <summary>语言模型运行失败的稳定异常边界。供应商异常保存在 <see cref="Exception.InnerException"/> 中。</summary>
public class LanguageModelException : TaviException
{
    /// <summary>创建带稳定错误码和脱敏诊断信息的语言模型异常。</summary>
    public LanguageModelException(string errorCode, TaviErrorCategory category, string message, bool isTransient = false, LanguageModelErrorDetails? details = null, Exception? innerException = null)
        : base(errorCode, category, message, details?.Operation, isTransient, (details ?? new LanguageModelErrorDetails()).ToDiagnosticDetails(), innerException)
    {
        LanguageModelDetails = details ?? new LanguageModelErrorDetails();
    }

    /// <summary>获取语言模型专有的强类型诊断信息。</summary>
    public LanguageModelErrorDetails LanguageModelDetails { get; }
}

/// <summary>表示业务设置与适配器能力无效或不兼容。</summary>
public sealed class LanguageModelConfigurationException : LanguageModelException
{
    private LanguageModelConfigurationException(string errorCode, string message, LanguageModelErrorDetails? details = null)
        : base(errorCode, TaviErrorCategory.Configuration, message, details: details)
    {
    }

    /// <summary>创建无效配置异常。</summary>
    public static LanguageModelConfigurationException Invalid(string message) => new(LanguageModelErrorCodes.InvalidConfiguration, message);

    /// <summary>创建适配器缺少必需能力的异常。</summary>
    public static LanguageModelConfigurationException Unsupported(string provider, string capability) =>
        new(LanguageModelErrorCodes.UnsupportedCapability, $"语言模型适配器“{provider}”不支持所需能力“{capability}”。", new LanguageModelErrorDetails { Provider = provider, Operation = capability });
}

/// <summary>表示供应商请求失败，并保留原始异常和服务端诊断信息。</summary>
public sealed class LanguageModelProviderException : LanguageModelException
{
    /// <summary>创建供应商异常。</summary>
    public LanguageModelProviderException(string errorCode, TaviErrorCategory category, string message, bool isTransient, LanguageModelErrorDetails details, Exception? innerException = null)
        : base(errorCode, category, message, isTransient, details, innerException)
    {
    }
}

/// <summary>表示适配器收到无法映射或违反约定的供应商响应。</summary>
public sealed class LanguageModelProtocolException : LanguageModelException
{
    /// <summary>创建协议异常。</summary>
    public LanguageModelProtocolException(string message, LanguageModelErrorDetails? details = null, Exception? innerException = null)
        : base(LanguageModelErrorCodes.InvalidProviderResponse, TaviErrorCategory.Protocol, message, details: details, innerException: innerException)
    {
    }
}

/// <summary>表示模型输出耗尽修复次数后仍未通过本地校验。</summary>
public sealed class ModelOutputValidationException : LanguageModelException
{
    /// <summary>创建输出校验异常。</summary>
    public ModelOutputValidationException(string message, LanguageModelErrorDetails details)
        : base(LanguageModelErrorCodes.OutputValidationFailed, TaviErrorCategory.Validation, message, details: details)
    {
    }
}

/// <summary>表示工具不存在或工具执行失败。</summary>
public sealed class ModelToolException : LanguageModelException
{
    /// <summary>创建工具异常。</summary>
    public ModelToolException(string errorCode, TaviErrorCategory category, string message, LanguageModelErrorDetails details, Exception? innerException = null)
        : base(errorCode, category, message, details: details, innerException: innerException)
    {
    }
}

/// <summary>表示工具循环达到业务配置的最大轮次。</summary>
public sealed class ModelRoundLimitException : LanguageModelException
{
    /// <summary>创建工具轮次上限异常。</summary>
    public ModelRoundLimitException(int maximum, Guid runId)
        : base(LanguageModelErrorCodes.RoundLimit, TaviErrorCategory.InvalidState, $"工具调用超过最大轮数 {maximum}。", details: new LanguageModelErrorDetails { RunId = runId, Operation = "ToolLoop" })
    {
    }
}

/// <summary>表示一次完整模型运行超过 Application 总超时。</summary>
public sealed class LanguageModelTimeoutException : LanguageModelException
{
    /// <summary>创建模型运行超时异常。</summary>
    public LanguageModelTimeoutException(TimeSpan timeout, Guid runId, Exception innerException)
        : base(LanguageModelErrorCodes.Timeout, TaviErrorCategory.Timeout, $"语言模型运行超过总超时时间 {timeout}。", true, new LanguageModelErrorDetails { RunId = runId, Operation = "Run" }, innerException)
    {
    }
}
