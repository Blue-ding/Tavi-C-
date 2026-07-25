namespace Tavi.Domain.Scenario;

/// <summary>描述一项不可变的 Scenario 修改意图。</summary>
public abstract record ScenarioOperation;

/// <summary>添加 Element。</summary>
/// <param name="ElementId">Element 标识。</param><param name="Name">名称。</param><param name="Description">说明。</param><param name="Type">开放类型。</param>
public sealed record AddElementOperation(Guid ElementId, string Name, string Description, ElementType Type) : ScenarioOperation;

/// <summary>删除 Element 及其结构依赖；Binding Scene 会解绑该 Element，Settled Scene 保留历史绑定。</summary>
/// <param name="ElementId">目标 Element 标识。</param>
public sealed record RemoveElementOperation(Guid ElementId) : ScenarioOperation;

/// <summary>更新 Element 的可变语义属性。</summary>
/// <param name="ElementId">目标 Element。</param><param name="Name">新名称。</param><param name="Description">新说明。</param><param name="Type">新类型。</param>
public sealed record UpdateElementOperation(Guid ElementId, string Name, string Description, ElementType Type) : ScenarioOperation;

/// <summary>添加 Scope。</summary>
/// <param name="ScopeId">Scope 标识。</param><param name="Quantity">整数数量。</param><param name="Type">开放类型。</param><param name="OwnerElementId">Owner Element。</param>
public sealed record AddScopeOperation(Guid ScopeId, int Quantity, ScopeType Type, Guid OwnerElementId) : ScenarioOperation;

/// <summary>删除 Scope 及其中全部断言。</summary>
/// <param name="ScopeId">目标 Scope 标识。</param>
public sealed record RemoveScopeOperation(Guid ScopeId) : ScenarioOperation;

/// <summary>更新 Scope 的可变语义属性。</summary>
/// <param name="ScopeId">目标 Scope。</param><param name="Quantity">新整数数量。</param><param name="Type">新类型。</param>
public sealed record UpdateScopeOperation(Guid ScopeId, int Quantity, ScopeType Type) : ScenarioOperation;

/// <summary>添加 Aspect。</summary>
/// <param name="AspectId">Aspect 标识。</param><param name="Quantity">整数数量。</param><param name="Type">开放类型。</param><param name="ElementId">目标 Element。</param><param name="ScopeId">唯一 Scope。</param>
public sealed record AddAspectOperation(Guid AspectId, int Quantity, AspectType Type, Guid ElementId, Guid ScopeId) : ScenarioOperation;

/// <summary>删除 Aspect。</summary>
/// <param name="AspectId">目标 Aspect 标识。</param>
public sealed record RemoveAspectOperation(Guid AspectId) : ScenarioOperation;

/// <summary>更新 Aspect 的可变语义属性。</summary>
/// <param name="AspectId">目标 Aspect。</param><param name="Quantity">新整数数量。</param><param name="Type">新类型。</param>
public sealed record UpdateAspectOperation(Guid AspectId, int Quantity, AspectType Type) : ScenarioOperation;

/// <summary>添加 Relation。</summary>
/// <param name="RelationId">Relation 标识。</param><param name="Quantity">整数数量。</param><param name="Type">开放类型。</param><param name="SourceElementId">来源 Element。</param><param name="TargetElementId">目标 Element。</param><param name="ScopeId">唯一 Scope。</param>
public sealed record AddRelationOperation(Guid RelationId, int Quantity, RelationType Type, Guid SourceElementId, Guid TargetElementId, Guid ScopeId) : ScenarioOperation;

/// <summary>删除 Relation。</summary>
/// <param name="RelationId">目标 Relation 标识。</param>
public sealed record RemoveRelationOperation(Guid RelationId) : ScenarioOperation;

/// <summary>更新 Relation 的可变语义属性。</summary>
/// <param name="RelationId">目标 Relation。</param><param name="Quantity">新整数数量。</param><param name="Type">新类型。</param>
public sealed record UpdateRelationOperation(Guid RelationId, int Quantity, RelationType Type) : ScenarioOperation;

/// <summary>添加由 SceneDefinition 实例化的 Scene。</summary>
/// <param name="SceneId">Scene 标识。</param><param name="DefinitionId">定义键。</param><param name="ModuleId">Module 标识。</param><param name="ModuleVersion">Module 版本。</param><param name="BasedOnScenarioStateId">定义依据的 StateId。</param><param name="Name">名称。</param><param name="Description">说明。</param><param name="SettlementOptions">结算能力。</param><param name="Slots">创建时冻结的槽位要求。</param>
public sealed record AddSceneOperation(Guid SceneId, SceneDefinitionType DefinitionId, string ModuleId, string ModuleVersion, Guid BasedOnScenarioStateId, string Name, string Description, SceneSettlementOptions SettlementOptions, IReadOnlyList<SceneSlotSpecification>? Slots = null) : ScenarioOperation;

/// <summary>删除 Scene。</summary>
/// <param name="SceneId">目标 Scene 标识。</param>
public sealed record RemoveSceneOperation(Guid SceneId) : ScenarioOperation;

/// <summary>替换 Scene 中一个槽位的全部 Element 绑定。</summary>
/// <param name="SceneId">目标 Scene。</param><param name="Binding">独立槽位绑定。</param>
public sealed record SetSceneSlotBindingOperation(Guid SceneId, SceneSlotBinding Binding) : ScenarioOperation;

/// <summary>清除 Scene 中一个槽位的绑定。</summary>
/// <param name="SceneId">目标 Scene。</param><param name="SlotId">槽位标识。</param>
public sealed record ClearSceneSlotBindingOperation(Guid SceneId, string SlotId) : ScenarioOperation;

/// <summary>更新 Scene 功能生命周期状态。</summary>
/// <param name="SceneId">目标 Scene。</param><param name="State">新状态。</param>
public sealed record UpdateSceneStateOperation(Guid SceneId, SceneState State) : ScenarioOperation;

/// <summary>删除 Scenario 中全部已结算 Scene；该操作不影响 Element 或 EARS 数据。</summary>
public sealed record ClearSettledScenesOperation : ScenarioOperation;

internal sealed record RestoreScenarioSnapshotOperation(ScenarioSnapshot Snapshot) : ScenarioOperation;

/// <summary>表示按确定顺序原子执行的不可变 Scenario 操作组。</summary>
public sealed class ScenarioChangeSet
{
    /// <summary>复制给定操作序列并创建操作组。</summary>
    public ScenarioChangeSet(IEnumerable<ScenarioOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ScenarioOperation[] copied = operations.ToArray();
        if (copied.Any(operation => operation is null))
            throw new ArgumentException("Scenario 操作组不能包含 null。", nameof(operations));
        Operations = Array.AsReadOnly(copied);
    }

    /// <summary>获取按执行顺序排列的操作。</summary>
    public IReadOnlyList<ScenarioOperation> Operations { get; }

    /// <summary>获取操作组是否为空。</summary>
    public bool IsEmpty => Operations.Count == 0;
}

/// <summary>保存一次成功提交的正向操作和可精确恢复前态的反向操作。</summary>
/// <param name="Forward">实际正向操作。</param><param name="Inverse">恢复完整前态的逆操作。</param>
public sealed record AppliedScenarioChangeSet(ScenarioChangeSet Forward, ScenarioChangeSet Inverse);
