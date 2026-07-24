using Tavi.Application.World;
using Tavi.Domain.World;
using Xunit;

namespace Tavi.Application.Tests;

public sealed class WorldStagingTests
{
    [Fact]
    public async Task ConflictingAnchorOperationsAreExcludedUntilOneIsDeleted()
    {
        await using WorldSession session = await CreateSessionAsync();
        Guid anchorId = Guid.NewGuid();
        Guid addId = session.Stage(new AddAnchorOperation(anchorId, "Alice", "", AnchorType.Character));
        Guid deleteId = session.Stage(new RemoveAnchorOperation(anchorId));

        WorldStagingSnapshot conflict = session.CreateStagingSnapshot();

        Assert.All(conflict.Changes, change => Assert.Equal(WorldStagedChangeStatus.Conflict, change.Status));
        Assert.DoesNotContain(anchorId, conflict.ProjectedWorld.Anchors.Keys);
        Assert.True(session.DeleteStaged(deleteId));
        WorldStagingSnapshot resolved = session.CreateStagingSnapshot();
        Assert.Equal(WorldStagedChangeStatus.Valid, Assert.Single(resolved.Changes).Status);
        Assert.Contains(anchorId, resolved.ProjectedWorld.Anchors.Keys);
        Assert.Equal(addId, resolved.Changes[0].Id);
    }

    [Fact]
    public async Task RelationBecomesInvalidWhenItsStagedAnchorIsConflicted()
    {
        await using WorldSession session = await CreateSessionAsync();
        Guid anchorId = Guid.NewGuid();
        Guid otherId = Guid.NewGuid();
        session.Stage(new AddAnchorOperation(anchorId, "Alice", "", AnchorType.Character));
        session.Stage(new AddAnchorOperation(otherId, "Key", "", AnchorType.Item));
        Guid relationId = session.Stage(new AddRelationOperation(Guid.NewGuid(), "持有", "", anchorId, otherId));
        session.Stage(new RemoveAnchorOperation(anchorId));

        WorldStagedChange relation = session.CreateStagingSnapshot().Changes.Single(change => change.Id == relationId);

        Assert.Equal(WorldStagedChangeStatus.Invalid, relation.Status);
        Assert.NotNull(relation.Issue);
    }

    [Fact]
    public async Task PartialCommitRejectsRelationWithoutSelectedAnchorDependencies()
    {
        await using WorldSession session = await CreateSessionAsync();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        session.Stage(new AddAnchorOperation(first, "Alice", "", AnchorType.Character));
        session.Stage(new AddAnchorOperation(second, "Key", "", AnchorType.Item));
        Guid relation = session.Stage(new AddRelationOperation(Guid.NewGuid(), "寻找", "", first, second));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => session.CommitStaged([relation], session.Revision));

        Assert.Contains("依赖", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompoundUpdateIsOneValidStagedItem()
    {
        Guid anchorId = Guid.NewGuid();
        await using WorldSession session = await CreateSessionAsync(new WorldSnapshot { Anchors = { [anchorId] = new Anchor(anchorId, "Alice", "", AnchorType.Character) } });
        Guid changeId = session.Stage(new WorldChangeSet([new UpdateAnchorNameOperation(anchorId, "Alicia"), new UpdateAnchorDescriptionOperation(anchorId, "旅行者")]));

        WorldStagingSnapshot snapshot = session.CreateStagingSnapshot();

        Assert.Equal(changeId, Assert.Single(snapshot.Changes).Id);
        Assert.Equal(WorldStagedChangeStatus.Valid, snapshot.Changes[0].Status);
        Assert.Equal("Alicia", snapshot.ProjectedWorld.Anchors[anchorId].Name);
        Assert.Equal("旅行者", snapshot.ProjectedWorld.Anchors[anchorId].Description);
    }

    private static async Task<WorldSession> CreateSessionAsync(WorldSnapshot? snapshot = null)
    {
        var session = new WorldSession(new MemoryWorldStore(snapshot ?? new WorldSnapshot()));
        await session.InitializeAsync();
        return session;
    }

    private sealed class MemoryWorldStore(WorldSnapshot snapshot) : IWorldStore
    {
        public Task<WorldSnapshot?> LoadAsync(string slot, CancellationToken cancellationToken = default) => Task.FromResult<WorldSnapshot?>(snapshot);
        public Task SaveAsync(string slot, WorldSnapshot snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
