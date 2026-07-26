namespace Tavi.Extensibility;

/// <summary>提供给 Module 的只读 Beat 槽位绑定。</summary>
public sealed record BeatSlotBindingView(string SlotId, IReadOnlyList<Guid> ElementIds);

/// <summary>提供给 Module 的只读 Beat 数据。</summary>
public sealed record PerformanceBeatView(
    Guid Id,
    SemanticKey DefinitionId,
    ModuleId Module,
    ModuleVersion ModuleVersion,
    Guid BasedOnPerformanceStateId,
    string State,
    IReadOnlyList<BeatSlotBindingView> Bindings);

/// <summary>表示与运行中 Performance 隔离的只读 Module 投影。</summary>
public interface IPerformanceView
{
    Guid StateId { get; }
    Guid SourceScenarioStateId { get; }
    Guid SourceSceneId { get; }
    string Status { get; }
    IReadOnlyCollection<ElementView> Elements { get; }
    IReadOnlyCollection<ScopeView> Scopes { get; }
    IReadOnlyCollection<AspectView> Aspects { get; }
    IReadOnlyCollection<RelationView> Relations { get; }
    IReadOnlyCollection<PerformanceBeatView> Beats { get; }
}

/// <summary>描述 Module 对 Performance 临时 EARS 图提出的一项结构化意图。</summary>
public abstract record PerformanceOperationIntent
{
    private PerformanceOperationIntent() { }
    public sealed record AddElement(Guid Id, string Name, string Description, SemanticKey Type) : PerformanceOperationIntent;
    public sealed record RemoveElement(Guid Id) : PerformanceOperationIntent;
    public sealed record UpdateElement(Guid Id, string Name, string Description, SemanticKey Type) : PerformanceOperationIntent;
    public sealed record AddScope(Guid Id, int Quantity, SemanticKey Type, Guid OwnerElementId) : PerformanceOperationIntent;
    public sealed record RemoveScope(Guid Id) : PerformanceOperationIntent;
    public sealed record UpdateScope(Guid Id, int Quantity, SemanticKey Type) : PerformanceOperationIntent;
    public sealed record AddAspect(Guid Id, int Quantity, SemanticKey Type, Guid ElementId, Guid ScopeId) : PerformanceOperationIntent;
    public sealed record RemoveAspect(Guid Id) : PerformanceOperationIntent;
    public sealed record UpdateAspect(Guid Id, int Quantity, SemanticKey Type) : PerformanceOperationIntent;
    public sealed record AddRelation(Guid Id, int Quantity, SemanticKey Type, Guid SourceElementId, Guid TargetElementId, Guid ScopeId) : PerformanceOperationIntent;
    public sealed record RemoveRelation(Guid Id) : PerformanceOperationIntent;
    public sealed record UpdateRelation(Guid Id, int Quantity, SemanticKey Type) : PerformanceOperationIntent;
}

/// <summary>表示 Module 将冻结 Scene 展开为临时 Performance 图的提案。</summary>
public sealed record PerformanceExpansionProposal
{
    public required Guid ExpectedScenarioStateId { get; init; }
    public IReadOnlyList<PerformanceOperationIntent> Operations { get; init; } = [];
    public string Rationale { get; init; } = string.Empty;
}

/// <summary>描述 BeatDefinition 中一个可以绑定 Performance Element 的槽位。</summary>
public sealed record BeatSlotDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public required int Minimum { get; init; }
    public int? Maximum { get; init; }
}

/// <summary>描述 Module 可在当前 Performance 中实例化的 Beat。</summary>
public sealed record BeatDefinition
{
    public required SemanticKey Id { get; init; }
    public required ModuleId Module { get; init; }
    public required ModuleVersion ModuleVersion { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<BeatSlotDefinition> Slots { get; init; } = [];
    public Guid? SourcePerformanceStateId { get; init; }
}

/// <summary>表示 Beat 解决后希望追加到 Manuscript 的稳定段落。</summary>
public sealed record BeatParagraphProposal(Guid Id, string Text);

/// <summary>表示 Module 对一个 Processing Beat 的完整局部结果。</summary>
public sealed record BeatResolutionProposal
{
    public required Guid ExpectedPerformanceStateId { get; init; }
    public IReadOnlyList<PerformanceOperationIntent> Operations { get; init; } = [];
    public IReadOnlyList<BeatParagraphProposal> Paragraphs { get; init; } = [];
    public string Rationale { get; init; } = string.Empty;
}
