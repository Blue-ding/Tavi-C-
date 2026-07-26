using Tavi.Application.Extensions;
using Tavi.Application.Extensions.Performance;
using Tavi.Application.Performance;
using Tavi.Application.Writing;
using Tavi.Domain.Performance;
using Tavi.Extensibility;
using Xunit;

namespace Tavi.Application.Tests;

public sealed class PerformanceSessionTests
{
    [Fact]
    public async Task SessionExpandsResolvesPublishesAndPreparesSettlement()
    {
        var module = new ModuleId("test");
        var version = new ModuleVersion("1.0.0");
        var plugin = new TestPerformancePlugin(module, version);
        ModuleCatalog catalog = ModuleCatalog.Create(
        [
            new ModulePackageDefinition
            {
                Manifest = new ModuleManifest { Id = module, Version = version, SchemaVersion = 1, Name = "Test" },
                Semantics = new SemanticModuleDefinition()
            }
        ]);
        var extensions = new FrozenModuleRuntime(catalog, new Dictionary<ModuleId, IReadOnlyDictionary<string, string>>(), [plugin]);
        Guid scenarioStateId = Guid.NewGuid();
        Guid sceneId = Guid.NewGuid();
        Guid elementId = Guid.NewGuid();
        var scene = new SceneContextView
        {
            ScenarioStateId = scenarioStateId,
            Scene = new ScenarioSceneView(sceneId, new SemanticKey("test:scene"), module, version, scenarioStateId, "Processing", [new SceneSlotBindingView("subject", [elementId])]),
            Elements = [new ElementView(elementId, "Hero", string.Empty, new SemanticKey("core:none"))]
        };
        var store = new WritingBeatPublicationTests.MemoryManuscriptStore();
        await using var writing = new WritingSession(store);
        await writing.InitializeAsync();
        Guid manuscriptStateId = (await writing.CreateAsync("Story")).Manuscript!.StateId;
        var session = new PerformanceSession(scene, writing, extensions);

        await session.InitializeAsync(1);
        BeatDefinition definition = Assert.Single(await session.GetBeatDefinitionsAsync(1));
        session.CreateBeat(definition, session.StateId);
        Guid beatId = Assert.Single(session.GetSnapshot().Beats.Keys);
        session.SetBeatBinding(beatId, "subject", [elementId], session.StateId);
        session.BeginBeatProcessing(beatId, session.StateId);
        await session.ResolveBeatAsync(beatId, "continue", 1, session.StateId);
        session.PublishBeat(beatId, session.StateId, manuscriptStateId);
        SceneSettlementProposal settlement = await session.PrepareSettlementAsync();
        session.Complete(session.StateId);

        Assert.Equal(scenarioStateId, settlement.ExpectedScenarioStateId);
        Assert.Equal(PerformanceStatus.Completed, session.Status);
        Assert.Equal("A readable beat.", Assert.Single(writing.GetSnapshot().Manuscript!.Paragraphs).Text);
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
                    Slots = [new BeatSlotDefinition { Id = "subject", Name = "Subject", Minimum = 1, Maximum = 1 }]
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
