using Tavi.Application.Scenario;
using Tavi.Application.Extensions;
using Tavi.Application.Extensions.Loading;
using Tavi.Application.Extensions.Scenario;
using Tavi.Domain.Scenario;
using Tavi.Extensibility;
using Xunit;

namespace Tavi.Application.Tests;

/// <summary>验证声明式语义、Scene 显式生命周期和 Module 局部结算边界。</summary>
public sealed class ScenarioSessionTests
{
    /// <summary>验证 Character Element、Identity Scope 和 Gender Aspect 可以在同一候选状态中原子创建。</summary>
    [Fact]
    public async Task CharacterConstraintValidatesFinalCandidateState()
    {
        await using ScenarioSession session = await CreateSessionAsync();
        AddCharacter(session, "Alice");
        Assert.Single(session.Queries.GetElements());
    }

    /// <summary>验证 Scene 可以先创建并持久保留不完整绑定，再显式进入 Processing。</summary>
    [Fact]
    public async Task SceneBindingAndProcessingAreSeparate()
    {
        await using ScenarioSession session = await CreateSessionAsync();
        Guid characterId = AddCharacter(session, "Alice");
        SceneDefinition definition = CharacterScene(session.StateId);
        session.CreateScene(definition, session.StateId);
        Scene scene = Assert.Single(session.Queries.GetScenes());
        Assert.Equal(SceneState.Binding, scene.State);
        session.SetSceneBinding(scene.Id, "actor", [characterId], session.StateId);
        session.BeginSceneProcessing(scene.Id, session.StateId);
        Assert.Equal(SceneState.Processing, session.Queries.GetScene(scene.Id).State);
        Assert.Equal(characterId, Assert.Single(session.GetProcessingContext(scene.Id).Elements).Id);
    }

    /// <summary>验证规则结算器只能收到 Scene 局部视图并可修改内部 Element。</summary>
    [Fact]
    public async Task RuleSettlementUsesLocalContext()
    {
        var plugin = new CharacterTestPlugin();
        FrozenModuleRuntime runtime = new ExtensionSession([LoadCharacterPackage()], plugins: [plugin]).Freeze();
        await using var session = new ScenarioSession(new MemoryScenarioStore(), runtime, Guid.NewGuid());
        await session.InitializeAsync();
        Guid actorId = AddCharacter(session, "Alice");
        _ = AddCharacter(session, "Outside");
        session.CreateScene(CharacterScene(session.StateId), session.StateId);
        Scene scene = Assert.Single(session.Queries.GetScenes());
        session.SetSceneBinding(scene.Id, "actor", [actorId], session.StateId);
        session.BeginSceneProcessing(scene.Id, session.StateId);
        await session.SettleSceneByRulesAsync(scene.Id, 42, session.StateId);
        Assert.Equal(1, plugin.Settler.Calls);
        Assert.Equal(1, plugin.Settler.VisibleElementCount);
        Assert.Equal("Rule settled", session.Queries.GetElement(actorId).Description);
        Assert.Equal(SceneState.Settled, session.Queries.GetScene(scene.Id).State);
    }

    /// <summary>验证结算允许创建新 Element，但拒绝修改未绑定的既有 Element。</summary>
    [Fact]
    public async Task SettlementCanCreateButCannotModifyExternalElement()
    {
        await using ScenarioSession session = await CreateSessionAsync();
        Guid actorId = AddCharacter(session, "Alice");
        Guid outsideId = AddCharacter(session, "Outside");
        session.CreateScene(CharacterScene(session.StateId), session.StateId);
        Scene scene = Assert.Single(session.Queries.GetScenes());
        session.SetSceneBinding(scene.Id, "actor", [actorId], session.StateId);
        session.BeginSceneProcessing(scene.Id, session.StateId);
        Guid createdId = Guid.NewGuid();
        Guid actorScopeId = session.Queries.GetScopes().Single(scope => scope.OwnerElementId == actorId).Id;
        Guid stateId = session.StateId;
        Assert.Throws<ScenarioApplicationException>(() => session.SettleScene(scene.Id, new SceneSettlementProposal { ExpectedScenarioStateId = stateId, Operations = [new SceneOperationIntent.UpdateElement(outsideId, "Changed", "", new SemanticKey("character:character"))] }, stateId));
        Assert.Throws<ScenarioApplicationException>(() => session.SettleScene(scene.Id, new SceneSettlementProposal { ExpectedScenarioStateId = stateId, Operations = [new SceneOperationIntent.AddRelation(Guid.NewGuid(), "Outside", "", 1, new SemanticKey("core:none"), actorId, outsideId, actorScopeId)] }, stateId));
        Assert.Equal(stateId, session.StateId);
        Guid createdScopeId = Guid.NewGuid();
        session.SettleScene(scene.Id, new SceneSettlementProposal
        {
            ExpectedScenarioStateId = stateId,
            Operations =
            [
                new SceneOperationIntent.AddElement(createdId, "Created", "", new SemanticKey("core:none")),
                new SceneOperationIntent.AddScope(createdScopeId, "Created scope", "", 1, new SemanticKey("core:none"), createdId),
                new SceneOperationIntent.AddAspect(Guid.NewGuid(), "Created aspect", "", 1, new SemanticKey("core:none"), createdId, createdScopeId),
                new SceneOperationIntent.AddRelation(Guid.NewGuid(), "Created relation", "", 1, new SemanticKey("core:none"), actorId, createdId, actorScopeId)
            ]
        }, stateId);
        Assert.Equal("Created", session.Queries.GetElement(createdId).Name);
    }

    /// <summary>验证删除 Binding Scene 释放 Element，而批量清理只删除 Settled Scene。</summary>
    [Fact]
    public async Task RemovingAndCleaningScenesFollowLifecycle()
    {
        await using ScenarioSession session = await CreateSessionAsync();
        Guid actorId = AddCharacter(session, "Alice");
        session.CreateScene(CharacterScene(session.StateId), session.StateId);
        Scene first = Assert.Single(session.Queries.GetScenes());
        session.SetSceneBinding(first.Id, "actor", [actorId], session.StateId);
        session.RemoveScene(first.Id, session.StateId);
        session.CreateScene(CharacterScene(session.StateId), session.StateId);
        Scene second = Assert.Single(session.Queries.GetScenes());
        session.SetSceneBinding(second.Id, "actor", [actorId], session.StateId);
        session.BeginSceneProcessing(second.Id, session.StateId);
        session.SettleScene(second.Id, new SceneSettlementProposal { ExpectedScenarioStateId = session.StateId }, session.StateId);
        session.ClearSettledScenes(session.StateId);
        Assert.Empty(session.Queries.GetScenes());
    }

    private static async Task<ScenarioSession> CreateSessionAsync()
    {
        var session = new ScenarioSession(new MemoryScenarioStore(), LoadCharacterCatalog(), Guid.NewGuid());
        await session.InitializeAsync();
        return session;
    }

    private static Guid AddCharacter(ScenarioSession session, string name)
    {
        Guid elementId = Guid.NewGuid();
        Guid scopeId = Guid.NewGuid();
        session.Apply(new ScenarioChangeSet([new AddElementOperation(elementId, name, "", new ElementType("character:character")), new AddScopeOperation(scopeId, "Identity", "", 1, new ScopeType("character:identity"), elementId), new AddAspectOperation(Guid.NewGuid(), "Unspecified", "", 1, new AspectType("character:gender.unspecified"), elementId, scopeId)]), session.StateId);
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

    private static ModuleCatalog LoadCharacterCatalog() => ModuleCatalog.Create([LoadCharacterPackage()]);
    private static ModulePackageDefinition LoadCharacterPackage() => ModulePackageLoader.Load(FindCharacterDirectory());

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

    private sealed class CharacterTestPlugin : ITaviPlugin, IScenarioSettlementExtension
    {
        internal CharacterRuleSettler Settler { get; } = new();
        public ModuleId Module => new("character");
        public ModuleVersion Version => new("1.0.0");
        public IReadOnlySet<SemanticKey> Definitions => Settler.Definitions;
        public ValueTask<SceneSettlementProposal> SettleAsync(SceneSettlementContext context, CancellationToken cancellationToken) => Settler.SettleAsync(context, cancellationToken);
    }

    private sealed class CharacterRuleSettler
    {
        internal int Calls { get; private set; }
        internal int VisibleElementCount { get; private set; }
        public IReadOnlySet<SemanticKey> Definitions { get; } = new HashSet<SemanticKey> { new("character:test-action") };

        public ValueTask<SceneSettlementProposal> SettleAsync(SceneSettlementContext context, CancellationToken cancellationToken)
        {
            Calls++;
            VisibleElementCount = context.Context.Elements.Count;
            ElementView actor = Assert.Single(context.Context.Elements);
            return ValueTask.FromResult(new SceneSettlementProposal { ExpectedScenarioStateId = context.Context.ScenarioStateId, Operations = [new SceneOperationIntent.UpdateElement(actor.Id, actor.Name, "Rule settled", actor.Type)] });
        }
    }
}
