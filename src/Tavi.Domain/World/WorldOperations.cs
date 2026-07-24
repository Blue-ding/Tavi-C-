namespace Tavi.Domain.World;

/// <summary>描述一项不可变的 World 修改意图；操作本身不持有 World，也不能自行执行。</summary>
public abstract record WorldOperation;

/// <summary>添加具有确定标识的 Element。</summary>
/// <param name="ElementId">新 Element 标识。</param><param name="Name">名称。</param><param name="Description">说明。</param><param name="Type">开放类型。</param>
public sealed record AddElementOperation(Guid ElementId, string Name, string Description, ElementType Type) : WorldOperation;

/// <summary>删除 Element 及所有直接或经其持有 Scope 依赖它的断言。</summary>
/// <param name="ElementId">目标 Element 标识。</param>
public sealed record RemoveElementOperation(Guid ElementId) : WorldOperation;

/// <summary>更新 Element 名称。</summary>
/// <param name="ElementId">目标 Element 标识。</param><param name="Name">新名称。</param>
public sealed record UpdateElementNameOperation(Guid ElementId, string Name) : WorldOperation;

/// <summary>更新 Element 说明。</summary>
/// <param name="ElementId">目标 Element 标识。</param><param name="Description">新说明。</param>
public sealed record UpdateElementDescriptionOperation(Guid ElementId, string Description) : WorldOperation;

/// <summary>更新 Element 开放类型。</summary>
/// <param name="ElementId">目标 Element 标识。</param><param name="Type">新开放类型。</param>
public sealed record UpdateElementTypeOperation(Guid ElementId, ElementType Type) : WorldOperation;

/// <summary>在唯一 Scope 中添加针对一个 Element 的一元断言。</summary>
/// <param name="AspectId">新 Aspect 标识。</param><param name="Name">名称。</param><param name="Description">说明。</param><param name="Quantity">有限强度。</param><param name="Type">开放类型。</param><param name="ElementId">目标 Element 标识。</param><param name="ScopeId">唯一所属 Scope 标识。</param>
public sealed record AddAspectOperation(Guid AspectId, string Name, string Description, double Quantity, AspectType Type, Guid ElementId, Guid ScopeId) : WorldOperation;

/// <summary>删除一元断言。</summary>
/// <param name="AspectId">目标 Aspect 标识。</param>
public sealed record RemoveAspectOperation(Guid AspectId) : WorldOperation;

/// <summary>更新 Aspect 名称。</summary>
/// <param name="AspectId">目标 Aspect 标识。</param><param name="Name">新名称。</param>
public sealed record UpdateAspectNameOperation(Guid AspectId, string Name) : WorldOperation;

/// <summary>更新 Aspect 说明。</summary>
/// <param name="AspectId">目标 Aspect 标识。</param><param name="Description">新说明。</param>
public sealed record UpdateAspectDescriptionOperation(Guid AspectId, string Description) : WorldOperation;

/// <summary>更新由对应 Module 解释的 Aspect 强度。</summary>
/// <param name="AspectId">目标 Aspect 标识。</param><param name="Quantity">新有限强度。</param>
public sealed record UpdateAspectQuantityOperation(Guid AspectId, double Quantity) : WorldOperation;

/// <summary>更新 Aspect 开放类型。</summary>
/// <param name="AspectId">目标 Aspect 标识。</param><param name="Type">新开放类型。</param>
public sealed record UpdateAspectTypeOperation(Guid AspectId, AspectType Type) : WorldOperation;

/// <summary>在唯一 Scope 中添加两个 Element 之间的有向二元断言。</summary>
/// <param name="RelationId">新 Relation 标识。</param><param name="Name">名称。</param><param name="Description">说明。</param><param name="Quantity">有限强度。</param><param name="Type">开放类型。</param><param name="SourceElementId">来源 Element 标识。</param><param name="TargetElementId">目标 Element 标识。</param><param name="ScopeId">唯一所属 Scope 标识。</param>
public sealed record AddRelationOperation(Guid RelationId, string Name, string Description, double Quantity, RelationType Type, Guid SourceElementId, Guid TargetElementId, Guid ScopeId) : WorldOperation;

/// <summary>删除二元断言。</summary>
/// <param name="RelationId">目标 Relation 标识。</param>
public sealed record RemoveRelationOperation(Guid RelationId) : WorldOperation;

/// <summary>更新 Relation 名称。</summary>
/// <param name="RelationId">目标 Relation 标识。</param><param name="Name">新名称。</param>
public sealed record UpdateRelationNameOperation(Guid RelationId, string Name) : WorldOperation;

/// <summary>更新 Relation 说明。</summary>
/// <param name="RelationId">目标 Relation 标识。</param><param name="Description">新说明。</param>
public sealed record UpdateRelationDescriptionOperation(Guid RelationId, string Description) : WorldOperation;

/// <summary>更新由对应 Module 解释的 Relation 强度。</summary>
/// <param name="RelationId">目标 Relation 标识。</param><param name="Quantity">新有限强度。</param>
public sealed record UpdateRelationQuantityOperation(Guid RelationId, double Quantity) : WorldOperation;

/// <summary>更新 Relation 开放类型。</summary>
/// <param name="RelationId">目标 Relation 标识。</param><param name="Type">新开放类型。</param>
public sealed record UpdateRelationTypeOperation(Guid RelationId, RelationType Type) : WorldOperation;

/// <summary>添加由一个 Element 持有的独立断言域。</summary>
/// <param name="ScopeId">新 Scope 标识。</param><param name="Name">名称。</param><param name="Description">说明。</param><param name="Quantity">有限强度。</param><param name="Type">开放类型。</param><param name="OwnerElementId">Owner Element 标识。</param>
public sealed record AddScopeOperation(Guid ScopeId, string Name, string Description, double Quantity, ScopeType Type, Guid OwnerElementId) : WorldOperation;

/// <summary>删除 Scope 及其中的全部 Aspect 和 Relation。</summary>
/// <param name="ScopeId">目标 Scope 标识。</param>
public sealed record RemoveScopeOperation(Guid ScopeId) : WorldOperation;

/// <summary>更新 Scope 名称。</summary>
/// <param name="ScopeId">目标 Scope 标识。</param><param name="Name">新名称。</param>
public sealed record UpdateScopeNameOperation(Guid ScopeId, string Name) : WorldOperation;

/// <summary>更新 Scope 说明。</summary>
/// <param name="ScopeId">目标 Scope 标识。</param><param name="Description">新说明。</param>
public sealed record UpdateScopeDescriptionOperation(Guid ScopeId, string Description) : WorldOperation;

/// <summary>更新由对应 Module 解释的 Scope 强度。</summary>
/// <param name="ScopeId">目标 Scope 标识。</param><param name="Quantity">新有限强度。</param>
public sealed record UpdateScopeQuantityOperation(Guid ScopeId, double Quantity) : WorldOperation;

/// <summary>更新 Scope 开放类型。</summary>
/// <param name="ScopeId">目标 Scope 标识。</param><param name="Type">新开放类型。</param>
public sealed record UpdateScopeTypeOperation(Guid ScopeId, ScopeType Type) : WorldOperation;

/// <summary>提供创建常用 World 操作和原子操作组的工厂。</summary>
public static class WorldOperations
{
    /// <summary>创建添加 Element 的操作并分配标识。</summary>
    /// <param name="name">Element 名称。</param><param name="description">Element 说明。</param><param name="type">开放 ElementType。</param><returns>具有新标识的添加操作。</returns>
    public static AddElementOperation AddElement(string name, string description, ElementType type) => new(Guid.NewGuid(), name, description, type);

    /// <summary>创建添加 Aspect 的操作并分配标识。</summary>
    /// <param name="name">Aspect 名称。</param><param name="description">Aspect 说明。</param><param name="quantity">有限强度。</param><param name="type">开放 AspectType。</param><param name="elementId">目标 Element 标识。</param><param name="scopeId">唯一所属 Scope 标识。</param><returns>具有新标识的添加操作。</returns>
    public static AddAspectOperation AddAspect(string name, string description, double quantity, AspectType type, Guid elementId, Guid scopeId) => new(Guid.NewGuid(), name, description, quantity, type, elementId, scopeId);

    /// <summary>创建添加 Relation 的操作并分配标识。</summary>
    /// <param name="name">Relation 名称。</param><param name="description">Relation 说明。</param><param name="quantity">有限强度。</param><param name="type">开放 RelationType。</param><param name="sourceElementId">来源 Element 标识。</param><param name="targetElementId">目标 Element 标识。</param><param name="scopeId">唯一所属 Scope 标识。</param><returns>具有新标识的添加操作。</returns>
    public static AddRelationOperation AddRelation(string name, string description, double quantity, RelationType type, Guid sourceElementId, Guid targetElementId, Guid scopeId) => new(Guid.NewGuid(), name, description, quantity, type, sourceElementId, targetElementId, scopeId);

    /// <summary>创建添加 Scope 的操作并分配标识。</summary>
    /// <param name="name">Scope 名称。</param><param name="description">Scope 说明。</param><param name="quantity">有限强度。</param><param name="type">开放 ScopeType。</param><param name="ownerElementId">Owner Element 标识。</param><returns>具有新标识的添加操作。</returns>
    public static AddScopeOperation AddScope(string name, string description, double quantity, ScopeType type, Guid ownerElementId) => new(Guid.NewGuid(), name, description, quantity, type, ownerElementId);

    /// <summary>按给定顺序组合一个原子操作组。</summary>
    /// <param name="operations">按执行顺序排列的操作。</param><returns>原子操作组。</returns>
    public static WorldChangeSet Combine(params WorldOperation[] operations) => new(operations);

    /// <summary>将单项操作包装为原子操作组。</summary>
    /// <param name="operation">唯一操作。</param><returns>单项原子操作组。</returns>
    public static WorldChangeSet Single(WorldOperation operation) => new([operation]);
}

/// <summary>表示按确定顺序原子执行的不可变 World 操作组。</summary>
public sealed class WorldChangeSet
{
    /// <summary>复制给定操作序列并创建操作组。</summary>
    /// <param name="operations">按执行顺序排列且不能包含 null 的操作序列。</param>
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
/// <param name="Forward">按原始执行顺序排列的正向操作。</param>
/// <param name="Inverse">按撤销执行顺序排列的反向操作。</param>
public sealed record AppliedWorldChangeSet(WorldChangeSet Forward, WorldChangeSet Inverse);

/// <summary>表示 World 操作失败且内部回滚也未能完整恢复内存状态。</summary>
public sealed class WorldTransactionException : TaviException
{
    /// <summary>使用原始操作异常和回滚异常创建不可恢复的事务异常。</summary>
    /// <param name="operationException">触发回滚的原始操作异常。</param>
    /// <param name="rollbackException">回滚期间产生的异常。</param>
    public WorldTransactionException(Exception operationException, Exception rollbackException)
        : base(WorldErrorCodes.TransactionRollbackFailed, TaviErrorCategory.InternalFailure, "世界事务执行失败，且内部回滚未能完整恢复状态。", nameof(World.Apply), false, null, new AggregateException(operationException, rollbackException))
    {
        OperationException = operationException;
        RollbackException = rollbackException;
    }

    /// <summary>获取触发回滚的原始异常。</summary>
    public Exception OperationException { get; }

    /// <summary>获取回滚期间产生的异常。</summary>
    public Exception RollbackException { get; }
}
