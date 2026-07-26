using System.Collections.Frozen;
using Tavi.Extensibility;
using Tavi.Domain.World;

namespace Tavi.Application.Extensions;

/// <summary>保存经过统一校验的声明式 Module 和可选 Plugin 能力。</summary>
public sealed class ModuleCatalog : IWorldTypePolicy
{
    private readonly Dictionary<ModuleId, ModulePackageDefinition> _modules;
    private readonly ElementTypeDefinition[] _elementTypes;
    private readonly ScopeTypeDefinition[] _scopeTypes;
    private readonly Dictionary<SemanticKey, AspectGroupDefinition> _aspectGroups;
    private readonly AspectTypeDefinition[] _aspectTypes;
    private readonly RelationTypeDefinition[] _relationTypes;
    private readonly Dictionary<SemanticKey, SceneDefinition> _scenes;
    private ModuleCatalog(IEnumerable<ModulePackageDefinition> packages)
    {
        ModulePackageDefinition[] copied = packages.Select(ExtensibilityCopies.Package).ToArray();
        _modules = Unique(copied, package => package.Manifest.Id, "Module");
        _elementTypes = UniqueList([CoreElementType, .. copied.SelectMany(package => package.Semantics.ElementTypes)], definition => definition.Key, "ElementType");
        _scopeTypes = UniqueList([CoreScopeType, .. copied.SelectMany(package => package.Semantics.ScopeTypes)], definition => definition.Key, "ScopeType");
        _aspectGroups = Unique(copied.SelectMany(package => package.Semantics.AspectGroups), definition => definition.Key, "AspectGroup");
        _aspectTypes = UniqueList([CoreAspectType, .. copied.SelectMany(package => package.Semantics.AspectTypes)], definition => definition.Key, "AspectType");
        _relationTypes = UniqueList([CoreRelationType, .. copied.SelectMany(package => package.Semantics.RelationTypes)], definition => definition.Key, "RelationType");
        _scenes = Unique(copied.SelectMany(package => package.Scenes), definition => definition.Id, "SceneDefinition");
        Validate();
    }

    private static ElementTypeDefinition CoreElementType { get; } = new() { Key = new SemanticKey("core:none"), Name = "无类型", Description = "不携带 Module 专属语义的 Element。", Tags = Array.Empty<SemanticKey>().ToFrozenSet() };
    private static ScopeTypeDefinition CoreScopeType { get; } = new() { Key = new SemanticKey("core:none"), Name = "无类型", Description = "不携带 Module 专属语义的 Scope。", OwnerElementTypes = Array.Empty<SemanticKey>().ToFrozenSet(), Tags = Array.Empty<SemanticKey>().ToFrozenSet() };
    private static AspectTypeDefinition CoreAspectType { get; } = new() { Key = new SemanticKey("core:none"), Name = "无类型", Description = "不携带 Module 专属语义的 Aspect。", SubjectElementTypes = Array.Empty<SemanticKey>().ToFrozenSet(), Tags = Array.Empty<SemanticKey>().ToFrozenSet() };
    private static RelationTypeDefinition CoreRelationType { get; } = new() { Key = new SemanticKey("core:none"), Name = "无类型", Description = "不携带 Module 专属语义的 Relation。", SourceElementTypes = Array.Empty<SemanticKey>().ToFrozenSet(), TargetElementTypes = Array.Empty<SemanticKey>().ToFrozenSet(), Tags = Array.Empty<SemanticKey>().ToFrozenSet() };

    /// <summary>校验并创建 Module Catalog；Catalog 创建后声明式定义不可替换。</summary>
    public static ModuleCatalog Create(IEnumerable<ModulePackageDefinition> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        return new ModuleCatalog(packages);
    }

    /// <summary>获取按 Module 标识稳定排序的 Manifest。</summary>
    public IReadOnlyList<ModuleManifest> Modules => Array.AsReadOnly(_modules.Values.Select(package => ExtensibilityCopies.Manifest(package.Manifest)).OrderBy(manifest => manifest.Id.Value, StringComparer.Ordinal).ToArray());

    /// <summary>获取按定义键稳定排序的静态 SceneDefinition。</summary>
    public IReadOnlyList<SceneDefinition> StaticScenes => Array.AsReadOnly(_scenes.Values.Select(ExtensibilityCopies.Scene).OrderBy(scene => scene.Id.Value, StringComparer.Ordinal).ToArray());

    /// <summary>根据稳定键获取静态 SceneDefinition；不存在时返回 null。</summary>
    public SceneDefinition? FindStaticScene(SemanticKey id) => _scenes.TryGetValue(id, out SceneDefinition? value) ? ExtensibilityCopies.Scene(value) : null;

    /// <summary>获取按稳定键排序的全部 ElementType 定义。</summary>
    public IReadOnlyList<ElementTypeDefinition> GetElementTypes() => Copy(_elementTypes);

    /// <summary>获取按稳定键排序的全部 ScopeType 定义。</summary>
    public IReadOnlyList<ScopeTypeDefinition> GetScopeTypes() => Copy(_scopeTypes);

    /// <summary>获取按稳定键排序的全部 AspectType 定义。</summary>
    public IReadOnlyList<AspectTypeDefinition> GetAspectTypes() => Copy(_aspectTypes);

    /// <summary>获取按稳定键排序的全部 RelationType 定义。</summary>
    public IReadOnlyList<RelationTypeDefinition> GetRelationTypes() => Copy(_relationTypes);

    /// <summary>查找指定 ElementType 定义；未注册时返回 null。</summary>
    public ElementTypeDefinition? FindElementType(ElementType type) => _elementTypes.FirstOrDefault(value => value.Key.Value == type.Value) is { } value ? value with { } : null;

    /// <summary>查找指定 ScopeType 定义；未注册时返回 null。</summary>
    public ScopeTypeDefinition? FindScopeType(ScopeType type) => _scopeTypes.FirstOrDefault(value => value.Key.Value == type.Value) is { } value ? value with { } : null;

    /// <summary>查找指定 AspectType 定义；未注册时返回 null。</summary>
    public AspectTypeDefinition? FindAspectType(AspectType type) => _aspectTypes.FirstOrDefault(value => value.Key.Value == type.Value) is { } value ? value with { } : null;

    /// <summary>查找指定 RelationType 定义；未注册时返回 null。</summary>
    public RelationTypeDefinition? FindRelationType(RelationType type) => _relationTypes.FirstOrDefault(value => value.Key.Value == type.Value) is { } value ? value with { } : null;

    /// <inheritdoc />
    public bool IsRegistered(ElementType type) => FindElementType(type) is not null;

    /// <inheritdoc />
    public bool IsRegistered(ScopeType type) => FindScopeType(type) is not null;

    /// <inheritdoc />
    public bool IsRegistered(AspectType type) => FindAspectType(type) is not null;

    /// <inheritdoc />
    public bool IsRegistered(RelationType type) => FindRelationType(type) is not null;

    internal IReadOnlyCollection<ModulePackageDefinition> Packages => _modules.Values;
    internal IReadOnlyDictionary<SemanticKey, AspectGroupDefinition> AspectGroups => _aspectGroups;
    internal ElementTypeDefinition? FindElementType(SemanticKey key) => _elementTypes.FirstOrDefault(value => value.Key == key);
    internal ScopeTypeDefinition? FindScopeType(SemanticKey key) => _scopeTypes.FirstOrDefault(value => value.Key == key);
    internal AspectTypeDefinition? FindAspectType(SemanticKey key) => _aspectTypes.FirstOrDefault(value => value.Key == key);
    internal RelationTypeDefinition? FindRelationType(SemanticKey key) => _relationTypes.FirstOrDefault(value => value.Key == key);

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
            EnsureOwned(package.Writing.Narrations.Select(value => value.BeatDefinition), manifest.Id, "BeatNarration");
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
        if (package.Manifest.Parameters.GroupBy(parameter => parameter.Key, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw Invalid(nameof(Create), $"Module {package.Manifest.Id} 包含重复参数键。");
        if (package.Semantics.ElementTypes.Any(value => string.IsNullOrWhiteSpace(value.Name) || string.IsNullOrWhiteSpace(value.Description)) || package.Semantics.ScopeTypes.Any(value => string.IsNullOrWhiteSpace(value.Name) || string.IsNullOrWhiteSpace(value.Description)) || package.Semantics.AspectTypes.Any(value => string.IsNullOrWhiteSpace(value.Name) || string.IsNullOrWhiteSpace(value.Description)) || package.Semantics.RelationTypes.Any(value => string.IsNullOrWhiteSpace(value.Name) || string.IsNullOrWhiteSpace(value.Description)))
            throw Invalid(nameof(Create), $"Module {package.Manifest.Id} 的开放类型必须提供名称和语义说明。");
        foreach (ModuleParameterDefinition parameter in package.Manifest.Parameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Key) || !char.IsAsciiLetterLower(parameter.Key[0]) || parameter.Key.Any(character => !char.IsAsciiLetterLower(character) && !char.IsDigit(character) && character is not '.' and not '_' and not '-'))
                throw Invalid(nameof(Create), $"Module {package.Manifest.Id} 的参数键 {parameter.Key} 无效。");
            if (string.IsNullOrWhiteSpace(parameter.Name) || parameter.Description is null || parameter.DefaultValue is null || parameter.Minimum > parameter.Maximum || parameter.AllowedValues.Distinct(StringComparer.Ordinal).Count() != parameter.AllowedValues.Count)
                throw Invalid(nameof(Create), $"Module 参数 {package.Manifest.Id}:{parameter.Key} 定义无效。");
            if (parameter.Type is ModuleParameterType.Boolean or ModuleParameterType.String && (parameter.Minimum.HasValue || parameter.Maximum.HasValue))
                throw Invalid(nameof(Create), $"非数值 Module 参数 {package.Manifest.Id}:{parameter.Key} 不能声明数值范围。");
        }
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
            if (FindElementType(constraint.SubjectElementType) is null || FindScopeType(constraint.ScopeType) is null)
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
        foreach (BeatNarrationDefinition narration in package.Writing.Narrations)
        {
            foreach (WritingParagraphDefinition paragraph in narration.Paragraphs)
            {
                foreach (WritingBindingDefinition binding in paragraph.Bindings.Values)
                {
                    if (binding.Source == WritingBindingSource.Parameter &&
                        package.Manifest.Parameters.All(parameter => parameter.Key != binding.Key))
                        throw Invalid(nameof(Create), $"BeatNarration {narration.BeatDefinition} 引用了不存在的 Setting {binding.Key}。");
                    if (binding.Source == WritingBindingSource.Aspect &&
                        binding.Type is SemanticKey type &&
                        FindAspectType(type) is null)
                        throw Invalid(nameof(Create), $"BeatNarration {narration.BeatDefinition} 引用了不存在的 AspectType {type}。");
                }
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

    private static TValue[] UniqueList<TValue, TKey>(IEnumerable<TValue> values, Func<TValue, TKey> keySelector, string kind) where TKey : notnull
    {
        TValue[] result = values.OrderBy(value => keySelector(value).ToString(), StringComparer.Ordinal).ToArray();
        IGrouping<TKey, TValue>? duplicate = result.GroupBy(keySelector).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw Invalid(nameof(Create), $"{kind} {duplicate.Key} 重复注册。");
        return result;
    }

    private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values) where T : class => Array.AsReadOnly(values.ToArray());

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

    private static ModuleConfigurationException Invalid(string operation, string message) => new($"TAVI.EXTENSIONS.{operation.ToUpperInvariant()}.INVALID_MODULE", message);
}
