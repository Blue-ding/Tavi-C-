namespace Tavi.Domain.World;

/// <summary>表示运行时世界断言图，负责隔离状态、执行原子操作并维护 Element、Aspect、Relation 与 Scope 的结构不变量。</summary>
public sealed class World
{
    private readonly WorldSnapshot _data;

    private World(WorldSnapshot data)
    {
        _data = CloneWorldSnapshot(data);
    }

    /// <summary>完整校验并深复制给定快照后创建运行时 World。</summary>
    /// <param name="data">待加载的独立领域快照。</param>
    /// <returns>不再受输入快照外部修改影响的运行时 World。</returns>
    public static World Create(WorldSnapshot data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ValidateWorldSnapshot(data);
        return new World(data);
    }

    /// <summary>获取当前世界状态标识；每个实际生效的原子操作组提交后都会替换为新的非空标识。</summary>
    public Guid StateId => _data.Id;

    /// <summary>创建当前世界断言图的独立深复制快照。</summary>
    public WorldSnapshot CreateSnapshot() => CloneWorldSnapshot(_data);

    /// <summary>根据标识获取独立的 Element 副本。</summary>
    public Element GetElement(Guid elementId)
    {
        EnsureId(elementId, nameof(GetElement), nameof(elementId));
        return _data.Elements.TryGetValue(elementId, out Element? element) ? CloneElement(element) : throw NotFound(nameof(GetElement), "Element", elementId);
    }

    /// <summary>根据标识获取独立的 Aspect 副本。</summary>
    public Aspect GetAspect(Guid aspectId)
    {
        EnsureId(aspectId, nameof(GetAspect), nameof(aspectId));
        return _data.Aspects.TryGetValue(aspectId, out Aspect? aspect) ? CloneAspect(aspect) : throw NotFound(nameof(GetAspect), "Aspect", aspectId);
    }

    /// <summary>根据标识获取独立的 Relation 副本。</summary>
    public Relation GetRelation(Guid relationId)
    {
        EnsureId(relationId, nameof(GetRelation), nameof(relationId));
        return _data.Relations.TryGetValue(relationId, out Relation? relation) ? CloneRelation(relation) : throw NotFound(nameof(GetRelation), "Relation", relationId);
    }

    /// <summary>根据标识获取独立的 Scope 副本。</summary>
    public Scope GetScope(Guid scopeId)
    {
        EnsureId(scopeId, nameof(GetScope), nameof(scopeId));
        return _data.Scopes.TryGetValue(scopeId, out Scope? scope) ? CloneScope(scope) : throw NotFound(nameof(GetScope), "Scope", scopeId);
    }

    /// <summary>获取全部 Element 的独立副本。</summary>
    public IReadOnlyCollection<Element> GetElements() => _data.Elements.Values.Select(CloneElement).ToArray();

    /// <summary>获取全部 Aspect 的独立副本。</summary>
    public IReadOnlyCollection<Aspect> GetAspects() => _data.Aspects.Values.Select(CloneAspect).ToArray();

    /// <summary>获取全部 Relation 的独立副本。</summary>
    public IReadOnlyCollection<Relation> GetRelations() => _data.Relations.Values.Select(CloneRelation).ToArray();

    /// <summary>获取全部 Scope 的独立副本。</summary>
    public IReadOnlyCollection<Scope> GetScopes() => _data.Scopes.Values.Select(CloneScope).ToArray();

    /// <summary>获取针对指定 Element 的全部 Aspect 副本。</summary>
    public IReadOnlyCollection<Aspect> GetAspectsForElement(Guid elementId)
    {
        GetElementForOperation(elementId, nameof(GetAspectsForElement), "Element");
        return _data.Aspects.Values.Where(aspect => aspect.ElementId == elementId).Select(CloneAspect).ToArray();
    }

    /// <summary>获取指定 Scope 中的全部 Aspect 副本。</summary>
    public IReadOnlyCollection<Aspect> GetAspectsInScope(Guid scopeId)
    {
        GetScopeForOperation(scopeId, nameof(GetAspectsInScope));
        return _data.Aspects.Values.Where(aspect => aspect.ScopeId == scopeId).Select(CloneAspect).ToArray();
    }

    /// <summary>获取与指定 Element 任一方向相连的全部 Relation 副本。</summary>
    public IReadOnlyCollection<Relation> GetRelationsForElement(Guid elementId)
    {
        GetElementForOperation(elementId, nameof(GetRelationsForElement), "Element");
        return _data.Relations.Values.Where(relation => relation.SourceElementId == elementId || relation.TargetElementId == elementId).Select(CloneRelation).ToArray();
    }

    /// <summary>获取以指定 Element 为目标的全部 Relation 副本。</summary>
    public IReadOnlyCollection<Relation> GetIncomingRelations(Guid elementId)
    {
        GetElementForOperation(elementId, nameof(GetIncomingRelations), "Element");
        return _data.Relations.Values.Where(relation => relation.TargetElementId == elementId).Select(CloneRelation).ToArray();
    }

    /// <summary>获取以指定 Element 为来源的全部 Relation 副本。</summary>
    public IReadOnlyCollection<Relation> GetOutgoingRelations(Guid elementId)
    {
        GetElementForOperation(elementId, nameof(GetOutgoingRelations), "Element");
        return _data.Relations.Values.Where(relation => relation.SourceElementId == elementId).Select(CloneRelation).ToArray();
    }

    /// <summary>获取指定 Scope 中的全部 Relation 副本。</summary>
    public IReadOnlyCollection<Relation> GetRelationsInScope(Guid scopeId)
    {
        GetScopeForOperation(scopeId, nameof(GetRelationsInScope));
        return _data.Relations.Values.Where(relation => relation.ScopeId == scopeId).Select(CloneRelation).ToArray();
    }

    /// <summary>获取由指定 Element 持有的全部 Scope 副本。</summary>
    public IReadOnlyCollection<Scope> GetScopesOwnedByElement(Guid elementId)
    {
        GetElementForOperation(elementId, nameof(GetScopesOwnedByElement), "Owner Element");
        return _data.Scopes.Values.Where(scope => scope.OwnerElementId == elementId).Select(CloneScope).ToArray();
    }

    /// <summary>在当前 World 上原地执行操作组；调用方必须持有 WorldSession 写边界，失败时内部回滚且不改变 StateId。</summary>
    internal WorldApplyResult Apply(WorldChangeSet changeSet)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        if (changeSet.IsEmpty)
            return WorldApplyResult.Unchanged(StateId);
        var transaction = new WorldTransaction();
        try
        {
            foreach (WorldOperation operation in changeSet.Operations)
                ApplyOperation(operation, transaction);
        }
        catch (Exception operationException)
        {
            try
            {
                transaction.Rollback();
            }
            catch (Exception rollbackException)
            {
                throw new WorldTransactionException(operationException, rollbackException);
            }
            throw;
        }
        if (!transaction.HasChanges)
            return WorldApplyResult.Unchanged(StateId);
        Guid previousStateId = StateId;
        _data.Id = Guid.NewGuid();
        return new WorldApplyResult(previousStateId, StateId, transaction.CreateAppliedChangeSet());
    }

    private void ApplyOperation(WorldOperation operation, WorldTransaction transaction)
    {
        switch (operation)
        {
            case AddElementOperation value: ApplyAddElement(value, transaction); break;
            case RemoveElementOperation value: ApplyRemoveElement(value, transaction); break;
            case UpdateElementNameOperation value: ApplyUpdateElementName(value, transaction); break;
            case UpdateElementDescriptionOperation value: ApplyUpdateElementDescription(value, transaction); break;
            case UpdateElementTypeOperation value: ApplyUpdateElementType(value, transaction); break;
            case AddAspectOperation value: ApplyAddAspect(value, transaction); break;
            case RemoveAspectOperation value: ApplyRemoveAspect(value, transaction); break;
            case UpdateAspectNameOperation value: ApplyUpdateAspectName(value, transaction); break;
            case UpdateAspectDescriptionOperation value: ApplyUpdateAspectDescription(value, transaction); break;
            case UpdateAspectQuantityOperation value: ApplyUpdateAspectQuantity(value, transaction); break;
            case UpdateAspectTypeOperation value: ApplyUpdateAspectType(value, transaction); break;
            case AddRelationOperation value: ApplyAddRelation(value, transaction); break;
            case RemoveRelationOperation value: ApplyRemoveRelation(value, transaction); break;
            case UpdateRelationNameOperation value: ApplyUpdateRelationName(value, transaction); break;
            case UpdateRelationDescriptionOperation value: ApplyUpdateRelationDescription(value, transaction); break;
            case UpdateRelationQuantityOperation value: ApplyUpdateRelationQuantity(value, transaction); break;
            case UpdateRelationTypeOperation value: ApplyUpdateRelationType(value, transaction); break;
            case AddScopeOperation value: ApplyAddScope(value, transaction); break;
            case RemoveScopeOperation value: ApplyRemoveScope(value, transaction); break;
            case UpdateScopeNameOperation value: ApplyUpdateScopeName(value, transaction); break;
            case UpdateScopeDescriptionOperation value: ApplyUpdateScopeDescription(value, transaction); break;
            case UpdateScopeQuantityOperation value: ApplyUpdateScopeQuantity(value, transaction); break;
            case UpdateScopeTypeOperation value: ApplyUpdateScopeType(value, transaction); break;
            default: throw new WorldException(WorldErrorCodes.InvalidArgument, nameof(Apply), $"不支持的 World 操作类型 {operation.GetType().FullName}。");
        }
    }

    private void ApplyAddElement(AddElementOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(AddElementOperation);
        EnsureId(operation.ElementId, name, nameof(operation.ElementId));
        EnsureName(operation.Name, name, nameof(operation.Name));
        EnsureDescription(operation.Description, name, nameof(operation.Description));
        EnsureType(operation.Type, name, nameof(operation.Type));
        if (_data.Elements.ContainsKey(operation.ElementId))
            throw Duplicate(name, "Element", operation.ElementId);
        var element = new Element(operation.ElementId, operation.Name, operation.Description, operation.Type);
        transaction.RecordRollback(() => _data.Elements.Remove(element.Id));
        _data.Elements.Add(element.Id, element);
        transaction.RecordApplied(operation, [new RemoveElementOperation(element.Id)]);
    }

    private void ApplyRemoveElement(RemoveElementOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(RemoveElementOperation);
        Element element = GetElementForOperation(operation.ElementId, name, "Element");
        Scope[] scopes = _data.Scopes.Values.Where(scope => scope.OwnerElementId == element.Id).ToArray();
        HashSet<Guid> scopeIds = scopes.Select(scope => scope.Id).ToHashSet();
        Aspect[] aspects = _data.Aspects.Values.Where(aspect => aspect.ElementId == element.Id || scopeIds.Contains(aspect.ScopeId)).ToArray();
        Relation[] relations = _data.Relations.Values.Where(relation => relation.SourceElementId == element.Id || relation.TargetElementId == element.Id || scopeIds.Contains(relation.ScopeId)).ToArray();
        transaction.RecordRollback(() =>
        {
            _data.Elements[element.Id] = element;
            foreach (Scope scope in scopes)
                _data.Scopes[scope.Id] = scope;
            foreach (Aspect aspect in aspects)
                _data.Aspects[aspect.Id] = aspect;
            foreach (Relation relation in relations)
                _data.Relations[relation.Id] = relation;
        });
        foreach (Aspect aspect in aspects)
            _data.Aspects.Remove(aspect.Id);
        foreach (Relation relation in relations)
            _data.Relations.Remove(relation.Id);
        foreach (Scope scope in scopes)
            _data.Scopes.Remove(scope.Id);
        _data.Elements.Remove(element.Id);
        var inverse = new List<WorldOperation> { ToAddOperation(element) };
        inverse.AddRange(scopes.Select(ToAddOperation));
        inverse.AddRange(aspects.Select(ToAddOperation));
        inverse.AddRange(relations.Select(ToAddOperation));
        transaction.RecordApplied(operation, inverse);
    }

    private void ApplyUpdateElementName(UpdateElementNameOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(UpdateElementNameOperation);
        EnsureName(operation.Name, name, nameof(operation.Name));
        Element element = GetElementForOperation(operation.ElementId, name, "Element");
        if (string.Equals(element.Name, operation.Name, StringComparison.Ordinal))
            return;
        string previous = element.Name;
        transaction.RecordRollback(() => element.UpdateName(previous));
        element.UpdateName(operation.Name);
        transaction.RecordApplied(operation, [new UpdateElementNameOperation(element.Id, previous)]);
    }

    private void ApplyUpdateElementDescription(UpdateElementDescriptionOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(UpdateElementDescriptionOperation);
        EnsureDescription(operation.Description, name, nameof(operation.Description));
        Element element = GetElementForOperation(operation.ElementId, name, "Element");
        if (string.Equals(element.Description, operation.Description, StringComparison.Ordinal))
            return;
        string previous = element.Description;
        transaction.RecordRollback(() => element.UpdateDescription(previous));
        element.UpdateDescription(operation.Description);
        transaction.RecordApplied(operation, [new UpdateElementDescriptionOperation(element.Id, previous)]);
    }

    private void ApplyUpdateElementType(UpdateElementTypeOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(UpdateElementTypeOperation);
        EnsureType(operation.Type, name, nameof(operation.Type));
        Element element = GetElementForOperation(operation.ElementId, name, "Element");
        if (element.Type == operation.Type)
            return;
        ElementType previous = element.Type;
        transaction.RecordRollback(() => element.UpdateType(previous));
        element.UpdateType(operation.Type);
        transaction.RecordApplied(operation, [new UpdateElementTypeOperation(element.Id, previous)]);
    }

    private void ApplyAddAspect(AddAspectOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(AddAspectOperation);
        EnsureId(operation.AspectId, name, nameof(operation.AspectId));
        EnsureName(operation.Name, name, nameof(operation.Name));
        EnsureDescription(operation.Description, name, nameof(operation.Description));
        EnsureQuantity(operation.Quantity, name, nameof(operation.Quantity));
        EnsureType(operation.Type, name, nameof(operation.Type));
        GetElementForOperation(operation.ElementId, name, "Element");
        GetScopeForOperation(operation.ScopeId, name);
        if (_data.Aspects.ContainsKey(operation.AspectId))
            throw Duplicate(name, "Aspect", operation.AspectId);
        var aspect = new Aspect(operation.AspectId, operation.Name, operation.Description, operation.Quantity, operation.Type, operation.ElementId, operation.ScopeId);
        transaction.RecordRollback(() => _data.Aspects.Remove(aspect.Id));
        _data.Aspects.Add(aspect.Id, aspect);
        transaction.RecordApplied(operation, [new RemoveAspectOperation(aspect.Id)]);
    }

    private void ApplyRemoveAspect(RemoveAspectOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(RemoveAspectOperation);
        Aspect aspect = GetAspectForOperation(operation.AspectId, name);
        transaction.RecordRollback(() => _data.Aspects[aspect.Id] = aspect);
        _data.Aspects.Remove(aspect.Id);
        transaction.RecordApplied(operation, [ToAddOperation(aspect)]);
    }

    private void ApplyUpdateAspectName(UpdateAspectNameOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(UpdateAspectNameOperation);
        EnsureName(operation.Name, name, nameof(operation.Name));
        Aspect aspect = GetAspectForOperation(operation.AspectId, name);
        if (string.Equals(aspect.Name, operation.Name, StringComparison.Ordinal))
            return;
        string previous = aspect.Name;
        transaction.RecordRollback(() => aspect.UpdateName(previous));
        aspect.UpdateName(operation.Name);
        transaction.RecordApplied(operation, [new UpdateAspectNameOperation(aspect.Id, previous)]);
    }

    private void ApplyUpdateAspectDescription(UpdateAspectDescriptionOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(UpdateAspectDescriptionOperation);
        EnsureDescription(operation.Description, name, nameof(operation.Description));
        Aspect aspect = GetAspectForOperation(operation.AspectId, name);
        if (string.Equals(aspect.Description, operation.Description, StringComparison.Ordinal))
            return;
        string previous = aspect.Description;
        transaction.RecordRollback(() => aspect.UpdateDescription(previous));
        aspect.UpdateDescription(operation.Description);
        transaction.RecordApplied(operation, [new UpdateAspectDescriptionOperation(aspect.Id, previous)]);
    }

    private void ApplyUpdateAspectQuantity(UpdateAspectQuantityOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(UpdateAspectQuantityOperation);
        EnsureQuantity(operation.Quantity, name, nameof(operation.Quantity));
        Aspect aspect = GetAspectForOperation(operation.AspectId, name);
        if (aspect.Quantity.Equals(operation.Quantity))
            return;
        double previous = aspect.Quantity;
        transaction.RecordRollback(() => aspect.UpdateQuantity(previous));
        aspect.UpdateQuantity(operation.Quantity);
        transaction.RecordApplied(operation, [new UpdateAspectQuantityOperation(aspect.Id, previous)]);
    }

    private void ApplyUpdateAspectType(UpdateAspectTypeOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(UpdateAspectTypeOperation);
        EnsureType(operation.Type, name, nameof(operation.Type));
        Aspect aspect = GetAspectForOperation(operation.AspectId, name);
        if (aspect.Type == operation.Type)
            return;
        AspectType previous = aspect.Type;
        transaction.RecordRollback(() => aspect.UpdateType(previous));
        aspect.UpdateType(operation.Type);
        transaction.RecordApplied(operation, [new UpdateAspectTypeOperation(aspect.Id, previous)]);
    }

    private void ApplyAddRelation(AddRelationOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(AddRelationOperation);
        EnsureId(operation.RelationId, name, nameof(operation.RelationId));
        EnsureName(operation.Name, name, nameof(operation.Name));
        EnsureDescription(operation.Description, name, nameof(operation.Description));
        EnsureQuantity(operation.Quantity, name, nameof(operation.Quantity));
        EnsureType(operation.Type, name, nameof(operation.Type));
        GetElementForOperation(operation.SourceElementId, name, "Source Element");
        GetElementForOperation(operation.TargetElementId, name, "Target Element");
        GetScopeForOperation(operation.ScopeId, name);
        if (_data.Relations.ContainsKey(operation.RelationId))
            throw Duplicate(name, "Relation", operation.RelationId);
        var relation = new Relation(operation.RelationId, operation.Name, operation.Description, operation.Quantity, operation.Type, operation.SourceElementId, operation.TargetElementId, operation.ScopeId);
        transaction.RecordRollback(() => _data.Relations.Remove(relation.Id));
        _data.Relations.Add(relation.Id, relation);
        transaction.RecordApplied(operation, [new RemoveRelationOperation(relation.Id)]);
    }

    private void ApplyRemoveRelation(RemoveRelationOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(RemoveRelationOperation);
        Relation relation = GetRelationForOperation(operation.RelationId, name);
        transaction.RecordRollback(() => _data.Relations[relation.Id] = relation);
        _data.Relations.Remove(relation.Id);
        transaction.RecordApplied(operation, [ToAddOperation(relation)]);
    }

    private void ApplyUpdateRelationName(UpdateRelationNameOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(UpdateRelationNameOperation);
        EnsureName(operation.Name, name, nameof(operation.Name));
        Relation relation = GetRelationForOperation(operation.RelationId, name);
        if (string.Equals(relation.Name, operation.Name, StringComparison.Ordinal))
            return;
        string previous = relation.Name;
        transaction.RecordRollback(() => relation.UpdateName(previous));
        relation.UpdateName(operation.Name);
        transaction.RecordApplied(operation, [new UpdateRelationNameOperation(relation.Id, previous)]);
    }

    private void ApplyUpdateRelationDescription(UpdateRelationDescriptionOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(UpdateRelationDescriptionOperation);
        EnsureDescription(operation.Description, name, nameof(operation.Description));
        Relation relation = GetRelationForOperation(operation.RelationId, name);
        if (string.Equals(relation.Description, operation.Description, StringComparison.Ordinal))
            return;
        string previous = relation.Description;
        transaction.RecordRollback(() => relation.UpdateDescription(previous));
        relation.UpdateDescription(operation.Description);
        transaction.RecordApplied(operation, [new UpdateRelationDescriptionOperation(relation.Id, previous)]);
    }

    private void ApplyUpdateRelationQuantity(UpdateRelationQuantityOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(UpdateRelationQuantityOperation);
        EnsureQuantity(operation.Quantity, name, nameof(operation.Quantity));
        Relation relation = GetRelationForOperation(operation.RelationId, name);
        if (relation.Quantity.Equals(operation.Quantity))
            return;
        double previous = relation.Quantity;
        transaction.RecordRollback(() => relation.UpdateQuantity(previous));
        relation.UpdateQuantity(operation.Quantity);
        transaction.RecordApplied(operation, [new UpdateRelationQuantityOperation(relation.Id, previous)]);
    }

    private void ApplyUpdateRelationType(UpdateRelationTypeOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(UpdateRelationTypeOperation);
        EnsureType(operation.Type, name, nameof(operation.Type));
        Relation relation = GetRelationForOperation(operation.RelationId, name);
        if (relation.Type == operation.Type)
            return;
        RelationType previous = relation.Type;
        transaction.RecordRollback(() => relation.UpdateType(previous));
        relation.UpdateType(operation.Type);
        transaction.RecordApplied(operation, [new UpdateRelationTypeOperation(relation.Id, previous)]);
    }

    private void ApplyAddScope(AddScopeOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(AddScopeOperation);
        EnsureId(operation.ScopeId, name, nameof(operation.ScopeId));
        EnsureName(operation.Name, name, nameof(operation.Name));
        EnsureDescription(operation.Description, name, nameof(operation.Description));
        EnsureQuantity(operation.Quantity, name, nameof(operation.Quantity));
        EnsureType(operation.Type, name, nameof(operation.Type));
        GetElementForOperation(operation.OwnerElementId, name, "Owner Element");
        if (_data.Scopes.ContainsKey(operation.ScopeId))
            throw Duplicate(name, "Scope", operation.ScopeId);
        var scope = new Scope(operation.ScopeId, operation.Name, operation.Description, operation.Quantity, operation.Type, operation.OwnerElementId);
        transaction.RecordRollback(() => _data.Scopes.Remove(scope.Id));
        _data.Scopes.Add(scope.Id, scope);
        transaction.RecordApplied(operation, [new RemoveScopeOperation(scope.Id)]);
    }

    private void ApplyRemoveScope(RemoveScopeOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(RemoveScopeOperation);
        Scope scope = GetScopeForOperation(operation.ScopeId, name);
        Aspect[] aspects = _data.Aspects.Values.Where(aspect => aspect.ScopeId == scope.Id).ToArray();
        Relation[] relations = _data.Relations.Values.Where(relation => relation.ScopeId == scope.Id).ToArray();
        transaction.RecordRollback(() =>
        {
            _data.Scopes[scope.Id] = scope;
            foreach (Aspect aspect in aspects)
                _data.Aspects[aspect.Id] = aspect;
            foreach (Relation relation in relations)
                _data.Relations[relation.Id] = relation;
        });
        foreach (Aspect aspect in aspects)
            _data.Aspects.Remove(aspect.Id);
        foreach (Relation relation in relations)
            _data.Relations.Remove(relation.Id);
        _data.Scopes.Remove(scope.Id);
        var inverse = new List<WorldOperation> { ToAddOperation(scope) };
        inverse.AddRange(aspects.Select(ToAddOperation));
        inverse.AddRange(relations.Select(ToAddOperation));
        transaction.RecordApplied(operation, inverse);
    }

    private void ApplyUpdateScopeName(UpdateScopeNameOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(UpdateScopeNameOperation);
        EnsureName(operation.Name, name, nameof(operation.Name));
        Scope scope = GetScopeForOperation(operation.ScopeId, name);
        if (string.Equals(scope.Name, operation.Name, StringComparison.Ordinal))
            return;
        string previous = scope.Name;
        transaction.RecordRollback(() => scope.UpdateName(previous));
        scope.UpdateName(operation.Name);
        transaction.RecordApplied(operation, [new UpdateScopeNameOperation(scope.Id, previous)]);
    }

    private void ApplyUpdateScopeDescription(UpdateScopeDescriptionOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(UpdateScopeDescriptionOperation);
        EnsureDescription(operation.Description, name, nameof(operation.Description));
        Scope scope = GetScopeForOperation(operation.ScopeId, name);
        if (string.Equals(scope.Description, operation.Description, StringComparison.Ordinal))
            return;
        string previous = scope.Description;
        transaction.RecordRollback(() => scope.UpdateDescription(previous));
        scope.UpdateDescription(operation.Description);
        transaction.RecordApplied(operation, [new UpdateScopeDescriptionOperation(scope.Id, previous)]);
    }

    private void ApplyUpdateScopeQuantity(UpdateScopeQuantityOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(UpdateScopeQuantityOperation);
        EnsureQuantity(operation.Quantity, name, nameof(operation.Quantity));
        Scope scope = GetScopeForOperation(operation.ScopeId, name);
        if (scope.Quantity.Equals(operation.Quantity))
            return;
        double previous = scope.Quantity;
        transaction.RecordRollback(() => scope.UpdateQuantity(previous));
        scope.UpdateQuantity(operation.Quantity);
        transaction.RecordApplied(operation, [new UpdateScopeQuantityOperation(scope.Id, previous)]);
    }

    private void ApplyUpdateScopeType(UpdateScopeTypeOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(UpdateScopeTypeOperation);
        EnsureType(operation.Type, name, nameof(operation.Type));
        Scope scope = GetScopeForOperation(operation.ScopeId, name);
        if (scope.Type == operation.Type)
            return;
        ScopeType previous = scope.Type;
        transaction.RecordRollback(() => scope.UpdateType(previous));
        scope.UpdateType(operation.Type);
        transaction.RecordApplied(operation, [new UpdateScopeTypeOperation(scope.Id, previous)]);
    }

    private Element GetElementForOperation(Guid id, string operation, string entityName)
    {
        EnsureId(id, operation, nameof(id));
        return _data.Elements.TryGetValue(id, out Element? element) ? element : throw NotFound(operation, entityName, id);
    }

    private Aspect GetAspectForOperation(Guid id, string operation)
    {
        EnsureId(id, operation, nameof(id));
        return _data.Aspects.TryGetValue(id, out Aspect? aspect) ? aspect : throw NotFound(operation, "Aspect", id);
    }

    private Relation GetRelationForOperation(Guid id, string operation)
    {
        EnsureId(id, operation, nameof(id));
        return _data.Relations.TryGetValue(id, out Relation? relation) ? relation : throw NotFound(operation, "Relation", id);
    }

    private Scope GetScopeForOperation(Guid id, string operation)
    {
        EnsureId(id, operation, nameof(id));
        return _data.Scopes.TryGetValue(id, out Scope? scope) ? scope : throw NotFound(operation, "Scope", id);
    }

    private static AddElementOperation ToAddOperation(Element value) => new(value.Id, value.Name, value.Description, value.Type);
    private static AddAspectOperation ToAddOperation(Aspect value) => new(value.Id, value.Name, value.Description, value.Quantity, value.Type, value.ElementId, value.ScopeId);
    private static AddRelationOperation ToAddOperation(Relation value) => new(value.Id, value.Name, value.Description, value.Quantity, value.Type, value.SourceElementId, value.TargetElementId, value.ScopeId);
    private static AddScopeOperation ToAddOperation(Scope value) => new(value.Id, value.Name, value.Description, value.Quantity, value.Type, value.OwnerElementId);

    private static void EnsureId(Guid id, string operation, string parameterName)
    {
        if (id == Guid.Empty)
            throw new WorldException(WorldErrorCodes.InvalidArgument, operation, $"参数 {parameterName} 不能是空 Guid。");
    }

    private static void EnsureName(string value, string operation, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new WorldException(WorldErrorCodes.InvalidArgument, operation, $"参数 {parameterName} 不能为空或只包含空白字符。");
    }

    private static void EnsureDescription(string value, string operation, string parameterName)
    {
        if (value is null)
            throw new WorldException(WorldErrorCodes.InvalidArgument, operation, $"参数 {parameterName} 不能为 null。");
    }

    private static void EnsureQuantity(double value, string operation, string parameterName)
    {
        if (!double.IsFinite(value))
            throw new WorldException(WorldErrorCodes.InvalidArgument, operation, $"参数 {parameterName} 必须是有限 double。");
    }

    private static void EnsureType(ElementType value, string operation, string parameterName)
    {
        if (value.IsEmpty)
            throw new WorldException(WorldErrorCodes.InvalidArgument, operation, $"参数 {parameterName} 不能是未初始化的 ElementType。");
    }

    private static void EnsureType(AspectType value, string operation, string parameterName)
    {
        if (value.IsEmpty)
            throw new WorldException(WorldErrorCodes.InvalidArgument, operation, $"参数 {parameterName} 不能是未初始化的 AspectType。");
    }

    private static void EnsureType(RelationType value, string operation, string parameterName)
    {
        if (value.IsEmpty)
            throw new WorldException(WorldErrorCodes.InvalidArgument, operation, $"参数 {parameterName} 不能是未初始化的 RelationType。");
    }

    private static void EnsureType(ScopeType value, string operation, string parameterName)
    {
        if (value.IsEmpty)
            throw new WorldException(WorldErrorCodes.InvalidArgument, operation, $"参数 {parameterName} 不能是未初始化的 ScopeType。");
    }

    private static WorldException NotFound(string operation, string entityName, Guid entityId) => new(WorldErrorCodes.NotFound, operation, $"未找到 {entityName}。", entityId);
    private static WorldException Duplicate(string operation, string entityName, Guid entityId) => new(WorldErrorCodes.Duplicate, operation, $"{entityName} {entityId} 已存在。", entityId);

    private static void ValidateWorldSnapshot(WorldSnapshot data)
    {
        const string operation = nameof(Create);
        var errors = new List<string>();
        if (data.Id == Guid.Empty)
            errors.Add("WorldSnapshot.Id 不能是空 Guid。");
        Dictionary<Guid, Element>? elements = data.Elements;
        Dictionary<Guid, Aspect>? aspects = data.Aspects;
        Dictionary<Guid, Relation>? relations = data.Relations;
        Dictionary<Guid, Scope>? scopes = data.Scopes;
        if (elements is null)
            errors.Add("WorldSnapshot.Elements 不能为 null。");
        if (aspects is null)
            errors.Add("WorldSnapshot.Aspects 不能为 null。");
        if (relations is null)
            errors.Add("WorldSnapshot.Relations 不能为 null。");
        if (scopes is null)
            errors.Add("WorldSnapshot.Scopes 不能为 null。");
        elements ??= [];
        aspects ??= [];
        relations ??= [];
        scopes ??= [];
        foreach ((Guid key, Element? element) in elements)
            ValidateElement(key, element, $"Elements[{key}]", errors);
        foreach ((Guid key, Scope? scope) in scopes)
            ValidateScope(key, scope, $"Scopes[{key}]", elements, errors);
        foreach ((Guid key, Aspect? aspect) in aspects)
            ValidateAspect(key, aspect, $"Aspects[{key}]", elements, scopes, errors);
        foreach ((Guid key, Relation? relation) in relations)
            ValidateRelation(key, relation, $"Relations[{key}]", elements, scopes, errors);
        if (errors.Count > 0)
            throw new WorldException(WorldErrorCodes.InvalidWorldSnapshot, operation, $"WorldSnapshot 初始化校验发现 {errors.Count} 个错误。", validationErrors: errors);
    }

    private static void ValidateElement(Guid key, Element? element, string path, List<string> errors)
    {
        if (element is null)
        {
            errors.Add($"{path} 不能为 null。");
            return;
        }
        ValidateIdentity(key, element.Id, path, "Element", errors);
        ValidateCommon(element.Name, element.Description, element.Type.IsEmpty, path, "ElementType", errors);
    }

    private static void ValidateScope(Guid key, Scope? scope, string path, IReadOnlyDictionary<Guid, Element> elements, List<string> errors)
    {
        if (scope is null)
        {
            errors.Add($"{path} 不能为 null。");
            return;
        }
        ValidateIdentity(key, scope.Id, path, "Scope", errors);
        ValidateCommon(scope.Name, scope.Description, scope.Type.IsEmpty, path, "ScopeType", errors);
        ValidateQuantity(scope.Quantity, path, errors);
        ValidateReference(scope.OwnerElementId, elements, $"{path}.OwnerElementId", "Element", errors);
    }

    private static void ValidateAspect(Guid key, Aspect? aspect, string path, IReadOnlyDictionary<Guid, Element> elements, IReadOnlyDictionary<Guid, Scope> scopes, List<string> errors)
    {
        if (aspect is null)
        {
            errors.Add($"{path} 不能为 null。");
            return;
        }
        ValidateIdentity(key, aspect.Id, path, "Aspect", errors);
        ValidateCommon(aspect.Name, aspect.Description, aspect.Type.IsEmpty, path, "AspectType", errors);
        ValidateQuantity(aspect.Quantity, path, errors);
        ValidateReference(aspect.ElementId, elements, $"{path}.ElementId", "Element", errors);
        ValidateReference(aspect.ScopeId, scopes, $"{path}.ScopeId", "Scope", errors);
    }

    private static void ValidateRelation(Guid key, Relation? relation, string path, IReadOnlyDictionary<Guid, Element> elements, IReadOnlyDictionary<Guid, Scope> scopes, List<string> errors)
    {
        if (relation is null)
        {
            errors.Add($"{path} 不能为 null。");
            return;
        }
        ValidateIdentity(key, relation.Id, path, "Relation", errors);
        ValidateCommon(relation.Name, relation.Description, relation.Type.IsEmpty, path, "RelationType", errors);
        ValidateQuantity(relation.Quantity, path, errors);
        ValidateReference(relation.SourceElementId, elements, $"{path}.SourceElementId", "Element", errors);
        ValidateReference(relation.TargetElementId, elements, $"{path}.TargetElementId", "Element", errors);
        ValidateReference(relation.ScopeId, scopes, $"{path}.ScopeId", "Scope", errors);
    }

    private static void ValidateIdentity(Guid key, Guid id, string path, string entityName, List<string> errors)
    {
        if (key == Guid.Empty)
            errors.Add($"{path} 的字典键不能是空 Guid。");
        if (id == Guid.Empty)
            errors.Add($"{path}.Id 不能是空 Guid。");
        if (key != id)
            errors.Add($"{path} 的字典键与 {entityName}.Id {id} 不一致。");
    }

    private static void ValidateCommon(string name, string description, bool typeIsEmpty, string path, string typeName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(name))
            errors.Add($"{path}.Name 不能为空或只包含空白字符。");
        if (description is null)
            errors.Add($"{path}.Description 不能为 null。");
        if (typeIsEmpty)
            errors.Add($"{path}.Type 不能是未初始化的 {typeName}。");
    }

    private static void ValidateQuantity(double quantity, string path, List<string> errors)
    {
        if (!double.IsFinite(quantity))
            errors.Add($"{path}.Quantity 必须是有限 double。");
    }

    private static void ValidateReference<T>(Guid id, IReadOnlyDictionary<Guid, T> values, string path, string entityName, List<string> errors)
    {
        if (id == Guid.Empty)
            errors.Add($"{path} 不能是空 Guid。");
        else if (!values.ContainsKey(id))
            errors.Add($"{path} {id} 不指向任何 {entityName}。");
    }

    private static WorldSnapshot CloneWorldSnapshot(WorldSnapshot source) => new()
    {
        Id = source.Id,
        Elements = source.Elements.ToDictionary(pair => pair.Key, pair => CloneElement(pair.Value)),
        Aspects = source.Aspects.ToDictionary(pair => pair.Key, pair => CloneAspect(pair.Value)),
        Relations = source.Relations.ToDictionary(pair => pair.Key, pair => CloneRelation(pair.Value)),
        Scopes = source.Scopes.ToDictionary(pair => pair.Key, pair => CloneScope(pair.Value))
    };

    private static Element CloneElement(Element source) => new(source.Id, source.Name, source.Description, source.Type);
    private static Aspect CloneAspect(Aspect source) => new(source.Id, source.Name, source.Description, source.Quantity, source.Type, source.ElementId, source.ScopeId);
    private static Relation CloneRelation(Relation source) => new(source.Id, source.Name, source.Description, source.Quantity, source.Type, source.SourceElementId, source.TargetElementId, source.ScopeId);
    private static Scope CloneScope(Scope source) => new(source.Id, source.Name, source.Description, source.Quantity, source.Type, source.OwnerElementId);

    /// <summary>记录精确内部回滚动作和成功提交后可公开执行的正反操作序列。</summary>
    private sealed class WorldTransaction
    {
        private readonly Stack<Action> _rollback = new();
        private readonly List<WorldOperation> _forward = [];
        private readonly List<WorldOperation> _inverse = [];

        internal bool HasChanges => _forward.Count > 0;

        internal void RecordRollback(Action rollback) => _rollback.Push(rollback);

        internal void RecordApplied(WorldOperation forward, IEnumerable<WorldOperation> inverse)
        {
            _forward.Add(forward);
            _inverse.InsertRange(0, inverse);
        }

        internal void Rollback()
        {
            List<Exception>? failures = null;
            while (_rollback.TryPop(out Action? rollback))
            {
                try
                {
                    rollback();
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
            }
            if (failures is not null)
                throw new AggregateException("世界事务回滚失败。", failures);
        }

        internal AppliedWorldChangeSet CreateAppliedChangeSet() => new(new WorldChangeSet(_forward), new WorldChangeSet(_inverse));
    }
}

/// <summary>描述一次内部 World Apply 的状态标识变化和实际操作集。</summary>
internal sealed record WorldApplyResult(Guid PreviousStateId, Guid StateId, AppliedWorldChangeSet? ChangeSet)
{
    /// <summary>获取本次 Apply 是否产生实际领域写入。</summary>
    internal bool Changed => ChangeSet is not null;

    /// <summary>创建未发生实际写入的结果。</summary>
    internal static WorldApplyResult Unchanged(Guid stateId) => new(stateId, stateId, null);
}
