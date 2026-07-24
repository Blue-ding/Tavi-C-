using System.Text.Json.Serialization;

namespace Tavi.Host.ViewModels;

/// <summary>表示 Guidance 功能的可用状态。</summary>
/// <param name="Available">当前是否可以启动 Guidance。</param>
/// <param name="Provider">可用语言模型供应商；不可用时为空。</param>
/// <param name="Message">面向玩家的状态说明。</param>
public sealed record GuidanceAvailabilityViewModel(bool Available, string? Provider, string Message);

/// <summary>表示 Guidance 对话中的一条消息。</summary>
/// <param name="Role">消息发送方。</param>
/// <param name="Text">消息文本。</param>
public sealed record GuidanceMessageViewModel(string Role, string Text);

/// <summary>表示提案中的 Anchor 引用。</summary>
/// <param name="Kind">Existing 或 Proposed。</param>
/// <param name="AnchorId">真实或临时 Anchor 标识。</param>
public sealed record ProposalAnchorReferenceViewModel(string Kind, Guid AnchorId);

/// <summary>表示提案 Relation 的写入范围。</summary>
/// <param name="Kind">World 或 SubWorld。</param>
/// <param name="Character">子世界所属 Character；事实世界时为空。</param>
public sealed record ProposedRelationScopeViewModel(string Kind, ProposalAnchorReferenceViewModel? Character);

/// <summary>表示可供玩家独立选择的一项提案修改。</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ProposeAddAnchorViewModel), "AddAnchor")]
[JsonDerivedType(typeof(ProposeAddRelationViewModel), "AddRelation")]
public abstract record ProposalChangeViewModel
{
    /// <summary>获取本轮提案内稳定的修改标识。</summary>
    public required string Id { get; init; }

    /// <summary>获取提出此项修改的理由。</summary>
    public required string Rationale { get; init; }
}

/// <summary>表示添加 Anchor 的提案修改。</summary>
public sealed record ProposeAddAnchorViewModel : ProposalChangeViewModel
{
    /// <summary>获取提交前使用的临时 Anchor 标识。</summary>
    public required Guid AnchorId { get; init; }

    /// <summary>获取 Anchor 名称。</summary>
    public required string Name { get; init; }

    /// <summary>获取 Anchor 描述。</summary>
    public required string Description { get; init; }

    /// <summary>获取 Anchor 类型。</summary>
    public required string Type { get; init; }
}

/// <summary>表示添加 Relation 的提案修改。</summary>
public sealed record ProposeAddRelationViewModel : ProposalChangeViewModel
{
    /// <summary>获取 Relation 名称。</summary>
    public required string Name { get; init; }

    /// <summary>获取 Relation 描述。</summary>
    public required string Description { get; init; }

    /// <summary>获取 Relation 起点。</summary>
    public required ProposalAnchorReferenceViewModel Source { get; init; }

    /// <summary>获取 Relation 终点。</summary>
    public required ProposalAnchorReferenceViewModel Target { get; init; }

    /// <summary>获取 Relation 写入范围。</summary>
    public required ProposedRelationScopeViewModel Scope { get; init; }
}

/// <summary>表示供前端审阅的完整世界提案。</summary>
/// <param name="Id">提案标识。</param>
/// <param name="BaseWorldRevision">提案基于的世界 revision。</param>
/// <param name="Summary">面向玩家的提案摘要。</param>
/// <param name="Changes">按构筑顺序排列的修改。</param>
public sealed record WorldProposalViewModel(Guid Id, long BaseWorldRevision, string Summary, IReadOnlyList<ProposalChangeViewModel> Changes);

/// <summary>表示经过脱敏的 Guidance 失败信息。</summary>
/// <param name="Code">稳定错误码。</param>
/// <param name="Message">面向玩家的错误消息。</param>
/// <param name="IsTransient">稍后重试是否可能成功。</param>
public sealed record GuidanceFailureViewModel(string Code, string Message, bool IsTransient);

/// <summary>表示可安全交给前端读取的 Guidance 会话快照。</summary>
/// <param name="SessionId">会话标识。</param>
/// <param name="State">当前会话状态。</param>
/// <param name="BaseWorldRevision">Guidance 开始时的世界 revision。</param>
/// <param name="Messages">按发生顺序排列的对话消息。</param>
/// <param name="Proposal">当前可审阅提案；尚未形成时为空。</param>
/// <param name="Failure">最近一次可恢复失败；未失败时为空。</param>
/// <param name="RetryMessage">失败后保留的可编辑玩家消息；没有可重试消息时为空。</param>
public sealed record GuidanceSnapshotViewModel(Guid SessionId, string State, long BaseWorldRevision, IReadOnlyList<GuidanceMessageViewModel> Messages, WorldProposalViewModel? Proposal, GuidanceFailureViewModel? Failure, string? RetryMessage);

/// <summary>表示已被 Host 接管的一次 Guidance 异步操作。</summary>
/// <param name="OperationId">操作标识。</param>
/// <param name="SessionId">所属会话标识。</param>
/// <param name="State">操作当前状态。</param>
/// <param name="Snapshot">请求返回时的权威会话快照。</param>
public sealed record GuidanceOperationViewModel(Guid OperationId, Guid SessionId, string State, GuidanceSnapshotViewModel Snapshot);

/// <summary>表示提交后创建的临时 Anchor 到真实 Anchor 的映射。</summary>
/// <param name="ProposalAnchorId">临时 Anchor 标识。</param>
/// <param name="WorldAnchorId">真实世界 Anchor 标识。</param>
public sealed record CreatedAnchorViewModel(Guid ProposalAnchorId, Guid WorldAnchorId);

/// <summary>表示一项阻止提交或需要玩家处理的问题。</summary>
/// <param name="Code">稳定问题代码。</param>
/// <param name="Message">面向玩家的问题说明。</param>
/// <param name="ChangeId">相关提案修改标识；不对应单项修改时为空。</param>
public sealed record GuidanceIssueViewModel(string Code, string Message, string? ChangeId);

/// <summary>表示 Guidance 提案提交结果。</summary>
/// <param name="Status">业务提交状态。</param>
/// <param name="WorldRevision">成功提交后的世界 revision。</param>
/// <param name="CreatedAnchors">临时 Anchor 到真实 Anchor 的映射。</param>
/// <param name="Issues">阻止提交或需要处理的问题。</param>
/// <param name="ExpectedWorldRevision">发生冲突时提案期望的 revision。</param>
/// <param name="ActualWorldRevision">发生冲突时世界实际的 revision。</param>
/// <param name="Snapshot">提交后的 Guidance 会话快照。</param>
public sealed record GuidanceCommitViewModel(string Status, long? WorldRevision, IReadOnlyList<CreatedAnchorViewModel> CreatedAnchors, IReadOnlyList<GuidanceIssueViewModel> Issues, long? ExpectedWorldRevision, long? ActualWorldRevision, GuidanceSnapshotViewModel Snapshot);

/// <summary>表示推送给 Guidance 前端订阅者的实时事件。</summary>
/// <param name="Type">稳定事件类型。</param>
/// <param name="SessionId">相关会话标识。</param>
/// <param name="OperationId">相关操作标识。</param>
/// <param name="Text">本次文本增量。</param>
/// <param name="Snapshot">状态变化后的权威会话快照。</param>
/// <param name="Error">经过脱敏的错误消息。</param>
public sealed record GuidanceEventViewModel(string Type, Guid SessionId, Guid? OperationId, string? Text, GuidanceSnapshotViewModel? Snapshot, string? Error);

/// <summary>表示启动 Guidance 会话的请求。</summary>
/// <param name="Potential">玩家提供的初始叙事势能。</param>
public sealed record StartGuidanceRequest(string Potential);

/// <summary>表示继续 Guidance 对话的请求。</summary>
/// <param name="Message">玩家追加的文本。</param>
public sealed record ContinueGuidanceRequest(string Message);

/// <summary>表示编辑并重试上次失败玩家消息的请求。</summary>
/// <param name="Message">编辑后的玩家消息。</param>
public sealed record RetryGuidanceRequest(string Message);

/// <summary>表示提交 Guidance 提案的请求。</summary>
/// <param name="AcceptedChangeIds">玩家接受的提案修改标识。</param>
public sealed record CommitGuidanceRequest(IReadOnlyList<string> AcceptedChangeIds);
