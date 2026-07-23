using Tavi.Application.World;
using Tavi.Domain.World;
using Xunit;
using RuntimeWorld = Tavi.Domain.World.World;

namespace Tavi.Application.Tests;

public sealed class WorldSessionTests
{
    [Fact]
    public async Task InitializeLoadsExistingWorldThroughQueries()
    {
        RuntimeWorld world = RuntimeWorld.Create(new WorldSnapshot());
        Guid anchorId = world.AddAnchor("Loaded", "", AnchorType.Item);
        var store = new RecordingWorldStore(world.CreateSnapshot());
        await using var session = new WorldSession(store, autoSaveDelay: TimeSpan.FromMinutes(1));

        await session.InitializeAsync();

        Assert.Equal(anchorId, session.Queries.GetAnchor(anchorId).Id);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public async Task SuccessfulApplyTriggersDebouncedSave()
    {
        var store = new RecordingWorldStore(new WorldSnapshot());
        await using var session = new WorldSession(store, autoSaveDelay: TimeSpan.FromMilliseconds(20));
        await session.InitializeAsync();

        session.Apply(WorldOperations.Single(WorldOperations.AddAnchor("Saved", "", AnchorType.Item)), session.Revision);
        WorldSnapshot saved = await store.Saved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Single(saved.Anchors);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public async Task DisposeFlushesDirtyNewWorld()
    {
        var store = new RecordingWorldStore(null);
        var session = new WorldSession(store, autoSaveDelay: TimeSpan.FromMinutes(1));
        await session.InitializeAsync();

        await session.DisposeAsync();

        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task AtomicGroupRaisesOneSessionEvent()
    {
        var store = new RecordingWorldStore(new WorldSnapshot());
        await using var session = new WorldSession(store, autoSaveDelay: TimeSpan.FromMinutes(1));
        await session.InitializeAsync();
        var observed = new List<WorldSessionChangedEventArgs>();
        session.Changed += (_, eventArgs) =>
        {
            observed.Add(eventArgs);
            _ = session.Queries.GetAnchors();
        };
        AddAnchorOperation first = WorldOperations.AddAnchor("First", "", AnchorType.Item);
        AddAnchorOperation second = WorldOperations.AddAnchor("Second", "", AnchorType.Item);

        WorldCommitResult commit = session.Apply(WorldOperations.Combine(first, second), session.Revision);

        WorldSessionChangedEventArgs change = Assert.Single(observed);
        Assert.Equal(1, commit.Revision);
        Assert.Equal(commit.Revision, change.Revision);
        Assert.Equal(WorldSessionOperation.Apply, change.Operation);
        Assert.Equal(2, change.ChangeSet.Forward.Operations.Count);
    }

    [Fact]
    public async Task PartialFailureRollsBackWithoutDirtyingSession()
    {
        var store = new RecordingWorldStore(new WorldSnapshot());
        await using var session = new WorldSession(store, autoSaveDelay: TimeSpan.FromMinutes(1));
        await session.InitializeAsync();
        AddAnchorOperation first = WorldOperations.AddAnchor("Alice", "", AnchorType.Character);
        AddAnchorOperation duplicate = WorldOperations.AddAnchor("Alice", "", AnchorType.Character);

        Assert.Throws<WorldException>(() => session.Apply(WorldOperations.Combine(first, duplicate), session.Revision));

        Assert.False(session.IsDirty);
        Assert.Equal(0, session.Revision);
        Assert.Empty(session.Queries.GetAnchors());
        Assert.Equal(WorldSessionHealth.Healthy, session.Health);
    }

    [Fact]
    public async Task QueriesReturnDetachedEntities()
    {
        RuntimeWorld world = RuntimeWorld.Create(new WorldSnapshot());
        Guid anchorId = world.AddAnchor("Anchor", "Original", AnchorType.Item);
        var store = new RecordingWorldStore(world.CreateSnapshot());
        await using var session = new WorldSession(store, autoSaveDelay: TimeSpan.FromMinutes(1));
        await session.InitializeAsync();

        Anchor first = session.Queries.GetAnchor(anchorId);
        Anchor second = session.Queries.GetAnchor(anchorId);

        Assert.NotSame(first, second);
        first.UpdateDescription("Changed outside");
        Assert.Equal("Original", session.Queries.GetAnchor(anchorId).Description);
    }

    [Fact]
    public async Task RevisionConflictDoesNotApplyChange()
    {
        var store = new RecordingWorldStore(new WorldSnapshot());
        await using var session = new WorldSession(store, autoSaveDelay: TimeSpan.FromMinutes(1));
        await session.InitializeAsync();
        session.Apply(WorldOperations.Single(WorldOperations.AddAnchor("First", "", AnchorType.Item)), 0);

        WorldRevisionConflictException exception = Assert.Throws<WorldRevisionConflictException>(() =>
            session.Apply(WorldOperations.Single(WorldOperations.AddAnchor("Stale", "", AnchorType.Item)), 0));

        Assert.Equal(0, exception.ExpectedRevision);
        Assert.Equal(1, exception.ActualRevision);
        Assert.Single(session.Queries.GetAnchors());
    }

    [Fact]
    public async Task UndoAndRedoAreNewAtomicCommits()
    {
        var store = new RecordingWorldStore(new WorldSnapshot());
        await using var session = new WorldSession(store, autoSaveDelay: TimeSpan.FromMinutes(1));
        await session.InitializeAsync();
        AddAnchorOperation add = WorldOperations.AddAnchor("Anchor", "", AnchorType.Item);
        session.Apply(WorldOperations.Single(add), 0);

        WorldCommitResult undo = session.Undo(1);
        WorldCommitResult redo = session.Redo(2);

        Assert.Equal(2, undo.Revision);
        Assert.Equal(3, redo.Revision);
        Assert.Equal(add.AnchorId, Assert.Single(session.Queries.GetAnchors()).Id);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public async Task StateChangedReportsDirtyAndSuccessfulSaveTransitions()
    {
        var store = new RecordingWorldStore(new WorldSnapshot());
        await using var session = new WorldSession(store, autoSaveDelay: TimeSpan.FromMinutes(1));
        await session.InitializeAsync();
        var changes = new List<WorldSessionStateChangedEventArgs>();
        session.StateChanged += (_, eventArgs) => changes.Add(eventArgs);

        session.Apply(WorldOperations.Single(WorldOperations.AddAnchor("Changed", "", AnchorType.Item)), session.Revision);
        await session.SaveAsync();

        Assert.Collection(
            changes,
            change => { Assert.Equal(WorldSessionStateChange.DirtyChanged, change.Change); Assert.True(change.IsDirty); },
            change => { Assert.Equal(WorldSessionStateChange.SaveStarted, change.Change); Assert.True(change.IsDirty); },
            change => { Assert.Equal(WorldSessionStateChange.DirtyChanged, change.Change); Assert.False(change.IsDirty); },
            change => { Assert.Equal(WorldSessionStateChange.SaveCompleted, change.Change); Assert.False(change.IsDirty); });
    }

    [Fact]
    public async Task StateChangedReportsSaveFailure()
    {
        var expected = new IOException("save failed");
        var store = new RecordingWorldStore(new WorldSnapshot()) { SaveException = expected };
        await using var session = new WorldSession(store, autoSaveDelay: TimeSpan.FromMinutes(1));
        await session.InitializeAsync();
        var changes = new List<WorldSessionStateChangedEventArgs>();
        session.StateChanged += (_, eventArgs) => changes.Add(eventArgs);
        session.Apply(WorldOperations.Single(WorldOperations.AddAnchor("Changed", "", AnchorType.Item)), session.Revision);

        IOException actual = await Assert.ThrowsAsync<IOException>(() => session.SaveAsync());

        Assert.Same(expected, actual);
        WorldSessionStateChangedEventArgs failure = Assert.Single(changes, change => change.Change == WorldSessionStateChange.SaveFailed);
        Assert.True(failure.IsDirty);
        Assert.Same(expected, failure.Exception);
    }

    [Fact]
    public async Task StateChangedReportsSaveCancellation()
    {
        var expected = new OperationCanceledException("save cancelled");
        var store = new RecordingWorldStore(new WorldSnapshot()) { SaveException = expected };
        await using var session = new WorldSession(store, autoSaveDelay: TimeSpan.FromMinutes(1));
        await session.InitializeAsync();
        var changes = new List<WorldSessionStateChangedEventArgs>();
        session.StateChanged += (_, eventArgs) => changes.Add(eventArgs);
        session.Apply(WorldOperations.Single(WorldOperations.AddAnchor("Changed", "", AnchorType.Item)), session.Revision);

        OperationCanceledException actual = await Assert.ThrowsAsync<OperationCanceledException>(() => session.SaveAsync());

        Assert.Same(expected, actual);
        WorldSessionStateChangedEventArgs cancellation = Assert.Single(changes, change => change.Change == WorldSessionStateChange.SaveCancelled);
        Assert.True(cancellation.IsDirty);
        Assert.Same(expected, cancellation.Exception);
    }

    private sealed class RecordingWorldStore(WorldSnapshot? snapshot) : IWorldStore
    {
        internal TaskCompletionSource<WorldSnapshot> Saved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int SaveCount { get; private set; }
        internal Exception? SaveException { get; set; }

        public Task<WorldSnapshot?> LoadAsync(string slot, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);

        public Task SaveAsync(string slot, WorldSnapshot savedSnapshot, CancellationToken cancellationToken = default)
        {
            if (SaveException is not null)
            {
                Exception exception = SaveException;
                SaveException = null;
                return Task.FromException(exception);
            }
            SaveCount++;
            Saved.TrySetResult(savedSnapshot);
            return Task.CompletedTask;
        }
    }
}
