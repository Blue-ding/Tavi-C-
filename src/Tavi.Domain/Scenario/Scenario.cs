namespace Tavi.Domain.Scenario;

/// <summary>表示当前演绎世界状态，负责隔离 EARS 与 Scene、执行原子操作并维护结构不变量。</summary>
public sealed class Scenario
{
    private readonly ScenarioSnapshot _data;

    private Scenario(ScenarioSnapshot snapshot) => _data = CloneSnapshot(snapshot);

    /// <summary>完整校验并深复制给定快照后创建运行时 Scenario。</summary>
    public static Scenario Create(ScenarioSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSnapshot(snapshot);
        return new Scenario(snapshot);
    }

    /// <summary>获取当前 Scenario 状态标识；该值不表达时间顺序。</summary>
    public Guid StateId => _data.Id;

    /// <summary>获取创建该 Scenario 时所依据的 World StateId。</summary>
    public Guid SourceWorldStateId => _data.SourceWorldStateId;

    /// <summary>创建当前 Scenario 的独立深复制快照。</summary>
    public ScenarioSnapshot CreateSnapshot() => CloneSnapshot(_data);

    /// <summary>获取全部 Module 引用的独立副本。</summary>
    public IReadOnlyCollection<ScenarioModuleReference> GetModules() => _data.Modules.Select(CloneModule).ToArray();

    /// <summary>根据标识获取独立的 Element 副本。</summary>
    public Element GetElement(Guid elementId) => CloneElement(GetElementCore(elementId, nameof(GetElement)));

    /// <summary>根据标识获取独立的 Aspect 副本。</summary>
    public Aspect GetAspect(Guid aspectId) => CloneAspect(GetAspectCore(aspectId, nameof(GetAspect)));

    /// <summary>根据标识获取独立的 Relation 副本。</summary>
    public Relation GetRelation(Guid relationId) => CloneRelation(GetRelationCore(relationId, nameof(GetRelation)));

    /// <summary>根据标识获取独立的 Scope 副本。</summary>
    public Scope GetScope(Guid scopeId) => CloneScope(GetScopeCore(scopeId, nameof(GetScope)));

    /// <summary>根据标识获取独立的 Scene 副本。</summary>
    public Scene GetScene(Guid sceneId) => CloneScene(GetSceneCore(sceneId, nameof(GetScene)));

    /// <summary>获取全部 Element 的独立副本。</summary>
    public IReadOnlyCollection<Element> GetElements() => _data.Elements.Values.Select(CloneElement).ToArray();

    /// <summary>获取全部 Aspect 的独立副本。</summary>
    public IReadOnlyCollection<Aspect> GetAspects() => _data.Aspects.Values.Select(CloneAspect).ToArray();

    /// <summary>获取全部 Relation 的独立副本。</summary>
    public IReadOnlyCollection<Relation> GetRelations() => _data.Relations.Values.Select(CloneRelation).ToArray();

    /// <summary>获取全部 Scope 的独立副本。</summary>
    public IReadOnlyCollection<Scope> GetScopes() => _data.Scopes.Values.Select(CloneScope).ToArray();

    /// <summary>获取全部 Scene 的独立副本。</summary>
    public IReadOnlyCollection<Scene> GetScenes() => _data.Scenes.Values.Select(CloneScene).ToArray();

    /// <summary>获取针对指定 Element 的全部 Aspect 副本。</summary>
    public IReadOnlyCollection<Aspect> GetAspectsForElement(Guid elementId)
    {
        _ = GetElementCore(elementId, nameof(GetAspectsForElement));
        return _data.Aspects.Values.Where(aspect => aspect.ElementId == elementId).Select(CloneAspect).ToArray();
    }

    /// <summary>获取指定 Scope 中的全部 Aspect 副本。</summary>
    public IReadOnlyCollection<Aspect> GetAspectsInScope(Guid scopeId)
    {
        _ = GetScopeCore(scopeId, nameof(GetAspectsInScope));
        return _data.Aspects.Values.Where(aspect => aspect.ScopeId == scopeId).Select(CloneAspect).ToArray();
    }

    /// <summary>获取与指定 Element 任一方向相连的全部 Relation 副本。</summary>
    public IReadOnlyCollection<Relation> GetRelationsForElement(Guid elementId)
    {
        _ = GetElementCore(elementId, nameof(GetRelationsForElement));
        return _data.Relations.Values.Where(relation => relation.SourceElementId == elementId || relation.TargetElementId == elementId).Select(CloneRelation).ToArray();
    }

    /// <summary>获取指定 Scope 中的全部 Relation 副本。</summary>
    public IReadOnlyCollection<Relation> GetRelationsInScope(Guid scopeId)
    {
        _ = GetScopeCore(scopeId, nameof(GetRelationsInScope));
        return _data.Relations.Values.Where(relation => relation.ScopeId == scopeId).Select(CloneRelation).ToArray();
    }

    /// <summary>获取由指定 Element 持有的全部 Scope 副本。</summary>
    public IReadOnlyCollection<Scope> GetScopesOwnedByElement(Guid elementId)
    {
        _ = GetElementCore(elementId, nameof(GetScopesOwnedByElement));
        return _data.Scopes.Values.Where(scope => scope.OwnerElementId == elementId).Select(CloneScope).ToArray();
    }

    /// <summary>在 ScenarioSession 写边界内原子应用操作组，失败时恢复完整前态。</summary>
    internal ScenarioApplyResult Apply(ScenarioChangeSet changeSet)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        if (changeSet.IsEmpty)
            return ScenarioApplyResult.Unchanged(StateId);
        ScenarioSnapshot before = CreateSnapshot();
        bool changed = false;
        try
        {
            foreach (ScenarioOperation operation in changeSet.Operations)
                changed |= ApplyOperation(operation);
        }
        catch (Exception operationException)
        {
            try
            {
                Restore(before);
            }
            catch (Exception rollbackException)
            {
                throw new ScenarioException(ScenarioErrorCodes.RollbackFailed, TaviErrorCategory.InternalFailure, nameof(Apply), "Scenario 操作失败且无法恢复完整前态。", new AggregateException(operationException, rollbackException));
            }
            throw;
        }
        if (!changed)
            return ScenarioApplyResult.Unchanged(StateId);
        Guid previousStateId = StateId;
        _data.Id = Guid.NewGuid();
        ScenarioChangeSet inverse = new([new RestoreScenarioSnapshotOperation(before)]);
        return new ScenarioApplyResult(previousStateId, StateId, new AppliedScenarioChangeSet(changeSet, inverse));
    }

    private bool ApplyOperation(ScenarioOperation operation) => operation switch
    {
        AddElementOperation value => ApplyAddElement(value),
        RemoveElementOperation value => ApplyRemoveElement(value),
        UpdateElementOperation value => ApplyUpdateElement(value),
        AddScopeOperation value => ApplyAddScope(value),
        RemoveScopeOperation value => ApplyRemoveScope(value),
        UpdateScopeOperation value => ApplyUpdateScope(value),
        AddAspectOperation value => ApplyAddAspect(value),
        RemoveAspectOperation value => ApplyRemoveAspect(value),
        UpdateAspectOperation value => ApplyUpdateAspect(value),
        AddRelationOperation value => ApplyAddRelation(value),
        RemoveRelationOperation value => ApplyRemoveRelation(value),
        UpdateRelationOperation value => ApplyUpdateRelation(value),
        AddSceneOperation value => ApplyAddScene(value),
        RemoveSceneOperation value => ApplyRemoveScene(value),
        SetSceneSlotBindingOperation value => ApplySetSceneSlotBinding(value),
        ClearSceneSlotBindingOperation value => ApplyClearSceneSlotBinding(value),
        UpdateSceneStateOperation value => ApplyUpdateSceneState(value),
        ClearSettledScenesOperation => ApplyClearSettledScenes(),
        RestoreScenarioSnapshotOperation value => ApplyRestoreSnapshot(value),
        _ => throw Invalid(nameof(Apply), $"不支持的 Scenario 操作类型 {operation.GetType().FullName}。")
    };

    private bool ApplyAddElement(AddElementOperation operation)
    {
        const string name = nameof(AddElementOperation);
        EnsureId(operation.ElementId, name);
        EnsureCommon(operation.Name, operation.Description, operation.Type.IsEmpty, name);
        if (_data.Elements.ContainsKey(operation.ElementId))
            throw Duplicate(name, "Element", operation.ElementId);
        _data.Elements.Add(operation.ElementId, new Element(operation.ElementId, operation.Name, operation.Description, operation.Type));
        return true;
    }

    private bool ApplyRemoveElement(RemoveElementOperation operation)
    {
        const string name = nameof(RemoveElementOperation);
        Element element = GetElementCore(operation.ElementId, name);
        EnsureElementsNotProcessing([element.Id], name);
        Guid[] ownedScopeIds = _data.Scopes.Values.Where(scope => scope.OwnerElementId == element.Id).Select(scope => scope.Id).ToArray();
        HashSet<Guid> ownedScopes = ownedScopeIds.ToHashSet();
        foreach (Guid aspectId in _data.Aspects.Values.Where(aspect => aspect.ElementId == element.Id || ownedScopes.Contains(aspect.ScopeId)).Select(aspect => aspect.Id).ToArray())
            _data.Aspects.Remove(aspectId);
        foreach (Guid relationId in _data.Relations.Values.Where(relation => relation.SourceElementId == element.Id || relation.TargetElementId == element.Id || ownedScopes.Contains(relation.ScopeId)).Select(relation => relation.Id).ToArray())
            _data.Relations.Remove(relationId);
        foreach (Guid scopeId in ownedScopeIds)
            _data.Scopes.Remove(scopeId);
        foreach (Scene scene in _data.Scenes.Values.Where(scene => scene.State == SceneState.Binding))
            scene.RemoveElementFromBindings(element.Id);
        _data.Elements.Remove(element.Id);
        return true;
    }

    private bool ApplyUpdateElement(UpdateElementOperation operation)
    {
        const string name = nameof(UpdateElementOperation);
        EnsureCommon(operation.Name, operation.Description, operation.Type.IsEmpty, name);
        Element element = GetElementCore(operation.ElementId, name);
        EnsureElementsNotProcessing([element.Id], name);
        if (element.Name == operation.Name && element.Description == operation.Description && element.Type == operation.Type)
            return false;
        element.Update(operation.Name, operation.Description, operation.Type);
        return true;
    }

    private bool ApplyAddScope(AddScopeOperation operation)
    {
        const string name = nameof(AddScopeOperation);
        EnsureId(operation.ScopeId, name);
        EnsureType(operation.Type.IsEmpty, name);
        _ = GetElementCore(operation.OwnerElementId, name);
        EnsureElementsNotProcessing([operation.OwnerElementId], name);
        if (_data.Scopes.ContainsKey(operation.ScopeId))
            throw Duplicate(name, "Scope", operation.ScopeId);
        _data.Scopes.Add(operation.ScopeId, new Scope(operation.ScopeId, operation.Quantity, operation.Type, operation.OwnerElementId));
        return true;
    }

    private bool ApplyRemoveScope(RemoveScopeOperation operation)
    {
        const string name = nameof(RemoveScopeOperation);
        Scope scope = GetScopeCore(operation.ScopeId, name);
        EnsureElementsNotProcessing([scope.OwnerElementId], name);
        foreach (Guid aspectId in _data.Aspects.Values.Where(aspect => aspect.ScopeId == scope.Id).Select(aspect => aspect.Id).ToArray())
            _data.Aspects.Remove(aspectId);
        foreach (Guid relationId in _data.Relations.Values.Where(relation => relation.ScopeId == scope.Id).Select(relation => relation.Id).ToArray())
            _data.Relations.Remove(relationId);
        _data.Scopes.Remove(scope.Id);
        return true;
    }

    private bool ApplyUpdateScope(UpdateScopeOperation operation)
    {
        const string name = nameof(UpdateScopeOperation);
        EnsureType(operation.Type.IsEmpty, name);
        Scope scope = GetScopeCore(operation.ScopeId, name);
        EnsureElementsNotProcessing([scope.OwnerElementId], name);
        if (scope.Quantity == operation.Quantity && scope.Type == operation.Type)
            return false;
        scope.Update(operation.Quantity, operation.Type);
        return true;
    }

    private bool ApplyAddAspect(AddAspectOperation operation)
    {
        const string name = nameof(AddAspectOperation);
        EnsureId(operation.AspectId, name);
        EnsureType(operation.Type.IsEmpty, name);
        _ = GetElementCore(operation.ElementId, name);
        Scope scope = GetScopeCore(operation.ScopeId, name);
        EnsureElementsNotProcessing([operation.ElementId, scope.OwnerElementId], name);
        if (_data.Aspects.ContainsKey(operation.AspectId))
            throw Duplicate(name, "Aspect", operation.AspectId);
        _data.Aspects.Add(operation.AspectId, new Aspect(operation.AspectId, operation.Quantity, operation.Type, operation.ElementId, operation.ScopeId));
        return true;
    }

    private bool ApplyRemoveAspect(RemoveAspectOperation operation)
    {
        const string name = nameof(RemoveAspectOperation);
        Aspect aspect = GetAspectCore(operation.AspectId, name);
        EnsureElementsNotProcessing([aspect.ElementId, GetScopeCore(aspect.ScopeId, name).OwnerElementId], name);
        _data.Aspects.Remove(aspect.Id);
        return true;
    }

    private bool ApplyUpdateAspect(UpdateAspectOperation operation)
    {
        const string name = nameof(UpdateAspectOperation);
        EnsureType(operation.Type.IsEmpty, name);
        Aspect aspect = GetAspectCore(operation.AspectId, name);
        EnsureElementsNotProcessing([aspect.ElementId, GetScopeCore(aspect.ScopeId, name).OwnerElementId], name);
        if (aspect.Quantity == operation.Quantity && aspect.Type == operation.Type)
            return false;
        aspect.Update(operation.Quantity, operation.Type);
        return true;
    }

    private bool ApplyAddRelation(AddRelationOperation operation)
    {
        const string name = nameof(AddRelationOperation);
        EnsureId(operation.RelationId, name);
        EnsureType(operation.Type.IsEmpty, name);
        _ = GetElementCore(operation.SourceElementId, name);
        _ = GetElementCore(operation.TargetElementId, name);
        Scope scope = GetScopeCore(operation.ScopeId, name);
        EnsureElementsNotProcessing([operation.SourceElementId, operation.TargetElementId, scope.OwnerElementId], name);
        if (_data.Relations.ContainsKey(operation.RelationId))
            throw Duplicate(name, "Relation", operation.RelationId);
        _data.Relations.Add(operation.RelationId, new Relation(operation.RelationId, operation.Quantity, operation.Type, operation.SourceElementId, operation.TargetElementId, operation.ScopeId));
        return true;
    }

    private bool ApplyRemoveRelation(RemoveRelationOperation operation)
    {
        const string name = nameof(RemoveRelationOperation);
        Relation relation = GetRelationCore(operation.RelationId, name);
        EnsureElementsNotProcessing([relation.SourceElementId, relation.TargetElementId, GetScopeCore(relation.ScopeId, name).OwnerElementId], name);
        _data.Relations.Remove(relation.Id);
        return true;
    }

    private bool ApplyUpdateRelation(UpdateRelationOperation operation)
    {
        const string name = nameof(UpdateRelationOperation);
        EnsureType(operation.Type.IsEmpty, name);
        Relation relation = GetRelationCore(operation.RelationId, name);
        EnsureElementsNotProcessing([relation.SourceElementId, relation.TargetElementId, GetScopeCore(relation.ScopeId, name).OwnerElementId], name);
        if (relation.Quantity == operation.Quantity && relation.Type == operation.Type)
            return false;
        relation.Update(operation.Quantity, operation.Type);
        return true;
    }

    private bool ApplyAddScene(AddSceneOperation operation)
    {
        const string name = nameof(AddSceneOperation);
        EnsureId(operation.SceneId, name);
        EnsureId(operation.BasedOnScenarioStateId, name);
        EnsureText(operation.ModuleId, name, nameof(operation.ModuleId));
        EnsureText(operation.ModuleVersion, name, nameof(operation.ModuleVersion));
        EnsureText(operation.Name, name, nameof(operation.Name));
        EnsureDescription(operation.Description, name);
        if (operation.DefinitionId.IsEmpty)
            throw Invalid(name, "SceneDefinition 键不能是未初始化值。");
        EnsureSettlementOptions(operation.SettlementOptions, name);
        if (_data.Scenes.ContainsKey(operation.SceneId))
            throw Duplicate(name, "Scene", operation.SceneId);
        _data.Scenes.Add(operation.SceneId, new Scene(operation.SceneId, operation.DefinitionId, operation.ModuleId, operation.ModuleVersion, operation.BasedOnScenarioStateId, operation.Name, operation.Description, operation.SettlementOptions, SceneState.Binding, operation.Slots));
        return true;
    }

    private bool ApplyRemoveScene(RemoveSceneOperation operation)
    {
        const string name = nameof(RemoveSceneOperation);
        Scene scene = GetSceneCore(operation.SceneId, name);
        if (scene.State == SceneState.Processing)
            throw Invalid(name, $"Scene {scene.Id} 正在处理，结算前不能删除。");
        _data.Scenes.Remove(scene.Id);
        return true;
    }

    private bool ApplySetSceneSlotBinding(SetSceneSlotBindingOperation operation)
    {
        const string name = nameof(SetSceneSlotBindingOperation);
        ArgumentNullException.ThrowIfNull(operation.Binding);
        Scene scene = GetSceneCore(operation.SceneId, name);
        EnsureSceneBindingMutable(scene, name);
        if (scene.GetSlotSpecifications().Count > 0 && scene.GetSlotSpecifications().All(slot => slot.Id != operation.Binding.SlotId))
            throw Invalid(name, $"Scene {scene.Id} 不包含槽位 {operation.Binding.SlotId}。");
        foreach (Guid elementId in operation.Binding.ElementIds)
        {
            _ = GetElementCore(elementId, name);
            Scene? occupied = _data.Scenes.Values.FirstOrDefault(candidate => candidate.Id != scene.Id && candidate.State != SceneState.Settled && candidate.ContainsElement(elementId));
            if (occupied is not null)
                throw Invalid(name, $"Element {elementId} 已归属 Scene {occupied.Id}。");
            SceneSlotBinding? duplicateSlot = scene.GetBindings().FirstOrDefault(binding => binding.SlotId != operation.Binding.SlotId && binding.ElementIds.Contains(elementId));
            if (duplicateSlot is not null)
                throw Invalid(name, $"Element {elementId} 已绑定到当前 Scene 的槽位 {duplicateSlot.SlotId}。");
        }
        return scene.SetBinding(operation.Binding);
    }

    private bool ApplyClearSceneSlotBinding(ClearSceneSlotBindingOperation operation)
    {
        const string name = nameof(ClearSceneSlotBindingOperation);
        EnsureText(operation.SlotId, name, nameof(operation.SlotId));
        Scene scene = GetSceneCore(operation.SceneId, name);
        EnsureSceneBindingMutable(scene, name);
        return scene.ClearBinding(operation.SlotId);
    }

    private bool ApplyUpdateSceneState(UpdateSceneStateOperation operation)
    {
        const string name = nameof(UpdateSceneStateOperation);
        if (!Enum.IsDefined(operation.State))
            throw Invalid(name, "SceneState 不是已定义值。");
        Scene scene = GetSceneCore(operation.SceneId, name);
        bool validTransition = scene.State == SceneState.Binding && operation.State == SceneState.Processing || scene.State == SceneState.Processing && operation.State == SceneState.Settled;
        if (!validTransition)
            throw Invalid(name, $"Scene {scene.Id} 不能从 {scene.State} 转换为 {operation.State}。");
        return scene.UpdateState(operation.State);
    }

    private bool ApplyClearSettledScenes()
    {
        Guid[] settledIds = _data.Scenes.Values.Where(scene => scene.State == SceneState.Settled).Select(scene => scene.Id).ToArray();
        foreach (Guid sceneId in settledIds)
            _data.Scenes.Remove(sceneId);
        return settledIds.Length > 0;
    }

    private bool ApplyRestoreSnapshot(RestoreScenarioSnapshotOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation.Snapshot);
        ValidateSnapshot(operation.Snapshot);
        Restore(operation.Snapshot);
        return true;
    }

    private void Restore(ScenarioSnapshot snapshot)
    {
        ScenarioSnapshot copy = CloneSnapshot(snapshot);
        _data.Id = copy.Id;
        _data.SourceWorldStateId = copy.SourceWorldStateId;
        _data.Modules = copy.Modules;
        _data.Elements = copy.Elements;
        _data.Aspects = copy.Aspects;
        _data.Relations = copy.Relations;
        _data.Scopes = copy.Scopes;
        _data.Scenes = copy.Scenes;
    }

    private Element GetElementCore(Guid id, string operation)
    {
        EnsureId(id, operation);
        return _data.Elements.TryGetValue(id, out Element? value) ? value : throw NotFound(operation, "Element", id);
    }

    private Aspect GetAspectCore(Guid id, string operation)
    {
        EnsureId(id, operation);
        return _data.Aspects.TryGetValue(id, out Aspect? value) ? value : throw NotFound(operation, "Aspect", id);
    }

    private Relation GetRelationCore(Guid id, string operation)
    {
        EnsureId(id, operation);
        return _data.Relations.TryGetValue(id, out Relation? value) ? value : throw NotFound(operation, "Relation", id);
    }

    private Scope GetScopeCore(Guid id, string operation)
    {
        EnsureId(id, operation);
        return _data.Scopes.TryGetValue(id, out Scope? value) ? value : throw NotFound(operation, "Scope", id);
    }

    private Scene GetSceneCore(Guid id, string operation)
    {
        EnsureId(id, operation);
        return _data.Scenes.TryGetValue(id, out Scene? value) ? value : throw NotFound(operation, "Scene", id);
    }

    private static void ValidateSnapshot(ScenarioSnapshot snapshot)
    {
        var errors = new List<string>();
        if (snapshot.Id == Guid.Empty)
            errors.Add("ScenarioSnapshot.Id 不能为空。");
        if (snapshot.SourceWorldStateId == Guid.Empty)
            errors.Add("ScenarioSnapshot.SourceWorldStateId 不能为空。");
        if (snapshot.Modules is null || snapshot.Elements is null || snapshot.Aspects is null || snapshot.Relations is null || snapshot.Scopes is null || snapshot.Scenes is null)
            throw InvalidSnapshot(["ScenarioSnapshot 集合不能为 null。"]);
        if (errors.Count > 0)
            throw InvalidSnapshot(errors);
        ValidateModules(snapshot.Modules, errors);
        foreach ((Guid key, Element? element) in snapshot.Elements)
        {
            if (element is null || key == Guid.Empty || element.Id != key || string.IsNullOrWhiteSpace(element.Name) || element.Description is null || element.Type.IsEmpty)
                errors.Add($"Element {key} 不合法。");
        }
        foreach ((Guid key, Scope? scope) in snapshot.Scopes)
        {
            if (scope is null || key == Guid.Empty || scope.Id != key || scope.Type.IsEmpty || !snapshot.Elements.ContainsKey(scope.OwnerElementId))
                errors.Add($"Scope {key} 不合法。");
        }
        foreach ((Guid key, Aspect? aspect) in snapshot.Aspects)
        {
            if (aspect is null || key == Guid.Empty || aspect.Id != key || aspect.Type.IsEmpty || !snapshot.Elements.ContainsKey(aspect.ElementId) || !snapshot.Scopes.ContainsKey(aspect.ScopeId))
                errors.Add($"Aspect {key} 不合法。");
        }
        foreach ((Guid key, Relation? relation) in snapshot.Relations)
        {
            if (relation is null || key == Guid.Empty || relation.Id != key || relation.Type.IsEmpty || !snapshot.Elements.ContainsKey(relation.SourceElementId) || !snapshot.Elements.ContainsKey(relation.TargetElementId) || !snapshot.Scopes.ContainsKey(relation.ScopeId))
                errors.Add($"Relation {key} 不合法。");
        }
        foreach ((Guid key, Scene? scene) in snapshot.Scenes)
        {
            if (scene is null || key == Guid.Empty || scene.Id != key || scene.DefinitionId.IsEmpty || string.IsNullOrWhiteSpace(scene.ModuleId) || string.IsNullOrWhiteSpace(scene.ModuleVersion) || scene.BasedOnScenarioStateId == Guid.Empty || string.IsNullOrWhiteSpace(scene.Name) || scene.Description is null || !Enum.IsDefined(scene.State) || !IsSettlementOptionsValid(scene.SettlementOptions))
            {
                errors.Add($"Scene {key} 不合法。");
                continue;
            }
            foreach (SceneSlotBinding binding in scene.GetBindings())
            {
                if (scene.GetSlotSpecifications().Count > 0 && scene.GetSlotSpecifications().All(slot => slot.Id != binding.SlotId) || scene.State != SceneState.Settled && binding.ElementIds.Any(id => !snapshot.Elements.ContainsKey(id)))
                    errors.Add($"Scene {key} 的槽位 {binding.SlotId} 不合法。");
            }
        }
        Guid? duplicateActiveBinding = snapshot.Scenes.Values.Where(scene => scene.State != SceneState.Settled).SelectMany(scene => scene.GetBindings().SelectMany(binding => binding.ElementIds.Select(elementId => (scene.Id, elementId)))).GroupBy(value => value.elementId).Where(group => group.Select(value => value.Id).Distinct().Count() > 1 || group.Count() > 1).Select(group => (Guid?)group.Key).FirstOrDefault();
        if (duplicateActiveBinding.HasValue)
            errors.Add($"Element {duplicateActiveBinding.Value} 同时绑定到多个活动槽位。");
        if (errors.Count > 0)
            throw InvalidSnapshot(errors);
    }

    private static void ValidateModules(IEnumerable<ScenarioModuleReference> modules, List<string> errors)
    {
        ScenarioModuleReference[] copied = modules.ToArray();
        if (copied.Any(module => module is null || string.IsNullOrWhiteSpace(module.Id) || string.IsNullOrWhiteSpace(module.Version)))
            errors.Add("Scenario Module 引用不能为 null、空标识或空版本。");
        if (copied.Where(module => module is not null).Any(module => module.Parameters.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)))
            errors.Add("Scenario Module 冻结参数不能包含空键或 null 值。");
        if (copied.Where(module => module is not null).GroupBy(module => module.Id, StringComparer.Ordinal).Any(group => group.Count() > 1))
            errors.Add("Scenario Module 引用不能包含重复标识。");
    }

    private static ScenarioSnapshot CloneSnapshot(ScenarioSnapshot source) => new()
    {
        Id = source.Id,
        SourceWorldStateId = source.SourceWorldStateId,
        Modules = source.Modules.Select(CloneModule).ToList(),
        Elements = source.Elements.ToDictionary(pair => pair.Key, pair => CloneElement(pair.Value)),
        Aspects = source.Aspects.ToDictionary(pair => pair.Key, pair => CloneAspect(pair.Value)),
        Relations = source.Relations.ToDictionary(pair => pair.Key, pair => CloneRelation(pair.Value)),
        Scopes = source.Scopes.ToDictionary(pair => pair.Key, pair => CloneScope(pair.Value)),
        Scenes = source.Scenes.ToDictionary(pair => pair.Key, pair => CloneScene(pair.Value))
    };

    private static ScenarioModuleReference CloneModule(ScenarioModuleReference source) => new(source.Id, source.Version, source.Parameters);

    private static Element CloneElement(Element source) => new(source.Id, source.Name, source.Description, source.Type);

    private static Aspect CloneAspect(Aspect source) => new(source.Id, source.Quantity, source.Type, source.ElementId, source.ScopeId);

    private static Relation CloneRelation(Relation source) => new(source.Id, source.Quantity, source.Type, source.SourceElementId, source.TargetElementId, source.ScopeId);

    private static Scope CloneScope(Scope source) => new(source.Id, source.Quantity, source.Type, source.OwnerElementId);

    private static Scene CloneScene(Scene source) => new(source.Id, source.DefinitionId, source.ModuleId, source.ModuleVersion, source.BasedOnScenarioStateId, source.Name, source.Description, source.SettlementOptions, source.State, source.GetSlotSpecifications(), source.GetBindings(), source.DefinitionFrozen);

    private static void EnsureId(Guid id, string operation)
    {
        if (id == Guid.Empty)
            throw Invalid(operation, "标识不能是空 Guid。");
    }

    private static void EnsureCommon(string name, string description, bool typeIsEmpty, string operation)
    {
        EnsureText(name, operation, nameof(name));
        EnsureDescription(description, operation);
        if (typeIsEmpty)
            throw Invalid(operation, "类型不能是未初始化值。");
    }

    private static void EnsureText(string value, string operation, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw Invalid(operation, $"{parameterName} 不能为空或只包含空白字符。");
    }

    private static void EnsureDescription(string description, string operation)
    {
        if (description is null)
            throw Invalid(operation, "Description 不能为 null。");
    }

    private static void EnsureType(bool typeIsEmpty, string operation)
    {
        if (typeIsEmpty)
            throw Invalid(operation, "类型不能是未初始化值。");
    }

    private static void EnsureSettlementOptions(SceneSettlementOptions options, string operation)
    {
        if (!IsSettlementOptionsValid(options))
            throw Invalid(operation, "Scene 必须声明至少一种已知结算路径。");
    }

    private static bool IsSettlementOptionsValid(SceneSettlementOptions options) => options != SceneSettlementOptions.None && (options & ~(SceneSettlementOptions.Rules | SceneSettlementOptions.Writing)) == 0;

    private static void EnsureSceneBindingMutable(Scene scene, string operation)
    {
        if (scene.State != SceneState.Binding)
            throw Invalid(operation, $"Scene {scene.Id} 当前处于 {scene.State}，不能修改槽位绑定。");
    }

    private void EnsureElementsNotProcessing(IEnumerable<Guid> elementIds, string operation)
    {
        HashSet<Guid> targets = elementIds.ToHashSet();
        Scene? processingScene = _data.Scenes.Values.FirstOrDefault(scene => scene.State == SceneState.Processing && scene.GetBindings().SelectMany(binding => binding.ElementIds).Any(targets.Contains));
        if (processingScene is not null)
            throw Invalid(operation, $"Scene {processingScene.Id} 正在处理相关 Element，结算前不能修改其局部结构。");
    }

    private static ScenarioException Invalid(string operation, string message) => new(ScenarioErrorCodes.InvalidArgument, TaviErrorCategory.Validation, operation, message);

    private static ScenarioException NotFound(string operation, string entity, Guid id) => new(ScenarioErrorCodes.NotFound, TaviErrorCategory.NotFound, operation, $"未找到 {entity} {id}。");

    private static ScenarioException Duplicate(string operation, string entity, Guid id) => new(ScenarioErrorCodes.Duplicate, TaviErrorCategory.Conflict, operation, $"{entity} {id} 已存在。");

    private static ScenarioException InvalidSnapshot(IEnumerable<string> errors) => new(ScenarioErrorCodes.InvalidSnapshot, TaviErrorCategory.Validation, nameof(Create), $"Scenario 快照无效：{string.Join("; ", errors)}");
}

/// <summary>描述一次内部 Scenario Apply 的状态标识变化和实际操作集。</summary>
internal sealed record ScenarioApplyResult(Guid PreviousStateId, Guid StateId, AppliedScenarioChangeSet? ChangeSet)
{
    /// <summary>获取本次 Apply 是否改变了 Scenario。</summary>
    internal bool Changed => ChangeSet is not null;

    /// <summary>创建未发生变化的 Apply 结果。</summary>
    internal static ScenarioApplyResult Unchanged(Guid stateId) => new(stateId, stateId, null);
}
