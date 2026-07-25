namespace Tavi.Utilities.Concurrency;

/// <summary>表示版本化工作区的内存状态是否仍然可靠。</summary>
public enum VersionedWorkspaceHealth
{
    Healthy,
    Faulted
}

/// <summary>表示乐观并发条件与工作区当前版本不一致。</summary>
public sealed class OptimisticConcurrencyConflictException : InvalidOperationException
{
    public OptimisticConcurrencyConflictException(Guid expectedStateId, Guid actualStateId)
        : base($"状态冲突：期望 {expectedStateId}，实际 {actualStateId}。")
    {
        ExpectedStateId = expectedStateId;
        ActualStateId = actualStateId;
    }

    public Guid ExpectedStateId { get; }
    public Guid ActualStateId { get; }
}

/// <summary>表示领域原子执行失败且无法恢复可靠前态；工作区捕获后会进入 Faulted。</summary>
public sealed class AtomicStateRecoveryException : Exception
{
    public AtomicStateRecoveryException(Exception cause)
        : base("领域原子执行无法恢复可靠前态。", cause ?? throw new ArgumentNullException(nameof(cause)))
    {
    }
}

/// <summary>描述领域模型完成一次原子操作后的状态及可逆历史。</summary>
public sealed record AtomicApplyResult<TState, THistory>
    where TState : class
    where THistory : class
{
    private AtomicApplyResult(TState state, THistory? history)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        History = history;
    }

    public TState State { get; }
    public THistory? History { get; }
    public bool Changed => History is not null;

    public static AtomicApplyResult<TState, THistory> Unchanged(TState state) => new(state, null);
    public static AtomicApplyResult<TState, THistory> ChangedState(TState state, THistory history) => new(state, history ?? throw new ArgumentNullException(nameof(history)));
}

/// <summary>
/// 把领域专属的原子执行和历史重放接入通用版本化工作区。
/// Apply、Undo 和 Redo 抛出异常时必须保持传入状态没有可观察变化。
/// </summary>
public interface IVersionedOperationModel<TState, TBatch, THistory>
    where TState : class
    where TBatch : class
    where THistory : class
{
    Guid GetStateId(TState state);
    AtomicApplyResult<TState, THistory> Apply(TState state, TBatch batch);
    AtomicApplyResult<TState, THistory> Undo(TState state, THistory history);
    AtomicApplyResult<TState, THistory> Redo(TState state, THistory history);

    /// <summary>判断异常是否表示领域原子执行无法恢复可靠前态。</summary>
    bool IsRollbackFailure(Exception exception);
}

/// <summary>描述一次版本化工作区提交。</summary>
public sealed record VersionedCommitResult<THistory>(
    Guid CommitId,
    Guid PreviousStateId,
    Guid StateId,
    THistory? History)
    where THistory : class
{
    public bool Changed => History is not null;

    public static VersionedCommitResult<THistory> Unchanged(Guid stateId) => new(Guid.Empty, stateId, stateId, null);
}

/// <summary>
/// 独占一个版本化状态，在同一临界区完成版本检查、原子执行和 Undo/Redo 历史迁移。
/// 实例应当与拥有该状态的 Session 保持相同生命周期。
/// </summary>
public sealed class VersionedWorkspace<TState, TBatch, THistory>
    where TState : class
    where TBatch : class
    where THistory : class
{
    private readonly object _sync = new();
    private readonly IVersionedOperationModel<TState, TBatch, THistory> _model;
    private readonly Stack<THistory> _undo = new();
    private readonly Stack<THistory> _redo = new();
    private TState? _state;
    private VersionedWorkspaceHealth _health = VersionedWorkspaceHealth.Healthy;

    public VersionedWorkspace(IVersionedOperationModel<TState, TBatch, THistory> model)
        => _model = model ?? throw new ArgumentNullException(nameof(model));

    public bool IsInitialized
    {
        get
        {
            lock (_sync)
                return _state is not null;
        }
    }

    public VersionedWorkspaceHealth Health
    {
        get
        {
            lock (_sync)
                return _health;
        }
    }

    public Guid StateId => Read(_model.GetStateId);

    public bool CanUndo
    {
        get
        {
            lock (_sync)
            {
                EnsureUsableLocked();
                return _undo.Count > 0;
            }
        }
    }

    public bool CanRedo
    {
        get
        {
            lock (_sync)
            {
                EnsureUsableLocked();
                return _redo.Count > 0;
            }
        }
    }

    public void Initialize(TState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_sync)
        {
            if (_state is not null)
                throw new InvalidOperationException("版本化工作区已经初始化。");
            SetStateLocked(state);
        }
    }

    /// <summary>在 Session 的受控生命周期转换中替换完整状态并清空提交历史。</summary>
    public void Reset(TState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_sync)
        {
            if (_state is null)
                throw new InvalidOperationException("版本化工作区尚未初始化。");
            SetStateLocked(state);
        }
    }

    public TResult Read<TResult>(Func<TState, TResult> reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        lock (_sync)
        {
            EnsureUsableLocked();
            return reader(_state!);
        }
    }

    /// <summary>
    /// 在工作区写锁内执行附属状态变更。用于把暂存区准备、提交和消费组成一个不可分割操作。
    /// 回调不得把可变领域状态或依赖锁保护的引用泄漏给调用方。
    /// </summary>
    public TResult ExecuteExclusive<TResult>(Func<TResult> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_sync)
        {
            EnsureUsableLocked();
            return action();
        }
    }

    public VersionedCommitResult<THistory> Commit(TBatch batch, Guid expectedStateId)
    {
        ArgumentNullException.ThrowIfNull(batch);
        lock (_sync)
        {
            EnsureExpectedLocked(expectedStateId);
            Guid previous = _model.GetStateId(_state!);
            AtomicApplyResult<TState, THistory> applied = ExecuteAtomicLocked(() => _model.Apply(_state!, batch));
            if (!applied.Changed)
                return VersionedCommitResult<THistory>.Unchanged(previous);
            Guid current = EnsureVersionAdvancedLocked(previous, applied.State);
            _state = applied.State;
            _undo.Push(applied.History!);
            _redo.Clear();
            return new VersionedCommitResult<THistory>(Guid.NewGuid(), previous, current, applied.History);
        }
    }

    public VersionedCommitResult<THistory> Undo(Guid expectedStateId)
    {
        lock (_sync)
        {
            EnsureExpectedLocked(expectedStateId);
            if (!_undo.TryPeek(out THistory? history))
                throw new InvalidOperationException("没有可撤销的操作。");
            Guid previous = _model.GetStateId(_state!);
            AtomicApplyResult<TState, THistory> applied = ExecuteAtomicLocked(() => _model.Undo(_state!, history));
            if (!applied.Changed)
                return VersionedCommitResult<THistory>.Unchanged(previous);
            Guid current = EnsureVersionAdvancedLocked(previous, applied.State);
            _state = applied.State;
            _undo.Pop();
            _redo.Push(history);
            return new VersionedCommitResult<THistory>(Guid.NewGuid(), previous, current, applied.History);
        }
    }

    public VersionedCommitResult<THistory> Redo(Guid expectedStateId)
    {
        lock (_sync)
        {
            EnsureExpectedLocked(expectedStateId);
            if (!_redo.TryPeek(out THistory? history))
                throw new InvalidOperationException("没有可重做的操作。");
            Guid previous = _model.GetStateId(_state!);
            AtomicApplyResult<TState, THistory> applied = ExecuteAtomicLocked(() => _model.Redo(_state!, history));
            if (!applied.Changed)
                return VersionedCommitResult<THistory>.Unchanged(previous);
            Guid current = EnsureVersionAdvancedLocked(previous, applied.State);
            _state = applied.State;
            _redo.Pop();
            _undo.Push(history);
            return new VersionedCommitResult<THistory>(Guid.NewGuid(), previous, current, applied.History);
        }
    }

    private AtomicApplyResult<TState, THistory> ExecuteAtomicLocked(Func<AtomicApplyResult<TState, THistory>> action)
    {
        try
        {
            return action();
        }
        catch (Exception exception) when (_model.IsRollbackFailure(exception))
        {
            _health = VersionedWorkspaceHealth.Faulted;
            throw;
        }
    }

    private Guid EnsureVersionAdvancedLocked(Guid previousStateId, TState state)
    {
        Guid stateId = _model.GetStateId(state);
        if (stateId == previousStateId)
        {
            _health = VersionedWorkspaceHealth.Faulted;
            throw new InvalidOperationException("领域模型报告状态已改变，但没有推进状态标识；工作区已进入 Faulted。");
        }
        return stateId;
    }

    private void EnsureExpectedLocked(Guid expectedStateId)
    {
        EnsureUsableLocked();
        Guid actualStateId = _model.GetStateId(_state!);
        if (actualStateId != expectedStateId)
            throw new OptimisticConcurrencyConflictException(expectedStateId, actualStateId);
    }

    private void EnsureUsableLocked()
    {
        if (_state is null)
            throw new InvalidOperationException("版本化工作区尚未初始化。");
        if (_health == VersionedWorkspaceHealth.Faulted)
            throw new InvalidOperationException("版本化工作区因原子恢复失败已进入 Faulted 状态。");
    }

    private void SetStateLocked(TState state)
    {
        _state = state;
        _undo.Clear();
        _redo.Clear();
        _health = VersionedWorkspaceHealth.Healthy;
    }
}
