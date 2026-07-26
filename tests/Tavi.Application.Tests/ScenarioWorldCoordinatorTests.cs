using Tavi.Application.Extensions;
using Tavi.Application.Scenario;
using Tavi.Application.World;
using Tavi.Domain;
using Tavi.Domain.World;
using Xunit;
using ScenarioUpdateAspectOperation = Tavi.Domain.Scenario.UpdateAspectOperation;
using ScenarioChangeSet = Tavi.Domain.Scenario.ScenarioChangeSet;
using WorldUpdateAspectQuantityOperation = Tavi.Domain.World.UpdateAspectQuantityOperation;

namespace Tavi.Application.Tests;

public sealed class ScenarioWorldCoordinatorTests
{
    [Fact]
    public async Task CompletedOutcomeStagesAndCommitsAsSingleScenarioChange()
    {
        TestGraph graph = CreateGraph();
        var catalog = ModuleCatalog.Create([]);
        var worldStore = new MemoryWorldStore(graph.World);
        await using var world = new WorldSession(worldStore, catalog, autoSaveDelay: TimeSpan.FromHours(1));
        await world.InitializeAsync();
        var scenarioStore = new MemoryScenarioStore();
        await using var scenario = new ScenarioSession(scenarioStore, catalog, world.Queries.CreateSnapshot(), autoSaveDelay: TimeSpan.FromHours(1));
        await scenario.InitializeAsync();
        var coordinator = new ScenarioWorldCoordinator();

        Assert.Single(scenario.Queries.GetAspects());
        scenario.Commands.Apply(new ScenarioChangeSet([new ScenarioUpdateAspectOperation(graph.AspectId, 3, AspectType.None)]), scenario.StateId);

        ScenarioWorldStageResult staged = coordinator.StageOutcome(world, scenario, world.StateId, scenario.StateId);

        Assert.True(staged.Changed);
        WorldStagedChange change = Assert.Single(world.CreateStagingSnapshot().Changes);
        Assert.Equal(WorldStagedChangeSource.Scenario, change.Source);
        var operation = Assert.IsType<WorldUpdateAspectQuantityOperation>(Assert.Single(change.ChangeSet.Operations));
        Assert.Equal(3, operation.Quantity);

        world.Commands.CommitStaged([staged.ChangeId!.Value], world.StateId);
        Assert.Equal(3, world.Queries.GetAspect(graph.AspectId).Aspect.Quantity);
    }

    [Fact]
    public async Task OutcomeIsRejectedAfterWorldLeavesScenarioOrigin()
    {
        TestGraph graph = CreateGraph();
        var catalog = ModuleCatalog.Create([]);
        var worldStore = new MemoryWorldStore(graph.World);
        await using var world = new WorldSession(worldStore, catalog, autoSaveDelay: TimeSpan.FromHours(1));
        await world.InitializeAsync();
        await using var scenario = new ScenarioSession(new MemoryScenarioStore(), catalog, world.Queries.CreateSnapshot(), autoSaveDelay: TimeSpan.FromHours(1));
        await scenario.InitializeAsync();
        var coordinator = new ScenarioWorldCoordinator();
        scenario.Commands.Apply(new ScenarioChangeSet([new ScenarioUpdateAspectOperation(graph.AspectId, 3, AspectType.None)]), scenario.StateId);
        world.Commands.Apply(new WorldChangeSet([new WorldUpdateAspectQuantityOperation(graph.AspectId, 4)]), world.StateId);

        ScenarioApplicationException exception = Assert.Throws<ScenarioApplicationException>(
            () => coordinator.StageOutcome(world, scenario, world.StateId, scenario.StateId));

        Assert.Equal(ScenarioApplicationErrorCodes.StateConflict, exception.ErrorCode);
        Assert.Empty(world.CreateStagingSnapshot().Changes);
    }

    [Fact]
    public async Task OutcomeRequiresAnEmptyWorldStagingArea()
    {
        TestGraph graph = CreateGraph();
        var catalog = ModuleCatalog.Create([]);
        var worldStore = new MemoryWorldStore(graph.World);
        await using var world = new WorldSession(worldStore, catalog, autoSaveDelay: TimeSpan.FromHours(1));
        await world.InitializeAsync();
        await using var scenario = new ScenarioSession(new MemoryScenarioStore(), catalog, world.Queries.CreateSnapshot(), autoSaveDelay: TimeSpan.FromHours(1));
        await scenario.InitializeAsync();
        var coordinator = new ScenarioWorldCoordinator();
        _ = world.Commands.Stage(new WorldUpdateAspectQuantityOperation(graph.AspectId, 4));

        ScenarioApplicationException exception = Assert.Throws<ScenarioApplicationException>(
            () => coordinator.StageOutcome(world, scenario, world.StateId, scenario.StateId));

        Assert.Equal(ScenarioApplicationErrorCodes.InvalidSessionState, exception.ErrorCode);
    }

    private static TestGraph CreateGraph()
    {
        Guid elementId = Guid.NewGuid();
        Guid scopeId = Guid.NewGuid();
        Guid aspectId = Guid.NewGuid();
        Guid localAspectId = Guid.NewGuid();
        var snapshot = new WorldSnapshot
        {
            Id = Guid.NewGuid(),
            Elements = new Dictionary<Guid, Element> { [elementId] = new(elementId, "Mage", string.Empty, ElementType.None) },
            Scopes = new Dictionary<Guid, Scope> { [scopeId] = new(scopeId, 1, ScopeType.None, elementId) },
            Aspects = new Dictionary<Guid, Aspect> { [aspectId] = new(aspectId, 5, AspectType.None, elementId, scopeId) },
            LocalAspects = new Dictionary<Guid, LocalAspect> { [localAspectId] = new(localAspectId, "Secret", string.Empty, 1, elementId, scopeId) }
        };
        return new TestGraph(snapshot, aspectId);
    }

    private sealed record TestGraph(WorldSnapshot World, Guid AspectId);

    private sealed class MemoryWorldStore(WorldSnapshot snapshot) : IWorldStore
    {
        private WorldSnapshot _snapshot = Tavi.Domain.World.World.Create(snapshot).CreateSnapshot();

        public Task<WorldSnapshot?> LoadAsync(string slot, CancellationToken cancellationToken = default)
            => Task.FromResult<WorldSnapshot?>(Tavi.Domain.World.World.Create(_snapshot).CreateSnapshot());

        public Task SaveAsync(string slot, WorldSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            _snapshot = Tavi.Domain.World.World.Create(snapshot).CreateSnapshot();
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryScenarioStore : IScenarioStore
    {
        private Tavi.Domain.Scenario.ScenarioSnapshot? _snapshot;

        public Task<Tavi.Domain.Scenario.ScenarioSnapshot?> LoadAsync(string slot, CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshot is null ? null : Tavi.Domain.Scenario.Scenario.Create(_snapshot).CreateSnapshot());

        public Task SaveAsync(string slot, Tavi.Domain.Scenario.ScenarioSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            _snapshot = Tavi.Domain.Scenario.Scenario.Create(snapshot).CreateSnapshot();
            return Task.CompletedTask;
        }
    }
}
