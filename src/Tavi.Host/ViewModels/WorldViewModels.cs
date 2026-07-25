using System.Text.Json;

namespace Tavi.Host.ViewModels;

/// <summary>表示前端世界断言图所需的完整可观察状态。</summary>
/// <param name="StateId">当前 World 状态标识。</param>
/// <param name="StagingRevision">当前暂存区 revision。</param>
/// <param name="IsDirty">是否包含尚未保存的修改。</param>
/// <param name="CanUndo">是否可以撤销。</param>
/// <param name="CanRedo">是否可以重做。</param>
/// <param name="Health">世界会话健康状态。</param>
/// <param name="Elements">全部 Element。</param>
/// <param name="Aspects">全部一元断言。</param>
/// <param name="Relations">全部有向二元断言。</param>
/// <param name="Scopes">全部断言域。</param>
/// <param name="StagedChanges">全部暂存日志项。</param>
public sealed record WorldGraphViewModel(Guid StateId, long StagingRevision, bool IsDirty, bool CanUndo, bool CanRedo, string Health, IReadOnlyList<ElementViewModel> Elements, IReadOnlyList<AspectViewModel> Aspects, IReadOnlyList<RelationViewModel> Relations, IReadOnlyList<ScopeViewModel> Scopes, IReadOnlyList<WorldStagedChangeViewModel> StagedChanges);

/// <summary>表示一项不可变的 World 暂存日志记录。</summary>
/// <param name="Id">暂存项标识。</param>
/// <param name="Source">Player 或 Guidance。</param>
/// <param name="Status">Valid、Conflict 或 Invalid。</param>
/// <param name="Operation">便于玩家识别的操作说明。</param>
/// <param name="Issue">阻止提交的状态说明。</param>
/// <param name="ConflictingChangeIds">与本项冲突的暂存项标识。</param>
public sealed record WorldStagedChangeViewModel(Guid Id, string Source, string Status, string Operation, string? Issue, IReadOnlyList<Guid> ConflictingChangeIds);

/// <summary>表示追加或清理暂存区后的权威结果。</summary>
/// <param name="AffectedChangeIds">本次影响的暂存项标识。</param>
/// <param name="World">操作后的临时 World 和暂存区。</param>
public sealed record WorldStagingResultViewModel(IReadOnlyList<Guid> AffectedChangeIds, WorldGraphViewModel World);

/// <summary>表示选择暂存项提交的请求。</summary>
/// <param name="ExpectedStateId">调用方观察到的真实 World 状态标识。</param>
/// <param name="ChangeIds">需要原子提交的暂存项标识。</param>
public sealed record CommitStagedRequest(Guid ExpectedStateId, IReadOnlyList<Guid> ChangeIds);

/// <summary>表示前端可调用的一项 Module World Authoring Action。</summary>
/// <param name="Id">稳定 SemanticKey。</param><param name="Name">面向玩家的名称。</param><param name="Description">操作说明。</param><param name="ParameterSchema">JSON Schema 参数定义。</param>
public sealed record WorldAuthoringActionViewModel(string Id, string Name, string Description, string ParameterSchema);

/// <summary>表示调用 Module World Authoring Action 的请求。</summary>
/// <param name="ExpectedStateId">调用方观察到的 World 状态标识。</param><param name="Arguments">符合 Action Schema 的 JSON 参数。</param>
public sealed record InvokeWorldAuthoringActionRequest(Guid ExpectedStateId, JsonElement Arguments);

/// <summary>表示世界断言图中的 Element。</summary>
/// <param name="Id">Element 标识。</param>
/// <param name="Name">Element 名称。</param>
/// <param name="Description">Element 说明。</param>
/// <param name="Type">开放 ElementType 文本。</param>
public sealed record ElementViewModel(Guid Id, string Name, string Description, string Type);

/// <summary>表示唯一属于一个 Scope 的一元断言。</summary>
/// <param name="Id">Aspect 标识。</param>
/// <param name="Name">Aspect 名称。</param>
/// <param name="Description">Aspect 说明。</param>
/// <param name="Quantity">由对应 Module 解释的有限强度。</param>
/// <param name="Type">开放 AspectType 文本。</param>
/// <param name="ElementId">目标 Element 标识。</param>
/// <param name="ScopeId">唯一所属 Scope 标识。</param>
public sealed record AspectViewModel(Guid Id, string Name, string Description, double Quantity, string Type, Guid ElementId, Guid ScopeId);

/// <summary>表示唯一属于一个 Scope 的有向二元断言。</summary>
/// <param name="Id">Relation 标识。</param>
/// <param name="Name">Relation 名称。</param>
/// <param name="Description">Relation 说明。</param>
/// <param name="Quantity">由对应 Module 解释的有限强度。</param>
/// <param name="Type">开放 RelationType 文本。</param>
/// <param name="SourceElementId">来源 Element 标识。</param>
/// <param name="TargetElementId">目标 Element 标识。</param>
/// <param name="ScopeId">唯一所属 Scope 标识。</param>
public sealed record RelationViewModel(Guid Id, string Name, string Description, double Quantity, string Type, Guid SourceElementId, Guid TargetElementId, Guid ScopeId);

/// <summary>表示由一个 Element 持有的独立断言域。</summary>
/// <param name="Id">Scope 标识。</param>
/// <param name="Name">Scope 名称。</param>
/// <param name="Description">Scope 说明。</param>
/// <param name="Quantity">由对应 Module 解释的有限强度。</param>
/// <param name="Type">开放 ScopeType 文本。</param>
/// <param name="OwnerElementId">Owner Element 标识。</param>
public sealed record ScopeViewModel(Guid Id, string Name, string Description, double Quantity, string Type, Guid OwnerElementId);

/// <summary>表示一次世界提交的展示层结果。</summary>
/// <param name="CommitId">提交标识；未产生修改时为空标识。</param>
/// <param name="PreviousStateId">提交前状态标识。</param>
/// <param name="StateId">提交后状态标识。</param>
/// <param name="Changed">提交是否产生实际修改。</param>
/// <param name="EntityId">由操作新增或直接影响的实体标识。</param>
public sealed record WorldCommitViewModel(Guid CommitId, Guid PreviousStateId, Guid StateId, bool Changed, Guid? EntityId = null);

/// <summary>表示推送给前端的世界变化或保存状态事件。</summary>
/// <param name="Type">稳定事件类型。</param>
/// <param name="StateId">事件发生时的 World 状态标识。</param>
/// <param name="IsDirty">事件发生时是否存在未保存修改。</param>
/// <param name="CommitId">相关提交标识。</param>
/// <param name="Operation">相关世界会话操作。</param>
/// <param name="Error">经过展示层处理的错误消息。</param>
public sealed record WorldEventViewModel(string Type, Guid StateId, bool IsDirty, Guid? CommitId, string? Operation, string? Error);

/// <summary>表示添加 Element 的请求。</summary>
/// <param name="ExpectedStateId">调用方观察到的真实 World 状态标识。</param>
/// <param name="Name">Element 名称。</param>
/// <param name="Description">Element 说明。</param>
/// <param name="Type">开放 ElementType 文本。</param>
public sealed record AddElementRequest(Guid ExpectedStateId, string Name, string Description, string Type);

/// <summary>表示更新 Element 非结构字段的请求；null 属性保持不变。</summary>
/// <param name="ExpectedStateId">调用方观察到的真实 World 状态标识。</param>
/// <param name="Name">可选的新名称。</param>
/// <param name="Description">可选的新说明。</param>
/// <param name="Type">可选的新开放类型文本。</param>
public sealed record UpdateElementRequest(Guid ExpectedStateId, string? Name, string? Description, string? Type);

/// <summary>表示添加 Scope 的请求。</summary>
/// <param name="ExpectedStateId">调用方观察到的真实 World 状态标识。</param>
/// <param name="Name">Scope 名称。</param>
/// <param name="Description">Scope 说明。</param>
/// <param name="Quantity">由对应 Module 解释的有限强度。</param>
/// <param name="Type">开放 ScopeType 文本。</param>
/// <param name="OwnerElementId">Owner Element 标识。</param>
public sealed record AddScopeRequest(Guid ExpectedStateId, string Name, string Description, double Quantity, string Type, Guid OwnerElementId);

/// <summary>表示更新 Scope 非结构字段的请求；null 属性保持不变。</summary>
/// <param name="ExpectedStateId">调用方观察到的真实 World 状态标识。</param>
/// <param name="Name">可选的新名称。</param>
/// <param name="Description">可选的新说明。</param>
/// <param name="Quantity">可选的新有限强度。</param>
/// <param name="Type">可选的新开放类型文本。</param>
public sealed record UpdateScopeRequest(Guid ExpectedStateId, string? Name, string? Description, double? Quantity, string? Type);

/// <summary>表示添加 Aspect 的请求。</summary>
/// <param name="ExpectedStateId">调用方观察到的真实 World 状态标识。</param>
/// <param name="Name">Aspect 名称。</param>
/// <param name="Description">Aspect 说明。</param>
/// <param name="Quantity">由对应 Module 解释的有限强度。</param>
/// <param name="Type">开放 AspectType 文本。</param>
/// <param name="ElementId">目标 Element 标识。</param>
/// <param name="ScopeId">唯一所属 Scope 标识。</param>
public sealed record AddAspectRequest(Guid ExpectedStateId, string Name, string Description, double Quantity, string Type, Guid ElementId, Guid ScopeId);

/// <summary>表示更新 Aspect 非结构字段的请求；null 属性保持不变。</summary>
/// <param name="ExpectedStateId">调用方观察到的真实 World 状态标识。</param>
/// <param name="Name">可选的新名称。</param>
/// <param name="Description">可选的新说明。</param>
/// <param name="Quantity">可选的新有限强度。</param>
/// <param name="Type">可选的新开放类型文本。</param>
public sealed record UpdateAspectRequest(Guid ExpectedStateId, string? Name, string? Description, double? Quantity, string? Type);

/// <summary>表示添加 Relation 的请求。</summary>
/// <param name="ExpectedStateId">调用方观察到的真实 World 状态标识。</param>
/// <param name="Name">Relation 名称。</param>
/// <param name="Description">Relation 说明。</param>
/// <param name="Quantity">由对应 Module 解释的有限强度。</param>
/// <param name="Type">开放 RelationType 文本。</param>
/// <param name="SourceElementId">来源 Element 标识。</param>
/// <param name="TargetElementId">目标 Element 标识。</param>
/// <param name="ScopeId">唯一所属 Scope 标识。</param>
public sealed record AddRelationRequest(Guid ExpectedStateId, string Name, string Description, double Quantity, string Type, Guid SourceElementId, Guid TargetElementId, Guid ScopeId);

/// <summary>表示更新 Relation 非结构字段的请求；null 属性保持不变。</summary>
/// <param name="ExpectedStateId">调用方观察到的真实 World 状态标识。</param>
/// <param name="Name">可选的新名称。</param>
/// <param name="Description">可选的新说明。</param>
/// <param name="Quantity">可选的新有限强度。</param>
/// <param name="Type">可选的新开放类型文本。</param>
public sealed record UpdateRelationRequest(Guid ExpectedStateId, string? Name, string? Description, double? Quantity, string? Type);

/// <summary>表示依赖当前 World 状态的操作请求。</summary>
/// <param name="ExpectedStateId">调用方观察到的 World 状态标识。</param>
public sealed record WorldStateRequest(Guid ExpectedStateId);

/// <summary>表示手动保存后的世界状态。</summary>
/// <param name="StateId">保存时的 World 状态标识。</param>
/// <param name="IsDirty">保存完成后是否仍有未保存修改。</param>
public sealed record SaveWorldViewModel(Guid StateId, bool IsDirty);

/// <summary>表示 Host 向前端返回的稳定错误结构。</summary>
public sealed record ErrorViewModel(string Code, string Message, string Category, string? Operation, bool IsTransient, IReadOnlyDictionary<string, string> Details, string TraceId);
