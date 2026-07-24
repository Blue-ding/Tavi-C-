using Tavi.Domain.World;

namespace Tavi.Application.Guidance;

/// <summary>
/// Guidance 内部 Anchor 的临时强类型标识；它只在提案提交前有效，不能作为真实领域 Guid 使用。
/// </summary>
public readonly record struct ProposalAnchorId(Guid Value)
{
    /// <summary>
    /// 创建新的临时 Anchor 标识。
    /// </summary>
    public static ProposalAnchorId New() => new(Guid.NewGuid());
}

/// <summary>
/// 描述提案中的 Anchor 引用。
/// </summary>
public abstract record ProposalAnchorReference
{
    private ProposalAnchorReference()
    {
    }

    /// <summary>
    /// 引用当前 World 中已经存在的 Anchor。
    /// </summary>
    public sealed record Existing(Guid AnchorId) : ProposalAnchorReference;

    /// <summary>
    /// 引用同一 GuidanceSession 中尚未提交的临时 Anchor。
    /// </summary>
    public sealed record Proposed(ProposalAnchorId AnchorId) : ProposalAnchorReference;
}

/// <summary>
/// 描述提案 Relation 的写入范围。
/// </summary>
public abstract record ProposedRelationScope
{
    private ProposedRelationScope()
    {
    }

    /// <summary>
    /// 表示事实世界。
    /// </summary>
    public sealed record World : ProposedRelationScope;

    /// <summary>
    /// 表示由指定 Character 持有的子世界。
    /// </summary>
    public sealed record SubWorld(ProposalAnchorReference Character) : ProposedRelationScope;
}

/// <summary>
/// 描述一项可由玩家独立审阅的提案修改。
/// </summary>
public abstract record ProposalChange(string Id, string Rationale);

/// <summary>
/// 提议添加临时 Anchor。
/// </summary>
public sealed record ProposeAddAnchor(string Id, string Rationale, ProposalAnchorId AnchorId, string Name, string Description, AnchorType Type) : ProposalChange(Id, Rationale);

/// <summary>
/// 提议添加可引用现有或临时 Anchor 的 Relation。
/// </summary>
public sealed record ProposeAddRelation(string Id, string Rationale, string Name, string Description, ProposalAnchorReference Source, ProposalAnchorReference Target, ProposedRelationScope Scope) : ProposalChange(Id, Rationale);

/// <summary>
/// 表示一次 GuidanceSession 在特定时刻产生的独立提案快照。
/// </summary>
public sealed record WorldProposal
{
    /// <summary>
    /// 获取提案标识。
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// 获取提案基于的 World 状态标识；提交时必须以此进行乐观并发检查。
    /// </summary>
    public required Guid BaseWorldStateId { get; init; }

    /// <summary>
    /// 获取面向玩家的提案摘要。
    /// </summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// 获取按构筑顺序排列的提案修改。
    /// </summary>
    public IReadOnlyList<ProposalChange> Changes { get; init; } = [];
}
