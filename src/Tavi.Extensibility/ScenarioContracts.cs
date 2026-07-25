namespace Tavi.Extensibility;

/// <summary>提供给 Plugin 的只读 Scenario Element 数据。</summary>
/// <param name="Id">Element 标识。</param><param name="Name">名称。</param><param name="Description">说明。</param><param name="Type">开放类型。</param>
public sealed record ScenarioElementView(Guid Id, string Name, string Description, SemanticKey Type);

/// <summary>提供给 Plugin 的只读 Scenario Aspect 数据。</summary>
/// <param name="Id">Aspect 标识。</param><param name="Name">名称。</param><param name="Description">说明。</param><param name="Quantity">有限强度。</param><param name="Type">开放类型。</param><param name="ElementId">目标 Element。</param><param name="ScopeId">唯一 Scope。</param>
public sealed record ScenarioAspectView(Guid Id, string Name, string Description, double Quantity, SemanticKey Type, Guid ElementId, Guid ScopeId);

/// <summary>提供给 Plugin 的只读 Scenario Relation 数据。</summary>
/// <param name="Id">Relation 标识。</param><param name="Name">名称。</param><param name="Description">说明。</param><param name="Quantity">有限强度。</param><param name="Type">开放类型。</param><param name="SourceElementId">来源 Element。</param><param name="TargetElementId">目标 Element。</param><param name="ScopeId">唯一 Scope。</param>
public sealed record ScenarioRelationView(Guid Id, string Name, string Description, double Quantity, SemanticKey Type, Guid SourceElementId, Guid TargetElementId, Guid ScopeId);

/// <summary>提供给 Plugin 的只读 Scenario Scope 数据。</summary>
/// <param name="Id">Scope 标识。</param><param name="Name">名称。</param><param name="Description">说明。</param><param name="Quantity">有限强度。</param><param name="Type">开放类型。</param><param name="OwnerElementId">Owner Element。</param>
public sealed record ScenarioScopeView(Guid Id, string Name, string Description, double Quantity, SemanticKey Type, Guid OwnerElementId);

/// <summary>提供给 Plugin 的只读 Scene 槽位绑定数据。</summary>
/// <param name="SlotId">槽位标识。</param><param name="ElementIds">按绑定顺序排列的 Element 标识。</param>
public sealed record SceneSlotBindingView(string SlotId, IReadOnlyList<Guid> ElementIds);

/// <summary>提供给 Plugin 的只读活跃 Scene 数据。</summary>
/// <param name="Id">Scene 标识。</param><param name="DefinitionId">SceneDefinition 键。</param><param name="Module">所属 Module。</param><param name="ModuleVersion">Module 版本。</param><param name="BasedOnScenarioStateId">定义依据的 StateId。</param><param name="State">功能生命周期状态。</param><param name="Bindings">槽位绑定。</param>
public sealed record ScenarioSceneView(Guid Id, SemanticKey DefinitionId, ModuleId Module, ModuleVersion ModuleVersion, Guid BasedOnScenarioStateId, string State, IReadOnlyList<SceneSlotBindingView> Bindings);

/// <summary>表示与运行中 Scenario 完全隔离的只读 Plugin 视图。</summary>
public interface IScenarioView
{
    /// <summary>获取当前 Scenario StateId。</summary>
    Guid StateId { get; }

    /// <summary>获取 Scenario 所基于的 World StateId。</summary>
    Guid SourceWorldStateId { get; }

    /// <summary>获取全部 Element。</summary>
    IReadOnlyCollection<ScenarioElementView> Elements { get; }

    /// <summary>获取全部 Aspect。</summary>
    IReadOnlyCollection<ScenarioAspectView> Aspects { get; }

    /// <summary>获取全部 Relation。</summary>
    IReadOnlyCollection<ScenarioRelationView> Relations { get; }

    /// <summary>获取全部 Scope。</summary>
    IReadOnlyCollection<ScenarioScopeView> Scopes { get; }

    /// <summary>获取全部活跃 Scene。</summary>
    IReadOnlyCollection<ScenarioSceneView> Scenes { get; }
}

/// <summary>描述 Plugin 希望由 Evolution 校验并提交的一项 Scenario 修改意图。</summary>
public abstract record ScenarioOperationIntent
{
    private ScenarioOperationIntent() { }

    /// <summary>建议添加 Element。</summary>
    public sealed record AddElement(Guid Id, string Name, string Description, SemanticKey Type) : ScenarioOperationIntent;

    /// <summary>建议删除 Element 及其结构依赖。</summary>
    public sealed record RemoveElement(Guid Id) : ScenarioOperationIntent;

    /// <summary>建议更新 Element 的可变语义属性。</summary>
    public sealed record UpdateElement(Guid Id, string Name, string Description, SemanticKey Type) : ScenarioOperationIntent;

    /// <summary>建议添加 Scope。</summary>
    public sealed record AddScope(Guid Id, string Name, string Description, double Quantity, SemanticKey Type, Guid OwnerElementId) : ScenarioOperationIntent;

    /// <summary>建议删除 Scope 及其中断言。</summary>
    public sealed record RemoveScope(Guid Id) : ScenarioOperationIntent;

    /// <summary>建议更新 Scope 的可变语义属性。</summary>
    public sealed record UpdateScope(Guid Id, string Name, string Description, double Quantity, SemanticKey Type) : ScenarioOperationIntent;

    /// <summary>建议添加 Aspect。</summary>
    public sealed record AddAspect(Guid Id, string Name, string Description, double Quantity, SemanticKey Type, Guid ElementId, Guid ScopeId) : ScenarioOperationIntent;

    /// <summary>建议删除 Aspect。</summary>
    public sealed record RemoveAspect(Guid Id) : ScenarioOperationIntent;

    /// <summary>建议更新 Aspect 的可变语义属性。</summary>
    public sealed record UpdateAspect(Guid Id, string Name, string Description, double Quantity, SemanticKey Type) : ScenarioOperationIntent;

    /// <summary>建议添加 Relation。</summary>
    public sealed record AddRelation(Guid Id, string Name, string Description, double Quantity, SemanticKey Type, Guid SourceElementId, Guid TargetElementId, Guid ScopeId) : ScenarioOperationIntent;

    /// <summary>建议删除 Relation。</summary>
    public sealed record RemoveRelation(Guid Id) : ScenarioOperationIntent;

    /// <summary>建议更新 Relation 的可变语义属性。</summary>
    public sealed record UpdateRelation(Guid Id, string Name, string Description, double Quantity, SemanticKey Type) : ScenarioOperationIntent;
}

/// <summary>表示 Plugin 对 Scenario 的完整结构化修改提案。</summary>
public sealed record ScenarioChangeProposal
{
    /// <summary>获取产生该提案时依据的 Scenario StateId。</summary>
    public required Guid ExpectedScenarioStateId { get; init; }

    /// <summary>获取按执行顺序排列的修改意图。</summary>
    public IReadOnlyList<ScenarioOperationIntent> Operations { get; init; } = [];

    /// <summary>获取供诊断、作者或玩家理解的理由；Evolution 不解析该文本。</summary>
    public string Rationale { get; init; } = string.Empty;
}
