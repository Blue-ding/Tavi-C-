using System.Collections.ObjectModel;

namespace Tavi;

/// <summary>定义可跨 Tavi 模块边界稳定识别的错误分类。</summary>
public enum TaviErrorCategory
{
    /// <summary>输入、输出或业务数据未通过校验。</summary>
    Validation,

    /// <summary>请求的资源或实体不存在。</summary>
    NotFound,

    /// <summary>请求与当前版本或已有状态发生冲突。</summary>
    Conflict,

    /// <summary>配置缺失、无效或不支持请求的能力。</summary>
    Configuration,

    /// <summary>持久化介质的读取、写入或恢复失败。</summary>
    Storage,

    /// <summary>外部服务拒绝请求、暂时不可用或通信失败。</summary>
    ExternalService,

    /// <summary>外部响应或模块间数据违反协议约定。</summary>
    Protocol,

    /// <summary>操作超过允许的执行时间。</summary>
    Timeout,

    /// <summary>对象或流程的当前状态不允许执行请求的操作。</summary>
    InvalidState,

    /// <summary>内部不变量被破坏，调用方通常无法自行恢复。</summary>
    InternalFailure
}

/// <summary>表示可由调用方通过稳定错误码和结构化上下文识别的 Tavi 运行时失败。</summary>
public abstract class TaviException : Exception
{
    private static readonly IReadOnlyDictionary<string, string> EmptyDetails =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    /// <summary>创建具有稳定错误契约的 Tavi 异常。</summary>
    /// <param name="errorCode">稳定错误码。必须遵循 <c>TAVI.&lt;AREA&gt;.&lt;SUBJECT&gt;.&lt;REASON&gt;</c>，各段使用大写字母、数字或下划线。</param>
    /// <param name="category">供统一日志、展示和传输层映射使用的错误分类。</param>
    /// <param name="message">供人员阅读的错误消息；调用方不得解析该消息来判断错误类型。</param>
    /// <param name="operation">发生失败的稳定操作名称；无法归属具体操作时为 <see langword="null"/>。</param>
    /// <param name="isTransient">稍后以相同输入重试是否可能成功。</param>
    /// <param name="details">经过脱敏、可安全记录的结构化诊断详情。</param>
    /// <param name="innerException">触发当前边界异常的原始异常。</param>
    protected TaviException(string errorCode, TaviErrorCategory category, string message, string? operation = null, bool isTransient = false, IReadOnlyDictionary<string, string>? details = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ValidateErrorCode(errorCode);
        if (operation is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ErrorCode = errorCode;
        Category = category;
        Operation = operation;
        IsTransient = isTransient;
        Details = details is null
            ? EmptyDetails
            : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(details, StringComparer.Ordinal));
    }

    /// <summary>获取遵循 <c>TAVI.&lt;AREA&gt;.&lt;SUBJECT&gt;.&lt;REASON&gt;</c> 约定的稳定错误码。</summary>
    public string ErrorCode { get; }

    /// <summary>获取供统一日志、展示和传输层映射使用的错误分类。</summary>
    public TaviErrorCategory Category { get; }

    /// <summary>获取发生失败的稳定操作名称；无法归属具体操作时为 <see langword="null"/>。</summary>
    public string? Operation { get; }

    /// <summary>获取稍后以相同输入重试是否可能成功。</summary>
    public bool IsTransient { get; }

    /// <summary>获取经过脱敏、可安全记录且不依赖消息解析的结构化诊断详情。</summary>
    public IReadOnlyDictionary<string, string> Details { get; }

    private static void ValidateErrorCode(string errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        string[] segments = errorCode.Split('.');
        if (segments.Length < 4 || segments[0] != "TAVI" || segments.Any(segment => segment.Length == 0 || segment.Any(character => character is not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') and not '_')))
            throw new ArgumentException("错误码必须遵循 TAVI.<AREA>.<SUBJECT>.<REASON>，各段只能包含大写字母、数字或下划线。", nameof(errorCode));
    }
}
