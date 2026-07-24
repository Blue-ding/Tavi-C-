namespace Tavi.Host.ViewModels;

/// <summary>表示前端世界图所需的完整可观察状态。</summary>
/// <param name="StateId">当前 World 状态标识。</param>
/// <param name="StagingRevision">当前暂存区 revision。</param>
/// <param name="IsDirty">是否包含尚未保存的修改。</param>
/// <param name="CanUndo">是否可以撤销。</param>
/// <param name="CanRedo">是否可以重做。</param>
/// <param name="Health">世界会话健康状态。</param>
/// <param name="Nodes">全部 Anchor 节点。</param>
/// <param name="Edges">全部事实世界和子世界 Relation。</param>
/// <param name="SubWorlds">全部 Character 子世界。</param>
public sealed record WorldGraphViewModel(Guid StateId, long StagingRevision, bool IsDirty, bool CanUndo, bool CanRedo, string Health, IReadOnlyList<AnchorViewModel> Nodes, IReadOnlyList<RelationViewModel> Edges, IReadOnlyList<SubWorldViewModel> SubWorlds, IReadOnlyList<WorldStagedChangeViewModel> StagedChanges);

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

/// <summary>表示世界图中的一个 Anchor 节点。</summary>
/// <param name="Id">Anchor 标识。</param>
/// <param name="Name">Anchor 名称。</param>
/// <param name="Description">Anchor 描述。</param>
/// <param name="Type">Anchor 类型。</param>
/// <param name="HasSubWorld">该 Character 是否持有子世界。</param>
public sealed record AnchorViewModel(Guid Id, string Name, string Description, string Type, bool HasSubWorld);

/// <summary>表示世界图中的一条 Relation 边及其所属范围。</summary>
/// <param name="Id">Relation 标识。</param>
/// <param name="Name">Relation 名称。</param>
/// <param name="Description">Relation 描述。</param>
/// <param name="SourceId">起点 Anchor 标识。</param>
/// <param name="TargetId">终点 Anchor 标识。</param>
/// <param name="Scope">事实世界或子世界范围。</param>
/// <param name="DomainCharacterId">子世界所属 Character；事实世界关系为空。</param>
public sealed record RelationViewModel(Guid Id, string Name, string Description, Guid SourceId, Guid TargetId, string Scope, Guid? DomainCharacterId);

/// <summary>表示 Character 持有的子世界。</summary>
/// <param name="Id">子世界标识。</param>
/// <param name="CharacterId">持有子世界的 Character 标识。</param>
public sealed record SubWorldViewModel(Guid Id, Guid CharacterId);

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

/// <summary>表示添加 Anchor 的请求。</summary>
/// <param name="ExpectedStateId">调用方观察到的 World 状态标识。</param>
/// <param name="Name">新 Anchor 名称。</param>
/// <param name="Description">新 Anchor 描述。</param>
/// <param name="Type">新 Anchor 类型。</param>
public sealed record AddAnchorRequest(Guid ExpectedStateId, string Name, string Description, string Type);

/// <summary>表示更新 Anchor 可变属性的请求；空值属性保持不变。</summary>
/// <param name="ExpectedStateId">调用方观察到的 World 状态标识。</param>
/// <param name="Name">新名称；为空时保持不变。</param>
/// <param name="Description">新描述；为空时保持不变。</param>
/// <param name="Type">新类型；为空时保持不变。</param>
public sealed record UpdateAnchorRequest(Guid ExpectedStateId, string? Name, string? Description, string? Type);

/// <summary>表示添加 Relation 的请求；DomainCharacterId 为空时添加到事实世界。</summary>
/// <param name="ExpectedStateId">调用方观察到的 World 状态标识。</param>
/// <param name="Name">新 Relation 名称。</param>
/// <param name="Description">新 Relation 描述。</param>
/// <param name="SourceId">起点 Anchor 标识。</param>
/// <param name="TargetId">终点 Anchor 标识。</param>
/// <param name="DomainCharacterId">子世界所属 Character；事实世界关系为空。</param>
public sealed record AddRelationRequest(Guid ExpectedStateId, string Name, string Description, Guid SourceId, Guid TargetId, Guid? DomainCharacterId);

/// <summary>表示更新 Relation 可变属性的请求；空值属性保持不变。</summary>
/// <param name="ExpectedStateId">调用方观察到的 World 状态标识。</param>
/// <param name="Name">新名称；为空时保持不变。</param>
/// <param name="Description">新描述；为空时保持不变。</param>
public sealed record UpdateRelationRequest(Guid ExpectedStateId, string? Name, string? Description);

/// <summary>表示依赖当前 World 状态的操作请求。</summary>
/// <param name="ExpectedStateId">调用方观察到的 World 状态标识。</param>
public sealed record WorldStateRequest(Guid ExpectedStateId);

/// <summary>表示为 Character 创建子世界的请求。</summary>
/// <param name="ExpectedStateId">调用方观察到的 World 状态标识。</param>
/// <param name="CharacterId">目标 Character 标识。</param>
public sealed record CreateSubWorldRequest(Guid ExpectedStateId, Guid CharacterId);

/// <summary>表示手动保存后的世界状态。</summary>
/// <param name="StateId">保存时的 World 状态标识。</param>
/// <param name="IsDirty">保存完成后是否仍有未保存修改。</param>
public sealed record SaveWorldViewModel(Guid StateId, bool IsDirty);

/// <summary>表示 Host 向前端返回的稳定错误结构。</summary>
/// <param name="Code">稳定错误码。</param>
/// <param name="Message">适合向用户展示的错误消息。</param>
/// <param name="Category">错误分类。</param>
/// <param name="Operation">发生失败的稳定操作名称。</param>
/// <param name="IsTransient">稍后重试是否可能成功。</param>
/// <param name="Details">经过脱敏的结构化错误详情。</param>
/// <param name="TraceId">用于关联服务器日志的请求标识。</param>
public sealed record ErrorViewModel(string Code, string Message, string Category, string? Operation, bool IsTransient, IReadOnlyDictionary<string, string> Details, string TraceId);
