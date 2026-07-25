using Tavi.Extensibility;

namespace Tavi.Application.Evolution;

/// <summary>保存经过统一校验的声明式 Module 和可选 Plugin 能力。</summary>
public sealed class EvolutionModuleCatalog
{
    private readonly Dictionary<ModuleId, ModulePackageDefinition> _modules;
    private readonly Dictionary<SemanticKey, ElementTypeDefinition> _elementTypes;
    private readonly Dictionary<SemanticKey, ScopeTypeDefinition> _scopeTypes;
    private readonly Dictionary<SemanticKey, AspectGroupDefinition> _aspectGroups;
    private readonly Dictionary<SemanticKey, AspectTypeDefinition> _aspectTypes;
    private readonly Dictionary<SemanticKey, RelationTypeDefinition> _relationTypes;
    private readonly Dictionary<SemanticKey, SceneDefinition> _scenes;
    private readonly PluginRegistrar _plugins;

    private EvolutionModuleCatalog(IEnumerable<ModulePackageDefinition> packages)
    {
        ModulePackageDefinition[] copied = packages.Select(ExtensibilityCopies.Package).ToArray();
        _modules = Unique(copied, package => package.Manifest.Id, "Module");
        _elementTypes = Unique(copied.SelectMany(package => package.Semantics.ElementTypes), definition => definition.Key, "ElementType");
        _scopeTypes = Unique(copied.SelectMany(package => package.Semantics.ScopeTypes), definition => definition.Key, "ScopeType");
        _aspectGroups = Unique(copied.SelectMany(package => package.Semantics.AspectGroups), definition => definition.Key, "AspectGroup");
        _aspectTypes = Unique(copied.SelectMany(package => package.Semantics.AspectTypes), definition => definition.Key, "AspectType");
        _relationTypes = Unique(copied.SelectMany(package => package.Semantics.RelationTypes), definition => definition.Key, "RelationType");
        _scenes = Unique(copied.SelectMany(package => package.Scenes), definition => definition.Id, "SceneDefinition");
        _plugins = new PluginRegistrar(_modules);
        Validate();
    }

    /// <summary>校验并创建 Module Catalog；Catalog 创建后声明式定义不可替换。</summary>
    public static EvolutionModuleCatalog Create(IEnumerable<ModulePackageDefinition> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        return new EvolutionModuleCatalog(packages);
    }

    /// <summary>获取按 Module 标识稳定排序的 Manifest。</summary>
    public IReadOnlyList<ModuleManifest> Modules => Array.AsReadOnly(_modules.Values.Select(package => ExtensibilityCopies.Manifest(package.Manifest)).OrderBy(manifest => manifest.Id.Value, StringComparer.Ordinal).ToArray());

    /// <summary>获取按定义键稳定排序的静态 SceneDefinition。</summary>
    public IReadOnlyList<SceneDefinition> StaticScenes => Array.AsReadOnly(_scenes.Values.Select(ExtensibilityCopies.Scene).OrderBy(scene => scene.Id.Value, StringComparer.Ordinal).ToArray());

    /// <summary>注册并立即校验一个代码 Plugin 的细粒度能力。</summary>
    public void RegisterPlugin(ITaviPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        if (!_modules.TryGetValue(plugin.Module, out ModulePackageDefinition? package))
            throw Invalid(nameof(RegisterPlugin), $"Plugin 所属 Module {plugin.Module} 未加载。");
        if (plugin.Version != package.Manifest.Version)
            throw Invalid(nameof(RegisterPlugin), $"Plugin {plugin.Module} 版本 {plugin.Version} 与 Module 版本 {package.Manifest.Version} 不一致。");
        plugin.Register(_plugins.For(plugin.Module));
    }

    /// <summary>根据稳定键获取静态 SceneDefinition；不存在时返回 null。</summary>
    public SceneDefinition? FindStaticScene(SemanticKey id) => _scenes.TryGetValue(id, out SceneDefinition? value) ? ExtensibilityCopies.Scene(value) : null;

    internal IReadOnlyCollection<ModulePackageDefinition> Packages => _modules.Values;
    internal IReadOnlyDictionary<SemanticKey, ElementTypeDefinition> ElementTypes => _elementTypes;
    internal IReadOnlyDictionary<SemanticKey, ScopeTypeDefinition> ScopeTypes => _scopeTypes;
    internal IReadOnlyDictionary<SemanticKey, AspectGroupDefinition> AspectGroups => _aspectGroups;
    internal IReadOnlyDictionary<SemanticKey, AspectTypeDefinition> AspectTypes => _aspectTypes;
    internal IReadOnlyDictionary<SemanticKey, RelationTypeDefinition> RelationTypes => _relationTypes;
    internal IReadOnlyList<ISceneDefinitionProvider> SceneProviders => _plugins.SceneProviders;
    internal IReadOnlyList<ISceneRuleSettler> RuleSettlers => _plugins.RuleSettlers;

    internal bool IsModuleActive(ModuleId id, IReadOnlyDictionary<string, string> references) => references.TryGetValue(id.Value, out string? version) && _modules.TryGetValue(id, out ModulePackageDefinition? package) && package.Manifest.Version.Value == version;

    private void Validate()
    {
        foreach (ModulePackageDefinition package in _modules.Values)
        {
            ModuleManifest manifest = package.Manifest;
            EnsureOwned(package.Semantics.ElementTypes.Select(value => value.Key), manifest.Id, "ElementType");
            EnsureOwned(package.Semantics.ScopeTypes.Select(value => value.Key), manifest.Id, "ScopeType");
            EnsureOwned(package.Semantics.AspectGroups.Select(value => value.Key), manifest.Id, "AspectGroup");
            EnsureOwned(package.Semantics.AspectTypes.Select(value => value.Key), manifest.Id, "AspectType");
            EnsureOwned(package.Semantics.RelationTypes.Select(value => value.Key), manifest.Id, "RelationType");
            EnsureOwned(package.Semantics.Constraints.Select(value => value.Key), manifest.Id, "Constraint");
            EnsureOwned(package.Scenes.Select(value => value.Id), manifest.Id, "SceneDefinition");
            foreach (ModuleDependency dependency in manifest.Dependencies)
            {
                if (!_modules.TryGetValue(dependency.Id, out ModulePackageDefinition? target))
                    throw Invalid(nameof(Create), $"Module {manifest.Id} 缺少依赖 {dependency.Id}。");
                if (Compare(target.Manifest.Version, dependency.MinimumVersion) < 0)
                    throw Invalid(nameof(Create), $"Module {manifest.Id} 需要 {dependency.Id} >= {dependency.MinimumVersion}，当前为 {target.Manifest.Version}。");
            }
            ValidateDefinitions(package);
        }
    }

    private void ValidateDefinitions(ModulePackageDefinition package)
    {
        foreach (AspectTypeDefinition aspect in package.Semantics.AspectTypes)
        {
            EnsureRange(aspect.MinimumQuantity, aspect.MaximumQuantity, aspect.Key);
            if (aspect.Group is not SemanticKey group)
                continue;
            if (!_aspectGroups.TryGetValue(group, out AspectGroupDefinition? definition))
                throw Invalid(nameof(Create), $"AspectType {aspect.Key} 引用了不存在的 AspectGroup {group}。");
            if (group.Namespace != package.Manifest.Id && !definition.Extensible)
                throw Invalid(nameof(Create), $"AspectGroup {group} 不允许 Module {package.Manifest.Id} 扩充。");
        }
        foreach (RelationTypeDefinition relation in package.Semantics.RelationTypes)
            EnsureRange(relation.MinimumQuantity, relation.MaximumQuantity, relation.Key);
        foreach (SemanticConstraintDefinition constraint in package.Semantics.Constraints)
        {
            if (constraint.Minimum < 0 || constraint.Maximum < constraint.Minimum)
                throw Invalid(nameof(Create), $"约束 {constraint.Key} 的基数范围无效。");
            if (!_elementTypes.ContainsKey(constraint.SubjectElementType) || !_scopeTypes.ContainsKey(constraint.ScopeType))
                throw Invalid(nameof(Create), $"约束 {constraint.Key} 引用了不存在的 ElementType 或 ScopeType。");
            if (constraint.Kind == SemanticConstraintKind.AspectGroupCardinality && (constraint.AspectGroup is not SemanticKey group || !_aspectGroups.ContainsKey(group)))
                throw Invalid(nameof(Create), $"约束 {constraint.Key} 缺少有效 AspectGroup。");
        }
        foreach (SceneDefinition scene in package.Scenes)
        {
            if (scene.Module != package.Manifest.Id || scene.ModuleVersion != package.Manifest.Version || scene.SettlementCapabilities == SceneSettlementCapabilities.None)
                throw Invalid(nameof(Create), $"SceneDefinition {scene.Id} 的 Module、版本或结算能力无效。");
            if (scene.Slots.GroupBy(slot => slot.Id, StringComparer.Ordinal).Any(group => group.Count() > 1))
                throw Invalid(nameof(Create), $"SceneDefinition {scene.Id} 包含重复槽位。");
            foreach (SceneSlotDefinition slot in scene.Slots)
            {
                if (string.IsNullOrWhiteSpace(slot.Id) || string.IsNullOrWhiteSpace(slot.Name) || slot.Minimum < 0 || slot.Maximum < slot.Minimum)
                    throw Invalid(nameof(Create), $"SceneDefinition {scene.Id} 的槽位 {slot.Id} 无效。");
            }
        }
    }

    private static Dictionary<TKey, TValue> Unique<TValue, TKey>(IEnumerable<TValue> values, Func<TValue, TKey> keySelector, string kind) where TKey : notnull
    {
        var result = new Dictionary<TKey, TValue>();
        foreach (TValue value in values)
        {
            TKey key = keySelector(value);
            if (!result.TryAdd(key, value))
                throw Invalid(nameof(Create), $"{kind} {key} 重复注册。");
        }
        return result;
    }

    private static void EnsureOwned(IEnumerable<SemanticKey> keys, ModuleId owner, string kind)
    {
        SemanticKey? foreign = keys.Cast<SemanticKey?>().FirstOrDefault(key => key!.Value.Namespace != owner);
        if (foreign.HasValue)
            throw Invalid(nameof(Create), $"{kind} {foreign.Value} 不属于 Module 命名空间 {owner}。");
    }

    private static void EnsureRange(double? minimum, double? maximum, SemanticKey key)
    {
        if (minimum is double min && !double.IsFinite(min) || maximum is double max && !double.IsFinite(max) || minimum > maximum)
            throw Invalid(nameof(Create), $"语义类型 {key} 的 Quantity 范围无效。");
    }

    private static int Compare(ModuleVersion left, ModuleVersion right)
    {
        int[] leftParts = left.Value.Split('.').Select(int.Parse).ToArray();
        int[] rightParts = right.Value.Split('.').Select(int.Parse).ToArray();
        for (int index = 0; index < 3; index++)
        {
            int compared = leftParts[index].CompareTo(rightParts[index]);
            if (compared != 0)
                return compared;
        }
        return 0;
    }

    private static EvolutionException Invalid(string operation, string message) => new(EvolutionErrorCodes.InvalidModule, TaviErrorCategory.Configuration, operation, message);

    private sealed class PluginRegistrar
    {
        private readonly IReadOnlyDictionary<ModuleId, ModulePackageDefinition> _modules;
        private readonly List<ISceneDefinitionProvider> _sceneProviders = [];
        private readonly List<ISceneRuleSettler> _ruleSettlers = [];
        private readonly List<IWritingContextContributor> _writingContributors = [];
        private readonly List<IWritingInteractionPolicy> _writingPolicies = [];
        private readonly List<IWrittenSceneOutcomeContributor> _outcomeContributors = [];

        internal PluginRegistrar(IReadOnlyDictionary<ModuleId, ModulePackageDefinition> modules) => _modules = modules;

        internal IReadOnlyList<ISceneDefinitionProvider> SceneProviders => _sceneProviders.OrderBy(value => value.Module.Value, StringComparer.Ordinal).ToArray();
        internal IReadOnlyList<ISceneRuleSettler> RuleSettlers => _ruleSettlers.OrderBy(value => value.Module.Value, StringComparer.Ordinal).ToArray();

        internal IPluginRegistrar For(ModuleId owner) => new ScopedRegistrar(this, owner);

        private void Add<T>(T capability, ModuleId owner, Func<T, ModuleId> getModule, List<T> values) where T : class
        {
            ArgumentNullException.ThrowIfNull(capability);
            if (getModule(capability) != owner || !_modules.ContainsKey(owner))
                throw Invalid(nameof(RegisterPlugin), $"Plugin 能力声明的 Module 与注册边界不一致：{getModule(capability)} != {owner}。");
            values.Add(capability);
        }

        private sealed class ScopedRegistrar : IPluginRegistrar
        {
            private readonly PluginRegistrar _owner;
            private readonly ModuleId _module;

            internal ScopedRegistrar(PluginRegistrar owner, ModuleId module)
            {
                _owner = owner;
                _module = module;
            }

            public void AddSceneDefinitionProvider(ISceneDefinitionProvider provider) => _owner.Add(provider, _module, value => value.Module, _owner._sceneProviders);
            public void AddSceneRuleSettler(ISceneRuleSettler settler)
            {
                ArgumentNullException.ThrowIfNull(settler);
                SemanticKey? duplicate = settler.Definitions.FirstOrDefault(definition => _owner._ruleSettlers.Any(existing => existing.Definitions.Contains(definition)));
                if (duplicate.HasValue && !duplicate.Value.IsEmpty)
                    throw Invalid(nameof(RegisterPlugin), $"SceneDefinition {duplicate.Value} 已有规则结算器。");
                _owner.Add(settler, _module, value => value.Module, _owner._ruleSettlers);
            }
            public void AddWritingContextContributor(IWritingContextContributor contributor) => _owner.Add(contributor, _module, value => value.Module, _owner._writingContributors);
            public void AddWritingInteractionPolicy(IWritingInteractionPolicy policy) => _owner.Add(policy, _module, value => value.Module, _owner._writingPolicies);
            public void AddWrittenSceneOutcomeContributor(IWrittenSceneOutcomeContributor contributor) => _owner.Add(contributor, _module, value => value.Module, _owner._outcomeContributors);
        }
    }
}
