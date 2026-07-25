namespace Tavi.Domain.World;

/// <summary>表示运行时世界事实图，负责隔离状态、执行原子操作并维护 EARS 与 Local 事实的结构不变量。</summary>
public sealed class World
{
    private readonly WorldSnapshot _data;

    private World(WorldSnapshot data) => _data = CloneWorldSnapshot(data);

    /// <summary>完整校验并深复制给定快照后创建运行时 World。</summary>
    /// <param name="data">待加载的独立领域快照。</param><returns>运行时 World。</returns>
    public static World Create(WorldSnapshot data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ValidateWorldSnapshot(data);
        return new World(data);
    }

    /// <summary>获取当前世界状态标识。</summary>
    public Guid StateId => _data.Id;

    /// <summary>创建当前 World 的独立深复制快照。</summary>
    public WorldSnapshot CreateSnapshot() => CloneWorldSnapshot(_data);

    /// <summary>根据标识获取 Element 副本。</summary>
    public Element GetElement(Guid elementId) => CloneElement(GetElementForOperation(elementId, nameof(GetElement), "Element"));

    /// <summary>根据标识获取 Aspect 副本。</summary>
    public Aspect GetAspect(Guid aspectId) => CloneAspect(GetAspectForOperation(aspectId, nameof(GetAspect)));

    /// <summary>根据标识获取 Relation 副本。</summary>
    public Relation GetRelation(Guid relationId) => CloneRelation(GetRelationForOperation(relationId, nameof(GetRelation)));

    /// <summary>根据标识获取 Scope 副本。</summary>
    public Scope GetScope(Guid scopeId) => CloneScope(GetScopeForOperation(scopeId, nameof(GetScope)));

    /// <summary>根据标识获取 LocalAspect 副本。</summary>
    public LocalAspect GetLocalAspect(Guid localAspectId) => CloneLocalAspect(GetLocalAspectForOperation(localAspectId, nameof(GetLocalAspect)));

    /// <summary>根据标识获取 LocalRelation 副本。</summary>
    public LocalRelation GetLocalRelation(Guid localRelationId) => CloneLocalRelation(GetLocalRelationForOperation(localRelationId, nameof(GetLocalRelation)));

    /// <summary>获取全部 Element 副本。</summary>
    public IReadOnlyCollection<Element> GetElements() => _data.Elements.Values.Select(CloneElement).ToArray();

    /// <summary>获取全部 Aspect 副本。</summary>
    public IReadOnlyCollection<Aspect> GetAspects() => _data.Aspects.Values.Select(CloneAspect).ToArray();

    /// <summary>获取全部 Relation 副本。</summary>
    public IReadOnlyCollection<Relation> GetRelations() => _data.Relations.Values.Select(CloneRelation).ToArray();

    /// <summary>获取全部 Scope 副本。</summary>
    public IReadOnlyCollection<Scope> GetScopes() => _data.Scopes.Values.Select(CloneScope).ToArray();

    /// <summary>获取全部 LocalAspect 副本。</summary>
    public IReadOnlyCollection<LocalAspect> GetLocalAspects() => _data.LocalAspects.Values.Select(CloneLocalAspect).ToArray();

    /// <summary>获取全部 LocalRelation 副本。</summary>
    public IReadOnlyCollection<LocalRelation> GetLocalRelations() => _data.LocalRelations.Values.Select(CloneLocalRelation).ToArray();

    /// <summary>获取针对指定 Element 的全部 Aspect 副本。</summary>
    public IReadOnlyCollection<Aspect> GetAspectsForElement(Guid elementId)
    {
        GetElementForOperation(elementId, nameof(GetAspectsForElement), "Element");
        return _data.Aspects.Values.Where(value => value.ElementId == elementId).Select(CloneAspect).ToArray();
    }

    /// <summary>获取指定 Scope 中的全部 Aspect 副本。</summary>
    public IReadOnlyCollection<Aspect> GetAspectsInScope(Guid scopeId)
    {
        GetScopeForOperation(scopeId, nameof(GetAspectsInScope));
        return _data.Aspects.Values.Where(value => value.ScopeId == scopeId).Select(CloneAspect).ToArray();
    }

    /// <summary>获取与指定 Element 相连的全部 Relation 副本。</summary>
    public IReadOnlyCollection<Relation> GetRelationsForElement(Guid elementId)
    {
        GetElementForOperation(elementId, nameof(GetRelationsForElement), "Element");
        return _data.Relations.Values.Where(value => value.SourceElementId == elementId || value.TargetElementId == elementId).Select(CloneRelation).ToArray();
    }

    /// <summary>获取以指定 Element 为目标的全部 Relation 副本。</summary>
    public IReadOnlyCollection<Relation> GetIncomingRelations(Guid elementId)
    {
        GetElementForOperation(elementId, nameof(GetIncomingRelations), "Element");
        return _data.Relations.Values.Where(value => value.TargetElementId == elementId).Select(CloneRelation).ToArray();
    }

    /// <summary>获取以指定 Element 为来源的全部 Relation 副本。</summary>
    public IReadOnlyCollection<Relation> GetOutgoingRelations(Guid elementId)
    {
        GetElementForOperation(elementId, nameof(GetOutgoingRelations), "Element");
        return _data.Relations.Values.Where(value => value.SourceElementId == elementId).Select(CloneRelation).ToArray();
    }

    /// <summary>获取指定 Scope 中的全部 Relation 副本。</summary>
    public IReadOnlyCollection<Relation> GetRelationsInScope(Guid scopeId)
    {
        GetScopeForOperation(scopeId, nameof(GetRelationsInScope));
        return _data.Relations.Values.Where(value => value.ScopeId == scopeId).Select(CloneRelation).ToArray();
    }

    /// <summary>获取由指定 Element 持有的全部 Scope 副本。</summary>
    public IReadOnlyCollection<Scope> GetScopesOwnedByElement(Guid elementId)
    {
        GetElementForOperation(elementId, nameof(GetScopesOwnedByElement), "Owner Element");
        return _data.Scopes.Values.Where(value => value.OwnerElementId == elementId).Select(CloneScope).ToArray();
    }

    /// <summary>获取针对指定 Element 的全部 LocalAspect 副本。</summary>
    public IReadOnlyCollection<LocalAspect> GetLocalAspectsForElement(Guid elementId)
    {
        GetElementForOperation(elementId, nameof(GetLocalAspectsForElement), "Element");
        return _data.LocalAspects.Values.Where(value => value.ElementId == elementId).Select(CloneLocalAspect).ToArray();
    }

    /// <summary>获取指定 Scope 中的全部 LocalAspect 副本。</summary>
    public IReadOnlyCollection<LocalAspect> GetLocalAspectsInScope(Guid scopeId)
    {
        GetScopeForOperation(scopeId, nameof(GetLocalAspectsInScope));
        return _data.LocalAspects.Values.Where(value => value.ScopeId == scopeId).Select(CloneLocalAspect).ToArray();
    }

    /// <summary>获取与指定 Element 相连的全部 LocalRelation 副本。</summary>
    public IReadOnlyCollection<LocalRelation> GetLocalRelationsForElement(Guid elementId)
    {
        GetElementForOperation(elementId, nameof(GetLocalRelationsForElement), "Element");
        return _data.LocalRelations.Values.Where(value => value.SourceElementId == elementId || value.TargetElementId == elementId).Select(CloneLocalRelation).ToArray();
    }

    /// <summary>获取指定 Scope 中的全部 LocalRelation 副本。</summary>
    public IReadOnlyCollection<LocalRelation> GetLocalRelationsInScope(Guid scopeId)
    {
        GetScopeForOperation(scopeId, nameof(GetLocalRelationsInScope));
        return _data.LocalRelations.Values.Where(value => value.ScopeId == scopeId).Select(CloneLocalRelation).ToArray();
    }

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
            case AddScopeOperation value: ApplyAddScope(value, transaction); break;
            case RemoveScopeOperation value: ApplyRemoveScope(value, transaction); break;
            case UpdateScopeQuantityOperation value: ApplyUpdateScopeQuantity(value, transaction); break;
            case UpdateScopeTypeOperation value: ApplyUpdateScopeType(value, transaction); break;
            case AddAspectOperation value: ApplyAddAspect(value, transaction); break;
            case RemoveAspectOperation value: ApplyRemoveAspect(value, transaction); break;
            case UpdateAspectQuantityOperation value: ApplyUpdateAspectQuantity(value, transaction); break;
            case UpdateAspectTypeOperation value: ApplyUpdateAspectType(value, transaction); break;
            case AddRelationOperation value: ApplyAddRelation(value, transaction); break;
            case RemoveRelationOperation value: ApplyRemoveRelation(value, transaction); break;
            case UpdateRelationQuantityOperation value: ApplyUpdateRelationQuantity(value, transaction); break;
            case UpdateRelationTypeOperation value: ApplyUpdateRelationType(value, transaction); break;
            case AddLocalAspectOperation value: ApplyAddLocalAspect(value, transaction); break;
            case RemoveLocalAspectOperation value: ApplyRemoveLocalAspect(value, transaction); break;
            case UpdateLocalAspectOperation value: ApplyUpdateLocalAspect(value, transaction); break;
            case AddLocalRelationOperation value: ApplyAddLocalRelation(value, transaction); break;
            case RemoveLocalRelationOperation value: ApplyRemoveLocalRelation(value, transaction); break;
            case UpdateLocalRelationOperation value: ApplyUpdateLocalRelation(value, transaction); break;
            default: throw Invalid(nameof(Apply), $"不支持的 World 操作类型 {operation.GetType().FullName}。");
        }
    }

    private void ApplyAddElement(AddElementOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(AddElementOperation);
        EnsureId(operation.ElementId, name, nameof(operation.ElementId));
        EnsureText(operation.Name, name, nameof(operation.Name));
        EnsureDescription(operation.Description, name, nameof(operation.Description));
        EnsureType(operation.Type, name, nameof(operation.Type));
        if (_data.Elements.ContainsKey(operation.ElementId))
            throw Duplicate(name, "Element", operation.ElementId);
        var value = new Element(operation.ElementId, operation.Name, operation.Description, operation.Type);
        transaction.RecordRollback(() => _data.Elements.Remove(value.Id));
        _data.Elements.Add(value.Id, value);
        transaction.RecordApplied(operation, [new RemoveElementOperation(value.Id)]);
    }

    private void ApplyRemoveElement(RemoveElementOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(RemoveElementOperation);
        Element element = GetElementForOperation(operation.ElementId, name, "Element");
        Scope[] scopes = _data.Scopes.Values.Where(value => value.OwnerElementId == element.Id).ToArray();
        HashSet<Guid> scopeIds = scopes.Select(value => value.Id).ToHashSet();
        Aspect[] aspects = _data.Aspects.Values.Where(value => value.ElementId == element.Id || scopeIds.Contains(value.ScopeId)).ToArray();
        Relation[] relations = _data.Relations.Values.Where(value => value.SourceElementId == element.Id || value.TargetElementId == element.Id || scopeIds.Contains(value.ScopeId)).ToArray();
        LocalAspect[] localAspects = _data.LocalAspects.Values.Where(value => value.ElementId == element.Id || scopeIds.Contains(value.ScopeId)).ToArray();
        LocalRelation[] localRelations = _data.LocalRelations.Values.Where(value => value.SourceElementId == element.Id || value.TargetElementId == element.Id || scopeIds.Contains(value.ScopeId)).ToArray();
        transaction.RecordRollback(() =>
        {
            _data.Elements[element.Id] = element;
            foreach (Scope value in scopes) _data.Scopes[value.Id] = value;
            foreach (Aspect value in aspects) _data.Aspects[value.Id] = value;
            foreach (Relation value in relations) _data.Relations[value.Id] = value;
            foreach (LocalAspect value in localAspects) _data.LocalAspects[value.Id] = value;
            foreach (LocalRelation value in localRelations) _data.LocalRelations[value.Id] = value;
        });
        foreach (Aspect value in aspects) _data.Aspects.Remove(value.Id);
        foreach (Relation value in relations) _data.Relations.Remove(value.Id);
        foreach (LocalAspect value in localAspects) _data.LocalAspects.Remove(value.Id);
        foreach (LocalRelation value in localRelations) _data.LocalRelations.Remove(value.Id);
        foreach (Scope value in scopes) _data.Scopes.Remove(value.Id);
        _data.Elements.Remove(element.Id);
        var inverse = new List<WorldOperation> { ToAddOperation(element) };
        inverse.AddRange(scopes.Select(ToAddOperation));
        inverse.AddRange(aspects.Select(ToAddOperation));
        inverse.AddRange(relations.Select(ToAddOperation));
        inverse.AddRange(localAspects.Select(ToAddOperation));
        inverse.AddRange(localRelations.Select(ToAddOperation));
        transaction.RecordApplied(operation, inverse);
    }

    private void ApplyUpdateElementName(UpdateElementNameOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(UpdateElementNameOperation);
        EnsureText(operation.Name, name, nameof(operation.Name));
        Element value = GetElementForOperation(operation.ElementId, name, "Element");
        if (value.Name == operation.Name)
            return;
        string previous = value.Name;
        transaction.RecordRollback(() => value.UpdateName(previous));
        value.UpdateName(operation.Name);
        transaction.RecordApplied(operation, [new UpdateElementNameOperation(value.Id, previous)]);
    }

    private void ApplyUpdateElementDescription(UpdateElementDescriptionOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(UpdateElementDescriptionOperation);
        EnsureDescription(operation.Description, name, nameof(operation.Description));
        Element value = GetElementForOperation(operation.ElementId, name, "Element");
        if (value.Description == operation.Description)
            return;
        string previous = value.Description;
        transaction.RecordRollback(() => value.UpdateDescription(previous));
        value.UpdateDescription(operation.Description);
        transaction.RecordApplied(operation, [new UpdateElementDescriptionOperation(value.Id, previous)]);
    }

    private void ApplyUpdateElementType(UpdateElementTypeOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(UpdateElementTypeOperation);
        EnsureType(operation.Type, name, nameof(operation.Type));
        Element value = GetElementForOperation(operation.ElementId, name, "Element");
        if (value.Type == operation.Type)
            return;
        ElementType previous = value.Type;
        transaction.RecordRollback(() => value.UpdateType(previous));
        value.UpdateType(operation.Type);
        transaction.RecordApplied(operation, [new UpdateElementTypeOperation(value.Id, previous)]);
    }

    private void ApplyAddScope(AddScopeOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(AddScopeOperation);
        EnsureId(operation.ScopeId, name, nameof(operation.ScopeId));
        EnsureType(operation.Type, name, nameof(operation.Type));
        GetElementForOperation(operation.OwnerElementId, name, "Owner Element");
        if (_data.Scopes.ContainsKey(operation.ScopeId))
            throw Duplicate(name, "Scope", operation.ScopeId);
        var value = new Scope(operation.ScopeId, operation.Quantity, operation.Type, operation.OwnerElementId);
        transaction.RecordRollback(() => _data.Scopes.Remove(value.Id));
        _data.Scopes.Add(value.Id, value);
        transaction.RecordApplied(operation, [new RemoveScopeOperation(value.Id)]);
    }

    private void ApplyRemoveScope(RemoveScopeOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(RemoveScopeOperation);
        Scope scope = GetScopeForOperation(operation.ScopeId, name);
        Aspect[] aspects = _data.Aspects.Values.Where(value => value.ScopeId == scope.Id).ToArray();
        Relation[] relations = _data.Relations.Values.Where(value => value.ScopeId == scope.Id).ToArray();
        LocalAspect[] localAspects = _data.LocalAspects.Values.Where(value => value.ScopeId == scope.Id).ToArray();
        LocalRelation[] localRelations = _data.LocalRelations.Values.Where(value => value.ScopeId == scope.Id).ToArray();
        transaction.RecordRollback(() =>
        {
            _data.Scopes[scope.Id] = scope;
            foreach (Aspect value in aspects) _data.Aspects[value.Id] = value;
            foreach (Relation value in relations) _data.Relations[value.Id] = value;
            foreach (LocalAspect value in localAspects) _data.LocalAspects[value.Id] = value;
            foreach (LocalRelation value in localRelations) _data.LocalRelations[value.Id] = value;
        });
        foreach (Aspect value in aspects) _data.Aspects.Remove(value.Id);
        foreach (Relation value in relations) _data.Relations.Remove(value.Id);
        foreach (LocalAspect value in localAspects) _data.LocalAspects.Remove(value.Id);
        foreach (LocalRelation value in localRelations) _data.LocalRelations.Remove(value.Id);
        _data.Scopes.Remove(scope.Id);
        var inverse = new List<WorldOperation> { ToAddOperation(scope) };
        inverse.AddRange(aspects.Select(ToAddOperation));
        inverse.AddRange(relations.Select(ToAddOperation));
        inverse.AddRange(localAspects.Select(ToAddOperation));
        inverse.AddRange(localRelations.Select(ToAddOperation));
        transaction.RecordApplied(operation, inverse);
    }

    private void ApplyUpdateScopeQuantity(UpdateScopeQuantityOperation operation, WorldTransaction transaction)
    {
        Scope value = GetScopeForOperation(operation.ScopeId, nameof(UpdateScopeQuantityOperation));
        if (value.Quantity == operation.Quantity)
            return;
        int previous = value.Quantity;
        transaction.RecordRollback(() => value.UpdateQuantity(previous));
        value.UpdateQuantity(operation.Quantity);
        transaction.RecordApplied(operation, [new UpdateScopeQuantityOperation(value.Id, previous)]);
    }

    private void ApplyUpdateScopeType(UpdateScopeTypeOperation operation, WorldTransaction transaction)
    {
        EnsureType(operation.Type, nameof(UpdateScopeTypeOperation), nameof(operation.Type));
        Scope value = GetScopeForOperation(operation.ScopeId, nameof(UpdateScopeTypeOperation));
        if (value.Type == operation.Type)
            return;
        ScopeType previous = value.Type;
        transaction.RecordRollback(() => value.UpdateType(previous));
        value.UpdateType(operation.Type);
        transaction.RecordApplied(operation, [new UpdateScopeTypeOperation(value.Id, previous)]);
    }

    private void ApplyAddAspect(AddAspectOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(AddAspectOperation);
        EnsureId(operation.AspectId, name, nameof(operation.AspectId));
        EnsureType(operation.Type, name, nameof(operation.Type));
        GetElementForOperation(operation.ElementId, name, "Element");
        GetScopeForOperation(operation.ScopeId, name);
        if (_data.Aspects.ContainsKey(operation.AspectId))
            throw Duplicate(name, "Aspect", operation.AspectId);
        var value = new Aspect(operation.AspectId, operation.Quantity, operation.Type, operation.ElementId, operation.ScopeId);
        transaction.RecordRollback(() => _data.Aspects.Remove(value.Id));
        _data.Aspects.Add(value.Id, value);
        transaction.RecordApplied(operation, [new RemoveAspectOperation(value.Id)]);
    }

    private void ApplyRemoveAspect(RemoveAspectOperation operation, WorldTransaction transaction)
    {
        Aspect value = GetAspectForOperation(operation.AspectId, nameof(RemoveAspectOperation));
        transaction.RecordRollback(() => _data.Aspects[value.Id] = value);
        _data.Aspects.Remove(value.Id);
        transaction.RecordApplied(operation, [ToAddOperation(value)]);
    }

    private void ApplyUpdateAspectQuantity(UpdateAspectQuantityOperation operation, WorldTransaction transaction)
    {
        Aspect value = GetAspectForOperation(operation.AspectId, nameof(UpdateAspectQuantityOperation));
        if (value.Quantity == operation.Quantity)
            return;
        int previous = value.Quantity;
        transaction.RecordRollback(() => value.UpdateQuantity(previous));
        value.UpdateQuantity(operation.Quantity);
        transaction.RecordApplied(operation, [new UpdateAspectQuantityOperation(value.Id, previous)]);
    }

    private void ApplyUpdateAspectType(UpdateAspectTypeOperation operation, WorldTransaction transaction)
    {
        EnsureType(operation.Type, nameof(UpdateAspectTypeOperation), nameof(operation.Type));
        Aspect value = GetAspectForOperation(operation.AspectId, nameof(UpdateAspectTypeOperation));
        if (value.Type == operation.Type)
            return;
        AspectType previous = value.Type;
        transaction.RecordRollback(() => value.UpdateType(previous));
        value.UpdateType(operation.Type);
        transaction.RecordApplied(operation, [new UpdateAspectTypeOperation(value.Id, previous)]);
    }

    private void ApplyAddRelation(AddRelationOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(AddRelationOperation);
        EnsureId(operation.RelationId, name, nameof(operation.RelationId));
        EnsureType(operation.Type, name, nameof(operation.Type));
        GetElementForOperation(operation.SourceElementId, name, "Source Element");
        GetElementForOperation(operation.TargetElementId, name, "Target Element");
        GetScopeForOperation(operation.ScopeId, name);
        if (_data.Relations.ContainsKey(operation.RelationId))
            throw Duplicate(name, "Relation", operation.RelationId);
        var value = new Relation(operation.RelationId, operation.Quantity, operation.Type, operation.SourceElementId, operation.TargetElementId, operation.ScopeId);
        transaction.RecordRollback(() => _data.Relations.Remove(value.Id));
        _data.Relations.Add(value.Id, value);
        transaction.RecordApplied(operation, [new RemoveRelationOperation(value.Id)]);
    }

    private void ApplyRemoveRelation(RemoveRelationOperation operation, WorldTransaction transaction)
    {
        Relation value = GetRelationForOperation(operation.RelationId, nameof(RemoveRelationOperation));
        transaction.RecordRollback(() => _data.Relations[value.Id] = value);
        _data.Relations.Remove(value.Id);
        transaction.RecordApplied(operation, [ToAddOperation(value)]);
    }

    private void ApplyUpdateRelationQuantity(UpdateRelationQuantityOperation operation, WorldTransaction transaction)
    {
        Relation value = GetRelationForOperation(operation.RelationId, nameof(UpdateRelationQuantityOperation));
        if (value.Quantity == operation.Quantity)
            return;
        int previous = value.Quantity;
        transaction.RecordRollback(() => value.UpdateQuantity(previous));
        value.UpdateQuantity(operation.Quantity);
        transaction.RecordApplied(operation, [new UpdateRelationQuantityOperation(value.Id, previous)]);
    }

    private void ApplyUpdateRelationType(UpdateRelationTypeOperation operation, WorldTransaction transaction)
    {
        EnsureType(operation.Type, nameof(UpdateRelationTypeOperation), nameof(operation.Type));
        Relation value = GetRelationForOperation(operation.RelationId, nameof(UpdateRelationTypeOperation));
        if (value.Type == operation.Type)
            return;
        RelationType previous = value.Type;
        transaction.RecordRollback(() => value.UpdateType(previous));
        value.UpdateType(operation.Type);
        transaction.RecordApplied(operation, [new UpdateRelationTypeOperation(value.Id, previous)]);
    }

    private void ApplyAddLocalAspect(AddLocalAspectOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(AddLocalAspectOperation);
        EnsureId(operation.LocalAspectId, name, nameof(operation.LocalAspectId));
        EnsureText(operation.Name, name, nameof(operation.Name));
        EnsureDescription(operation.Description, name, nameof(operation.Description));
        GetElementForOperation(operation.ElementId, name, "Element");
        GetScopeForOperation(operation.ScopeId, name);
        if (_data.LocalAspects.ContainsKey(operation.LocalAspectId))
            throw Duplicate(name, "LocalAspect", operation.LocalAspectId);
        var value = new LocalAspect(operation.LocalAspectId, operation.Name, operation.Description, operation.Quantity, operation.ElementId, operation.ScopeId);
        transaction.RecordRollback(() => _data.LocalAspects.Remove(value.Id));
        _data.LocalAspects.Add(value.Id, value);
        transaction.RecordApplied(operation, [new RemoveLocalAspectOperation(value.Id)]);
    }

    private void ApplyRemoveLocalAspect(RemoveLocalAspectOperation operation, WorldTransaction transaction)
    {
        LocalAspect value = GetLocalAspectForOperation(operation.LocalAspectId, nameof(RemoveLocalAspectOperation));
        transaction.RecordRollback(() => _data.LocalAspects[value.Id] = value);
        _data.LocalAspects.Remove(value.Id);
        transaction.RecordApplied(operation, [ToAddOperation(value)]);
    }

    private void ApplyUpdateLocalAspect(UpdateLocalAspectOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(UpdateLocalAspectOperation);
        EnsureText(operation.Name, name, nameof(operation.Name));
        EnsureDescription(operation.Description, name, nameof(operation.Description));
        LocalAspect value = GetLocalAspectForOperation(operation.LocalAspectId, name);
        if (value.Name == operation.Name && value.Description == operation.Description && value.Quantity == operation.Quantity)
            return;
        var previous = new UpdateLocalAspectOperation(value.Id, value.Name, value.Description, value.Quantity);
        transaction.RecordRollback(() => { value.UpdateName(previous.Name); value.UpdateDescription(previous.Description); value.UpdateQuantity(previous.Quantity); });
        value.UpdateName(operation.Name);
        value.UpdateDescription(operation.Description);
        value.UpdateQuantity(operation.Quantity);
        transaction.RecordApplied(operation, [previous]);
    }

    private void ApplyAddLocalRelation(AddLocalRelationOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(AddLocalRelationOperation);
        EnsureId(operation.LocalRelationId, name, nameof(operation.LocalRelationId));
        EnsureText(operation.Name, name, nameof(operation.Name));
        EnsureDescription(operation.Description, name, nameof(operation.Description));
        GetElementForOperation(operation.SourceElementId, name, "Source Element");
        GetElementForOperation(operation.TargetElementId, name, "Target Element");
        GetScopeForOperation(operation.ScopeId, name);
        if (_data.LocalRelations.ContainsKey(operation.LocalRelationId))
            throw Duplicate(name, "LocalRelation", operation.LocalRelationId);
        var value = new LocalRelation(operation.LocalRelationId, operation.Name, operation.Description, operation.Quantity, operation.SourceElementId, operation.TargetElementId, operation.ScopeId);
        transaction.RecordRollback(() => _data.LocalRelations.Remove(value.Id));
        _data.LocalRelations.Add(value.Id, value);
        transaction.RecordApplied(operation, [new RemoveLocalRelationOperation(value.Id)]);
    }

    private void ApplyRemoveLocalRelation(RemoveLocalRelationOperation operation, WorldTransaction transaction)
    {
        LocalRelation value = GetLocalRelationForOperation(operation.LocalRelationId, nameof(RemoveLocalRelationOperation));
        transaction.RecordRollback(() => _data.LocalRelations[value.Id] = value);
        _data.LocalRelations.Remove(value.Id);
        transaction.RecordApplied(operation, [ToAddOperation(value)]);
    }

    private void ApplyUpdateLocalRelation(UpdateLocalRelationOperation operation, WorldTransaction transaction)
    {
        const string name = nameof(UpdateLocalRelationOperation);
        EnsureText(operation.Name, name, nameof(operation.Name));
        EnsureDescription(operation.Description, name, nameof(operation.Description));
        LocalRelation value = GetLocalRelationForOperation(operation.LocalRelationId, name);
        if (value.Name == operation.Name && value.Description == operation.Description && value.Quantity == operation.Quantity)
            return;
        var previous = new UpdateLocalRelationOperation(value.Id, value.Name, value.Description, value.Quantity);
        transaction.RecordRollback(() => { value.UpdateName(previous.Name); value.UpdateDescription(previous.Description); value.UpdateQuantity(previous.Quantity); });
        value.UpdateName(operation.Name);
        value.UpdateDescription(operation.Description);
        value.UpdateQuantity(operation.Quantity);
        transaction.RecordApplied(operation, [previous]);
    }

    private Element GetElementForOperation(Guid id, string operation, string entityName)
    {
        EnsureId(id, operation, nameof(id));
        return _data.Elements.TryGetValue(id, out Element? value) ? value : throw NotFound(operation, entityName, id);
    }

    private Aspect GetAspectForOperation(Guid id, string operation)
    {
        EnsureId(id, operation, nameof(id));
        return _data.Aspects.TryGetValue(id, out Aspect? value) ? value : throw NotFound(operation, "Aspect", id);
    }

    private Relation GetRelationForOperation(Guid id, string operation)
    {
        EnsureId(id, operation, nameof(id));
        return _data.Relations.TryGetValue(id, out Relation? value) ? value : throw NotFound(operation, "Relation", id);
    }

    private Scope GetScopeForOperation(Guid id, string operation)
    {
        EnsureId(id, operation, nameof(id));
        return _data.Scopes.TryGetValue(id, out Scope? value) ? value : throw NotFound(operation, "Scope", id);
    }

    private LocalAspect GetLocalAspectForOperation(Guid id, string operation)
    {
        EnsureId(id, operation, nameof(id));
        return _data.LocalAspects.TryGetValue(id, out LocalAspect? value) ? value : throw NotFound(operation, "LocalAspect", id);
    }

    private LocalRelation GetLocalRelationForOperation(Guid id, string operation)
    {
        EnsureId(id, operation, nameof(id));
        return _data.LocalRelations.TryGetValue(id, out LocalRelation? value) ? value : throw NotFound(operation, "LocalRelation", id);
    }

    private static AddElementOperation ToAddOperation(Element value) => new(value.Id, value.Name, value.Description, value.Type);
    private static AddScopeOperation ToAddOperation(Scope value) => new(value.Id, value.Quantity, value.Type, value.OwnerElementId);
    private static AddAspectOperation ToAddOperation(Aspect value) => new(value.Id, value.Quantity, value.Type, value.ElementId, value.ScopeId);
    private static AddRelationOperation ToAddOperation(Relation value) => new(value.Id, value.Quantity, value.Type, value.SourceElementId, value.TargetElementId, value.ScopeId);
    private static AddLocalAspectOperation ToAddOperation(LocalAspect value) => new(value.Id, value.Name, value.Description, value.Quantity, value.ElementId, value.ScopeId);
    private static AddLocalRelationOperation ToAddOperation(LocalRelation value) => new(value.Id, value.Name, value.Description, value.Quantity, value.SourceElementId, value.TargetElementId, value.ScopeId);

    private static void EnsureId(Guid id, string operation, string parameterName)
    {
        if (id == Guid.Empty)
            throw Invalid(operation, $"参数 {parameterName} 不能是空 Guid。");
    }

    private static void EnsureText(string value, string operation, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw Invalid(operation, $"参数 {parameterName} 不能为空或只包含空白字符。");
    }

    private static void EnsureDescription(string value, string operation, string parameterName)
    {
        if (value is null)
            throw Invalid(operation, $"参数 {parameterName} 不能为 null。");
    }

    private static void EnsureType(ElementType value, string operation, string parameterName)
    {
        if (value.IsEmpty)
            throw Invalid(operation, $"参数 {parameterName} 不能是未初始化的 ElementType。");
    }

    private static void EnsureType(ScopeType value, string operation, string parameterName)
    {
        if (value.IsEmpty)
            throw Invalid(operation, $"参数 {parameterName} 不能是未初始化的 ScopeType。");
    }

    private static void EnsureType(AspectType value, string operation, string parameterName)
    {
        if (value.IsEmpty)
            throw Invalid(operation, $"参数 {parameterName} 不能是未初始化的 AspectType。");
    }

    private static void EnsureType(RelationType value, string operation, string parameterName)
    {
        if (value.IsEmpty)
            throw Invalid(operation, $"参数 {parameterName} 不能是未初始化的 RelationType。");
    }

    private static WorldException Invalid(string operation, string message) => new(WorldErrorCodes.InvalidArgument, operation, message);
    private static WorldException NotFound(string operation, string entityName, Guid entityId) => new(WorldErrorCodes.NotFound, operation, $"未找到 {entityName}。", entityId);
    private static WorldException Duplicate(string operation, string entityName, Guid entityId) => new(WorldErrorCodes.Duplicate, operation, $"{entityName} {entityId} 已存在。", entityId);

    private static void ValidateWorldSnapshot(WorldSnapshot data)
    {
        var errors = new List<string>();
        if (data.Id == Guid.Empty)
            errors.Add("WorldSnapshot.Id 不能是空 Guid。");
        Dictionary<Guid, Element> elements = data.Elements ?? [];
        Dictionary<Guid, Scope> scopes = data.Scopes ?? [];
        Dictionary<Guid, Aspect> aspects = data.Aspects ?? [];
        Dictionary<Guid, Relation> relations = data.Relations ?? [];
        Dictionary<Guid, LocalAspect> localAspects = data.LocalAspects ?? [];
        Dictionary<Guid, LocalRelation> localRelations = data.LocalRelations ?? [];
        if (data.Elements is null) errors.Add("WorldSnapshot.Elements 不能为 null。");
        if (data.Scopes is null) errors.Add("WorldSnapshot.Scopes 不能为 null。");
        if (data.Aspects is null) errors.Add("WorldSnapshot.Aspects 不能为 null。");
        if (data.Relations is null) errors.Add("WorldSnapshot.Relations 不能为 null。");
        if (data.LocalAspects is null) errors.Add("WorldSnapshot.LocalAspects 不能为 null。");
        if (data.LocalRelations is null) errors.Add("WorldSnapshot.LocalRelations 不能为 null。");
        foreach ((Guid key, Element? value) in elements)
        {
            if (!ValidateEntity(key, value?.Id, "Element", errors) || value is null) continue;
            if (string.IsNullOrWhiteSpace(value.Name)) errors.Add($"Elements[{key}].Name 不能为空。");
            if (value.Description is null) errors.Add($"Elements[{key}].Description 不能为 null。");
            if (value.Type.IsEmpty) errors.Add($"Elements[{key}].Type 不能未初始化。");
        }
        foreach ((Guid key, Scope? value) in scopes)
        {
            if (!ValidateEntity(key, value?.Id, "Scope", errors) || value is null) continue;
            if (value.Type.IsEmpty) errors.Add($"Scopes[{key}].Type 不能未初始化。");
            ValidateReference(value.OwnerElementId, elements, $"Scopes[{key}].OwnerElementId", "Element", errors);
        }
        foreach ((Guid key, Aspect? value) in aspects)
        {
            if (!ValidateEntity(key, value?.Id, "Aspect", errors) || value is null) continue;
            if (value.Type.IsEmpty) errors.Add($"Aspects[{key}].Type 不能未初始化。");
            ValidateReference(value.ElementId, elements, $"Aspects[{key}].ElementId", "Element", errors);
            ValidateReference(value.ScopeId, scopes, $"Aspects[{key}].ScopeId", "Scope", errors);
        }
        foreach ((Guid key, Relation? value) in relations)
        {
            if (!ValidateEntity(key, value?.Id, "Relation", errors) || value is null) continue;
            if (value.Type.IsEmpty) errors.Add($"Relations[{key}].Type 不能未初始化。");
            ValidateReference(value.SourceElementId, elements, $"Relations[{key}].SourceElementId", "Element", errors);
            ValidateReference(value.TargetElementId, elements, $"Relations[{key}].TargetElementId", "Element", errors);
            ValidateReference(value.ScopeId, scopes, $"Relations[{key}].ScopeId", "Scope", errors);
        }
        foreach ((Guid key, LocalAspect? value) in localAspects)
        {
            if (!ValidateEntity(key, value?.Id, "LocalAspect", errors) || value is null) continue;
            ValidateLocalText(value.Name, value.Description, $"LocalAspects[{key}]", errors);
            ValidateReference(value.ElementId, elements, $"LocalAspects[{key}].ElementId", "Element", errors);
            ValidateReference(value.ScopeId, scopes, $"LocalAspects[{key}].ScopeId", "Scope", errors);
        }
        foreach ((Guid key, LocalRelation? value) in localRelations)
        {
            if (!ValidateEntity(key, value?.Id, "LocalRelation", errors) || value is null) continue;
            ValidateLocalText(value.Name, value.Description, $"LocalRelations[{key}]", errors);
            ValidateReference(value.SourceElementId, elements, $"LocalRelations[{key}].SourceElementId", "Element", errors);
            ValidateReference(value.TargetElementId, elements, $"LocalRelations[{key}].TargetElementId", "Element", errors);
            ValidateReference(value.ScopeId, scopes, $"LocalRelations[{key}].ScopeId", "Scope", errors);
        }
        if (errors.Count > 0)
            throw new WorldException(WorldErrorCodes.InvalidWorldSnapshot, nameof(Create), $"WorldSnapshot 初始化校验发现 {errors.Count} 个错误。", validationErrors: errors);
    }

    private static bool ValidateEntity(Guid key, Guid? id, string entity, List<string> errors)
    {
        if (id is null)
        {
            errors.Add($"{entity}[{key}] 不能为 null。");
            return false;
        }
        if (key == Guid.Empty) errors.Add($"{entity} 字典键不能是空 Guid。");
        if (id == Guid.Empty) errors.Add($"{entity}[{key}].Id 不能是空 Guid。");
        if (key != id) errors.Add($"{entity}[{key}] 的字典键与 Id 不一致。");
        return true;
    }

    private static void ValidateLocalText(string name, string description, string path, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(name)) errors.Add($"{path}.Name 不能为空。");
        if (description is null) errors.Add($"{path}.Description 不能为 null。");
    }

    private static void ValidateReference<T>(Guid id, IReadOnlyDictionary<Guid, T> values, string path, string entity, List<string> errors)
    {
        if (id == Guid.Empty) errors.Add($"{path} 不能是空 Guid。");
        else if (!values.ContainsKey(id)) errors.Add($"{path} {id} 不指向任何 {entity}。");
    }

    private static WorldSnapshot CloneWorldSnapshot(WorldSnapshot source) => new()
    {
        Id = source.Id,
        Elements = source.Elements.ToDictionary(value => value.Key, value => CloneElement(value.Value)),
        Scopes = source.Scopes.ToDictionary(value => value.Key, value => CloneScope(value.Value)),
        Aspects = source.Aspects.ToDictionary(value => value.Key, value => CloneAspect(value.Value)),
        Relations = source.Relations.ToDictionary(value => value.Key, value => CloneRelation(value.Value)),
        LocalAspects = source.LocalAspects.ToDictionary(value => value.Key, value => CloneLocalAspect(value.Value)),
        LocalRelations = source.LocalRelations.ToDictionary(value => value.Key, value => CloneLocalRelation(value.Value))
    };

    private static Element CloneElement(Element value) => new(value.Id, value.Name, value.Description, value.Type);
    private static Scope CloneScope(Scope value) => new(value.Id, value.Quantity, value.Type, value.OwnerElementId);
    private static Aspect CloneAspect(Aspect value) => new(value.Id, value.Quantity, value.Type, value.ElementId, value.ScopeId);
    private static Relation CloneRelation(Relation value) => new(value.Id, value.Quantity, value.Type, value.SourceElementId, value.TargetElementId, value.ScopeId);
    private static LocalAspect CloneLocalAspect(LocalAspect value) => new(value.Id, value.Name, value.Description, value.Quantity, value.ElementId, value.ScopeId);
    private static LocalRelation CloneLocalRelation(LocalRelation value) => new(value.Id, value.Name, value.Description, value.Quantity, value.SourceElementId, value.TargetElementId, value.ScopeId);

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
                try { rollback(); }
                catch (Exception exception) { (failures ??= []).Add(exception); }
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
    internal bool Changed => ChangeSet is not null;
    internal static WorldApplyResult Unchanged(Guid stateId) => new(stateId, stateId, null);
}
