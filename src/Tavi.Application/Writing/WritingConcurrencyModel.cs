using Tavi.Domain.Story;
using Tavi.Utilities.Concurrency;

namespace Tavi.Application.Writing;

internal sealed record WritingWorkspaceState(Manuscript? Current);

internal sealed record WritingOperationBatch
{
    internal WritingOperationBatch(IEnumerable<ManuscriptOperation> operations)
        => Operations = operations?.ToArray() ?? throw new ArgumentNullException(nameof(operations));

    internal IReadOnlyList<ManuscriptOperation> Operations { get; }
}

internal sealed record WritingHistoryEntry(Manuscript Before, Manuscript After);

/// <summary>把 Writing 的不可变状态替换接入通用版本化工作区。</summary>
internal sealed class WritingConcurrencyModel : IVersionedOperationModel<WritingWorkspaceState, WritingOperationBatch, WritingHistoryEntry>
{
    public Guid GetStateId(WritingWorkspaceState state) => state.Current?.StateId ?? Guid.Empty;

    public AtomicApplyResult<WritingWorkspaceState, WritingHistoryEntry> Apply(WritingWorkspaceState state, WritingOperationBatch batch)
    {
        Manuscript before = state.Current ?? throw WritingException.NoActive("Commit");
        Manuscript after = ManuscriptEditor.Apply(before, batch.Operations);
        return ReferenceEquals(before, after)
            ? AtomicApplyResult<WritingWorkspaceState, WritingHistoryEntry>.Unchanged(state)
            : AtomicApplyResult<WritingWorkspaceState, WritingHistoryEntry>.ChangedState(new WritingWorkspaceState(after), new WritingHistoryEntry(before, after));
    }

    public AtomicApplyResult<WritingWorkspaceState, WritingHistoryEntry> Undo(WritingWorkspaceState state, WritingHistoryEntry history)
    {
        Manuscript current = state.Current ?? throw WritingException.NoActive("Undo");
        Manuscript restored = ManuscriptEditor.RebaseContent(history.Before);
        return AtomicApplyResult<WritingWorkspaceState, WritingHistoryEntry>.ChangedState(new WritingWorkspaceState(restored), new WritingHistoryEntry(current, restored));
    }

    public AtomicApplyResult<WritingWorkspaceState, WritingHistoryEntry> Redo(WritingWorkspaceState state, WritingHistoryEntry history)
    {
        Manuscript current = state.Current ?? throw WritingException.NoActive("Redo");
        Manuscript restored = ManuscriptEditor.RebaseContent(history.After);
        return AtomicApplyResult<WritingWorkspaceState, WritingHistoryEntry>.ChangedState(new WritingWorkspaceState(restored), new WritingHistoryEntry(current, restored));
    }

    public bool IsRollbackFailure(Exception exception) => false;
}
