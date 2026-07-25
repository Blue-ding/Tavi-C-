using Tavi.Domain.Scenario;
using Tavi.Extensibility;
using Tavi.Application.Extensions;

namespace Tavi.Application.Scenario;

/// <summary>描述一个阻止候选 Scenario 提交的声明式语义问题。</summary>
public sealed record ScenarioSemanticIssue
{
    /// <summary>获取稳定问题代码。</summary>
    public required string Code { get; init; }

    /// <summary>获取产生问题的 Module；核心注册问题没有归属时为 null。</summary>
    public ModuleId? Module { get; init; }

    /// <summary>获取相关语义定义键；问题不对应单项定义时为 null。</summary>
    public SemanticKey? Definition { get; init; }

    /// <summary>获取相关 Scenario 实体标识；问题不对应单项实体时为 null。</summary>
    public Guid? EntityId { get; init; }

    /// <summary>获取供人员理解的诊断消息；调用方不得解析消息判断问题种类。</summary>
    public required string Message { get; init; }
}

/// <summary>在完整候选 Scenario 上执行已激活 Module 的声明式语义约束。</summary>
public sealed class ScenarioSemanticValidator
{
    private readonly ModuleCatalog _catalog;

    /// <summary>创建使用指定 Module Catalog 的语义校验器。</summary>
    public ScenarioSemanticValidator(ModuleCatalog catalog) => _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    /// <summary>校验完整快照并返回全部可发现问题；该方法不修改输入快照。</summary>
    public IReadOnlyList<ScenarioSemanticIssue> Validate(ScenarioSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var issues = new List<ScenarioSemanticIssue>();
        Dictionary<string, string> references = snapshot.Modules.ToDictionary(module => module.Id, module => module.Version, StringComparer.Ordinal);
        ValidateModuleReferences(references, issues);
        ValidateRegisteredTypes(snapshot, references, issues);
        ValidateTypeRules(snapshot, references, issues);
        ValidateConstraints(snapshot, references, issues);
        ValidateScenes(snapshot, references, issues);
        return issues;
    }

    /// <summary>校验完整快照，并在发现问题时抛出带结构化详情的 ScenarioApplicationException。</summary>
    public void EnsureValid(ScenarioSnapshot snapshot, string operation)
    {
        IReadOnlyList<ScenarioSemanticIssue> issues = Validate(snapshot);
        if (issues.Count == 0)
            return;
        var details = new Dictionary<string, string> { ["Issues"] = string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}")) };
        throw new ScenarioApplicationException(ScenarioApplicationErrorCodes.SemanticViolation, TaviErrorCategory.Validation, operation, $"候选 Scenario 包含 {issues.Count} 个语义问题。", details);
    }

    internal bool ElementMatchesSlot(ScenarioSnapshot snapshot, Element element, SceneSlotRequirement requirement)
    {
        if (requirement.ElementTypes.Count > 0 && !requirement.ElementTypes.Contains(new SemanticKey(element.Type.Value)))
            return false;
        if (requirement.RequiredAspectGroups.Count == 0)
            return true;
        HashSet<SemanticKey> elementGroups = snapshot.Aspects.Values.Where(aspect => aspect.ElementId == element.Id).Select(aspect => _catalog.AspectTypes.GetValueOrDefault(new SemanticKey(aspect.Type.Value))?.Group).OfType<SemanticKey>().ToHashSet();
        return requirement.RequiredAspectGroups.All(elementGroups.Contains);
    }

    private void ValidateModuleReferences(IReadOnlyDictionary<string, string> references, List<ScenarioSemanticIssue> issues)
    {
        foreach ((string id, string version) in references)
        {
            ModuleManifest? manifest;
            try
            {
                var moduleId = new ModuleId(id);
                manifest = _catalog.Modules.FirstOrDefault(value => value.Id == moduleId);
            }
            catch (ArgumentException)
            {
                manifest = null;
            }
            if (manifest is null || manifest.Version.Value != version)
                issues.Add(Issue("module.unavailable", $"Scenario 需要 Module {id} {version}，但 Catalog 中没有完全兼容版本。"));
        }
    }

    private void ValidateRegisteredTypes(ScenarioSnapshot snapshot, IReadOnlyDictionary<string, string> references, List<ScenarioSemanticIssue> issues)
    {
        foreach (Element element in snapshot.Elements.Values)
            ValidateType(new SemanticKey(element.Type.Value), element.Id, _catalog.ElementTypes.ContainsKey, references, issues);
        foreach (Scope scope in snapshot.Scopes.Values)
            ValidateType(new SemanticKey(scope.Type.Value), scope.Id, _catalog.ScopeTypes.ContainsKey, references, issues);
        foreach (Aspect aspect in snapshot.Aspects.Values)
            ValidateType(new SemanticKey(aspect.Type.Value), aspect.Id, _catalog.AspectTypes.ContainsKey, references, issues);
        foreach (Relation relation in snapshot.Relations.Values)
            ValidateType(new SemanticKey(relation.Type.Value), relation.Id, _catalog.RelationTypes.ContainsKey, references, issues);
    }

    private void ValidateType(SemanticKey key, Guid entityId, Func<SemanticKey, bool> isRegistered, IReadOnlyDictionary<string, string> references, List<ScenarioSemanticIssue> issues)
    {
        if (key.Value == "core:none")
            return;
        if (!isRegistered(key))
            issues.Add(Issue("type.unregistered", $"实体 {entityId} 使用了未注册类型 {key}。", key.Namespace, key, entityId));
        else if (!_catalog.IsModuleActive(key.Namespace, references))
            issues.Add(Issue("type.module_inactive", $"实体 {entityId} 使用了未在 Scenario 中激活的 Module 类型 {key}。", key.Namespace, key, entityId));
    }

    private void ValidateTypeRules(ScenarioSnapshot snapshot, IReadOnlyDictionary<string, string> references, List<ScenarioSemanticIssue> issues)
    {
        foreach (Scope scope in snapshot.Scopes.Values)
        {
            SemanticKey key = new(scope.Type.Value);
            if (!_catalog.ScopeTypes.TryGetValue(key, out ScopeTypeDefinition? definition) || !_catalog.IsModuleActive(key.Namespace, references) || definition.OwnerElementTypes.Count == 0)
                continue;
            Element owner = snapshot.Elements[scope.OwnerElementId];
            if (!definition.OwnerElementTypes.Contains(new SemanticKey(owner.Type.Value)))
                issues.Add(Issue("scope.owner_type", $"Scope {scope.Id} 的 Owner 类型不符合 {key} 定义。", key.Namespace, key, scope.Id));
        }
        foreach (Aspect aspect in snapshot.Aspects.Values)
        {
            SemanticKey key = new(aspect.Type.Value);
            if (!_catalog.AspectTypes.TryGetValue(key, out AspectTypeDefinition? definition) || !_catalog.IsModuleActive(key.Namespace, references))
                continue;
            Element subject = snapshot.Elements[aspect.ElementId];
            if (definition.SubjectElementTypes.Count > 0 && !definition.SubjectElementTypes.Contains(new SemanticKey(subject.Type.Value)))
                issues.Add(Issue("aspect.subject_type", $"Aspect {aspect.Id} 的目标 Element 类型不符合 {key} 定义。", key.Namespace, key, aspect.Id));
            ValidateQuantity(aspect.Quantity, definition.MinimumQuantity, definition.MaximumQuantity, key, aspect.Id, issues);
        }
        foreach (Relation relation in snapshot.Relations.Values)
        {
            SemanticKey key = new(relation.Type.Value);
            if (!_catalog.RelationTypes.TryGetValue(key, out RelationTypeDefinition? definition) || !_catalog.IsModuleActive(key.Namespace, references))
                continue;
            Element source = snapshot.Elements[relation.SourceElementId];
            Element target = snapshot.Elements[relation.TargetElementId];
            if (definition.SourceElementTypes.Count > 0 && !definition.SourceElementTypes.Contains(new SemanticKey(source.Type.Value)))
                issues.Add(Issue("relation.source_type", $"Relation {relation.Id} 的来源类型不符合 {key} 定义。", key.Namespace, key, relation.Id));
            if (definition.TargetElementTypes.Count > 0 && !definition.TargetElementTypes.Contains(new SemanticKey(target.Type.Value)))
                issues.Add(Issue("relation.target_type", $"Relation {relation.Id} 的目标类型不符合 {key} 定义。", key.Namespace, key, relation.Id));
            ValidateQuantity(relation.Quantity, definition.MinimumQuantity, definition.MaximumQuantity, key, relation.Id, issues);
        }
    }

    private void ValidateConstraints(ScenarioSnapshot snapshot, IReadOnlyDictionary<string, string> references, List<ScenarioSemanticIssue> issues)
    {
        Dictionary<SemanticKey, HashSet<SemanticKey>> groupMembers = _catalog.AspectTypes.Values.Where(type => type.Group.HasValue).GroupBy(type => type.Group!.Value).ToDictionary(group => group.Key, group => group.Select(type => type.Key).ToHashSet());
        foreach (ModulePackageDefinition package in _catalog.Packages.Where(package => _catalog.IsModuleActive(package.Manifest.Id, references)))
        {
            foreach (SemanticConstraintDefinition constraint in package.Semantics.Constraints)
            {
                foreach (Element element in snapshot.Elements.Values.Where(element => element.Type.Value == constraint.SubjectElementType.Value))
                {
                    Scope[] scopes = snapshot.Scopes.Values.Where(scope => scope.OwnerElementId == element.Id && scope.Type.Value == constraint.ScopeType.Value).ToArray();
                    if (constraint.Kind == SemanticConstraintKind.OwnedScopeCardinality)
                    {
                        CheckCardinality(constraint, element.Id, scopes.Length, issues);
                        continue;
                    }
                    HashSet<SemanticKey> members = groupMembers.GetValueOrDefault(constraint.AspectGroup!.Value, []);
                    int count = scopes.SelectMany(scope => snapshot.Aspects.Values.Where(aspect => aspect.ScopeId == scope.Id)).Count(aspect => members.Contains(new SemanticKey(aspect.Type.Value)) && (!constraint.AspectMustTargetScopeOwner || aspect.ElementId == element.Id));
                    CheckCardinality(constraint, element.Id, count, issues);
                }
            }
        }
    }

    private void ValidateScenes(ScenarioSnapshot snapshot, IReadOnlyDictionary<string, string> references, List<ScenarioSemanticIssue> issues)
    {
        foreach (Scene scene in snapshot.Scenes.Values)
        {
            ModuleId module;
            ModuleVersion version;
            try
            {
                module = new ModuleId(scene.ModuleId);
                version = new ModuleVersion(scene.ModuleVersion);
            }
            catch (ArgumentException)
            {
                issues.Add(Issue("scene.module_invalid", $"Scene {scene.Id} 的 Module 身份或版本无效。", entityId: scene.Id));
                continue;
            }
            if (!_catalog.IsModuleActive(module, references) || !_catalog.Modules.Any(manifest => manifest.Id == module && manifest.Version == version))
                issues.Add(Issue("scene.module_inactive", $"Scene {scene.Id} 所需 Module {module} {version} 未激活。", module, new SemanticKey(scene.DefinitionId.Value), scene.Id));
        }
    }

    private static void ValidateQuantity(double quantity, double? minimum, double? maximum, SemanticKey key, Guid entityId, List<ScenarioSemanticIssue> issues)
    {
        if (minimum is double min && quantity < min || maximum is double max && quantity > max)
            issues.Add(Issue("quantity.out_of_range", $"实体 {entityId} 的 Quantity {quantity} 超出 {key} 声明范围。", key.Namespace, key, entityId));
    }

    private static void CheckCardinality(SemanticConstraintDefinition constraint, Guid entityId, int count, List<ScenarioSemanticIssue> issues)
    {
        if (count >= constraint.Minimum && (!constraint.Maximum.HasValue || count <= constraint.Maximum.Value))
            return;
        string message = string.IsNullOrWhiteSpace(constraint.Message) ? $"Element {entityId} 不满足约束 {constraint.Key}，实际数量为 {count}。" : constraint.Message;
        issues.Add(Issue("constraint.cardinality", message, constraint.Key.Namespace, constraint.Key, entityId));
    }

    private static ScenarioSemanticIssue Issue(string code, string message, ModuleId? module = null, SemanticKey? definition = null, Guid? entityId = null) => new() { Code = code, Message = message, Module = module, Definition = definition, EntityId = entityId };
}
