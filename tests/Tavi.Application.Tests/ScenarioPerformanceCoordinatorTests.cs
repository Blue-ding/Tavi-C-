using Tavi.Application.Extensions;
using Tavi.Application.Extensions.Performance;
using Tavi.Application.Performance;
using Tavi.Application.Scenario;
using Tavi.Application.Writing;
using Tavi.Domain.Performance;
using Tavi.Domain.Scenario;
using Tavi.Extensibility;
using Xunit;
using WorldElement = Tavi.Domain.Element;
using WorldElementType = Tavi.Domain.ElementType;
using WorldSnapshot = Tavi.Domain.World.WorldSnapshot;

namespace Tavi.Application.Tests;

public sealed class ScenarioPerformanceCoordinatorTests
{
    [Fact]
    public async Task PerformanceCapableSceneStartsAndCompletesThroughCoordinator()
    {
        TestFixture fixture = CreateFixture(SceneSettlementCapabilities.Performance);
        await using ScenarioSession scenario = await CreateScenarioAsync(fixture);
        var changedOperations = new List<string>();
        scenario.Changed += (_, eventArgs) =>
            changedOperations.Add(eventArgs.Operation);
        Guid sceneId = BeginScene(scenario, fixture);
        var coordinator = new ScenarioPerformanceCoordinator();
        SceneContextView context = coordinator.PrepareStart(scenario, sceneId, scenario.StateId);

        var manuscriptStore = new WritingBeatPublicationTests.MemoryManuscriptStore();
        await using var writing = new WritingSession(manuscriptStore);
        await writing.InitializeAsync();
        Guid manuscriptStateId = (await writing.CreateAsync("Story")).Manuscript!.StateId;
        await using var performance = new PerformanceSession(
            new MemoryPerformanceStore(),
            context,
            1,
            writing,
            fixture.Extensions,
            TimeSpan.FromHours(1));
        await performance.InitializeAsync();

        BeatDefinition beatDefinition = Assert.Single(await performance.Commands.GetBeatDefinitionsAsync(1));
        performance.Commands.CreateBeat(beatDefinition, performance.StateId);
        Guid beatId = Assert.Single(performance.Queries.GetBeats()).Id;
        performance.Commands.SetBeatBinding(beatId, "subject", [fixture.ElementId], performance.StateId);
        performance.Commands.BeginBeatProcessing(beatId, performance.StateId);
        await performance.Commands.ResolveBeatAsync(beatId, "continue", 1, performance.StateId);
        performance.Commands.PublishBeat(beatId, performance.StateId, manuscriptStateId);

        ScenarioPerformanceCompletion completion = await coordinator.CompleteAsync(
            scenario,
            performance,
            scenario.StateId,
            performance.StateId);

        Assert.Equal(sceneId, completion.SceneId);
        Assert.True(completion.ScenarioCommit.Changed);
        Assert.True(completion.PerformanceCommit.Changed);
        Assert.Equal(SceneState.Settled, scenario.Queries.GetScene(sceneId).State);
        Assert.Equal(PerformanceStatus.Completed, performance.Status);
        Assert.Contains("SettleScene", changedOperations);
    }

    [Fact]
    public async Task RulesOnlySceneCannotStartPerformance()
    {
        TestFixture fixture = CreateFixture(SceneSettlementCapabilities.Rules);
        await using ScenarioSession scenario = await CreateScenarioAsync(fixture);
        Guid sceneId = BeginScene(scenario, fixture);
        var coordinator = new ScenarioPerformanceCoordinator();

        ScenarioApplicationException exception = Assert.Throws<ScenarioApplicationException>(
            () => coordinator.PrepareStart(scenario, sceneId, scenario.StateId));

        Assert.Equal(ScenarioApplicationErrorCodes.CapabilityUnavailable, exception.ErrorCode);
    }

    private static async Task<ScenarioSession> CreateScenarioAsync(TestFixture fixture)
    {
        var session = new ScenarioSession(
            new MemoryScenarioStore(),
            fixture.Extensions,
            new WorldSnapshot
            {
                Id = Guid.NewGuid(),
                Elements = new Dictionary<Guid, WorldElement>
                {
                    [fixture.ElementId] = new(fixture.ElementId, "Hero", string.Empty, WorldElementType.None)
                }
            },
            autoSaveDelay: TimeSpan.FromHours(1));
        await session.InitializeAsync();
        return session;
    }

    private static Guid BeginScene(ScenarioSession scenario, TestFixture fixture)
    {
        scenario.Commands.CreateScene(fixture.Scene, scenario.StateId);
        Guid sceneId = Assert.Single(scenario.Queries.GetScenes()).Id;
        scenario.Commands.SetSceneBinding(sceneId, "subject", [fixture.ElementId], scenario.StateId);
        scenario.Commands.BeginSceneProcessing(sceneId, scenario.StateId);
        return sceneId;
    }

    private static TestFixture CreateFixture(SceneSettlementCapabilities capabilities)
    {
        var module = new ModuleId("test");
        var version = new ModuleVersion("1.0.0");
        var scene = new SceneDefinition
        {
            Id = new SemanticKey("test:scene"),
            Module = module,
            ModuleVersion = version,
            Name = "Scene",
            Description = string.Empty,
            SettlementCapabilities = capabilities,
            Slots =
            [
                new SceneSlotDefinition
                {
                    Id = "subject",
                    Name = "Subject",
                    Description = string.Empty,
                    Minimum = 1,
                    Maximum = 1
                }
            ]
        };
        ModuleCatalog catalog = ModuleCatalog.Create(
        [
            new ModulePackageDefinition
            {
                Manifest = new ModuleManifest { Id = module, Version = version, SchemaVersion = 1, Name = "Test" },
                Semantics = new SemanticModuleDefinition(),
                Scenes = [scene]
            }
        ]);
        var plugin = new TestPerformancePlugin(module, version);
        var extensions = new FrozenModuleRuntime(
            catalog,
            new Dictionary<ModuleId, IReadOnlyDictionary<string, string>>(),
            [plugin]);
        return new TestFixture(scene, extensions, Guid.NewGuid());
    }

    private sealed record TestFixture(
        SceneDefinition Scene,
        FrozenModuleRuntime Extensions,
        Guid ElementId);

    private sealed class MemoryScenarioStore : IScenarioStore
    {
        private ScenarioSnapshot? _snapshot;

        public Task<ScenarioSnapshot?> LoadAsync(string slot, CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshot is null ? null : Tavi.Domain.Scenario.Scenario.Create(_snapshot).CreateSnapshot());

        public Task SaveAsync(string slot, ScenarioSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            _snapshot = Tavi.Domain.Scenario.Scenario.Create(snapshot).CreateSnapshot();
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryPerformanceStore : IPerformanceStore
    {
        private PerformanceSnapshot? _active;

        public Task<PerformanceSnapshot?> LoadActiveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_active is null ? null : Tavi.Domain.Performance.Performance.Create(_active).CreateSnapshot());

        public Task SaveActiveAsync(PerformanceSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            _active = Tavi.Domain.Performance.Performance.Create(snapshot).CreateSnapshot();
            return Task.CompletedTask;
        }

        public Task ArchiveAsync(PerformanceSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            _active = null;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PerformanceSnapshot>> ListArchivedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PerformanceSnapshot>>([]);

        public Task<PerformanceSnapshot?> LoadArchivedAsync(Guid performanceId, CancellationToken cancellationToken = default)
            => Task.FromResult<PerformanceSnapshot?>(null);
    }

    private sealed class TestPerformancePlugin(ModuleId module, ModuleVersion version) : ITaviPlugin, IPerformanceExtension
    {
        public ModuleId Module { get; } = module;
        public ModuleVersion Version { get; } = version;

        public ValueTask<PerformanceExpansionProposal> ExpandAsync(PerformanceExpansionContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new PerformanceExpansionProposal { ExpectedScenarioStateId = context.Scene.ScenarioStateId });

        public ValueTask<IReadOnlyList<BeatDefinition>> GetBeatDefinitionsAsync(BeatDefinitionContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult<IReadOnlyList<BeatDefinition>>(
            [
                new BeatDefinition
                {
                    Id = new SemanticKey("test:beat"),
                    Module = Module,
                    ModuleVersion = Version,
                    Name = "Beat",
                    SourcePerformanceStateId = context.Performance.StateId,
                    Slots =
                    [
                        new BeatSlotDefinition
                        {
                            Id = "subject",
                            Name = "Subject",
                            Minimum = 1,
                            Maximum = 1
                        }
                    ]
                }
            ]);

        public ValueTask<BeatResolutionProposal> ResolveBeatAsync(BeatResolutionContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new BeatResolutionProposal
            {
                ExpectedPerformanceStateId = context.Performance.StateId,
                Paragraphs = [new BeatParagraphProposal(Guid.NewGuid(), "A readable beat.")]
            });

        public ValueTask<SceneSettlementProposal> CompleteAsync(PerformanceCompletionContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new SceneSettlementProposal { ExpectedScenarioStateId = context.Scene.ScenarioStateId });
    }
}
