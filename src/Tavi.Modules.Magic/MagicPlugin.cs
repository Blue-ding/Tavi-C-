using System.Globalization;
using Tavi.Application.Extensions.Scenario;
using Tavi.Extensibility;

namespace Tavi.Modules.Magic;

/// <summary>为 Magic Module 提供确定性的法术施放规则结算。</summary>
public sealed class MagicPlugin : ITaviPlugin, IScenarioSettlementExtension
{
    private static readonly ModuleId MagicModule = new("magic");
    private static readonly ModuleVersion MagicVersion = new("1.0.0");
    private static readonly SemanticKey CastSpellDefinition = new("magic:cast-spell");
    private static readonly SemanticKey ManaType = new("magic:mana.current");
    private static readonly SemanticKey ManaCostType = new("magic:casting-cost.mana");

    /// <inheritdoc />
    public ModuleId Module => MagicModule;

    /// <inheritdoc />
    public ModuleVersion Version => MagicVersion;

    /// <inheritdoc />
    public IReadOnlySet<SemanticKey> Definitions { get; } = new HashSet<SemanticKey> { CastSpellDefinition };

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
