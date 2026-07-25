namespace Tavi.Extensibility;

/// <summary>表示与运行中 World 隔离的只读 Module 投影。</summary>
public interface IWorldView
{
    /// <summary>获取 World 当前状态标识。</summary>
    Guid StateId { get; }
    /// <summary>获取全部 Element。</summary>
    IReadOnlyCollection<ElementView> Elements { get; }
    /// <summary>获取全部 Scope。</summary>
    IReadOnlyCollection<ScopeView> Scopes { get; }
    /// <summary>获取全部 Aspect。</summary>
    IReadOnlyCollection<AspectView> Aspects { get; }
    /// <summary>获取全部 Relation。</summary>
    IReadOnlyCollection<RelationView> Relations { get; }
}

/// <summary>描述 Module 可向玩家或 Guidance 展示的一项组合式 World 创作操作。</summary>
public sealed record WorldAuthoringAction
{
    /// <summary>获取 Module 内稳定的操作键。</summary>
    public required SemanticKey Id { get; init; }
    /// <summary>获取面向玩家的名称。</summary>
    public required string Name { get; init; }
    /// <summary>获取操作说明。</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>获取 JSON Schema 格式的参数定义。</summary>
    public string ParameterSchema { get; init; } = """{"type":"object","additionalProperties":false}""";
}

/// <summary>描述一次 World Authoring 操作调用。</summary>
public sealed record WorldAuthoringRequest
{
    /// <summary>获取与真实 World 隔离的只读投影。</summary>
    public required IWorldView World { get; init; }
    /// <summary>获取所调用的操作键。</summary>
    public required SemanticKey ActionId { get; init; }
    /// <summary>获取调用方提供的 JSON 参数。</summary>
    public string Arguments { get; init; } = "{}";
    /// <summary>获取 Module 冻结参数。</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
}

/// <summary>描述 Module 为 World 提出的一项聚合专属修改意图。</summary>
public abstract record WorldAuthoringIntent
{
    private WorldAuthoringIntent() { }
    /// <summary>建议添加 Element。</summary>
    public sealed record AddElement(Guid Id, string Name, string Description, SemanticKey Type) : WorldAuthoringIntent;
    /// <summary>建议删除 Element。</summary>
    public sealed record RemoveElement(Guid Id) : WorldAuthoringIntent;
    /// <summary>建议更新 Element。</summary>
    public sealed record UpdateElement(Guid Id, string Name, string Description, SemanticKey Type) : WorldAuthoringIntent;
    /// <summary>建议添加 Scope。</summary>
    public sealed record AddScope(Guid Id, int Quantity, SemanticKey Type, Guid OwnerElementId) : WorldAuthoringIntent;
    /// <summary>建议删除 Scope。</summary>
    public sealed record RemoveScope(Guid Id) : WorldAuthoringIntent;
    /// <summary>建议更新 Scope。</summary>
    public sealed record UpdateScope(Guid Id, int Quantity, SemanticKey Type) : WorldAuthoringIntent;
    /// <summary>建议添加 Aspect。</summary>
    public sealed record AddAspect(Guid Id, int Quantity, SemanticKey Type, Guid ElementId, Guid ScopeId) : WorldAuthoringIntent;
    /// <summary>建议删除 Aspect。</summary>
    public sealed record RemoveAspect(Guid Id) : WorldAuthoringIntent;
    /// <summary>建议更新 Aspect。</summary>
    public sealed record UpdateAspect(Guid Id, int Quantity, SemanticKey Type) : WorldAuthoringIntent;
    /// <summary>建议添加 Relation。</summary>
    public sealed record AddRelation(Guid Id, int Quantity, SemanticKey Type, Guid SourceElementId, Guid TargetElementId, Guid ScopeId) : WorldAuthoringIntent;
    /// <summary>建议删除 Relation。</summary>
    public sealed record RemoveRelation(Guid Id) : WorldAuthoringIntent;
    /// <summary>建议更新 Relation。</summary>
    public sealed record UpdateRelation(Guid Id, int Quantity, SemanticKey Type) : WorldAuthoringIntent;
}

/// <summary>表示 Module 产生的原子 World 创作提案。</summary>
public sealed record WorldAuthoringProposal
{
    /// <summary>获取提案依据的 World 状态标识。</summary>
    public required Guid ExpectedWorldStateId { get; init; }
    /// <summary>获取必须按顺序整体接受的修改意图。</summary>
    public IReadOnlyList<WorldAuthoringIntent> Intents { get; init; } = [];
    /// <summary>获取供玩家理解的提案说明。</summary>
    public string Rationale { get; init; } = string.Empty;
}
