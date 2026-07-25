using Tavi.Application.Extensions;
using Tavi.Application.Extensions.Guidance;
using Tavi.Application.Extensions.Loading;
using Tavi.Application.Extensions.World;
using Tavi.Application.Scenario;
using Tavi.Domain.World;
using Tavi.Extensibility;
using Tavi.Modules.Magic;
using Xunit;

namespace Tavi.Application.Tests;

/// <summary>验证 Magic Module 声明、冻结参数与局部规则结算。</summary>
public sealed class MagicModuleTests
{
    /// <summary>验证冻结 Runtime 会索引 Magic 的 Guidance 与 World Authoring 能力。</summary>
    [Fact]
    public async Task GuidanceReceivesMagicSemanticsAndAtomicActions()
    {
        FrozenModuleRuntime runtime = CreateRuntime();
        (ModuleId Module, IGuidanceExtension Extension) guidance = Assert.Single(runtime.GuidanceExtensions);
        (ModuleId Module, IWorldAuthoringExtension Extension) authoring = Assert.Single(runtime.WorldAuthoringExtensions);
        IWorldView world = WorldExtensibilityAdapter.ToView(new WorldSnapshot());
        GuidanceInstructionContribution instruction = Assert.Single(await guidance.Extension.GetInstructionsAsync(world, runtime.GetParameters(guidance.Module), default));
        IReadOnlyList<WorldAuthoringAction> actions = await authoring.Extension.GetActionsAsync(world, runtime.GetParameters(authoring.Module), default);
        Assert.Equal(new ModuleId("magic"), guidance.Module);
        Assert.Contains("magic:spell-definition", instruction.Content, StringComparison.Ordinal);
        Assert.Equal(["magic:awaken-character", "magic:create-spell"], actions.Select(value => value.Id.Value).Order(StringComparer.Ordinal));
    }

    /// <summary>验证 Magic World Action 会把受约束结构作为一个原子提案返回。</summary>
    [Fact]
    public async Task MagicWorldActionsProduceCompleteStructures()
    {
        var plugin = new MagicPlugin();
        Guid characterId = Guid.NewGuid();
        var source = new WorldSnapshot();
        source.Elements.Add(characterId, new Element(characterId, "艾拉", "", new ElementType("character:character")));
        IWorldView world = WorldExtensibilityAdapter.ToView(source);
        WorldAuthoringProposal spell = await plugin.ProposeAsync(new WorldAuthoringRequest { World = world, ActionId = new SemanticKey("magic:create-spell"), Arguments = """{"name":"微光术","description":"制造微光","manaCost":3}""" }, default);
        WorldAuthoringProposal awakening = await plugin.ProposeAsync(new WorldAuthoringRequest { World = world, ActionId = new SemanticKey("magic:awaken-character"), Arguments = $$"""{"characterId":"{{characterId}}","initialMana":10}""" }, default);
        Assert.Collection(spell.Intents, value => Assert.IsType<WorldAuthoringIntent.AddElement>(value), value => Assert.IsType<WorldAuthoringIntent.AddScope>(value), value => Assert.IsType<WorldAuthoringIntent.AddAspect>(value));
        Assert.Collection(awakening.Intents, value => Assert.IsType<WorldAuthoringIntent.AddScope>(value), value => Assert.IsType<WorldAuthoringIntent.AddAspect>(value));
        _ = WorldExtensibilityAdapter.ToChangeSet(spell, source);
        _ = WorldExtensibilityAdapter.ToChangeSet(awakening, source);
    }

    /// <summary>验证施法 Scene 会按冻结倍率原子扣减施法者法力。</summary>
    [Fact]
    public async Task CastSpellConsumesMana()
    {
        FrozenModuleRuntime runtime = CreateRuntime();
        await using var session = new ScenarioSession(new MemoryScenarioStore(), runtime, CreateMagicWorld());
        await session.InitializeAsync();
        SceneDefinition definition = Assert.Single(await session.GetSceneDefinitionsAsync(17), value => value.Id == new SemanticKey("magic:cast-spell"));
        Guid sceneId = session.CreateScene(definition, session.StateId).ChangeSet!.Forward.Operations.OfType<Tavi.Domain.Scenario.AddSceneOperation>().Single().SceneId;
        Guid casterId = session.Queries.GetElements().Single(value => value.Type.Value == "character:character").Id;
        Guid spellId = session.Queries.GetElements().Single(value => value.Type.Value == "magic:spell").Id;
        _ = session.SetSceneBinding(sceneId, "caster", [casterId], session.StateId);
        _ = session.SetSceneBinding(sceneId, "spell", [spellId], session.StateId);
        _ = session.BeginSceneProcessing(sceneId, session.StateId);
        _ = await session.SettleSceneByRulesAsync(sceneId, 17, session.StateId);
        Assert.Equal(7, session.Queries.GetAspects().Single(value => value.Type.Value == "magic:mana.current").Quantity);
        Assert.Equal(Tavi.Domain.Scenario.SceneState.Settled, session.Queries.GetScene(sceneId).State);
    }

    /// <summary>验证法力不足通过稳定、可重试的 Module 错误报告。</summary>
    [Fact]
    public async Task InsufficientManaIsRetryable()
    {
        var plugin = new MagicPlugin();
        Guid casterId = Guid.NewGuid();
        Guid spellId = Guid.NewGuid();
        Guid manaId = Guid.NewGuid();
        var context = new Tavi.Application.Extensions.Scenario.SceneSettlementContext
        {
            Definition = new SceneDefinition { Id = new SemanticKey("magic:cast-spell"), Module = new ModuleId("magic"), ModuleVersion = new ModuleVersion("1.0.0"), Name = "施放法术", SettlementCapabilities = SceneSettlementCapabilities.Rules },
            Context = new SceneContextView
            {
                ScenarioStateId = Guid.NewGuid(),
                Scene = new ScenarioSceneView(Guid.NewGuid(), new SemanticKey("magic:cast-spell"), new ModuleId("magic"), new ModuleVersion("1.0.0"), Guid.NewGuid(), "Processing", [new SceneSlotBindingView("caster", [casterId]), new SceneSlotBindingView("spell", [spellId])]),
                Elements = [new ElementView(casterId, "施法者", "", new SemanticKey("character:character")), new ElementView(spellId, "法术", "", new SemanticKey("magic:spell"))],
                Aspects = [new AspectView(manaId, "法力", "", 2, new SemanticKey("magic:mana.current"), casterId, Guid.NewGuid()), new AspectView(Guid.NewGuid(), "消耗", "", 3, new SemanticKey("magic:casting-cost.mana"), spellId, Guid.NewGuid())]
            },
            RandomSeed = 0
        };
        ModuleArgumentException exception = await Assert.ThrowsAsync<ModuleArgumentException>(async () => await plugin.SettleAsync(context, default));
        Assert.Equal("TAVI.MAGIC.INSUFFICIENT_MANA", exception.Code);
        Assert.True(exception.Retryable);
    }

    private static FrozenModuleRuntime CreateRuntime()
    {
        string modules = FindModulesDirectory();
        var session = new ExtensionSession([ModulePackageLoader.Load(Path.Combine(modules, "Character")), ModulePackageLoader.Load(Path.Combine(modules, "Magic"))], plugins: [new MagicPlugin()]);
        return session.Freeze();
    }

    private static WorldSnapshot CreateMagicWorld()
    {
        Guid casterId = Guid.NewGuid();
        Guid identityScopeId = Guid.NewGuid();
        Guid arcaneScopeId = Guid.NewGuid();
        Guid spellId = Guid.NewGuid();
        Guid spellScopeId = Guid.NewGuid();
        var snapshot = new WorldSnapshot();
        snapshot.Elements.Add(casterId, new Element(casterId, "艾拉", "初学法师", new ElementType("character:character")));
        snapshot.Elements.Add(spellId, new Element(spellId, "微光术", "制造一束微光", new ElementType("magic:spell")));
        snapshot.Scopes.Add(identityScopeId, new Scope(identityScopeId, "身份", "", 1, new ScopeType("character:identity"), casterId));
        snapshot.Scopes.Add(arcaneScopeId, new Scope(arcaneScopeId, "魔法状态", "", 1, new ScopeType("magic:arcane-state"), casterId));
        snapshot.Scopes.Add(spellScopeId, new Scope(spellScopeId, "法术定义", "", 1, new ScopeType("magic:spell-definition"), spellId));
        Guid genderId = Guid.NewGuid();
        snapshot.Aspects.Add(genderId, new Aspect(genderId, "未指定性别", "", 1, new AspectType("character:gender.unspecified"), casterId, identityScopeId));
        Guid manaId = Guid.NewGuid();
        snapshot.Aspects.Add(manaId, new Aspect(manaId, "当前法力", "", 10, new AspectType("magic:mana.current"), casterId, arcaneScopeId));
        Guid costId = Guid.NewGuid();
        snapshot.Aspects.Add(costId, new Aspect(costId, "基础消耗", "", 3, new AspectType("magic:casting-cost.mana"), spellId, spellScopeId));
        return snapshot;
    }

    private static string FindModulesDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "Modules");
            if (Directory.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("未找到 Modules 目录。");
    }

    private sealed class MemoryScenarioStore : IScenarioStore
    {
        public Task<Tavi.Domain.Scenario.ScenarioSnapshot?> LoadAsync(string slot, CancellationToken cancellationToken = default) => Task.FromResult<Tavi.Domain.Scenario.ScenarioSnapshot?>(null);
        public Task SaveAsync(string slot, Tavi.Domain.Scenario.ScenarioSnapshot snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
