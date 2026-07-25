using Tavi.Domain.Scenario;
using Tavi.Utilities.Concurrency;
using RuntimeScenario = Tavi.Domain.Scenario.Scenario;

namespace Tavi.Application.Scenario;

/// <summary>把 Scenario 的候选语义校验和快照回滚事务接入通用版本化工作区。</summary>
internal sealed class ScenarioConcurrencyModel(ScenarioSemanticValidator validator)
    : IVersionedOperationModel<RuntimeScenario, ScenarioChangeSet, AppliedScenarioChangeSet>
{
    public Guid GetStateId(RuntimeScenario state) => state.StateId;

    public AtomicApplyResult<RuntimeScenario, AppliedScenarioChangeSet> Apply(RuntimeScenario state, ScenarioChangeSet batch)
        => ApplyCore(state, batch);

    public AtomicApplyResult<RuntimeScenario, AppliedScenarioChangeSet> Undo(RuntimeScenario state, AppliedScenarioChangeSet history)
        => ApplyCore(state, history.Inverse);

    public AtomicApplyResult<RuntimeScenario, AppliedScenarioChangeSet> Redo(RuntimeScenario state, AppliedScenarioChangeSet history)
        => ApplyCore(state, history.Forward);

    public bool IsRollbackFailure(Exception exception) => exception is AtomicStateRecoveryException;

    private AtomicApplyResult<RuntimeScenario, AppliedScenarioChangeSet> ApplyCore(RuntimeScenario state, ScenarioChangeSet changeSet)
    {
        RuntimeScenario projection = RuntimeScenario.Create(state.CreateSnapshot());
        _ = projection.Apply(changeSet);
        validator.EnsureValid(projection.CreateSnapshot(), nameof(ScenarioSession.Apply));
        ScenarioApplyResult applied;
        try
        {
            applied = state.Apply(changeSet);
        }
        catch (ScenarioException exception) when (exception.ErrorCode == ScenarioErrorCodes.RollbackFailed)
        {
            throw new AtomicStateRecoveryException(exception);
        }
        return applied.Changed
            ? AtomicApplyResult<RuntimeScenario, AppliedScenarioChangeSet>.ChangedState(state, applied.ChangeSet!)
            : AtomicApplyResult<RuntimeScenario, AppliedScenarioChangeSet>.Unchanged(state);
    }
}
