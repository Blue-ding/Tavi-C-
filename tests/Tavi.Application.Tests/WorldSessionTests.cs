using Tavi.Application.World;
using Tavi.Domain.World;
using Xunit;
using RuntimeWorld = Tavi.Domain.World.World;

namespace Tavi.Application.Tests;

public sealed class WorldSessionTests
{
    [Fact]
    public async Task InitializeLoadsExistingWorld()
    {
        RuntimeWorld world = RuntimeWorld.Create(new WorldSnapshot());
        Guid anchorId = world.AddAnchor("Loaded", "", AnchorType.Item);
        var store = new RecordingWorldStore(world.CreateSnapshot());
        await using var session = new WorldSession(store, autoSaveDelay: TimeSpan.FromMinutes(1));

        await session.InitializeAsync();

        Assert.Equal(anchorId, session.Current.GetAnchor(anchorId).Id);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public async Task SuccessfulUpdateTriggersDebouncedSave()
    {
        var store = new RecordingWorldStore(new WorldSnapshot());
        await using var session = new WorldSession(store, autoSaveDelay: TimeSpan.FromMilliseconds(20));
        await session.InitializeAsync();

        session.Update(graph => graph.AddAnchor("Saved", "", AnchorType.Item));
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
    public async Task SessionRepackagesDirectWorldChangesAndAutoSaves()
    {
        var store = new RecordingWorldStore(new WorldSnapshot());
        await using var session = new WorldSession(store, autoSaveDelay: TimeSpan.FromMilliseconds(20));
        await session.InitializeAsync();
        WorldSessionChangedEventArgs? observed = null;
        session.Changed += (_, eventArgs) => observed = eventArgs;

        session.Current.AddAnchor("Direct", "", AnchorType.Item);
        WorldSnapshot saved = await store.Saved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(observed);
        Assert.Equal(nameof(RuntimeWorld.AddAnchor), observed.Operation);
        Assert.Equal(session.Current.Revision, observed.Revision);
        Assert.Single(saved.Anchors);
    }

    [Fact]
    public async Task PartialUpdateFailureStillLeavesSessionDirty()
    {
        var store = new RecordingWorldStore(new WorldSnapshot());
        await using var session = new WorldSession(store, autoSaveDelay: TimeSpan.FromMinutes(1));
        await session.InitializeAsync();

        Assert.Throws<WorldException>(() => session.Update(world =>
        {
            world.AddAnchor("Alice", "", AnchorType.Character);
            world.AddAnchor("Alice", "", AnchorType.Character);
        }));

        Assert.True(session.IsDirty);
        Assert.Single(session.Read(world => world.GetAnchors()));
    }

    [Fact]
    public async Task StateChangedReportsDirtyAndSuccessfulSaveTransitions()
    {
        var store = new RecordingWorldStore(new WorldSnapshot());
        await using var session = new WorldSession(store, autoSaveDelay: TimeSpan.FromMinutes(1));
        await session.InitializeAsync();
        var changes = new List<WorldSessionStateChangedEventArgs>();
        session.StateChanged += (_, eventArgs) => changes.Add(eventArgs);

        session.Update(world => world.AddAnchor("Changed", "", AnchorType.Item));
        await session.SaveAsync();

        Assert.Collection(
            changes,
            change =>
            {
                Assert.Equal(WorldSessionStateChange.DirtyChanged, change.Change);
                Assert.True(change.IsDirty);
            },
            change =>
            {
                Assert.Equal(WorldSessionStateChange.SaveStarted, change.Change);
                Assert.True(change.IsDirty);
            },
            change =>
            {
                Assert.Equal(WorldSessionStateChange.DirtyChanged, change.Change);
                Assert.False(change.IsDirty);
            },
            change =>
            {
                Assert.Equal(WorldSessionStateChange.SaveCompleted, change.Change);
                Assert.False(change.IsDirty);
            }
        );
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
        session.Update(world => world.AddAnchor("Changed", "", AnchorType.Item));

        IOException actual = await Assert.ThrowsAsync<IOException>(() => session.SaveAsync());

        Assert.Same(expected, actual);
        WorldSessionStateChangedEventArgs failure =
            Assert.Single(changes, change => change.Change == WorldSessionStateChange.SaveFailed);
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
        session.Update(world => world.AddAnchor("Changed", "", AnchorType.Item));

        OperationCanceledException actual =
            await Assert.ThrowsAsync<OperationCanceledException>(() => session.SaveAsync());

        Assert.Same(expected, actual);
        WorldSessionStateChangedEventArgs cancellation =
            Assert.Single(changes, change => change.Change == WorldSessionStateChange.SaveCancelled);
        Assert.True(cancellation.IsDirty);
        Assert.Same(expected, cancellation.Exception);
    }

    private sealed class RecordingWorldStore(WorldSnapshot? snapshot) : IWorldStore
    {
        internal TaskCompletionSource<WorldSnapshot> Saved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int SaveCount { get; private set; }
        internal Exception? SaveException { get; set; }

        public Task<WorldSnapshot?> LoadAsync(string slot, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(snapshot);
        }

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
