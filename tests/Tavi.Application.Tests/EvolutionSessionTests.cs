using Tavi.Application.Evolution;
using Tavi.Domain.Scenario;
using Tavi.Extensibility;
using Xunit;

namespace Tavi.Application.Tests;

/// <summary>验证 Character 声明式语义、EvolutionSession 事务和两条 Scene 结算路径。</summary>
public sealed class EvolutionSessionTests
{
    /// <summary>验证 Catalog 会隔离调用方持有的可变 Module 集合及 Scene 要求集合。</summary>
    [Fact]
    public void CatalogDefensivelyCopiesModuleDefinitions()
    {
        ModulePackageDefinition loaded = ModulePackageLoader.Load(FindCharacterDirectory());
        var dependencies = new List<ModuleDependency>();
        var requiredElementTypes = new HashSet<SemanticKey> { new("character:character") };
        var scenes = new List<SceneDefinition>
        {
            CharacterScene(Guid.Empty) with
            {
                Slots = [new SceneSlotDefinition { Id = "actor", Name = "Actor", Minimum = 1, Maximum = 1, Requirement = new SceneSlotRequirement { ElementTypes = requiredElementTypes } }]
            }
        };
        ModulePackageDefinition package = loaded with { Manifest = loaded.Manifest with { Dependencies = dependencies }, Scenes = scenes };
        EvolutionModuleCatalog catalog = EvolutionModuleCatalog.Create([package]);

        dependencies.Add(new ModuleDependency { Id = new ModuleId("late-dependency"), MinimumVersion = new ModuleVersion("1.0.0") });
        requiredElementTypes.Add(new SemanticKey("character:late-type"));
        scenes.Clear();

        Assert.Empty(Assert.Single(catalog.Modules).Dependencies);
        SceneDefinition storedScene = Assert.Single(catalog.StaticScenes);
        Assert.Equal([new SemanticKey("character:character")], Assert.Single(storedScene.Slots).Requirement.ElementTypes);
    }

    /// <summary>验证 Character Element、Identity Scope 和 Gender Aspect 可以在同一候选状态中原子创建。</summary>
    [Fact]
    public async Task CharacterConstraintValidatesFinalCandidateState()
    {
        EvolutionModuleCatalog catalog = LoadCharacterCatalog();
        await using var session = new EvolutionSession(new MemoryScenarioStore(), catalog, Guid.NewGuid());
        await session.InitializeAsync();
        Guid elementId = Guid.NewGuid();
        Guid scopeId = Guid.NewGuid();
        ScenarioCommitResult result = session.Apply(new ScenarioChangeSet([
            new AddElementOperation(elementId, "Alice", "", new ElementType("character:character")),
            new AddScopeOperation(scopeId, "Identity", "", 1, new ScopeType("character:identity"), elementId),
            new AddAspectOperation(Guid.NewGuid(), "Female", "", 1, new AspectType("character:gender.female"), elementId, scopeId)
        ]), session.StateId);

        Assert.True(result.Changed);
        Assert.Single(session.Queries.GetElements());
    }

    /// <summary>验证缺少必要 Identity 与 Gender 的 Character 不会改变真实 Scenario。</summary>
    [Fact]
    public async Task InvalidCharacterProposalDoesNotChangeScenario()
    {
        EvolutionModuleCatalog catalog = LoadCharacterCatalog();
        await using var session = new EvolutionSession(new MemoryScenarioStore(), catalog, Guid.NewGuid());
        await session.InitializeAsync();
        Guid stateId = session.StateId;

        EvolutionException exception = Assert.Throws<EvolutionException>(() => session.Apply(new ScenarioChangeSet([new AddElementOperation(Guid.NewGuid(), "Alice", "", new ElementType("character:character"))]), stateId));

        Assert.Equal(EvolutionErrorCodes.SemanticViolation, exception.ErrorCode);
        Assert.Equal(stateId, session.StateId);
        Assert.Empty(session.Queries.GetElements());
    }

    /// <summary>验证 Written Scene 结果通过独立入口提交且绝不调用规则结算器。</summary>
    [Fact]
    public async Task WritingOutcomeDoesNotInvokeRuleSettler()
    {
        EvolutionModuleCatalog catalog = LoadCharacterCatalog();
        var plugin = new CharacterTestPlugin();
        catalog.RegisterPlugin(plugin);
        await using var session = new EvolutionSession(new MemoryScenarioStore(), catalog, Guid.NewGuid());
        await session.InitializeAsync();
        Guid characterId = AddCharacter(session);
        SceneDefinition definition = CharacterScene(session.StateId);
        session.CreateScene(definition, new Dictionary<string, IReadOnlyList<Guid>> { ["actor"] = [characterId] }, session.StateId);
        Scene scene = Assert.Single(session.Queries.GetScenes());
        session.BeginSceneWriting(scene.Id, session.StateId);
        Guid writingStateId = session.StateId;
        var outcome = new ScenarioChangeProposal
        {
            ExpectedScenarioStateId = writingStateId,
            Operations = [new ScenarioOperationIntent.UpdateElement(characterId, "Alicia", "由玩家改变目标后的名字。", new SemanticKey("character:character"))]
        };

        session.CommitWrittenSceneOutcome(scene.Id, outcome, writingStateId);

        Assert.Equal(0, plugin.Settler.Calls);
        Assert.Equal("Alicia", session.Queries.GetElement(characterId).Name);
        Assert.Equal(SceneState.Settled, session.Queries.GetScene(scene.Id).State);
    }

    /// <summary>验证规则路径调用所属 Settler，并将提案与 Scene 结算状态原子提交。</summary>
    [Fact]
    public async Task RuleSettlementUsesRegisteredSettler()
    {
        EvolutionModuleCatalog catalog = LoadCharacterCatalog();
        var plugin = new CharacterTestPlugin();
        catalog.RegisterPlugin(plugin);
        await using var session = new EvolutionSession(new MemoryScenarioStore(), catalog, Guid.NewGuid());
        await session.InitializeAsync();
        Guid characterId = AddCharacter(session);
        SceneDefinition definition = CharacterScene(session.StateId);
        session.CreateScene(definition, new Dictionary<string, IReadOnlyList<Guid>> { ["actor"] = [characterId] }, session.StateId);
        Scene scene = Assert.Single(session.Queries.GetScenes());
        SceneDefinition currentDefinition = definition with { SourceScenarioStateId = session.StateId };

        await session.SettleSceneByRulesAsync(scene.Id, currentDefinition, 42, session.StateId);

        Assert.Equal(1, plugin.Settler.Calls);
        Assert.Equal("Rule settled", session.Queries.GetElement(characterId).Description);
        Assert.Equal(SceneState.Settled, session.Queries.GetScene(scene.Id).State);
    }

    private static Guid AddCharacter(EvolutionSession session)
    {
        Guid elementId = Guid.NewGuid();
        Guid scopeId = Guid.NewGuid();
        session.Apply(new ScenarioChangeSet([
            new AddElementOperation(elementId, "Alice", "", new ElementType("character:character")),
            new AddScopeOperation(scopeId, "Identity", "", 1, new ScopeType("character:identity"), elementId),
            new AddAspectOperation(Guid.NewGuid(), "Female", "", 1, new AspectType("character:gender.female"), elementId, scopeId)
        ]), session.StateId);
        return elementId;
    }

    private static SceneDefinition CharacterScene(Guid stateId) => new()
    {
        Id = new SemanticKey("character:test-action"),
        Module = new ModuleId("character"),
        ModuleVersion = new ModuleVersion("1.0.0"),
        Name = "Test Action",
        SettlementCapabilities = SceneSettlementCapabilities.Rules | SceneSettlementCapabilities.Writing,
        SourceScenarioStateId = stateId,
        Slots = [new SceneSlotDefinition { Id = "actor", Name = "Actor", Minimum = 1, Maximum = 1, Requirement = new SceneSlotRequirement { ElementTypes = new HashSet<SemanticKey> { new("character:character") } } }]
    };

    private static EvolutionModuleCatalog LoadCharacterCatalog() => EvolutionModuleCatalog.Create([ModulePackageLoader.Load(FindCharacterDirectory())]);

    private static string FindCharacterDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "Modules", "Character");
            if (File.Exists(Path.Combine(candidate, "module.json")))
                return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("无法定位 Modules/Character。");
    }

    private sealed class MemoryScenarioStore : IScenarioStore
    {
        private ScenarioSnapshot? _snapshot;

        public Task<ScenarioSnapshot?> LoadAsync(string slot, CancellationToken cancellationToken = default) => Task.FromResult(_snapshot is null ? null : Tavi.Domain.Scenario.Scenario.Create(_snapshot).CreateSnapshot());

        public Task SaveAsync(string slot, ScenarioSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            _snapshot = Tavi.Domain.Scenario.Scenario.Create(snapshot).CreateSnapshot();
            return Task.CompletedTask;
        }
    }

    private sealed class CharacterTestPlugin : ITaviPlugin
    {
        internal CharacterRuleSettler Settler { get; } = new();
        public ModuleId Module => new("character");
        public ModuleVersion Version => new("1.0.0");
        public void Register(IPluginRegistrar registrar) => registrar.AddSceneRuleSettler(Settler);
    }

    private sealed class CharacterRuleSettler : ISceneRuleSettler
    {
        internal int Calls { get; private set; }
        public ModuleId Module => new("character");
        public IReadOnlySet<SemanticKey> Definitions { get; } = new HashSet<SemanticKey> { new("character:test-action") };

        public ValueTask<ScenarioChangeProposal> SettleAsync(RuleSettlementContext context, CancellationToken cancellationToken)
        {
            Calls++;
            Guid actorId = context.Scene.Bindings.Single(binding => binding.SlotId == "actor").ElementIds.Single();
            ScenarioElementView actor = context.Scenario.Elements.Single(element => element.Id == actorId);
            return ValueTask.FromResult(new ScenarioChangeProposal
            {
                ExpectedScenarioStateId = context.Scenario.StateId,
                Operations = [new ScenarioOperationIntent.UpdateElement(actor.Id, actor.Name, "Rule settled", actor.Type)]
            });
        }
    }
}
