namespace Tavi.Extensibility;

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
    IReadOnlyCollection<ElementView> Elements { get; }

    /// <summary>获取全部 Aspect。</summary>
    IReadOnlyCollection<AspectView> Aspects { get; }

    /// <summary>获取全部 Relation。</summary>
    IReadOnlyCollection<RelationView> Relations { get; }

    /// <summary>获取全部 Scope。</summary>
    IReadOnlyCollection<ScopeView> Scopes { get; }

    /// <summary>获取全部活跃 Scene。</summary>
    IReadOnlyCollection<ScenarioSceneView> Scenes { get; }
}

/// <summary>表示冻结 Scene 的局部只读视图；其中只包含已绑定 Element 及完全位于局部边界内的 EARS 数据。</summary>
public sealed record SceneContextView
{
    /// <summary>获取创建该局部视图时的 Scenario StateId。</summary>
    public required Guid ScenarioStateId { get; init; }

    /// <summary>获取正在处理的 Scene。</summary>
    public required ScenarioSceneView Scene { get; init; }

    /// <summary>获取处理开始时冻结的内部 Element。</summary>
    public IReadOnlyCollection<ElementView> Elements { get; init; } = [];

    /// <summary>获取由内部 Element 持有的 Scope。</summary>
    public IReadOnlyCollection<ScopeView> Scopes { get; init; } = [];

    /// <summary>获取目标和所属 Scope 均位于局部边界内的 Aspect。</summary>
    public IReadOnlyCollection<AspectView> Aspects { get; init; } = [];

    /// <summary>获取两个端点和所属 Scope 均位于局部边界内的 Relation。</summary>
    public IReadOnlyCollection<RelationView> Relations { get; init; } = [];
}

/// <summary>描述 Module 在冻结 Scene 局部边界内提出的一项结构化结算意图。</summary>
public abstract record SceneOperationIntent
{
    private SceneOperationIntent() { }

    /// <summary>建议创建新的内部 Element；新 Element 可以被同一提案中的后续操作引用。</summary>
    public sealed record AddElement(Guid Id, string Name, string Description, SemanticKey Type) : SceneOperationIntent;

    /// <summary>建议删除一个既有或本提案新建的内部 Element及其局部结构依赖。</summary>
    public sealed record RemoveElement(Guid Id) : SceneOperationIntent;

    /// <summary>建议更新内部 Element 的可变语义属性。</summary>
    public sealed record UpdateElement(Guid Id, string Name, string Description, SemanticKey Type) : SceneOperationIntent;

    /// <summary>建议为内部 Element 添加 Scope。</summary>
    public sealed record AddScope(Guid Id, int Quantity, SemanticKey Type, Guid OwnerElementId) : SceneOperationIntent;

    /// <summary>建议删除 Scope 及其中断言。</summary>
    public sealed record RemoveScope(Guid Id) : SceneOperationIntent;

    /// <summary>建议更新 Scope 的可变语义属性。</summary>
    public sealed record UpdateScope(Guid Id, int Quantity, SemanticKey Type) : SceneOperationIntent;

    /// <summary>建议添加 Aspect。</summary>
    public sealed record AddAspect(Guid Id, int Quantity, SemanticKey Type, Guid ElementId, Guid ScopeId) : SceneOperationIntent;

    /// <summary>建议删除 Aspect。</summary>
    public sealed record RemoveAspect(Guid Id) : SceneOperationIntent;

    /// <summary>建议更新 Aspect 的可变语义属性。</summary>
    public sealed record UpdateAspect(Guid Id, int Quantity, SemanticKey Type) : SceneOperationIntent;

    /// <summary>建议添加端点和所属 Scope 均位于 Scene 局部边界内的 Relation。</summary>
    public sealed record AddRelation(Guid Id, int Quantity, SemanticKey Type, Guid SourceElementId, Guid TargetElementId, Guid ScopeId) : SceneOperationIntent;

    /// <summary>建议删除 Relation。</summary>
    public sealed record RemoveRelation(Guid Id) : SceneOperationIntent;

    /// <summary>建议更新 Relation 的可变语义属性。</summary>
    public sealed record UpdateRelation(Guid Id, int Quantity, SemanticKey Type) : SceneOperationIntent;
}

/// <summary>表示 Module 对一个冻结 Scene 的完整局部结算提案。</summary>
public sealed record SceneSettlementProposal
{
    /// <summary>获取产生该提案时依据的 Scenario StateId。</summary>
    public required Guid ExpectedScenarioStateId { get; init; }

    /// <summary>获取按执行顺序排列且受 Scene 局部边界限制的修改意图。</summary>
    public IReadOnlyList<SceneOperationIntent> Operations { get; init; } = [];

    /// <summary>获取供诊断、作者或玩家理解的理由；ScenarioSession 不解析该文本。</summary>
    public string Rationale { get; init; } = string.Empty;
}
