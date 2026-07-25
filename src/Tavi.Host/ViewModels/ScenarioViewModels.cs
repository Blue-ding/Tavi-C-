namespace Tavi.Host.ViewModels;

/// <summary>表示独立 Scenario 页面需要的完整权威状态。</summary>
/// <param name="StateId">当前 Scenario 状态标识。</param><param name="SourceWorldStateId">Scenario 创建时依据的 World 状态标识。</param><param name="IsDirty">是否存在未保存修改。</param><param name="CanUndo">是否可以撤销。</param><param name="CanRedo">是否可以重做。</param><param name="Health">Scenario 会话健康状态。</param><param name="Modules">冻结的 Module 引用。</param><param name="Elements">全部 Element。</param><param name="Aspects">全部 Aspect。</param><param name="Relations">全部 Relation。</param><param name="Scopes">全部 Scope。</param><param name="Definitions">当前可用 SceneDefinition。</param><param name="Scenes">全部 Scene。</param>
public sealed record ScenarioWorkspaceViewModel(Guid StateId, Guid SourceWorldStateId, bool IsDirty, bool CanUndo, bool CanRedo, string Health, IReadOnlyList<ScenarioModuleViewModel> Modules, IReadOnlyList<ElementViewModel> Elements, IReadOnlyList<AspectViewModel> Aspects, IReadOnlyList<RelationViewModel> Relations, IReadOnlyList<ScopeViewModel> Scopes, IReadOnlyList<SceneDefinitionViewModel> Definitions, IReadOnlyList<SceneViewModel> Scenes);

/// <summary>表示 Scenario 冻结的一个 Module 身份与版本。</summary>
/// <param name="Id">Module 标识。</param><param name="Version">兼容版本。</param>
public sealed record ScenarioModuleViewModel(string Id, string Version);

/// <summary>表示前端可以实例化的 SceneDefinition。</summary>
/// <param name="Id">稳定 Definition 键。</param><param name="Module">所属 Module。</param><param name="ModuleVersion">Module 版本。</param><param name="Name">显示名称。</param><param name="Description">说明。</param><param name="Settlement">可用结算路径。</param><param name="Slots">槽位定义。</param>
public sealed record SceneDefinitionViewModel(string Id, string Module, string ModuleVersion, string Name, string Description, IReadOnlyList<string> Settlement, IReadOnlyList<SceneSlotViewModel> Slots);

/// <summary>表示 SceneDefinition 或 Scene 中的槽位及当前绑定。</summary>
/// <param name="Id">槽位标识。</param><param name="Name">显示名称。</param><param name="Description">说明。</param><param name="Minimum">最少绑定数。</param><param name="Maximum">最多绑定数。</param><param name="ElementTypes">允许的 Element 类型。</param><param name="RequiredAspectGroups">必须具备的 Aspect Group。</param><param name="ElementIds">当前绑定的 Element 标识。</param>
public sealed record SceneSlotViewModel(string Id, string Name, string Description, int Minimum, int? Maximum, IReadOnlyList<string> ElementTypes, IReadOnlyList<string> RequiredAspectGroups, IReadOnlyList<Guid> ElementIds);

/// <summary>表示 Scenario 页面中的一个持久化 Scene。</summary>
/// <param name="Id">Scene 标识。</param><param name="DefinitionId">Definition 稳定键。</param><param name="Module">所属 Module。</param><param name="ModuleVersion">Module 版本。</param><param name="Name">显示名称。</param><param name="Description">说明。</param><param name="State">生命周期状态。</param><param name="Settlement">可用结算路径。</param><param name="DefinitionFrozen">是否保存了完整槽位定义。</param><param name="Slots">冻结槽位及绑定。</param>
public sealed record SceneViewModel(Guid Id, string DefinitionId, string Module, string ModuleVersion, string Name, string Description, string State, IReadOnlyList<string> Settlement, bool DefinitionFrozen, IReadOnlyList<SceneSlotViewModel> Slots);

/// <summary>表示依据当前状态创建 Scene 的请求。</summary>
/// <param name="ExpectedStateId">调用方观察到的 Scenario 状态。</param><param name="DefinitionId">需要实例化的 Definition 键。</param><param name="RandomSeed">动态 Definition 的可复现随机种子。</param>
public sealed record CreateSceneRequest(Guid ExpectedStateId, string DefinitionId, long RandomSeed);

/// <summary>表示替换一个 Scene 槽位绑定的请求。</summary>
/// <param name="ExpectedStateId">调用方观察到的 Scenario 状态。</param><param name="ElementIds">按绑定顺序排列的 Element 标识。</param>
public sealed record SetSceneBindingRequest(Guid ExpectedStateId, IReadOnlyList<Guid> ElementIds);

/// <summary>表示依赖当前 Scenario 状态的请求。</summary>
/// <param name="ExpectedStateId">调用方观察到的 Scenario 状态。</param>
public sealed record ScenarioStateRequest(Guid ExpectedStateId);

/// <summary>表示使用 Module 规则结算 Scene 的请求。</summary>
/// <param name="ExpectedStateId">调用方观察到的 Scenario 状态。</param><param name="RandomSeed">规则结算使用的可复现随机种子。</param>
public sealed record SettleSceneRequest(Guid ExpectedStateId, long RandomSeed);
