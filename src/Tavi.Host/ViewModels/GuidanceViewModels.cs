using System.Text.Json.Serialization;

namespace Tavi.Host.ViewModels;

/// <summary>表示 Guidance 功能的可用状态。</summary>
/// <param name="Available">当前是否可以启动 Guidance。</param>
/// <param name="Provider">已配置的模型供应方。</param>
/// <param name="Message">面向玩家的状态说明。</param>
public sealed record GuidanceAvailabilityViewModel(bool Available, string? Provider, string Message);

/// <summary>表示 Guidance 对话中的一条消息。</summary>
/// <param name="Role">消息角色。</param>
/// <param name="Text">消息正文。</param>
public sealed record GuidanceMessageViewModel(string Role, string Text);

/// <summary>表示提案中的现有或临时 Element 引用。</summary>
/// <param name="Kind">Existing 或 Proposed。</param>
/// <param name="ElementId">现有或提案临时 Element 标识。</param>
public sealed record ProposalElementReferenceViewModel(string Kind, Guid ElementId);

/// <summary>表示提案中的现有或临时 Scope 引用。</summary>
/// <param name="Kind">Existing 或 Proposed。</param>
/// <param name="ScopeId">现有或提案临时 Scope 标识。</param>
public sealed record ProposalScopeReferenceViewModel(string Kind, Guid ScopeId);

/// <summary>表示可供玩家独立选择的一项提案修改。</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ProposeAddElementViewModel), "AddElement")]
[JsonDerivedType(typeof(ProposeAddScopeViewModel), "AddScope")]
[JsonDerivedType(typeof(ProposeAddAspectViewModel), "AddAspect")]
[JsonDerivedType(typeof(ProposeAddRelationViewModel), "AddRelation")]
[JsonDerivedType(typeof(ProposeAddLocalAspectViewModel), "AddLocalAspect")]
[JsonDerivedType(typeof(ProposeAddLocalRelationViewModel), "AddLocalRelation")]
public abstract record ProposalChangeViewModel
{
    /// <summary>获取本轮提案内稳定的修改标识。</summary>
    public required string Id { get; init; }

    /// <summary>获取提出此项修改的理由。</summary>
    public required string Rationale { get; init; }
}

/// <summary>表示添加 Element 的提案修改。</summary>
public sealed record ProposeAddElementViewModel : ProposalChangeViewModel
{
    /// <summary>获取提交前使用的临时 Element 标识。</summary>
    public required Guid ElementId { get; init; }

    /// <summary>获取 Element 名称。</summary>
    public required string Name { get; init; }

    /// <summary>获取 Element 说明。</summary>
    public required string Description { get; init; }

    /// <summary>获取开放 ElementType 文本。</summary>
    public required string Type { get; init; }
}

/// <summary>表示添加 Scope 的提案修改。</summary>
public sealed record ProposeAddScopeViewModel : ProposalChangeViewModel
{
    /// <summary>获取提交前使用的临时 Scope 标识。</summary>
    public required Guid ScopeId { get; init; }

    /// <summary>获取由对应 Module 解释的整数数量。</summary>
    public required int Quantity { get; init; }

    /// <summary>获取开放 ScopeType 文本。</summary>
    public required string Type { get; init; }

    /// <summary>获取 Owner Element 引用。</summary>
    public required ProposalElementReferenceViewModel Owner { get; init; }
}

/// <summary>表示添加 Aspect 的提案修改。</summary>
public sealed record ProposeAddAspectViewModel : ProposalChangeViewModel
{
    /// <summary>获取由对应 Module 解释的整数数量。</summary>
    public required int Quantity { get; init; }

    /// <summary>获取开放 AspectType 文本。</summary>
    public required string Type { get; init; }

    /// <summary>获取目标 Element 引用。</summary>
    public required ProposalElementReferenceViewModel Element { get; init; }

    /// <summary>获取唯一 Scope 引用。</summary>
    public required ProposalScopeReferenceViewModel Scope { get; init; }
}

/// <summary>表示添加 Relation 的提案修改。</summary>
public sealed record ProposeAddRelationViewModel : ProposalChangeViewModel
{
    /// <summary>获取由对应 Module 解释的整数数量。</summary>
    public required int Quantity { get; init; }

    /// <summary>获取开放 RelationType 文本。</summary>
    public required string Type { get; init; }

    /// <summary>获取 Source Element 引用。</summary>
    public required ProposalElementReferenceViewModel Source { get; init; }

    /// <summary>获取 Target Element 引用。</summary>
    public required ProposalElementReferenceViewModel Target { get; init; }

    /// <summary>获取唯一 Scope 引用。</summary>
    public required ProposalScopeReferenceViewModel Scope { get; init; }
}

/// <summary>表示添加 LocalAspect 的提案修改。</summary>
public sealed record ProposeAddLocalAspectViewModel : ProposalChangeViewModel
{
    /// <summary>获取自由语义名称。</summary>
    public required string Name { get; init; }

    /// <summary>获取自由语义说明。</summary>
    public required string Description { get; init; }

    /// <summary>获取整数数量。</summary>
    public required int Quantity { get; init; }

    /// <summary>获取目标 Element 引用。</summary>
    public required ProposalElementReferenceViewModel Element { get; init; }

    /// <summary>获取所属 Scope 引用。</summary>
    public required ProposalScopeReferenceViewModel Scope { get; init; }
}

/// <summary>表示添加 LocalRelation 的提案修改。</summary>
public sealed record ProposeAddLocalRelationViewModel : ProposalChangeViewModel
{
    /// <summary>获取自由语义名称。</summary>
    public required string Name { get; init; }

    /// <summary>获取自由语义说明。</summary>
    public required string Description { get; init; }

    /// <summary>获取整数数量。</summary>
    public required int Quantity { get; init; }

    /// <summary>获取 Source Element 引用。</summary>
    public required ProposalElementReferenceViewModel Source { get; init; }

    /// <summary>获取 Target Element 引用。</summary>
    public required ProposalElementReferenceViewModel Target { get; init; }

    /// <summary>获取所属 Scope 引用。</summary>
    public required ProposalScopeReferenceViewModel Scope { get; init; }
}

/// <summary>表示供前端审阅的完整 World 提案。</summary>
/// <param name="Id">提案标识。</param>
/// <param name="BaseWorldStateId">提案所依据的 World 状态标识。</param>
/// <param name="Summary">面向玩家的提案摘要。</param>
/// <param name="Changes">可独立选择但存在显式依赖的修改项。</param>
public sealed record WorldProposalViewModel(Guid Id, Guid BaseWorldStateId, string Summary, IReadOnlyList<ProposalChangeViewModel> Changes);

/// <summary>表示经过脱敏的 Guidance 失败信息。</summary>
/// <param name="Code">稳定错误码。</param>
/// <param name="Message">面向玩家的错误说明。</param>
/// <param name="IsTransient">重试是否可能成功。</param>
public sealed record GuidanceFailureViewModel(string Code, string Message, bool IsTransient);

/// <summary>表示可安全交给前端读取的 Guidance 会话快照。</summary>
/// <param name="SessionId">Guidance 会话标识。</param>
/// <param name="State">当前会话状态。</param>
/// <param name="BaseWorldStateId">本轮对话所依据的 World 状态标识。</param>
/// <param name="Messages">完整对话消息。</param>
/// <param name="Proposal">当前可审阅提案。</param>
/// <param name="Failure">当前脱敏失败信息。</param>
/// <param name="RetryMessage">可编辑后重试的玩家消息。</param>
public sealed record GuidanceSnapshotViewModel(Guid SessionId, string State, Guid BaseWorldStateId, IReadOnlyList<GuidanceMessageViewModel> Messages, WorldProposalViewModel? Proposal, GuidanceFailureViewModel? Failure, string? RetryMessage);

/// <summary>表示已被 Host 接管的一次 Guidance 异步操作。</summary>
/// <param name="OperationId">异步操作标识。</param>
/// <param name="SessionId">所属 Guidance 会话标识。</param>
/// <param name="State">操作状态。</param>
/// <param name="Snapshot">操作开始后的权威会话快照。</param>
public sealed record GuidanceOperationViewModel(Guid OperationId, Guid SessionId, string State, GuidanceSnapshotViewModel Snapshot);

/// <summary>表示提交后临时标识到真实 World 标识的映射。</summary>
/// <param name="ProposalId">提案内临时实体标识。</param>
/// <param name="WorldId">提交后真实实体标识。</param>
public sealed record CreatedWorldEntityViewModel(Guid ProposalId, Guid WorldId);

/// <summary>表示一项阻止提交或需要玩家处理的问题。</summary>
/// <param name="Code">稳定问题码。</param>
/// <param name="Message">面向玩家的问题说明。</param>
/// <param name="ChangeId">相关提案修改标识。</param>
public sealed record GuidanceIssueViewModel(string Code, string Message, string? ChangeId);

/// <summary>表示 Guidance 提案提交结果。</summary>
/// <param name="Status">提交状态。</param>
/// <param name="WorldStateId">成功提交后的 World 状态标识。</param>
/// <param name="CreatedElements">临时 Element 到真实 Element 的映射。</param>
/// <param name="CreatedScopes">临时 Scope 到真实 Scope 的映射。</param>
/// <param name="Issues">阻止提交的问题。</param>
/// <param name="ExpectedWorldStateId">提案预期的 World 状态标识。</param>
/// <param name="ActualWorldStateId">提交时实际的 World 状态标识。</param>
/// <param name="Snapshot">提交后的权威 Guidance 快照。</param>
public sealed record GuidanceCommitViewModel(string Status, Guid? WorldStateId, IReadOnlyList<CreatedWorldEntityViewModel> CreatedElements, IReadOnlyList<CreatedWorldEntityViewModel> CreatedScopes, IReadOnlyList<GuidanceIssueViewModel> Issues, Guid? ExpectedWorldStateId, Guid? ActualWorldStateId, GuidanceSnapshotViewModel Snapshot);

/// <summary>表示推送给 Guidance 前端订阅者的实时事件。</summary>
/// <param name="Type">稳定事件类型。</param>
/// <param name="SessionId">Guidance 会话标识。</param>
/// <param name="OperationId">相关异步操作标识。</param>
/// <param name="Text">流式文本增量。</param>
/// <param name="Snapshot">事件携带的权威会话快照。</param>
/// <param name="Error">经过脱敏的错误说明。</param>
public sealed record GuidanceEventViewModel(string Type, Guid SessionId, Guid? OperationId, string? Text, GuidanceSnapshotViewModel? Snapshot, string? Error);

/// <summary>表示启动 Guidance 会话的请求。</summary>
/// <param name="Potential">玩家提供的叙事势能文本。</param>
public sealed record StartGuidanceRequest(string Potential);

/// <summary>表示继续 Guidance 对话的请求。</summary>
/// <param name="Message">新的玩家消息。</param>
public sealed record ContinueGuidanceRequest(string Message);

/// <summary>表示编辑并重试上次失败玩家消息的请求。</summary>
/// <param name="Message">编辑后的玩家消息。</param>
public sealed record RetryGuidanceRequest(string Message);

/// <summary>表示提交 Guidance 提案的请求。</summary>
/// <param name="AcceptedChangeIds">玩家接受的提案修改标识。</param>
public sealed record CommitGuidanceRequest(IReadOnlyList<string> AcceptedChangeIds);
