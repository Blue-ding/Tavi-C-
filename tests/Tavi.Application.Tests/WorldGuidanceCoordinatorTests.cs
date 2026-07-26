using Tavi.Application.Extensions;
using Tavi.Application.Guidance;
using Tavi.Application.World;
using Tavi.Domain;
using Tavi.Domain.World;
using Xunit;

namespace Tavi.Application.Tests;

public sealed class WorldGuidanceCoordinatorTests
{
    [Fact]
    public async Task ProposalIsStagedThroughGuidanceContributor()
    {
        var catalog = ModuleCatalog.Create([]);
        await using var world = new WorldBuildSession(new MemoryWorldStore(), catalog, autoSaveDelay: TimeSpan.FromHours(1));
        await world.InitializeAsync();
        var coordinator = new WorldGuidanceCoordinator(world, world, world);
        WorldProposal proposal = CreateElementProposal(world.StateId);

        WorldProposal published = coordinator.StageProposal(proposal);

        WorldStagedChange staged = Assert.Single(world.CreateStagingSnapshot().Changes);
        Assert.Equal(WorldStagedChangeSource.Guidance, staged.Source);
        Assert.Equal(staged.Id.ToString(), Assert.Single(published.Changes).Id);
    }

    [Fact]
    public async Task ProposalIsRejectedWhenCommittedWorldLeavesItsBasis()
    {
        var catalog = ModuleCatalog.Create([]);
        await using var world = new WorldBuildSession(new MemoryWorldStore(), catalog, autoSaveDelay: TimeSpan.FromHours(1));
        await world.InitializeAsync();
        var coordinator = new WorldGuidanceCoordinator(world, world, world);
        WorldProposal proposal = CreateElementProposal(world.StateId);
        world.Commands.Apply(
            new WorldChangeSet([WorldOperations.AddElement("Player", string.Empty, ElementType.None)]),
            world.StateId);

        WorldStateConflictException exception = Assert.Throws<WorldStateConflictException>(
            () => coordinator.StageProposal(proposal));

        Assert.Equal(proposal.BaseWorldStateId, exception.ExpectedStateId);
        Assert.Empty(world.CreateStagingSnapshot().Changes);
    }

    private static WorldProposal CreateElementProposal(Guid baseWorldStateId)
    {
        var change = new ProposeAddElement(
            Guid.NewGuid().ToString("N"),
            "构筑测试",
            ProposalElementId.New(),
            "Guidance",
            string.Empty,
            ElementType.None);
        return new WorldProposal
        {
            Id = Guid.NewGuid(),
            BaseWorldStateId = baseWorldStateId,
            Changes = [change]
        };
    }

    private sealed class MemoryWorldStore : IWorldStore
    {
        private WorldSnapshot? _snapshot;

        public Task<WorldSnapshot?> LoadAsync(string slot, CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshot);

        public Task SaveAsync(string slot, WorldSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            _snapshot = Tavi.Domain.World.World.Create(snapshot).CreateSnapshot();
            return Task.CompletedTask;
        }
    }
}
