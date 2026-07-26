using Tavi.Application.Extensions;
using Tavi.Application.Extensions.Performance;
using Tavi.Application.LanguageModel;
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
        var performanceStore = new MemoryPerformanceStore();
        await using var session = new PerformanceSession(performanceStore, scene, 1, writing, extensions, TimeSpan.FromHours(1));

        await session.InitializeAsync();
        Guid performanceId = session.Id;
        BeatDefinition definition = Assert.Single(await session.Commands.GetBeatDefinitionsAsync(1));
        session.Commands.CreateBeat(definition, session.StateId);
        Guid beatId = Assert.Single(session.Queries.CreateSnapshot().Beats.Keys);
        session.Commands.SetBeatBinding(beatId, "subject", [elementId], session.StateId);
        session.Commands.BeginBeatProcessing(beatId, session.StateId);
        await session.Commands.ResolveBeatAsync(beatId, "continue", 1, session.StateId);
        session.Commands.PublishBeat(beatId, session.StateId, manuscriptStateId);
        SceneSettlementProposal settlement = await session.Commands.PrepareSettlementAsync();
        session.Commands.Complete(session.StateId);
        await session.Commands.SaveAsync();

        Assert.Equal(scenarioStateId, settlement.ExpectedScenarioStateId);
        Assert.Equal(PerformanceStatus.Completed, session.Status);
        Assert.Equal("A readable beat.", Assert.Single(writing.GetSnapshot().Manuscript!.Paragraphs).Text);
        Assert.Equal(performanceId, (await performanceStore.LoadActiveAsync())!.PerformanceId);

        await using var restored = new PerformanceSession(performanceStore, writing, extensions, TimeSpan.FromHours(1));
        await restored.InitializeAsync();
        Assert.Equal(performanceId, restored.Id);
        Assert.Equal(PerformanceStatus.Completed, restored.Status);
        Assert.Equal(sceneId, restored.Queries.CreateSnapshot().SourceScene.Id);
        Assert.Equal(1, plugin.ExpansionCalls);
    }

    [Fact]
    public async Task SessionRendersDeclarativeParagraphWithModelFill()
    {
        var module = new ModuleId("test");
        var version = new ModuleVersion("1.0.0");
        var plugin = new TestPerformancePlugin(
            module,
            version,
            includeDirectParagraph: false);
        ModuleWritingDefinitions writingDefinitions = ModuleWritingProfile.Parse(
            """
            {
              "schemaVersion": 1,
              "beatNarrations": [
                {
                  "beatDefinition": "test:beat",
                  "paragraphs": [
                    {
                      "key": "result",
                      "template": "{{subject.name}}继续前行，{{detail}}",
                      "bindings": {
                        "subject": {
                          "source": "beatSlot",
                          "slot": "subject",
                          "cardinality": "one"
                        }
                      },
                      "fills": {
                        "detail": {
                          "kind": "model",
                          "instruction": "根据上下文描写{{subject.name}}看到的景象。",
                          "context": ["subject.description"],
                          "minimumLength": 1
                        }
                      }
                    }
                  ]
                }
              ]
            }
            """,
            module);
        ModuleCatalog catalog = ModuleCatalog.Create(
        [
            new ModulePackageDefinition
            {
                Manifest = new ModuleManifest
                {
                    Id = module,
                    Version = version,
                    SchemaVersion = 1,
                    Name = "Test"
                },
                Semantics = new SemanticModuleDefinition(),
                Writing = writingDefinitions
            }
        ]);
        var extensions = new FrozenModuleRuntime(
            catalog,
            new Dictionary<ModuleId, IReadOnlyDictionary<string, string>>(),
            [plugin]);
        Guid scenarioStateId = Guid.NewGuid();
        Guid elementId = Guid.NewGuid();
        var scene = new SceneContextView
        {
            ScenarioStateId = scenarioStateId,
            Scene = new ScenarioSceneView(
                Guid.NewGuid(),
                new SemanticKey("test:scene"),
                module,
                version,
                scenarioStateId,
                "Processing",
                [new SceneSlotBindingView("subject", [elementId])]),
            Elements =
            [
                new ElementView(
                    elementId,
                    "旅人",
                    "刚刚离开故乡。",
                    new SemanticKey("core:none"))
            ]
        };
        var manuscriptStore =
            new WritingBeatPublicationTests.MemoryManuscriptStore();
        await using var manuscript = new WritingSession(manuscriptStore);
        await manuscript.InitializeAsync();
        _ = await manuscript.CreateAsync("Story");
        var languageModels = new LanguageModelRunner(
            new StubLanguageModelClient(
                """{"fills":{"result.detail":"看见了远方的灯火。"}}"""),
            new LanguageModelSettings
            {
                ToolCalls = FeaturePolicy.Disabled,
                NativeJsonOutput = FeaturePolicy.Required,
                Streaming = FeaturePolicy.Disabled
            });
        await using var session = new PerformanceSession(
            new MemoryPerformanceStore(),
            scene,
            1,
            manuscript,
            extensions,
            TimeSpan.FromHours(1),
            languageModels);

        await session.InitializeAsync();
        BeatDefinition definition =
            Assert.Single(await session.Commands.GetBeatDefinitionsAsync(1));
        session.Commands.CreateBeat(definition, session.StateId);
        Guid beatId = Assert.Single(session.Queries.CreateSnapshot().Beats.Keys);
        session.Commands.SetBeatBinding(
            beatId,
            "subject",
            [elementId],
            session.StateId);
        session.Commands.BeginBeatProcessing(beatId, session.StateId);

        await session.Commands.ResolveBeatAsync(
            beatId,
            "继续向前",
            1,
            session.StateId);

        Beat resolved = session.Queries.GetBeat(beatId);
        Assert.Equal(
            "旅人继续前行，看见了远方的灯火。",
            Assert.Single(resolved.Paragraphs).Text);
    }

    [Fact]
    public async Task RendererExpandsManyResolutionValuesWithoutNestedArray()
    {
        var module = new ModuleId("test");
        var version = new ModuleVersion("1.0.0");
        ModuleWritingDefinitions writing = ModuleWritingProfile.Parse(
            """
            {
              "schemaVersion": 1,
              "beatNarrations": [
                {
                  "beatDefinition": "test:beat",
                  "paragraphs": [
                    {
                      "key": "events",
                      "template": "{{events}}",
                      "bindings": {
                        "events": {
                          "source": "resolution",
                          "key": "events",
                          "member": "text",
                          "cardinality": "many"
                        }
                      }
                    }
                  ]
                }
              ]
            }
            """,
            module);
        ModuleCatalog catalog = ModuleCatalog.Create(
        [
            new ModulePackageDefinition
            {
                Manifest = new ModuleManifest
                {
                    Id = module,
                    Version = version,
                    SchemaVersion = 1,
                    Name = "Test"
                },
                Semantics = new SemanticModuleDefinition(),
                Writing = writing
            }
        ]);
        var extensions = new FrozenModuleRuntime(
            catalog,
            new Dictionary<ModuleId, IReadOnlyDictionary<string, string>>(),
            []);
        Guid stateId = Guid.NewGuid();
        var beat = new Beat(
            Guid.NewGuid(),
            new BeatDefinitionType("test:beat"),
            module.Value,
            version.Value,
            stateId,
            "Beat",
            string.Empty,
            BeatState.Processing,
            []);
        var performance = new PerformanceSnapshot
        {
            PerformanceId = Guid.NewGuid(),
            StateId = stateId,
            SourceScenarioStateId = Guid.NewGuid(),
            SourceScene = new PerformanceSourceScene
            {
                Id = Guid.NewGuid(),
                DefinitionId = "test:scene",
                ModuleId = module.Value,
                ModuleVersion = version.Value,
                BasedOnScenarioStateId = Guid.NewGuid(),
                State = "Processing"
            }
        };
        var proposal = new BeatResolutionProposal
        {
            ExpectedPerformanceStateId = stateId,
            Values = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["events"] = System.Text.Json.JsonSerializer.SerializeToElement(
                    new[] { new { text = "启程" }, new { text = "抵达" } })
            }
        };

        IReadOnlyList<BeatParagraph> paragraphs =
            await new BeatNarrationRenderer(extensions, null).RenderAsync(
                performance,
                beat,
                proposal,
                string.Empty,
                CancellationToken.None);

        Assert.Equal("启程、抵达", Assert.Single(paragraphs).Text);
    }

    private sealed class MemoryPerformanceStore : IPerformanceStore
    {
        private PerformanceSnapshot? _active;
        private readonly Dictionary<Guid, PerformanceSnapshot> _archived = [];

        public Task<PerformanceSnapshot?> LoadActiveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_active is null ? null : Clone(_active));

        public Task SaveActiveAsync(PerformanceSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            if (_active is not null && _active.PerformanceId != snapshot.PerformanceId)
                throw new InvalidOperationException("活动 Performance 已存在。");
            _active = Clone(snapshot);
            return Task.CompletedTask;
        }

        public Task ArchiveAsync(PerformanceSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            _archived[snapshot.PerformanceId] = Clone(snapshot);
            if (_active?.PerformanceId == snapshot.PerformanceId)
                _active = null;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PerformanceSnapshot>> ListArchivedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PerformanceSnapshot>>(_archived.Values.Select(Clone).ToArray());

        public Task<PerformanceSnapshot?> LoadArchivedAsync(Guid performanceId, CancellationToken cancellationToken = default)
            => Task.FromResult(_archived.TryGetValue(performanceId, out PerformanceSnapshot? value) ? Clone(value) : null);

        private static PerformanceSnapshot Clone(PerformanceSnapshot snapshot)
            => Tavi.Domain.Performance.Performance.Create(snapshot).CreateSnapshot();
    }

    private sealed class TestPerformancePlugin(
        ModuleId module,
        ModuleVersion version,
        bool includeDirectParagraph = true) : ITaviPlugin, IPerformanceExtension
    {
        public ModuleId Module { get; } = module;
        public ModuleVersion Version { get; } = version;
        public int ExpansionCalls { get; private set; }

        public ValueTask<PerformanceExpansionProposal> ExpandAsync(PerformanceExpansionContext context, CancellationToken cancellationToken)
        {
            ExpansionCalls++;
            return ValueTask.FromResult(new PerformanceExpansionProposal { ExpectedScenarioStateId = context.Scene.ScenarioStateId });
        }

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
                Paragraphs = includeDirectParagraph
                    ? [new BeatParagraphProposal(Guid.NewGuid(), "A readable beat.")]
                    : []
            });

        public ValueTask<SceneSettlementProposal> CompleteAsync(PerformanceCompletionContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new SceneSettlementProposal { ExpectedScenarioStateId = context.Scene.ScenarioStateId });
    }

    private sealed class StubLanguageModelClient(string output) : ILanguageModelClient
    {
        public LanguageModelCapabilities Capabilities { get; } = new()
        {
            Provider = "Stub",
            SupportsNativeJsonOutput = true,
            SupportsJsonSchema = true
        };

        public ILanguageModelSession CreateSession(LanguageModelSessionOptions options) =>
            new StubLanguageModelSession(output);
    }

    private sealed class StubLanguageModelSession(string output) : ILanguageModelSession
    {
        public Task<ModelTurnResult> CompleteAsync(
            IReadOnlyList<ModelMessage> messages,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ModelTurnResult.Completed(output));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
