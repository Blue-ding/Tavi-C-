namespace Tavi.Domain.World;

/// <summary>描述一项不可变的 World 修改意图。</summary>
public abstract record WorldOperation;

/// <summary>添加具有确定标识的 Element。</summary>
/// <param name="ElementId">Element 标识。</param><param name="Name">名称。</param><param name="Description">说明。</param><param name="Type">规则化类型。</param>
public sealed record AddElementOperation(Guid ElementId, string Name, string Description, ElementType Type) : WorldOperation;

/// <summary>删除 Element 及其全部结构依赖。</summary>
/// <param name="ElementId">目标 Element 标识。</param>
public sealed record RemoveElementOperation(Guid ElementId) : WorldOperation;

/// <summary>更新 Element 名称。</summary>
/// <param name="ElementId">目标 Element 标识。</param><param name="Name">新名称。</param>
public sealed record UpdateElementNameOperation(Guid ElementId, string Name) : WorldOperation;

/// <summary>更新 Element 说明。</summary>
/// <param name="ElementId">目标 Element 标识。</param><param name="Description">新说明。</param>
public sealed record UpdateElementDescriptionOperation(Guid ElementId, string Description) : WorldOperation;

/// <summary>更新 Element 规则化类型。</summary>
/// <param name="ElementId">目标 Element 标识。</param><param name="Type">新类型。</param>
public sealed record UpdateElementTypeOperation(Guid ElementId, ElementType Type) : WorldOperation;

/// <summary>添加规则化 Aspect。</summary>
/// <param name="AspectId">Aspect 标识。</param><param name="Quantity">整数数量。</param><param name="Type">规则化类型。</param><param name="ElementId">目标 Element 标识。</param><param name="ScopeId">唯一 Scope 标识。</param>
public sealed record AddAspectOperation(Guid AspectId, int Quantity, AspectType Type, Guid ElementId, Guid ScopeId) : WorldOperation;

/// <summary>删除规则化 Aspect。</summary>
/// <param name="AspectId">目标 Aspect 标识。</param>
public sealed record RemoveAspectOperation(Guid AspectId) : WorldOperation;

/// <summary>更新规则化 Aspect 数量。</summary>
/// <param name="AspectId">目标 Aspect 标识。</param><param name="Quantity">新整数数量。</param>
public sealed record UpdateAspectQuantityOperation(Guid AspectId, int Quantity) : WorldOperation;

/// <summary>更新规则化 Aspect 类型。</summary>
/// <param name="AspectId">目标 Aspect 标识。</param><param name="Type">新类型。</param>
public sealed record UpdateAspectTypeOperation(Guid AspectId, AspectType Type) : WorldOperation;

/// <summary>添加规则化 Relation。</summary>
/// <param name="RelationId">Relation 标识。</param><param name="Quantity">整数数量。</param><param name="Type">规则化类型。</param><param name="SourceElementId">来源 Element 标识。</param><param name="TargetElementId">目标 Element 标识。</param><param name="ScopeId">唯一 Scope 标识。</param>
public sealed record AddRelationOperation(Guid RelationId, int Quantity, RelationType Type, Guid SourceElementId, Guid TargetElementId, Guid ScopeId) : WorldOperation;

/// <summary>删除规则化 Relation。</summary>
/// <param name="RelationId">目标 Relation 标识。</param>
public sealed record RemoveRelationOperation(Guid RelationId) : WorldOperation;

/// <summary>更新规则化 Relation 数量。</summary>
/// <param name="RelationId">目标 Relation 标识。</param><param name="Quantity">新整数数量。</param>
public sealed record UpdateRelationQuantityOperation(Guid RelationId, int Quantity) : WorldOperation;

/// <summary>更新规则化 Relation 类型。</summary>
/// <param name="RelationId">目标 Relation 标识。</param><param name="Type">新类型。</param>
public sealed record UpdateRelationTypeOperation(Guid RelationId, RelationType Type) : WorldOperation;

/// <summary>添加规则化 Scope。</summary>
/// <param name="ScopeId">Scope 标识。</param><param name="Quantity">整数数量。</param><param name="Type">规则化类型。</param><param name="OwnerElementId">Owner Element 标识。</param>
public sealed record AddScopeOperation(Guid ScopeId, int Quantity, ScopeType Type, Guid OwnerElementId) : WorldOperation;

/// <summary>删除 Scope 及其中全部规则化与 Local 事实。</summary>
/// <param name="ScopeId">目标 Scope 标识。</param>
public sealed record RemoveScopeOperation(Guid ScopeId) : WorldOperation;

/// <summary>更新 Scope 数量。</summary>
/// <param name="ScopeId">目标 Scope 标识。</param><param name="Quantity">新整数数量。</param>
public sealed record UpdateScopeQuantityOperation(Guid ScopeId, int Quantity) : WorldOperation;

/// <summary>更新 Scope 类型。</summary>
/// <param name="ScopeId">目标 Scope 标识。</param><param name="Type">新类型。</param>
public sealed record UpdateScopeTypeOperation(Guid ScopeId, ScopeType Type) : WorldOperation;

/// <summary>添加 LocalAspect。</summary>
/// <param name="LocalAspectId">LocalAspect 标识。</param><param name="Name">自由谓词名称。</param><param name="Description">自由谓词说明。</param><param name="Quantity">整数数量。</param><param name="ElementId">目标 Element 标识。</param><param name="ScopeId">唯一 Scope 标识。</param>
public sealed record AddLocalAspectOperation(Guid LocalAspectId, string Name, string Description, int Quantity, Guid ElementId, Guid ScopeId) : WorldOperation;

/// <summary>删除 LocalAspect。</summary>
/// <param name="LocalAspectId">目标 LocalAspect 标识。</param>
public sealed record RemoveLocalAspectOperation(Guid LocalAspectId) : WorldOperation;

/// <summary>更新 LocalAspect 自由谓词文本和整数数量。</summary>
/// <param name="LocalAspectId">目标 LocalAspect 标识。</param><param name="Name">新名称。</param><param name="Description">新说明。</param><param name="Quantity">新整数数量。</param>
public sealed record UpdateLocalAspectOperation(Guid LocalAspectId, string Name, string Description, int Quantity) : WorldOperation;

/// <summary>添加 LocalRelation。</summary>
/// <param name="LocalRelationId">LocalRelation 标识。</param><param name="Name">自由谓词名称。</param><param name="Description">自由谓词说明。</param><param name="Quantity">整数数量。</param><param name="SourceElementId">来源 Element 标识。</param><param name="TargetElementId">目标 Element 标识。</param><param name="ScopeId">唯一 Scope 标识。</param>
public sealed record AddLocalRelationOperation(Guid LocalRelationId, string Name, string Description, int Quantity, Guid SourceElementId, Guid TargetElementId, Guid ScopeId) : WorldOperation;

/// <summary>删除 LocalRelation。</summary>
/// <param name="LocalRelationId">目标 LocalRelation 标识。</param>
public sealed record RemoveLocalRelationOperation(Guid LocalRelationId) : WorldOperation;

/// <summary>更新 LocalRelation 自由谓词文本和整数数量。</summary>
/// <param name="LocalRelationId">目标 LocalRelation 标识。</param><param name="Name">新名称。</param><param name="Description">新说明。</param><param name="Quantity">新整数数量。</param>
public sealed record UpdateLocalRelationOperation(Guid LocalRelationId, string Name, string Description, int Quantity) : WorldOperation;

/// <summary>提供创建常用 World 操作和原子操作组的工厂。</summary>
public static class WorldOperations
{
    /// <summary>创建添加 Element 的操作并分配标识。</summary>
    public static AddElementOperation AddElement(string name, string description, ElementType type) => new(Guid.NewGuid(), name, description, type);

    /// <summary>创建添加规则化 Aspect 的操作并分配标识。</summary>
    public static AddAspectOperation AddAspect(int quantity, AspectType type, Guid elementId, Guid scopeId) => new(Guid.NewGuid(), quantity, type, elementId, scopeId);

    /// <summary>创建添加规则化 Relation 的操作并分配标识。</summary>
    public static AddRelationOperation AddRelation(int quantity, RelationType type, Guid sourceElementId, Guid targetElementId, Guid scopeId) => new(Guid.NewGuid(), quantity, type, sourceElementId, targetElementId, scopeId);

    /// <summary>创建添加 Scope 的操作并分配标识。</summary>
    public static AddScopeOperation AddScope(int quantity, ScopeType type, Guid ownerElementId) => new(Guid.NewGuid(), quantity, type, ownerElementId);

    /// <summary>创建添加 LocalAspect 的操作并分配标识。</summary>
    public static AddLocalAspectOperation AddLocalAspect(string name, string description, int quantity, Guid elementId, Guid scopeId) => new(Guid.NewGuid(), name, description, quantity, elementId, scopeId);

    /// <summary>创建添加 LocalRelation 的操作并分配标识。</summary>
    public static AddLocalRelationOperation AddLocalRelation(string name, string description, int quantity, Guid sourceElementId, Guid targetElementId, Guid scopeId) => new(Guid.NewGuid(), name, description, quantity, sourceElementId, targetElementId, scopeId);

    /// <summary>按给定顺序组合一个原子操作组。</summary>
    public static WorldChangeSet Combine(params WorldOperation[] operations) => new(operations);

    /// <summary>将单项操作包装为原子操作组。</summary>
    public static WorldChangeSet Single(WorldOperation operation) => new([operation]);
}

/// <summary>表示按确定顺序原子执行的不可变 World 操作组。</summary>
public sealed class WorldChangeSet
{
    /// <summary>复制给定操作序列并创建操作组。</summary>
    public WorldChangeSet(IEnumerable<WorldOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        WorldOperation[] copied = operations.ToArray();
        if (copied.Any(operation => operation is null))
            throw new ArgumentException("World 操作组不能包含 null。", nameof(operations));
        Operations = Array.AsReadOnly(copied);
    }

    /// <summary>获取按执行顺序排列的操作。</summary>
    public IReadOnlyList<WorldOperation> Operations { get; }

    /// <summary>获取操作组是否为空。</summary>
    public bool IsEmpty => Operations.Count == 0;
}

/// <summary>保存一次成功提交实际执行的正向操作和可精确撤销的反向操作。</summary>
/// <param name="Forward">正向操作。</param><param name="Inverse">逆向操作。</param>
public sealed record AppliedWorldChangeSet(WorldChangeSet Forward, WorldChangeSet Inverse);

/// <summary>表示 World 操作失败且内部回滚也未能完整恢复内存状态。</summary>
public sealed class WorldTransactionException : TaviException
{
    /// <summary>使用原始操作异常和回滚异常创建异常。</summary>
    public WorldTransactionException(Exception operationException, Exception rollbackException) : base(WorldErrorCodes.TransactionRollbackFailed, TaviErrorCategory.InternalFailure, "世界事务执行失败，且内部回滚未能完整恢复状态。", nameof(World.Apply), false, null, new AggregateException(operationException, rollbackException))
    {
        OperationException = operationException;
        RollbackException = rollbackException;
    }

    /// <summary>获取触发回滚的原始异常。</summary>
    public Exception OperationException { get; }

    /// <summary>获取回滚期间产生的异常。</summary>
    public Exception RollbackException { get; }
}
