namespace Tavi.Domain.Performance;

/// <summary>独占一次 Scene 演绎产生的临时 EARS 图与 Beat，并维护其原子不变量。</summary>
public sealed class Performance
{
    private readonly PerformanceSnapshot _data;

    private Performance(PerformanceSnapshot snapshot) => _data = CloneSnapshot(snapshot);

    public static Performance Create(PerformanceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSnapshot(snapshot);
        return new Performance(snapshot);
    }

    public Guid Id => _data.PerformanceId;
    public Guid StateId => _data.StateId;
    public Guid SourceScenarioStateId => _data.SourceScenarioStateId;
    public Guid SourceSceneId => _data.SourceScene.Id;
    public PerformanceStatus Status => _data.Status;
    public PerformanceSnapshot CreateSnapshot() => CloneSnapshot(_data);
    public Element GetElement(Guid id) => Clone(GetElementCore(id, nameof(GetElement)));
    public Beat GetBeat(Guid id) => Clone(GetBeatCore(id, nameof(GetBeat)));
    public IReadOnlyCollection<Element> GetElements() => _data.Elements.Values.Select(Clone).ToArray();
    public IReadOnlyCollection<Scope> GetScopes() => _data.Scopes.Values.Select(Clone).ToArray();
    public IReadOnlyCollection<Aspect> GetAspects() => _data.Aspects.Values.Select(Clone).ToArray();
    public IReadOnlyCollection<Relation> GetRelations() => _data.Relations.Values.Select(Clone).ToArray();
    public IReadOnlyCollection<Beat> GetBeats() => _data.Beats.Values.Select(Clone).ToArray();

    internal PerformanceApplyResult Apply(PerformanceChangeSet changeSet)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        if (changeSet.IsEmpty)
            return PerformanceApplyResult.Unchanged(StateId);
        PerformanceSnapshot before = CreateSnapshot();
        bool changed = false;
        try
        {
            foreach (PerformanceOperation operation in changeSet.Operations)
                changed |= ApplyOperation(operation);
            ValidateSnapshot(_data);
        }
        catch (Exception operationException)
        {
            try
            {
                Restore(before);
            }
            catch (Exception rollbackException)
            {
                throw Error(PerformanceErrorCodes.RollbackFailed, TaviErrorCategory.InternalFailure, nameof(Apply), "Performance 操作失败且无法恢复完整前态。", new AggregateException(operationException, rollbackException));
            }
            throw;
        }
        if (!changed)
            return PerformanceApplyResult.Unchanged(StateId);
        Guid previous = StateId;
        _data.StateId = Guid.NewGuid();
        return new PerformanceApplyResult(previous, StateId, new AppliedPerformanceChangeSet(changeSet, new PerformanceChangeSet([new RestorePerformanceSnapshotOperation(before)])));
    }

    private bool ApplyOperation(PerformanceOperation operation)
    {
        EnsureActive(operation);
        return operation switch
        {
            AddPerformanceElementOperation or RemovePerformanceElementOperation or UpdatePerformanceElementOperation or
            AddPerformanceScopeOperation or RemovePerformanceScopeOperation or UpdatePerformanceScopeOperation or
            AddPerformanceAspectOperation or RemovePerformanceAspectOperation or UpdatePerformanceAspectOperation or
            AddPerformanceRelationOperation or RemovePerformanceRelationOperation or UpdatePerformanceRelationOperation
                => ApplyEarsOperation(operation, null),
            AddBeatOperation value => ApplyAddBeat(value),
            SetBeatSlotBindingOperation value => ApplySetBinding(value),
            ClearBeatSlotBindingOperation value => ApplyClearBinding(value),
            BeginBeatProcessingOperation value => ApplyBeginBeat(value),
            ResolveBeatOperation value => ApplyResolveBeat(value),
            MarkBeatPublishedOperation value => ApplyPublishBeat(value),
            CompletePerformanceOperation => ApplyComplete(),
            AbandonPerformanceOperation => ApplyAbandon(),
            RestorePerformanceSnapshotOperation value => ApplyRestore(value),
            _ => throw Invalid(nameof(Apply), $"不支持的 Performance 操作 {operation.GetType().Name}。")
        };
    }

    private bool ApplyEarsOperation(PerformanceOperation operation, Guid? ignoredProcessingBeatId)
    {
        switch (operation)
        {
            case AddPerformanceElementOperation value:
                EnsureNewId(value.ElementId, _data.Elements, nameof(AddPerformanceElementOperation), "Element");
                EnsureText(value.Name, nameof(AddPerformanceElementOperation));
                ArgumentNullException.ThrowIfNull(value.Description);
                _data.Elements.Add(value.ElementId, new Element(value.ElementId, value.Name, value.Description, value.Type));
                return true;
            case RemovePerformanceElementOperation value:
            {
                Element element = GetElementCore(value.ElementId, nameof(RemovePerformanceElementOperation));
                EnsureElementsNotProcessing([element.Id], ignoredProcessingBeatId, nameof(RemovePerformanceElementOperation));
                Guid[] scopeIds = _data.Scopes.Values.Where(scope => scope.OwnerElementId == element.Id).Select(scope => scope.Id).ToArray();
                HashSet<Guid> scopes = scopeIds.ToHashSet();
                foreach (Guid id in _data.Aspects.Values.Where(item => item.ElementId == element.Id || scopes.Contains(item.ScopeId)).Select(item => item.Id).ToArray())
                    _data.Aspects.Remove(id);
                foreach (Guid id in _data.Relations.Values.Where(item => item.SourceElementId == element.Id || item.TargetElementId == element.Id || scopes.Contains(item.ScopeId)).Select(item => item.Id).ToArray())
                    _data.Relations.Remove(id);
                foreach (Guid id in scopeIds)
                    _data.Scopes.Remove(id);
                _data.Elements.Remove(element.Id);
                return true;
            }
            case UpdatePerformanceElementOperation value:
            {
                EnsureText(value.Name, nameof(UpdatePerformanceElementOperation));
                ArgumentNullException.ThrowIfNull(value.Description);
                Element element = GetElementCore(value.ElementId, nameof(UpdatePerformanceElementOperation));
                EnsureElementsNotProcessing([element.Id], ignoredProcessingBeatId, nameof(UpdatePerformanceElementOperation));
                if (element.Name == value.Name && element.Description == value.Description && element.Type == value.Type)
                    return false;
                element.UpdateName(value.Name);
                element.UpdateDescription(value.Description);
                element.UpdateType(value.Type);
                return true;
            }
            case AddPerformanceScopeOperation value:
                EnsureNewId(value.ScopeId, _data.Scopes, nameof(AddPerformanceScopeOperation), "Scope");
                _ = GetElementCore(value.OwnerElementId, nameof(AddPerformanceScopeOperation));
                EnsureElementsNotProcessing([value.OwnerElementId], ignoredProcessingBeatId, nameof(AddPerformanceScopeOperation));
                _data.Scopes.Add(value.ScopeId, new Scope(value.ScopeId, value.Quantity, value.Type, value.OwnerElementId));
                return true;
            case RemovePerformanceScopeOperation value:
            {
                Scope scope = GetScopeCore(value.ScopeId, nameof(RemovePerformanceScopeOperation));
                EnsureElementsNotProcessing([scope.OwnerElementId], ignoredProcessingBeatId, nameof(RemovePerformanceScopeOperation));
                foreach (Guid id in _data.Aspects.Values.Where(item => item.ScopeId == scope.Id).Select(item => item.Id).ToArray())
                    _data.Aspects.Remove(id);
                foreach (Guid id in _data.Relations.Values.Where(item => item.ScopeId == scope.Id).Select(item => item.Id).ToArray())
                    _data.Relations.Remove(id);
                _data.Scopes.Remove(scope.Id);
                return true;
            }
            case UpdatePerformanceScopeOperation value:
            {
                Scope scope = GetScopeCore(value.ScopeId, nameof(UpdatePerformanceScopeOperation));
                EnsureElementsNotProcessing([scope.OwnerElementId], ignoredProcessingBeatId, nameof(UpdatePerformanceScopeOperation));
                if (scope.Quantity == value.Quantity && scope.Type == value.Type)
                    return false;
                scope.UpdateQuantity(value.Quantity);
                scope.UpdateType(value.Type);
                return true;
            }
            case AddPerformanceAspectOperation value:
                EnsureNewId(value.AspectId, _data.Aspects, nameof(AddPerformanceAspectOperation), "Aspect");
                _ = GetElementCore(value.ElementId, nameof(AddPerformanceAspectOperation));
                Scope aspectScope = GetScopeCore(value.ScopeId, nameof(AddPerformanceAspectOperation));
                EnsureElementsNotProcessing([value.ElementId, aspectScope.OwnerElementId], ignoredProcessingBeatId, nameof(AddPerformanceAspectOperation));
                _data.Aspects.Add(value.AspectId, new Aspect(value.AspectId, value.Quantity, value.Type, value.ElementId, value.ScopeId));
                return true;
            case RemovePerformanceAspectOperation value:
            {
                Aspect aspect = GetAspectCore(value.AspectId, nameof(RemovePerformanceAspectOperation));
                EnsureElementsNotProcessing([aspect.ElementId, GetScopeCore(aspect.ScopeId, nameof(RemovePerformanceAspectOperation)).OwnerElementId], ignoredProcessingBeatId, nameof(RemovePerformanceAspectOperation));
                _data.Aspects.Remove(aspect.Id);
                return true;
            }
            case UpdatePerformanceAspectOperation value:
            {
                Aspect aspect = GetAspectCore(value.AspectId, nameof(UpdatePerformanceAspectOperation));
                EnsureElementsNotProcessing([aspect.ElementId, GetScopeCore(aspect.ScopeId, nameof(UpdatePerformanceAspectOperation)).OwnerElementId], ignoredProcessingBeatId, nameof(UpdatePerformanceAspectOperation));
                if (aspect.Quantity == value.Quantity && aspect.Type == value.Type)
                    return false;
                aspect.UpdateQuantity(value.Quantity);
                aspect.UpdateType(value.Type);
                return true;
            }
            case AddPerformanceRelationOperation value:
                EnsureNewId(value.RelationId, _data.Relations, nameof(AddPerformanceRelationOperation), "Relation");
                _ = GetElementCore(value.SourceElementId, nameof(AddPerformanceRelationOperation));
                _ = GetElementCore(value.TargetElementId, nameof(AddPerformanceRelationOperation));
                Scope relationScope = GetScopeCore(value.ScopeId, nameof(AddPerformanceRelationOperation));
                EnsureElementsNotProcessing([value.SourceElementId, value.TargetElementId, relationScope.OwnerElementId], ignoredProcessingBeatId, nameof(AddPerformanceRelationOperation));
                _data.Relations.Add(value.RelationId, new Relation(value.RelationId, value.Quantity, value.Type, value.SourceElementId, value.TargetElementId, value.ScopeId));
                return true;
            case RemovePerformanceRelationOperation value:
            {
                Relation relation = GetRelationCore(value.RelationId, nameof(RemovePerformanceRelationOperation));
                EnsureElementsNotProcessing([relation.SourceElementId, relation.TargetElementId, GetScopeCore(relation.ScopeId, nameof(RemovePerformanceRelationOperation)).OwnerElementId], ignoredProcessingBeatId, nameof(RemovePerformanceRelationOperation));
                _data.Relations.Remove(relation.Id);
                return true;
            }
            case UpdatePerformanceRelationOperation value:
            {
                Relation relation = GetRelationCore(value.RelationId, nameof(UpdatePerformanceRelationOperation));
                EnsureElementsNotProcessing([relation.SourceElementId, relation.TargetElementId, GetScopeCore(relation.ScopeId, nameof(UpdatePerformanceRelationOperation)).OwnerElementId], ignoredProcessingBeatId, nameof(UpdatePerformanceRelationOperation));
                if (relation.Quantity == value.Quantity && relation.Type == value.Type)
                    return false;
                relation.UpdateQuantity(value.Quantity);
                relation.UpdateType(value.Type);
                return true;
            }
            default:
                throw Invalid(nameof(ApplyEarsOperation), $"{operation.GetType().Name} 不是 Performance EARS 操作。");
        }
    }

    private bool ApplyAddBeat(AddBeatOperation operation)
    {
        EnsureNewId(operation.BeatId, _data.Beats, nameof(AddBeatOperation), "Beat");
        if (operation.BasedOnPerformanceStateId != StateId)
            throw Invalid(nameof(AddBeatOperation), "BeatDefinition 未基于当前 Performance StateId。");
        _data.Beats.Add(operation.BeatId, new Beat(operation.BeatId, operation.DefinitionId, operation.ModuleId, operation.ModuleVersion, operation.BasedOnPerformanceStateId, operation.Name, operation.Description, BeatState.Binding, operation.Slots, writingProfileJson: operation.WritingProfileJson));
        return true;
    }

    private bool ApplySetBinding(SetBeatSlotBindingOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation.Binding);
        Beat beat = GetBeatCore(operation.BeatId, nameof(SetBeatSlotBindingOperation));
        EnsureBeatState(beat, BeatState.Binding, nameof(SetBeatSlotBindingOperation));
        BeatSlotSpecification slot = beat.GetSlotSpecifications().SingleOrDefault(value => value.Id == operation.Binding.SlotId)
            ?? throw Invalid(nameof(SetBeatSlotBindingOperation), $"Beat {beat.Id} 不包含槽位 {operation.Binding.SlotId}。");
        if (slot.Maximum is int maximum && operation.Binding.ElementIds.Count > maximum)
            throw Invalid(nameof(SetBeatSlotBindingOperation), $"Beat 槽位 {slot.Id} 的绑定数量不符合要求。");
        foreach (Guid elementId in operation.Binding.ElementIds)
        {
            _ = GetElementCore(elementId, nameof(SetBeatSlotBindingOperation));
            Beat? occupied = _data.Beats.Values.FirstOrDefault(candidate => candidate.Id != beat.Id && candidate.State != BeatState.Published && candidate.ContainsElement(elementId));
            if (occupied is not null)
                throw Invalid(nameof(SetBeatSlotBindingOperation), $"Element {elementId} 已归属 Beat {occupied.Id}。");
            if (beat.GetBindings().Any(value => value.SlotId != operation.Binding.SlotId && value.ElementIds.Contains(elementId)))
                throw Invalid(nameof(SetBeatSlotBindingOperation), $"Element {elementId} 已绑定到当前 Beat 的其他槽位。");
        }
        return beat.SetBinding(operation.Binding);
    }

    private bool ApplyClearBinding(ClearBeatSlotBindingOperation operation)
    {
        Beat beat = GetBeatCore(operation.BeatId, nameof(ClearBeatSlotBindingOperation));
        EnsureBeatState(beat, BeatState.Binding, nameof(ClearBeatSlotBindingOperation));
        return beat.ClearBinding(operation.SlotId);
    }

    private bool ApplyBeginBeat(BeginBeatProcessingOperation operation)
    {
        Beat beat = GetBeatCore(operation.BeatId, nameof(BeginBeatProcessingOperation));
        EnsureBeatState(beat, BeatState.Binding, nameof(BeginBeatProcessingOperation));
        foreach (BeatSlotSpecification slot in beat.GetSlotSpecifications())
        {
            int count = beat.FindBinding(slot.Id)?.ElementIds.Count ?? 0;
            if (count < slot.Minimum || slot.Maximum is int maximum && count > maximum)
                throw Invalid(nameof(BeginBeatProcessingOperation), $"Beat {beat.Id} 的槽位 {slot.Id} 尚未完整绑定。");
        }
        return beat.BeginProcessing();
    }

    private bool ApplyResolveBeat(ResolveBeatOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation.EarsChanges);
        ArgumentNullException.ThrowIfNull(operation.Paragraphs);
        Beat beat = GetBeatCore(operation.BeatId, nameof(ResolveBeatOperation));
        EnsureBeatState(beat, BeatState.Processing, nameof(ResolveBeatOperation));
        EnsureResolutionLocal(beat, operation.EarsChanges);
        bool changed = false;
        foreach (PerformanceOperation earsOperation in operation.EarsChanges.Operations)
            changed |= ApplyEarsOperation(earsOperation, beat.Id);
        changed |= beat.Resolve(operation.Paragraphs);
        return changed;
    }

    private bool ApplyPublishBeat(MarkBeatPublishedOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation.Receipt);
        Beat beat = GetBeatCore(operation.BeatId, nameof(MarkBeatPublishedOperation));
        EnsureBeatState(beat, BeatState.Resolved, nameof(MarkBeatPublishedOperation));
        return beat.Publish(operation.Receipt);
    }

    private bool ApplyComplete()
    {
        if (_data.Beats.Count == 0 || _data.Beats.Values.Any(value => value.State != BeatState.Published))
            throw Invalid(nameof(CompletePerformanceOperation), "Performance 只有在至少一个 Beat 全部发布后才能完成。");
        _data.Status = PerformanceStatus.Completed;
        return true;
    }

    private bool ApplyAbandon()
    {
        _data.Status = PerformanceStatus.Abandoned;
        return true;
    }

    private bool ApplyRestore(RestorePerformanceSnapshotOperation operation)
    {
        Restore(operation.Snapshot);
        return true;
    }

    private void EnsureResolutionLocal(Beat beat, PerformanceChangeSet changeSet)
    {
        HashSet<Guid> allowedElements = beat.GetBindings().SelectMany(value => value.ElementIds).ToHashSet();
        HashSet<Guid> elements = _data.Elements.Keys.ToHashSet();
        Dictionary<Guid, Guid> scopes = _data.Scopes.ToDictionary(pair => pair.Key, pair => pair.Value.OwnerElementId);
        Dictionary<Guid, (Guid ElementId, Guid ScopeId)> aspects = _data.Aspects.ToDictionary(pair => pair.Key, pair => (pair.Value.ElementId, pair.Value.ScopeId));
        Dictionary<Guid, (Guid SourceId, Guid TargetId, Guid ScopeId)> relations = _data.Relations.ToDictionary(pair => pair.Key, pair => (pair.Value.SourceElementId, pair.Value.TargetElementId, pair.Value.ScopeId));
        foreach (PerformanceOperation operation in changeSet.Operations)
        {
            switch (operation)
            {
                case AddPerformanceElementOperation value:
                    RequireLocal(elements.Add(value.ElementId), "新 Element 标识已经存在。");
                    allowedElements.Add(value.ElementId);
                    break;
                case RemovePerformanceElementOperation value:
                    RequireElement(value.ElementId);
                    elements.Remove(value.ElementId);
                    Guid[] ownedScopes = scopes.Where(pair => pair.Value == value.ElementId).Select(pair => pair.Key).ToArray();
                    foreach (Guid scopeId in ownedScopes)
                        scopes.Remove(scopeId);
                    foreach (Guid id in aspects.Where(pair => pair.Value.ElementId == value.ElementId || ownedScopes.Contains(pair.Value.ScopeId)).Select(pair => pair.Key).ToArray())
                        aspects.Remove(id);
                    foreach (Guid id in relations.Where(pair => pair.Value.SourceId == value.ElementId || pair.Value.TargetId == value.ElementId || ownedScopes.Contains(pair.Value.ScopeId)).Select(pair => pair.Key).ToArray())
                        relations.Remove(id);
                    break;
                case UpdatePerformanceElementOperation value:
                    RequireElement(value.ElementId);
                    break;
                case AddPerformanceScopeOperation value:
                    RequireElement(value.OwnerElementId);
                    RequireLocal(!scopes.ContainsKey(value.ScopeId), "新 Scope 标识已经存在。");
                    scopes.Add(value.ScopeId, value.OwnerElementId);
                    break;
                case RemovePerformanceScopeOperation value:
                    _ = RequireScope(value.ScopeId);
                    scopes.Remove(value.ScopeId);
                    foreach (Guid id in aspects.Where(pair => pair.Value.ScopeId == value.ScopeId).Select(pair => pair.Key).ToArray())
                        aspects.Remove(id);
                    foreach (Guid id in relations.Where(pair => pair.Value.ScopeId == value.ScopeId).Select(pair => pair.Key).ToArray())
                        relations.Remove(id);
                    break;
                case UpdatePerformanceScopeOperation value:
                    _ = RequireScope(value.ScopeId);
                    break;
                case AddPerformanceAspectOperation value:
                    RequireElement(value.ElementId);
                    _ = RequireScope(value.ScopeId);
                    RequireLocal(!aspects.ContainsKey(value.AspectId), "新 Aspect 标识已经存在。");
                    aspects.Add(value.AspectId, (value.ElementId, value.ScopeId));
                    break;
                case RemovePerformanceAspectOperation value:
                    RequireAspect(value.AspectId);
                    aspects.Remove(value.AspectId);
                    break;
                case UpdatePerformanceAspectOperation value:
                    RequireAspect(value.AspectId);
                    break;
                case AddPerformanceRelationOperation value:
                    RequireElement(value.SourceElementId);
                    RequireElement(value.TargetElementId);
                    _ = RequireScope(value.ScopeId);
                    RequireLocal(!relations.ContainsKey(value.RelationId), "新 Relation 标识已经存在。");
                    relations.Add(value.RelationId, (value.SourceElementId, value.TargetElementId, value.ScopeId));
                    break;
                case RemovePerformanceRelationOperation value:
                    RequireRelation(value.RelationId);
                    relations.Remove(value.RelationId);
                    break;
                case UpdatePerformanceRelationOperation value:
                    RequireRelation(value.RelationId);
                    break;
                default:
                    throw Invalid(nameof(ResolveBeatOperation), "Beat 结果只能包含 Performance EARS 操作。");
            }
        }

        void RequireElement(Guid id) => RequireLocal(elements.Contains(id) && allowedElements.Contains(id), $"Element {id} 超出 Beat 局部边界。");
        Guid RequireScope(Guid id)
        {
            RequireLocal(scopes.TryGetValue(id, out Guid owner) && allowedElements.Contains(owner), $"Scope {id} 超出 Beat 局部边界。");
            return owner;
        }
        void RequireAspect(Guid id)
        {
            RequireLocal(aspects.TryGetValue(id, out var references), $"Aspect {id} 不存在。");
            RequireElement(references.ElementId);
            _ = RequireScope(references.ScopeId);
        }
        void RequireRelation(Guid id)
        {
            RequireLocal(relations.TryGetValue(id, out var references), $"Relation {id} 不存在。");
            RequireElement(references.SourceId);
            RequireElement(references.TargetId);
            _ = RequireScope(references.ScopeId);
        }
        static void RequireLocal(bool condition, string message)
        {
            if (!condition)
                throw Invalid(nameof(ResolveBeatOperation), message);
        }
    }

    private void EnsureElementsNotProcessing(IEnumerable<Guid> ids, Guid? ignoredBeatId, string operation)
    {
        HashSet<Guid> targets = ids.ToHashSet();
        Beat? beat = _data.Beats.Values.FirstOrDefault(value => value.Id != ignoredBeatId && value.State == BeatState.Processing && value.GetBindings().SelectMany(binding => binding.ElementIds).Any(targets.Contains));
        if (beat is not null)
            throw Invalid(operation, $"Beat {beat.Id} 正在处理相关 Element。");
    }

    private void EnsureActive(PerformanceOperation operation)
    {
        if (operation is RestorePerformanceSnapshotOperation)
            return;
        if (_data.Status != PerformanceStatus.Active)
            throw Invalid(operation.GetType().Name, $"Performance 当前处于 {_data.Status}，不能继续修改。");
    }

    private static void EnsureBeatState(Beat beat, BeatState expected, string operation)
    {
        if (beat.State != expected)
            throw Invalid(operation, $"Beat {beat.Id} 当前状态 {beat.State}，请求需要 {expected}。");
    }

    private static void ValidateSnapshot(PerformanceSnapshot snapshot)
    {
        var errors = new List<string>();
        if (snapshot.PerformanceId == Guid.Empty || snapshot.StateId == Guid.Empty || snapshot.SourceScenarioStateId == Guid.Empty || snapshot.SourceScene is null || snapshot.SourceScene.Id == Guid.Empty)
            errors.Add("Performance、Scenario 和 Scene 标识均不能为空。");
        if (snapshot.SourceScene is not null)
        {
            if (string.IsNullOrWhiteSpace(snapshot.SourceScene.DefinitionId) ||
                string.IsNullOrWhiteSpace(snapshot.SourceScene.ModuleId) ||
                string.IsNullOrWhiteSpace(snapshot.SourceScene.ModuleVersion) ||
                snapshot.SourceScene.BasedOnScenarioStateId == Guid.Empty ||
                string.IsNullOrWhiteSpace(snapshot.SourceScene.State))
                errors.Add("Performance 来源 Scene 身份不完整。");
            if (snapshot.SourceScene.Bindings.Select(value => value.SlotId).Distinct(StringComparer.Ordinal).Count() != snapshot.SourceScene.Bindings.Count)
                errors.Add("Performance 来源 Scene 包含重复槽位。");
            HashSet<Guid> sourceElementIds = snapshot.SourceScene.Elements.Select(value => value.Id).ToHashSet();
            HashSet<Guid> sourceScopeIds = snapshot.SourceScene.Scopes.Select(value => value.Id).ToHashSet();
            if (sourceElementIds.Count != snapshot.SourceScene.Elements.Count ||
                sourceScopeIds.Count != snapshot.SourceScene.Scopes.Count ||
                snapshot.SourceScene.Bindings.SelectMany(value => value.ElementIds).Any(value => !sourceElementIds.Contains(value)) ||
                snapshot.SourceScene.Scopes.Any(value => !sourceElementIds.Contains(value.OwnerElementId)) ||
                snapshot.SourceScene.Aspects.Any(value => !sourceElementIds.Contains(value.ElementId) || !sourceScopeIds.Contains(value.ScopeId)) ||
                snapshot.SourceScene.Relations.Any(value => !sourceElementIds.Contains(value.SourceElementId) || !sourceElementIds.Contains(value.TargetElementId) || !sourceScopeIds.Contains(value.ScopeId)))
                errors.Add("Performance 来源 Scene 的冻结 EARS 图不完整。");
        }
        if (!Enum.IsDefined(snapshot.Status))
            errors.Add("PerformanceStatus 无效。");
        foreach ((Guid id, Scope scope) in snapshot.Scopes)
            if (id == Guid.Empty || scope is null || id != scope.Id || !snapshot.Elements.ContainsKey(scope.OwnerElementId))
                errors.Add($"Scope {id} 不合法。");
        foreach ((Guid id, Aspect aspect) in snapshot.Aspects)
            if (id == Guid.Empty || aspect is null || id != aspect.Id || !snapshot.Elements.ContainsKey(aspect.ElementId) || !snapshot.Scopes.ContainsKey(aspect.ScopeId))
                errors.Add($"Aspect {id} 不合法。");
        foreach ((Guid id, Relation relation) in snapshot.Relations)
            if (id == Guid.Empty || relation is null || id != relation.Id || !snapshot.Elements.ContainsKey(relation.SourceElementId) || !snapshot.Elements.ContainsKey(relation.TargetElementId) || !snapshot.Scopes.ContainsKey(relation.ScopeId))
                errors.Add($"Relation {id} 不合法。");
        foreach ((Guid id, Beat beat) in snapshot.Beats)
        {
            if (id == Guid.Empty || beat is null || id != beat.Id)
            {
                errors.Add($"Beat {id} 不合法。");
                continue;
            }
            if (beat.GetBindings().SelectMany(value => value.ElementIds).Any(elementId => !snapshot.Elements.ContainsKey(elementId)))
                errors.Add($"Beat {id} 引用了不存在的 Element。");
            if (beat.State is BeatState.Resolved or BeatState.Published && beat.Paragraphs.Select(value => value.Id).Distinct().Count() != beat.Paragraphs.Count)
                errors.Add($"Beat {id} 包含重复段落。");
            if (beat.State == BeatState.Published && beat.Publication is null || beat.State != BeatState.Published && beat.Publication is not null)
                errors.Add($"Beat {id} 的发布凭据与状态不一致。");
        }
        Guid? duplicate = snapshot.Beats.Values.Where(value => value.State != BeatState.Published)
            .SelectMany(beat => beat.GetBindings().SelectMany(binding => binding.ElementIds))
            .GroupBy(id => id).FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicate.HasValue)
            errors.Add($"Element {duplicate.Value} 同时属于多个活动 Beat 槽位。");
        if (snapshot.Status == PerformanceStatus.Completed && (snapshot.Beats.Count == 0 || snapshot.Beats.Values.Any(value => value.State != BeatState.Published)))
            errors.Add("已完成 Performance 必须包含且只包含已发布 Beat。");
        if (errors.Count > 0)
            throw Error(PerformanceErrorCodes.InvalidSnapshot, TaviErrorCategory.Validation, nameof(Create), string.Join(" ", errors));
    }

    private void Restore(PerformanceSnapshot snapshot)
    {
        PerformanceSnapshot copy = CloneSnapshot(snapshot);
        _data.PerformanceId = copy.PerformanceId;
        _data.StateId = copy.StateId;
        _data.SourceScenarioStateId = copy.SourceScenarioStateId;
        _data.SourceScene = copy.SourceScene;
        _data.Status = copy.Status;
        Replace(_data.Modules, copy.Modules);
        _data.ImportedElementIds = copy.ImportedElementIds;
        Replace(_data.Elements, copy.Elements);
        Replace(_data.Scopes, copy.Scopes);
        Replace(_data.Aspects, copy.Aspects);
        Replace(_data.Relations, copy.Relations);
        Replace(_data.Beats, copy.Beats);
    }

    private static PerformanceSnapshot CloneSnapshot(PerformanceSnapshot source) => new()
    {
        PerformanceId = source.PerformanceId,
        StateId = source.StateId,
        SourceScenarioStateId = source.SourceScenarioStateId,
        SourceScene = Clone(source.SourceScene),
        Status = source.Status,
        Modules = source.Modules.Select(value => new PerformanceModuleReference(value.Id, value.Version, value.Parameters)).ToList(),
        ImportedElementIds = source.ImportedElementIds.ToHashSet(),
        Elements = source.Elements.ToDictionary(pair => pair.Key, pair => Clone(pair.Value)),
        Scopes = source.Scopes.ToDictionary(pair => pair.Key, pair => Clone(pair.Value)),
        Aspects = source.Aspects.ToDictionary(pair => pair.Key, pair => Clone(pair.Value)),
        Relations = source.Relations.ToDictionary(pair => pair.Key, pair => Clone(pair.Value)),
        Beats = source.Beats.ToDictionary(pair => pair.Key, pair => Clone(pair.Value))
    };

    private static Beat Clone(Beat value) => new(value.Id, value.DefinitionId, value.ModuleId, value.ModuleVersion, value.BasedOnPerformanceStateId, value.Name, value.Description, value.State, value.GetSlotSpecifications(), value.GetBindings(), value.Paragraphs, value.Publication, value.WritingProfileJson);
    private static PerformanceSourceScene Clone(PerformanceSourceScene value) => new()
    {
        Id = value.Id,
        DefinitionId = value.DefinitionId,
        ModuleId = value.ModuleId,
        ModuleVersion = value.ModuleVersion,
        BasedOnScenarioStateId = value.BasedOnScenarioStateId,
        State = value.State,
        Bindings = value.Bindings.Select(binding => new PerformanceSourceSceneBinding(binding.SlotId, binding.ElementIds)).ToArray(),
        Elements = value.Elements.Select(Clone).ToArray(),
        Scopes = value.Scopes.Select(Clone).ToArray(),
        Aspects = value.Aspects.Select(Clone).ToArray(),
        Relations = value.Relations.Select(Clone).ToArray()
    };
    private static Element Clone(Element value) => new(value.Id, value.Name, value.Description, value.Type);
    private static Scope Clone(Scope value) => new(value.Id, value.Quantity, value.Type, value.OwnerElementId);
    private static Aspect Clone(Aspect value) => new(value.Id, value.Quantity, value.Type, value.ElementId, value.ScopeId);
    private static Relation Clone(Relation value) => new(value.Id, value.Quantity, value.Type, value.SourceElementId, value.TargetElementId, value.ScopeId);

    private Element GetElementCore(Guid id, string operation) => _data.Elements.TryGetValue(id, out Element? value) ? value : throw NotFound(operation, "Element", id);
    private Scope GetScopeCore(Guid id, string operation) => _data.Scopes.TryGetValue(id, out Scope? value) ? value : throw NotFound(operation, "Scope", id);
    private Aspect GetAspectCore(Guid id, string operation) => _data.Aspects.TryGetValue(id, out Aspect? value) ? value : throw NotFound(operation, "Aspect", id);
    private Relation GetRelationCore(Guid id, string operation) => _data.Relations.TryGetValue(id, out Relation? value) ? value : throw NotFound(operation, "Relation", id);
    private Beat GetBeatCore(Guid id, string operation) => _data.Beats.TryGetValue(id, out Beat? value) ? value : throw NotFound(operation, "Beat", id);

    private static void EnsureText(string value, string operation)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw Invalid(operation, "名称不能为空。");
    }

    private static void EnsureNewId<T>(Guid id, IDictionary<Guid, T> values, string operation, string entity)
    {
        if (id == Guid.Empty)
            throw Invalid(operation, $"{entity} 标识不能为空。");
        if (values.ContainsKey(id))
            throw Error(PerformanceErrorCodes.Duplicate, TaviErrorCategory.Conflict, operation, $"{entity} {id} 已存在。");
    }

    private static PerformanceException NotFound(string operation, string entity, Guid id) => Error(PerformanceErrorCodes.NotFound, TaviErrorCategory.NotFound, operation, $"{entity} {id} 不存在。");
    private static PerformanceException Invalid(string operation, string message) => Error(PerformanceErrorCodes.InvalidArgument, TaviErrorCategory.InvalidState, operation, message);
    private static PerformanceException Error(string code, TaviErrorCategory category, string operation, string message, Exception? inner = null) => new(code, category, operation, message, inner);

    private static void Replace<T>(ICollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (T item in source)
            target.Add(item);
    }

    private static void Replace<T>(IDictionary<Guid, T> target, IDictionary<Guid, T> source)
    {
        target.Clear();
        foreach ((Guid key, T value) in source)
            target.Add(key, value);
    }
}
