namespace Tavi.Domain.World;

/// <summary>定义遵循 <c>TAVI.&lt;AREA&gt;.&lt;SUBJECT&gt;.&lt;REASON&gt;</c> 约定的 World 稳定错误码。</summary>
public static class WorldErrorCodes
{
    /// <summary>World 操作参数未通过领域校验。</summary>
    public const string InvalidArgument = "TAVI.WORLD.OPERATION.INVALID_ARGUMENT";

    /// <summary>World 中不存在请求的实体。</summary>
    public const string NotFound = "TAVI.WORLD.ENTITY.NOT_FOUND";

    /// <summary>World 中已存在发生标识或唯一性冲突的实体。</summary>
    public const string Duplicate = "TAVI.WORLD.ENTITY.DUPLICATE";

    /// <summary>World 当前状态不允许请求的操作。</summary>
    public const string InvalidOperation = "TAVI.WORLD.OPERATION.INVALID_STATE";

    /// <summary>World 快照未通过完整性校验。</summary>
    public const string InvalidWorldSnapshot = "TAVI.WORLD.SNAPSHOT.INVALID";

    /// <summary>World 事务执行失败且回滚未能完整恢复内存状态。</summary>
    public const string TransactionRollbackFailed = "TAVI.WORLD.TRANSACTION.ROLLBACK_FAILED";
}

/// <summary>表示 World 操作或初始化校验失败。</summary>
public sealed class WorldException : TaviException
{
    /// <summary>创建包含操作上下文和校验详情的 World 异常。</summary>
    public WorldException(string errorCode, string operation, string detail, Guid? entityId = null, IReadOnlyList<string>? validationErrors = null, Exception? innerException = null)
        : base(errorCode, GetCategory(errorCode), BuildMessage(operation, detail, entityId, validationErrors), operation, details: CreateDetails(entityId, validationErrors), innerException: innerException)
    {
        EntityId = entityId;
        ValidationErrors = validationErrors?.ToArray() ?? Array.Empty<string>();
    }

    /// <summary>获取相关实体标识；错误不属于特定实体时为 <see langword="null"/>。</summary>
    public Guid? EntityId { get; }

    /// <summary>获取快照或操作未通过的具体校验项。</summary>
    public IReadOnlyList<string> ValidationErrors { get; }

    private static TaviErrorCategory GetCategory(string errorCode)
    {
        return errorCode switch
        {
            WorldErrorCodes.InvalidArgument or WorldErrorCodes.InvalidWorldSnapshot => TaviErrorCategory.Validation,
            WorldErrorCodes.NotFound => TaviErrorCategory.NotFound,
            WorldErrorCodes.Duplicate => TaviErrorCategory.Conflict,
            WorldErrorCodes.InvalidOperation => TaviErrorCategory.InvalidState,
            _ => throw new ArgumentException("不支持的 World 错误码。", nameof(errorCode))
        };
    }

    private static IReadOnlyDictionary<string, string> CreateDetails(Guid? entityId, IReadOnlyList<string>? validationErrors)
    {
        var details = new Dictionary<string, string>(StringComparer.Ordinal);
        if (entityId.HasValue)
            details.Add(nameof(EntityId), entityId.Value.ToString());
        if (validationErrors is { Count: > 0 })
            details.Add(nameof(ValidationErrors), string.Join(Environment.NewLine, validationErrors));
        return details;
    }

    private static string BuildMessage(string operation, string detail, Guid? entityId, IReadOnlyList<string>? validationErrors)
    {
        var message = $"World 操作“{operation}”失败：{detail}";
        if (entityId.HasValue)
            message += $" 实体 Id：{entityId.Value}。";
        if (validationErrors is { Count: > 0 })
            message += $"{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", validationErrors)}";
        return message;
    }
}
