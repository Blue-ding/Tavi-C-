using Tavi.Application.World;
using Tavi.Domain.World;
using Xunit;

namespace Tavi.Application.Tests;

public sealed class WorldSessionTests
{
    [Fact]
    public async Task InitializeLoadsExistingWorld()
    {
        WorldGraph graph = WorldGraph.Create(new WorldSnapshot());
        Guid anchorId = graph.AddAnchor("Loaded", "", AnchorType.Item);
        var store = new RecordingWorldStore(graph.CreateSnapshot());
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

    private sealed class RecordingWorldStore(WorldSnapshot? snapshot) : IWorldStore
    {
        internal TaskCompletionSource<WorldSnapshot> Saved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int SaveCount { get; private set; }

        public Task<WorldSnapshot?> LoadAsync(string slot, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(snapshot);
        }

        public Task SaveAsync(string slot, WorldSnapshot savedSnapshot, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            Saved.TrySetResult(savedSnapshot);
            return Task.CompletedTask;
        }
    }
}
