namespace Tavi.Domain.Performance;

/// <summary>描述一项不可变的 Performance 修改意图。</summary>
public abstract record PerformanceOperation;

public sealed record AddPerformanceElementOperation(Guid ElementId, string Name, string Description, ElementType Type) : PerformanceOperation;
public sealed record RemovePerformanceElementOperation(Guid ElementId) : PerformanceOperation;
public sealed record UpdatePerformanceElementOperation(Guid ElementId, string Name, string Description, ElementType Type) : PerformanceOperation;
public sealed record AddPerformanceScopeOperation(Guid ScopeId, int Quantity, ScopeType Type, Guid OwnerElementId) : PerformanceOperation;
public sealed record RemovePerformanceScopeOperation(Guid ScopeId) : PerformanceOperation;
public sealed record UpdatePerformanceScopeOperation(Guid ScopeId, int Quantity, ScopeType Type) : PerformanceOperation;
public sealed record AddPerformanceAspectOperation(Guid AspectId, int Quantity, AspectType Type, Guid ElementId, Guid ScopeId) : PerformanceOperation;
public sealed record RemovePerformanceAspectOperation(Guid AspectId) : PerformanceOperation;
public sealed record UpdatePerformanceAspectOperation(Guid AspectId, int Quantity, AspectType Type) : PerformanceOperation;
public sealed record AddPerformanceRelationOperation(Guid RelationId, int Quantity, RelationType Type, Guid SourceElementId, Guid TargetElementId, Guid ScopeId) : PerformanceOperation;
public sealed record RemovePerformanceRelationOperation(Guid RelationId) : PerformanceOperation;
public sealed record UpdatePerformanceRelationOperation(Guid RelationId, int Quantity, RelationType Type) : PerformanceOperation;

public sealed record AddBeatOperation(
    Guid BeatId,
    BeatDefinitionType DefinitionId,
    string ModuleId,
    string ModuleVersion,
    Guid BasedOnPerformanceStateId,
    string Name,
    string Description,
    IReadOnlyList<BeatSlotSpecification> Slots) : PerformanceOperation;

public sealed record SetBeatSlotBindingOperation(Guid BeatId, BeatSlotBinding Binding) : PerformanceOperation;
public sealed record ClearBeatSlotBindingOperation(Guid BeatId, string SlotId) : PerformanceOperation;
public sealed record BeginBeatProcessingOperation(Guid BeatId) : PerformanceOperation;

/// <summary>原子应用 Beat 的局部 EARS 结果并冻结正文贡献。</summary>
public sealed record ResolveBeatOperation(Guid BeatId, PerformanceChangeSet EarsChanges, IReadOnlyList<BeatParagraph> Paragraphs) : PerformanceOperation;

public sealed record MarkBeatPublishedOperation(Guid BeatId, BeatPublicationReceipt Receipt) : PerformanceOperation;
public sealed record CompletePerformanceOperation : PerformanceOperation;
public sealed record AbandonPerformanceOperation : PerformanceOperation;
internal sealed record RestorePerformanceSnapshotOperation(PerformanceSnapshot Snapshot) : PerformanceOperation;

/// <summary>表示按确定顺序原子执行的 Performance 操作组。</summary>
public sealed class PerformanceChangeSet
{
    public PerformanceChangeSet(IEnumerable<PerformanceOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        PerformanceOperation[] copied = operations.ToArray();
        if (copied.Any(value => value is null))
            throw new ArgumentException("Performance 操作组不能包含 null。", nameof(operations));
        Operations = Array.AsReadOnly(copied);
    }

    public IReadOnlyList<PerformanceOperation> Operations { get; }
    public bool IsEmpty => Operations.Count == 0;
}

public sealed record AppliedPerformanceChangeSet(PerformanceChangeSet Forward, PerformanceChangeSet Inverse);
internal sealed record PerformanceApplyResult(Guid PreviousStateId, Guid StateId, AppliedPerformanceChangeSet? ChangeSet)
{
    internal bool Changed => ChangeSet is not null;
    internal static PerformanceApplyResult Unchanged(Guid stateId) => new(stateId, stateId, null);
}
