using System.Globalization;
using System.Text.Json;
using Tavi.Application.Extensions.Guidance;
using Tavi.Application.Extensions.Scenario;
using Tavi.Application.Extensions.World;
using Tavi.Extensibility;

namespace Tavi.Modules.Magic;

/// <summary>为 Magic Module 提供 Guidance 语义、World 创作操作与确定性的法术施放规则结算。</summary>
public sealed class MagicPlugin : ITaviPlugin, IGuidanceExtension, IWorldAuthoringExtension, IScenarioSettlementExtension
{
    private static readonly ModuleId MagicModule = new("magic");
    private static readonly ModuleVersion MagicVersion = new("1.0.0");
    private static readonly SemanticKey CastSpellDefinition = new("magic:cast-spell");
    private static readonly SemanticKey GuidanceInstructions = new("magic:guidance.instructions");
    private static readonly SemanticKey CreateSpellAction = new("magic:create-spell");
    private static readonly SemanticKey AwakenCharacterAction = new("magic:awaken-character");
    private static readonly SemanticKey CharacterType = new("character:character");
    private static readonly SemanticKey SpellType = new("magic:spell");
    private static readonly SemanticKey ArcaneStateType = new("magic:arcane-state");
    private static readonly SemanticKey SpellDefinitionType = new("magic:spell-definition");
    private static readonly SemanticKey ManaType = new("magic:mana.current");
    private static readonly SemanticKey ManaCostType = new("magic:casting-cost.mana");
    private const string CreateSpellSchema = """{"type":"object","additionalProperties":false,"required":["name","manaCost"],"properties":{"name":{"type":"string","minLength":1},"description":{"type":"string"},"manaCost":{"type":"number","minimum":0}}}""";
    private const string AwakenCharacterSchema = """{"type":"object","additionalProperties":false,"required":["characterId","initialMana"],"properties":{"characterId":{"type":"string","format":"uuid"},"initialMana":{"type":"number","minimum":0}}}""";

    /// <inheritdoc />
    public ModuleId Module => MagicModule;

    /// <inheritdoc />
    public ModuleVersion Version => MagicVersion;

    /// <inheritdoc />
    public IReadOnlySet<SemanticKey> Definitions { get; } = new HashSet<SemanticKey> { CastSpellDefinition };

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<GuidanceInstructionContribution>> GetInstructionsAsync(IWorldView world, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(parameters);
        cancellationToken.ThrowIfCancellationRequested();
        int characters = world.Elements.Count(value => value.Type == CharacterType);
        int spells = world.Elements.Count(value => value.Type == SpellType);
        string multiplier = parameters.GetValueOrDefault("mana-cost-multiplier") ?? "1";
        string content = $"""
            Magic Module 语义：
            - character:character 只有在持有 magic:arcane-state Scope，且该 Scope 中包含指向角色自身的 magic:mana.current Aspect 时，才是可施法角色。
            - magic:spell 必须恰好持有一个 magic:spell-definition Scope；该 Scope 必须恰好包含一个指向法术自身的 magic:casting-cost.mana Aspect。
            - 创建法术必须使用 module_magic_create_spell，唤醒既有角色必须使用 module_magic_awaken_character；不要用通用提案工具分步拼装这些受约束结构。
            - magic:cast-spell 属于 Scenario 规则结算，不得通过 World 提案模拟施法或直接扣减法力。
            - 当前 World 中有 {characters} 个角色、{spells} 个法术；冻结的法力消耗倍率为 {multiplier}。
            """;
        return ValueTask.FromResult<IReadOnlyList<GuidanceInstructionContribution>>([new GuidanceInstructionContribution { Id = GuidanceInstructions, Content = content }]);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<WorldAuthoringAction>> GetActionsAsync(IWorldView world, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(parameters);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<WorldAuthoringAction> actions =
        [
            new WorldAuthoringAction { Id = CreateSpellAction, Name = "创建法术", Description = "原子创建法术、法术定义 Scope 和基础法力消耗。", ParameterSchema = CreateSpellSchema },
            new WorldAuthoringAction { Id = AwakenCharacterAction, Name = "唤醒施法者", Description = "为一个既有角色原子添加魔法状态与当前法力。", ParameterSchema = AwakenCharacterSchema }
        ];
        return ValueTask.FromResult(actions);
    }

    /// <inheritdoc />
    public ValueTask<WorldAuthoringProposal> ProposeAsync(WorldAuthoringRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return request.ActionId == CreateSpellAction ? ValueTask.FromResult(ProposeSpell(request)) : request.ActionId == AwakenCharacterAction ? ValueTask.FromResult(ProposeAwakening(request)) : throw new ModuleArgumentException("TAVI.MAGIC.ACTION_UNSUPPORTED", $"Magic Plugin 不支持 World Authoring Action {request.ActionId}。", "actionId");
    }

    /// <inheritdoc />
    public ValueTask<SceneSettlementProposal> SettleAsync(SceneSettlementContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Definition.Id != CastSpellDefinition)
            throw new ModuleArgumentException("TAVI.MAGIC.DEFINITION_UNSUPPORTED", $"Magic Plugin 不能结算 SceneDefinition {context.Definition.Id}。", nameof(context));
        Guid casterId = RequireSingleBinding(context.Context, "caster");
        Guid spellId = RequireSingleBinding(context.Context, "spell");
        AspectView mana = RequireSingleAspect(context.Context, casterId, ManaType);
        AspectView cost = RequireSingleAspect(context.Context, spellId, ManaCostType);
        double multiplier = ReadMultiplier(context.Parameters);
        double consumed = cost.Quantity * multiplier;
        if (mana.Quantity < consumed)
            throw new ModuleArgumentException("TAVI.MAGIC.INSUFFICIENT_MANA", $"施法者当前法力 {mana.Quantity.ToString("R", CultureInfo.InvariantCulture)}，无法支付 {consumed.ToString("R", CultureInfo.InvariantCulture)} 点法力。", "caster");
        var operation = new SceneOperationIntent.UpdateAspect(mana.Id, mana.Name, mana.Description, mana.Quantity - consumed, mana.Type);
        return ValueTask.FromResult(new SceneSettlementProposal { ExpectedScenarioStateId = context.Context.ScenarioStateId, Operations = [operation], Rationale = $"施放 {context.Context.Elements.Single(element => element.Id == spellId).Name}，消耗 {consumed.ToString("R", CultureInfo.InvariantCulture)} 点法力。" });
    }

    private static WorldAuthoringProposal ProposeSpell(WorldAuthoringRequest request)
    {
        using JsonDocument document = ParseArguments(request.Arguments);
        string name = RequiredString(document.RootElement, "name");
        string description = OptionalString(document.RootElement, "description");
        double manaCost = RequiredNumber(document.RootElement, "manaCost");
        if (manaCost < 0)
            throw new ModuleArgumentException("TAVI.MAGIC.MANA_COST_INVALID", "法术的 manaCost 必须大于或等于零。", "manaCost");
        Guid spellId = Guid.NewGuid();
        Guid scopeId = Guid.NewGuid();
        Guid costId = Guid.NewGuid();
        WorldAuthoringIntent[] intents =
        [
            new WorldAuthoringIntent.AddElement(spellId, name, description, SpellType),
            new WorldAuthoringIntent.AddScope(scopeId, $"{name} · 法术定义", "保存法术的稳定施放数据。", 1, SpellDefinitionType, spellId),
            new WorldAuthoringIntent.AddAspect(costId, "基础法力消耗", "", manaCost, ManaCostType, spellId, scopeId)
        ];
        return new WorldAuthoringProposal { ExpectedWorldStateId = request.World.StateId, Intents = intents, Rationale = $"创建法术“{name}”，基础法力消耗为 {manaCost.ToString("R", CultureInfo.InvariantCulture)}。" };
    }

    private static WorldAuthoringProposal ProposeAwakening(WorldAuthoringRequest request)
    {
        using JsonDocument document = ParseArguments(request.Arguments);
        Guid characterId = RequiredGuid(document.RootElement, "characterId");
        double initialMana = RequiredNumber(document.RootElement, "initialMana");
        if (initialMana < 0)
            throw new ModuleArgumentException("TAVI.MAGIC.INITIAL_MANA_INVALID", "initialMana 必须大于或等于零。", "initialMana");
        ElementView character = request.World.Elements.SingleOrDefault(value => value.Id == characterId) ?? throw new ModuleArgumentException("TAVI.MAGIC.CHARACTER_NOT_FOUND", $"World 中不存在角色 {characterId}。", "characterId");
        if (character.Type != CharacterType)
            throw new ModuleArgumentException("TAVI.MAGIC.CHARACTER_TYPE_INVALID", $"Element {characterId} 不是 character:character。", "characterId");
        if (request.World.Aspects.Any(value => value.ElementId == characterId && value.Type == ManaType))
            throw new ModuleArgumentException("TAVI.MAGIC.CHARACTER_ALREADY_AWAKENED", $"角色 {character.Name} 已经拥有法力。", "characterId");
        ScopeView[] scopes = request.World.Scopes.Where(value => value.OwnerElementId == characterId && value.Type == ArcaneStateType).ToArray();
        if (scopes.Length > 1)
            throw new ModuleSemanticException("TAVI.MAGIC.ARCANE_STATE_CARDINALITY", $"角色 {character.Name} 拥有多个 magic:arcane-state Scope。", ArcaneStateType, characterId);
        Guid scopeId = scopes.SingleOrDefault()?.Id ?? Guid.NewGuid();
        var intents = new List<WorldAuthoringIntent>();
        if (scopes.Length == 0)
            intents.Add(new WorldAuthoringIntent.AddScope(scopeId, "魔法状态", "保存角色当前的魔法资源。", 1, ArcaneStateType, characterId));
        intents.Add(new WorldAuthoringIntent.AddAspect(Guid.NewGuid(), "当前法力", "", initialMana, ManaType, characterId, scopeId));
        return new WorldAuthoringProposal { ExpectedWorldStateId = request.World.StateId, Intents = intents, Rationale = $"唤醒角色“{character.Name}”，初始法力为 {initialMana.ToString("R", CultureInfo.InvariantCulture)}。" };
    }

    private static JsonDocument ParseArguments(string arguments)
    {
        try
        {
            JsonDocument document = JsonDocument.Parse(arguments);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw new ModuleArgumentException("TAVI.MAGIC.ARGUMENTS_INVALID", "Magic Action 参数必须是 JSON object。", "arguments");
            }
            return document;
        }
        catch (JsonException exception)
        {
            throw new ModuleArgumentException("TAVI.MAGIC.ARGUMENTS_INVALID", "Magic Action 参数不是有效 JSON。", "arguments", exception);
        }
    }

    private static string RequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new ModuleArgumentException("TAVI.MAGIC.ARGUMENT_REQUIRED", $"参数 {name} 必须是非空字符串。", name);
        return value.GetString()!.Trim();
    }

    private static string OptionalString(JsonElement root, string name) => root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static double RequiredNumber(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value) || !value.TryGetDouble(out double number) || !double.IsFinite(number))
            throw new ModuleArgumentException("TAVI.MAGIC.ARGUMENT_REQUIRED", $"参数 {name} 必须是有限数字。", name);
        return number;
    }

    private static Guid RequiredGuid(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out Guid id) || id == Guid.Empty)
            throw new ModuleArgumentException("TAVI.MAGIC.ARGUMENT_REQUIRED", $"参数 {name} 必须是非空 UUID。", name);
        return id;
    }

    private static Guid RequireSingleBinding(SceneContextView context, string slotId)
    {
        SceneSlotBindingView binding = context.Scene.Bindings.SingleOrDefault(value => value.SlotId == slotId) ?? throw new ModuleConfigurationException("TAVI.MAGIC.BINDING_MISSING", $"施法 Scene 缺少 {slotId} 槽位绑定。");
        return binding.ElementIds.Count == 1 ? binding.ElementIds[0] : throw new ModuleConfigurationException("TAVI.MAGIC.BINDING_CARDINALITY", $"施法 Scene 的 {slotId} 槽位必须恰好绑定一个 Element。");
    }

    private static AspectView RequireSingleAspect(SceneContextView context, Guid elementId, SemanticKey type)
    {
        AspectView[] matches = context.Aspects.Where(value => value.ElementId == elementId && value.Type == type).ToArray();
        return matches.Length == 1 ? matches[0] : throw new ModuleConfigurationException("TAVI.MAGIC.ASPECT_CARDINALITY", $"Element {elementId} 必须恰好包含一个 {type} Aspect。");
    }

    private static double ReadMultiplier(IReadOnlyDictionary<string, string> parameters)
    {
        string value = parameters.GetValueOrDefault("mana-cost-multiplier") ?? "1";
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double multiplier) && double.IsFinite(multiplier) && multiplier >= 0 ? multiplier : throw new ModuleConfigurationException("TAVI.MAGIC.PARAMETER_INVALID", $"mana-cost-multiplier 参数值 {value} 无效。");
    }
}
