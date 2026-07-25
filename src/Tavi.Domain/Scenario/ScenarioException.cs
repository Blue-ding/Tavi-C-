namespace Tavi.Domain.Scenario;

/// <summary>定义 Scenario 领域稳定错误码。</summary>
public static class ScenarioErrorCodes
{
    /// <summary>Scenario 操作参数未通过校验。</summary>
    public const string InvalidArgument = "TAVI.SCENARIO.ARGUMENT.INVALID";

    /// <summary>Scenario 中不存在请求的实体。</summary>
    public const string NotFound = "TAVI.SCENARIO.ENTITY.NOT_FOUND";

    /// <summary>Scenario 中已经存在相同标识实体。</summary>
    public const string Duplicate = "TAVI.SCENARIO.ENTITY.DUPLICATE";

    /// <summary>Scenario 快照未通过完整性校验。</summary>
    public const string InvalidSnapshot = "TAVI.SCENARIO.SNAPSHOT.INVALID";

    /// <summary>Scenario 操作失败且前态恢复失败。</summary>
    public const string RollbackFailed = "TAVI.SCENARIO.TRANSACTION.ROLLBACK_FAILED";
}

/// <summary>表示 Scenario 操作、初始化或事务校验失败。</summary>
public sealed class ScenarioException : TaviException
{
    /// <summary>创建具有稳定错误码和操作上下文的 Scenario 异常。</summary>
    public ScenarioException(string errorCode, TaviErrorCategory category, string operation, string message, Exception? innerException = null)
        : base(errorCode, category, message, operation, innerException: innerException)
    {
    }
}
