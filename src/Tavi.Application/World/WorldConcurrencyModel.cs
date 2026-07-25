using Tavi.Domain.World;
using Tavi.Utilities.Concurrency;
using RuntimeWorld = Tavi.Domain.World.World;

namespace Tavi.Application.World;

/// <summary>把 World 的回滚日志事务接入通用版本化工作区。</summary>
internal sealed class WorldConcurrencyModel : IVersionedOperationModel<RuntimeWorld, WorldChangeSet, AppliedWorldChangeSet>
{
    public Guid GetStateId(RuntimeWorld state) => state.StateId;

    public AtomicApplyResult<RuntimeWorld, AppliedWorldChangeSet> Apply(RuntimeWorld state, WorldChangeSet batch)
        => ToResult(state, state.Apply(batch));

    public AtomicApplyResult<RuntimeWorld, AppliedWorldChangeSet> Undo(RuntimeWorld state, AppliedWorldChangeSet history)
        => ToResult(state, state.Apply(history.Inverse));

    public AtomicApplyResult<RuntimeWorld, AppliedWorldChangeSet> Redo(RuntimeWorld state, AppliedWorldChangeSet history)
        => ToResult(state, state.Apply(history.Forward));

    public bool IsRollbackFailure(Exception exception) => exception is WorldTransactionException;

    private static AtomicApplyResult<RuntimeWorld, AppliedWorldChangeSet> ToResult(RuntimeWorld state, WorldApplyResult result)
        => result.Changed
            ? AtomicApplyResult<RuntimeWorld, AppliedWorldChangeSet>.ChangedState(state, result.ChangeSet!)
            : AtomicApplyResult<RuntimeWorld, AppliedWorldChangeSet>.Unchanged(state);
}
