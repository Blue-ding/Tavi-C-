using Tavi.Utilities.Concurrency;
using Xunit;

namespace Tavi.Utilities.Tests;

public sealed class VersionedWorkspaceTests
{
    [Fact]
    public void Commit_checks_expected_version_and_records_history()
    {
        TestState initial = TestState.Create(1);
        var workspace = CreateWorkspace(initial);

        VersionedCommitResult<TestHistory> result = workspace.Commit(new TestBatch(2), initial.StateId);

        Assert.True(result.Changed);
        Assert.Equal(initial.StateId, result.PreviousStateId);
        Assert.Equal(3, workspace.Read(state => state.Value));
        Assert.True(workspace.CanUndo);
        Assert.False(workspace.CanRedo);
    }

    [Fact]
    public void Conflict_does_not_execute_or_change_state()
    {
        TestState initial = TestState.Create(1);
        var model = new TestModel();
        var workspace = new VersionedWorkspace<TestState, TestBatch, TestHistory>(model);
        workspace.Initialize(initial);

        OptimisticConcurrencyConflictException exception = Assert.Throws<OptimisticConcurrencyConflictException>(
            () => workspace.Commit(new TestBatch(2), Guid.NewGuid()));

        Assert.Equal(initial.StateId, exception.ActualStateId);
        Assert.Equal(0, model.ApplyCount);
        Assert.Equal(initial, workspace.Read(state => state));
    }

    [Fact]
    public void Undo_and_redo_are_new_versioned_commits()
    {
        TestState initial = TestState.Create(1);
        var workspace = CreateWorkspace(initial);
        VersionedCommitResult<TestHistory> committed = workspace.Commit(new TestBatch(2), initial.StateId);

        VersionedCommitResult<TestHistory> undone = workspace.Undo(committed.StateId);
        Assert.Equal(1, workspace.Read(state => state.Value));
        VersionedCommitResult<TestHistory> redone = workspace.Redo(undone.StateId);

        Assert.Equal(3, workspace.Read(state => state.Value));
        Assert.NotEqual(committed.StateId, undone.StateId);
        Assert.NotEqual(undone.StateId, redone.StateId);
        Assert.True(workspace.CanUndo);
        Assert.False(workspace.CanRedo);
    }

    [Fact]
    public void New_commit_after_undo_clears_redo()
    {
        TestState initial = TestState.Create(1);
        var workspace = CreateWorkspace(initial);
        VersionedCommitResult<TestHistory> first = workspace.Commit(new TestBatch(1), initial.StateId);
        VersionedCommitResult<TestHistory> undone = workspace.Undo(first.StateId);

        _ = workspace.Commit(new TestBatch(5), undone.StateId);

        Assert.False(workspace.CanRedo);
    }

    [Fact]
    public void Unchanged_apply_does_not_create_commit_or_history()
    {
        TestState initial = TestState.Create(1);
        var workspace = CreateWorkspace(initial);

        VersionedCommitResult<TestHistory> result = workspace.Commit(new TestBatch(0), initial.StateId);

        Assert.False(result.Changed);
        Assert.Equal(Guid.Empty, result.CommitId);
        Assert.False(workspace.CanUndo);
        Assert.Equal(initial, workspace.Read(state => state));
    }

    [Fact]
    public void Recovery_failure_faults_workspace()
    {
        TestState initial = TestState.Create(1);
        var workspace = CreateWorkspace(initial);

        Assert.Throws<AtomicStateRecoveryException>(() => workspace.Commit(new TestBatch(1, FailRecovery: true), initial.StateId));

        Assert.Equal(VersionedWorkspaceHealth.Faulted, workspace.Health);
        Assert.Throws<InvalidOperationException>(() => workspace.Read(state => state.Value));
    }

    [Fact]
    public void Exclusive_callback_can_commit_and_consume_staging_atomically()
    {
        TestState initial = TestState.Create(1);
        var workspace = CreateWorkspace(initial);
        var staged = new List<TestBatch> { new(2) };

        VersionedCommitResult<TestHistory> result = workspace.ExecuteExclusive(() =>
        {
            VersionedCommitResult<TestHistory> commit = workspace.Commit(staged[0], initial.StateId);
            staged.Clear();
            return commit;
        });

        Assert.True(result.Changed);
        Assert.Empty(staged);
        Assert.Equal(3, workspace.Read(state => state.Value));
    }

    [Fact]
    public async Task Concurrent_commits_with_same_expected_version_allow_only_one_winner()
    {
        TestState initial = TestState.Create(0);
        var model = new TestModel();
        var workspace = new VersionedWorkspace<TestState, TestBatch, TestHistory>(model);
        workspace.Initialize(initial);
        using var start = new ManualResetEventSlim();

        Task<object> first = Task.Run(() => TryCommit(workspace, initial.StateId, start));
        Task<object> second = Task.Run(() => TryCommit(workspace, initial.StateId, start));
        start.Set();
        object[] outcomes = await Task.WhenAll(first, second);

        Assert.Single(outcomes.OfType<VersionedCommitResult<TestHistory>>());
        Assert.Single(outcomes.OfType<OptimisticConcurrencyConflictException>());
        Assert.Equal(1, model.ApplyCount);
        Assert.Equal(1, workspace.Read(state => state.Value));
    }

    private static VersionedWorkspace<TestState, TestBatch, TestHistory> CreateWorkspace(TestState initial)
    {
        var workspace = new VersionedWorkspace<TestState, TestBatch, TestHistory>(new TestModel());
        workspace.Initialize(initial);
        return workspace;
    }

    private static object TryCommit(
        VersionedWorkspace<TestState, TestBatch, TestHistory> workspace,
        Guid expectedStateId,
        ManualResetEventSlim start)
    {
        start.Wait();
        try
        {
            return workspace.Commit(new TestBatch(1), expectedStateId);
        }
        catch (OptimisticConcurrencyConflictException exception)
        {
            return exception;
        }
    }

    private sealed record TestState(Guid StateId, int Value)
    {
        internal static TestState Create(int value) => new(Guid.NewGuid(), value);
    }

    private sealed record TestBatch(int Delta, bool FailRecovery = false);
    private sealed record TestHistory(TestState Before, TestState After);

    private sealed class TestModel : IVersionedOperationModel<TestState, TestBatch, TestHistory>
    {
        internal int ApplyCount { get; private set; }

        public Guid GetStateId(TestState state) => state.StateId;

        public AtomicApplyResult<TestState, TestHistory> Apply(TestState state, TestBatch batch)
        {
            ApplyCount++;
            if (batch.FailRecovery)
                throw new AtomicStateRecoveryException(new InvalidOperationException("recovery failed"));
            if (batch.Delta == 0)
                return AtomicApplyResult<TestState, TestHistory>.Unchanged(state);
            TestState after = TestState.Create(state.Value + batch.Delta);
            return AtomicApplyResult<TestState, TestHistory>.ChangedState(after, new TestHistory(state, after));
        }

        public AtomicApplyResult<TestState, TestHistory> Undo(TestState state, TestHistory history)
        {
            TestState restored = TestState.Create(history.Before.Value);
            return AtomicApplyResult<TestState, TestHistory>.ChangedState(restored, new TestHistory(state, restored));
        }

        public AtomicApplyResult<TestState, TestHistory> Redo(TestState state, TestHistory history)
        {
            TestState restored = TestState.Create(history.After.Value);
            return AtomicApplyResult<TestState, TestHistory>.ChangedState(restored, new TestHistory(state, restored));
        }

        public bool IsRollbackFailure(Exception exception) => exception is AtomicStateRecoveryException;
    }
}
