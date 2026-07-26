using Tavi.Domain.Performance;
using Tavi.Utilities.Concurrency;
using RuntimePerformance = Tavi.Domain.Performance.Performance;

namespace Tavi.Application.Performance;

internal sealed class PerformanceConcurrencyModel
    : IVersionedOperationModel<RuntimePerformance, PerformanceChangeSet, AppliedPerformanceChangeSet>
{
    public Guid GetStateId(RuntimePerformance state) => state.StateId;

    public AtomicApplyResult<RuntimePerformance, AppliedPerformanceChangeSet> Apply(RuntimePerformance state, PerformanceChangeSet batch)
        => ApplyCore(state, batch);

    public AtomicApplyResult<RuntimePerformance, AppliedPerformanceChangeSet> Undo(RuntimePerformance state, AppliedPerformanceChangeSet history)
        => ApplyCore(state, history.Inverse);

    public AtomicApplyResult<RuntimePerformance, AppliedPerformanceChangeSet> Redo(RuntimePerformance state, AppliedPerformanceChangeSet history)
        => ApplyCore(state, history.Forward);

    public bool IsRollbackFailure(Exception exception) => exception is AtomicStateRecoveryException;

    private static AtomicApplyResult<RuntimePerformance, AppliedPerformanceChangeSet> ApplyCore(RuntimePerformance state, PerformanceChangeSet changes)
    {
        PerformanceApplyResult applied;
        try
        {
            applied = state.Apply(changes);
        }
        catch (PerformanceException exception) when (exception.ErrorCode == PerformanceErrorCodes.RollbackFailed)
        {
            throw new AtomicStateRecoveryException(exception);
        }
        return applied.Changed
            ? AtomicApplyResult<RuntimePerformance, AppliedPerformanceChangeSet>.ChangedState(state, applied.ChangeSet!)
            : AtomicApplyResult<RuntimePerformance, AppliedPerformanceChangeSet>.Unchanged(state);
    }
}
