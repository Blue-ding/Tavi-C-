namespace Tavi.Domain.World;

/// <summary>
/// 描述一项不可变的世界修改意图。操作本身不持有 World，也不能自行执行。
/// </summary>
public abstract record WorldOperation;

/// <summary>
/// 添加具有确定标识的 Anchor。
/// </summary>
public sealed record AddAnchorOperation(Guid AnchorId, string Name, string Description, AnchorType Type) : WorldOperation;

/// <summary>
/// 删除 Anchor、相连 Relation，以及该 Character 持有的子世界。
/// </summary>
public sealed record RemoveAnchorOperation(Guid AnchorId) : WorldOperation;

/// <summary>
/// 更新 Anchor 名称。
/// </summary>
public sealed record UpdateAnchorNameOperation(Guid AnchorId, string Name) : WorldOperation;

/// <summary>
/// 更新 Anchor 描述。
/// </summary>
public sealed record UpdateAnchorDescriptionOperation(Guid AnchorId, string Description) : WorldOperation;

/// <summary>
/// 更新 Anchor 类型。
/// </summary>
public sealed record UpdateAnchorTypeOperation(Guid AnchorId, AnchorType Type) : WorldOperation;

/// <summary>
/// 添加具有确定标识的 Relation；DomainId 为空表示事实世界，否则表示 Character 的子世界。
/// </summary>
public sealed record AddRelationOperation(Guid RelationId, string Name, string Description, Guid SourceId, Guid TargetId, Guid? DomainId = null) : WorldOperation;

/// <summary>
/// 删除事实世界或任一子世界中的 Relation。
/// </summary>
public sealed record RemoveRelationOperation(Guid RelationId) : WorldOperation;

/// <summary>
/// 更新 Relation 名称。
/// </summary>
public sealed record UpdateRelationNameOperation(Guid RelationId, string Name) : WorldOperation;

/// <summary>
/// 更新 Relation 描述。
/// </summary>
public sealed record UpdateRelationDescriptionOperation(Guid RelationId, string Description) : WorldOperation;

/// <summary>
/// 为 Character 创建具有确定标识的子世界。
/// </summary>
public sealed record CreateSubWorldOperation(Guid SubWorldId, Guid CharacterId) : WorldOperation;

/// <summary>
/// 删除 Character 持有的子世界及其中的 Relation。
/// </summary>
public sealed record RemoveSubWorldOperation(Guid CharacterId) : WorldOperation;

/// <summary>
/// 提供创建常用世界操作及操作组的静态入口；这些函数只构造数据，不执行修改。
/// </summary>
public static class WorldOperations
{
    /// <summary>
    /// 创建添加 Anchor 的操作并分配标识。
    /// </summary>
    public static AddAnchorOperation AddAnchor(string name, string description, AnchorType type) => new(Guid.NewGuid(), name, description, type);

    /// <summary>
    /// 创建添加 Relation 的操作并分配标识。
    /// </summary>
    public static AddRelationOperation AddRelation(string name, string description, Guid sourceId, Guid targetId, Guid? domainId = null) => new(Guid.NewGuid(), name, description, sourceId, targetId, domainId);

    /// <summary>
    /// 创建子世界操作并分配标识。
    /// </summary>
    public static CreateSubWorldOperation CreateSubWorld(Guid characterId) => new(Guid.NewGuid(), characterId);

    /// <summary>
    /// 将操作按给定顺序组合为一个原子操作组。
    /// </summary>
    public static WorldChangeSet Combine(params WorldOperation[] operations) => new(operations);

    /// <summary>
    /// 将单项操作包装为原子操作组。
    /// </summary>
    public static WorldChangeSet Single(WorldOperation operation) => new([operation]);
}

/// <summary>
/// 按顺序执行的一组不可变世界操作。操作组由 WorldSession 作为一个事务提交。
/// </summary>
public sealed class WorldChangeSet
{
    /// <summary>
    /// 创建操作组；集合会立即复制，之后不受调用方修改影响。
    /// </summary>
    public WorldChangeSet(IEnumerable<WorldOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        WorldOperation[] copied = operations.ToArray();
        if (copied.Any(operation => operation is null))
            throw new ArgumentException("世界操作组不能包含 null。", nameof(operations));
        Operations = Array.AsReadOnly(copied);
    }

    /// <summary>
    /// 获取按执行顺序排列的操作。
    /// </summary>
    public IReadOnlyList<WorldOperation> Operations { get; }

    /// <summary>
    /// 获取操作组是否为空。
    /// </summary>
    public bool IsEmpty => Operations.Count == 0;
}

/// <summary>
/// 描述一次成功提交后可用于重做和撤销的确定性操作组。
/// </summary>
public sealed record AppliedWorldChangeSet(WorldChangeSet Forward, WorldChangeSet Inverse);

/// <summary>
/// 表示事务执行失败且内部回滚也未能完整恢复 World；此时内存状态不再可靠。
/// </summary>
public sealed class WorldTransactionException : TaviException
{
    internal WorldTransactionException(Exception operationException, Exception rollbackException)
        : base(
            WorldErrorCodes.TransactionRollbackFailed,
            TaviErrorCategory.InternalFailure,
            "世界事务执行失败，且回滚未能完整恢复内存状态。",
            "Rollback",
            details: new Dictionary<string, string>
            {
                [nameof(OperationException)] = operationException.GetType().FullName ?? operationException.GetType().Name,
                [nameof(RollbackException)] = rollbackException.GetType().FullName ?? rollbackException.GetType().Name
            },
            innerException: new AggregateException(operationException, rollbackException))
    {
        OperationException = operationException;
        RollbackException = rollbackException;
    }

    /// <summary>
    /// 获取触发回滚的原始操作异常。
    /// </summary>
    public Exception OperationException { get; }

    /// <summary>
    /// 获取回滚过程中发生的异常。
    /// </summary>
    public Exception RollbackException { get; }
}
