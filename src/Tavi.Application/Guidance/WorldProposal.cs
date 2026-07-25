using Tavi.Domain.World;

namespace Tavi.Application.Guidance;

/// <summary>表示 Guidance 提案提交前使用的临时 Element 标识。</summary>
/// <param name="Value">只在当前提案内有效的非空 Guid。</param>
public readonly record struct ProposalElementId(Guid Value)
{
    /// <summary>创建新的临时 Element 标识。</summary>
    public static ProposalElementId New() => new(Guid.NewGuid());
}

/// <summary>表示 Guidance 提案提交前使用的临时 Scope 标识。</summary>
/// <param name="Value">只在当前提案内有效的非空 Guid。</param>
public readonly record struct ProposalScopeId(Guid Value)
{
    /// <summary>创建新的临时 Scope 标识。</summary>
    public static ProposalScopeId New() => new(Guid.NewGuid());
}

/// <summary>描述提案对现有或同一提案内临时 Element 的强类型引用。</summary>
public abstract record ProposalElementReference
{
    private ProposalElementReference() { }

    /// <summary>引用当前临时 World 中已经存在的 Element。</summary>
    /// <param name="ElementId">现有 Element 标识。</param>
    public sealed record Existing(Guid ElementId) : ProposalElementReference;

    /// <summary>引用同一 Guidance 提案中尚未提交的 Element。</summary>
    /// <param name="ElementId">临时 Element 标识。</param>
    public sealed record Proposed(ProposalElementId ElementId) : ProposalElementReference;
}

/// <summary>描述提案对现有或同一提案内临时 Scope 的强类型引用。</summary>
public abstract record ProposalScopeReference
{
    private ProposalScopeReference() { }

    /// <summary>引用当前临时 World 中已经存在的 Scope。</summary>
    /// <param name="ScopeId">现有 Scope 标识。</param>
    public sealed record Existing(Guid ScopeId) : ProposalScopeReference;

    /// <summary>引用同一 Guidance 提案中尚未提交的 Scope。</summary>
    /// <param name="ScopeId">临时 Scope 标识。</param>
    public sealed record Proposed(ProposalScopeId ScopeId) : ProposalScopeReference;
}

/// <summary>描述一项可由玩家独立审阅的 Guidance 提案修改。</summary>
/// <param name="Id">提案内稳定的修改标识。</param>
/// <param name="Rationale">向玩家说明该修改叙事意义的文本。</param>
public abstract record ProposalChange(string Id, string Rationale);

/// <summary>提议添加一个临时 Element。</summary>
/// <param name="Id">修改标识。</param><param name="Rationale">修改理由。</param><param name="ElementId">临时 Element 标识。</param><param name="Name">名称。</param><param name="Description">说明。</param><param name="Type">规则化类型。</param>
public sealed record ProposeAddElement(string Id, string Rationale, ProposalElementId ElementId, string Name, string Description, ElementType Type) : ProposalChange(Id, Rationale);

/// <summary>提议添加一个由现有或临时 Element 持有的临时 Scope。</summary>
/// <param name="Id">修改标识。</param><param name="Rationale">修改理由。</param><param name="ScopeId">临时 Scope 标识。</param><param name="Quantity">整数数量。</param><param name="Type">规则化类型。</param><param name="Owner">Owner Element 引用。</param>
public sealed record ProposeAddScope(string Id, string Rationale, ProposalScopeId ScopeId, int Quantity, ScopeType Type, ProposalElementReference Owner) : ProposalChange(Id, Rationale);

/// <summary>提议添加一个可引用现有或临时 Element 与 Scope 的 Aspect。</summary>
/// <param name="Id">修改标识。</param><param name="Rationale">修改理由。</param><param name="Quantity">整数数量。</param><param name="Type">规则化类型。</param><param name="Element">目标 Element 引用。</param><param name="Scope">所属 Scope 引用。</param>
public sealed record ProposeAddAspect(string Id, string Rationale, int Quantity, AspectType Type, ProposalElementReference Element, ProposalScopeReference Scope) : ProposalChange(Id, Rationale);

/// <summary>提议添加一个可引用现有或临时 Element 与 Scope 的 Relation。</summary>
/// <param name="Id">修改标识。</param><param name="Rationale">修改理由。</param><param name="Quantity">整数数量。</param><param name="Type">规则化类型。</param><param name="Source">来源 Element 引用。</param><param name="Target">目标 Element 引用。</param><param name="Scope">所属 Scope 引用。</param>
public sealed record ProposeAddRelation(string Id, string Rationale, int Quantity, RelationType Type, ProposalElementReference Source, ProposalElementReference Target, ProposalScopeReference Scope) : ProposalChange(Id, Rationale);

/// <summary>提议添加一个仅存在于 World 的本地一元语义。</summary>
/// <param name="Id">修改标识。</param><param name="Rationale">修改理由。</param><param name="Name">自由名称。</param><param name="Description">自由说明。</param><param name="Quantity">整数数量。</param><param name="Element">目标 Element 引用。</param><param name="Scope">所属 Scope 引用。</param>
public sealed record ProposeAddLocalAspect(string Id, string Rationale, string Name, string Description, int Quantity, ProposalElementReference Element, ProposalScopeReference Scope) : ProposalChange(Id, Rationale);

/// <summary>提议添加一个仅存在于 World 的本地关系语义。</summary>
/// <param name="Id">修改标识。</param><param name="Rationale">修改理由。</param><param name="Name">自由名称。</param><param name="Description">自由说明。</param><param name="Quantity">整数数量。</param><param name="Source">来源 Element 引用。</param><param name="Target">目标 Element 引用。</param><param name="Scope">所属 Scope 引用。</param>
public sealed record ProposeAddLocalRelation(string Id, string Rationale, string Name, string Description, int Quantity, ProposalElementReference Source, ProposalElementReference Target, ProposalScopeReference Scope) : ProposalChange(Id, Rationale);

/// <summary>表示一次 GuidanceSession 在特定时刻产生的独立提案快照。</summary>
public sealed record WorldProposal
{
    /// <summary>获取提案标识。</summary>
    public required Guid Id { get; init; }

    /// <summary>获取提案基于的 World 状态标识；提交时必须以此进行乐观并发检查。</summary>
    public required Guid BaseWorldStateId { get; init; }

    /// <summary>获取面向玩家的提案摘要。</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>获取按构筑顺序排列的提案修改。</summary>
    public IReadOnlyList<ProposalChange> Changes { get; init; } = [];
}
