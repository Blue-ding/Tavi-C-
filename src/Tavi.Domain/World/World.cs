namespace Tavi.Domain.World
{
    /// <summary>
    /// 运行时世界图。
    /// 负责基于 Id 和图结构的查询，并维护世界图的领域不变量。
    /// </summary>
    public sealed class World
    {
        private readonly WorldSnapshot _data;
        private readonly Dictionary<string, Guid> _characterIdsByName;

        private World(WorldSnapshot data)
        {
            _data = CloneWorldSnapshot(data);
            _characterIdsByName = _data.Anchors.Values
                .Where(anchor => anchor.Type == AnchorType.Character)
                .ToDictionary(
                    anchor => anchor.Name,
                    anchor => anchor.Id,
                    StringComparer.Ordinal
                );
        }

        /// <summary>
        /// 校验并创建运行时世界图。所有初始化错误会在一个异常中汇总。
        /// </summary>
        public static World Create(WorldSnapshot data)
        {
            ValidateWorldSnapshot(data);
            return new World(data);
        }

        /// <summary>
        /// 获取当前运行时世界版本；每个实际生效的原子操作组提交后递增一次。
        /// </summary>
        public long Revision { get; private set; }

        /// <summary>
        /// 创建当前世界图的独立领域快照。
        /// </summary>
        public WorldSnapshot CreateSnapshot()
        {
            return CloneWorldSnapshot(_data);
        }

        /// <summary>
        /// 根据 Id 获取 Anchor。
        /// </summary>
        public Anchor GetAnchor(Guid anchorId)
        {
            EnsureId(anchorId, nameof(GetAnchor), nameof(anchorId));
            if (_data.Anchors.TryGetValue(anchorId, out var anchor))
                return CloneAnchor(anchor);
            throw NotFound(nameof(GetAnchor), "Anchor", anchorId);
        }

        /// <summary>
        /// 根据 Id 获取主世界或任一子世界中的 Relation。
        /// </summary>
        public Relation GetRelation(Guid relationId)
        {
            return CloneRelation(FindRelation(relationId, nameof(GetRelation)).Relation);
        }

        /// <summary>
        /// 获取全部 Anchor。
        /// </summary>
        public IReadOnlyCollection<Anchor> GetAnchors()
        {
            return _data.Anchors.Values.Select(CloneAnchor).ToArray();
        }

        /// <summary>
        /// 获取全部 Character 类型的 Anchor。
        /// </summary>
        public IReadOnlyCollection<Anchor> GetCharacters()
        {
            return _characterIdsByName.Values
                .Select(characterId => CloneAnchor(_data.Anchors[characterId]))
                .ToArray();
        }

        /// <summary>
        /// 获取全部主世界 Relation。
        /// </summary>
        public IReadOnlyCollection<Relation> GetWorldRelations()
        {
            return _data.Relations.Values.Select(CloneRelation).ToArray();
        }

        /// <summary>
        /// 获取所有以指定 Anchor 为目标的 Relation。
        /// </summary>
        public IReadOnlyCollection<Relation> GetIncomingRelations(Guid anchorId)
        {
            GetAnchorForOperation(anchorId, nameof(GetIncomingRelations), "Anchor");
            return EnumerateAllRelations()
                .Where(relation => relation.TargetId == anchorId)
                .Select(CloneRelation)
                .ToArray();
        }

        /// <summary>
        /// 获取所有以指定 Anchor 为来源的 Relation。
        /// </summary>
        public IReadOnlyCollection<Relation> GetOutgoingRelations(Guid anchorId)
        {
            GetAnchorForOperation(anchorId, nameof(GetOutgoingRelations), "Anchor");
            return EnumerateAllRelations()
                .Where(relation => relation.SourceId == anchorId)
                .Select(CloneRelation)
                .ToArray();
        }

        /// <summary>
        /// 获取所有与指定 Anchor 相连的 Relation。
        /// </summary>
        public IReadOnlyCollection<Relation> GetRelations(Guid anchorId)
        {
            GetAnchorForOperation(anchorId, nameof(GetRelations), "Anchor");
            return EnumerateAllRelations()
                .Where(relation => relation.SourceId == anchorId || relation.TargetId == anchorId)
                .Select(CloneRelation)
                .ToArray();
        }

        /// <summary>
        /// 获取指定 Character 持有的子世界快照。
        /// </summary>
        public SubWorldSnapshot GetSubWorld(Guid characterId)
        {
            EnsureCharacter(characterId, nameof(GetSubWorld));
            SubWorldSnapshot subWorld = FindSubWorld(characterId) ??
                                        throw NotFound(nameof(GetSubWorld), "SubWorldSnapshot", characterId);
            return CloneSubWorldSnapshot(subWorld);
        }

        /// <summary>
        /// 获取全部子世界快照。
        /// </summary>
        public IReadOnlyCollection<SubWorldSnapshot> GetSubWorlds()
        {
            return _data.SubWorlds
                .Select(CloneSubWorldSnapshot)
                .ToArray();
        }

        /// <summary>
        /// 获取指定 Character 子世界中的全部 Relation。
        /// </summary>
        public IReadOnlyCollection<Relation> GetSubWorldRelations(Guid characterId)
        {
            EnsureCharacter(characterId, nameof(GetSubWorldRelations));
            SubWorldSnapshot subWorld = FindSubWorld(characterId) ??
                                        throw NotFound(nameof(GetSubWorldRelations), "SubWorldSnapshot", characterId);
            return subWorld.Relations.Values.Select(CloneRelation).ToArray();
        }

        /// <summary>
        /// 在当前 World 上原地执行操作组。调用方必须持有 WorldSession 写边界；失败时内部回滚，中间状态不改变 revision，也不触发外部事件。
        /// </summary>
        internal WorldApplyResult Apply(WorldChangeSet changeSet)
        {
            ArgumentNullException.ThrowIfNull(changeSet);
            if (changeSet.IsEmpty)
                return WorldApplyResult.Unchanged(Revision);
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
                return WorldApplyResult.Unchanged(Revision);
            long previousRevision = Revision;
            Revision++;
            return new WorldApplyResult(previousRevision, Revision, transaction.CreateAppliedChangeSet());
        }

        private void ApplyOperation(WorldOperation operation, WorldTransaction transaction)
        {
            switch (operation)
            {
                case AddAnchorOperation add:
                    ApplyAddAnchor(add, transaction);
                    break;
                case RemoveAnchorOperation remove:
                    ApplyRemoveAnchor(remove, transaction);
                    break;
                case UpdateAnchorNameOperation update:
                    ApplyUpdateAnchorName(update, transaction);
                    break;
                case UpdateAnchorDescriptionOperation update:
                    ApplyUpdateAnchorDescription(update, transaction);
                    break;
                case UpdateAnchorTypeOperation update:
                    ApplyUpdateAnchorType(update, transaction);
                    break;
                case AddRelationOperation add:
                    ApplyAddRelation(add, transaction);
                    break;
                case RemoveRelationOperation remove:
                    ApplyRemoveRelation(remove, transaction);
                    break;
                case UpdateRelationNameOperation update:
                    ApplyUpdateRelationName(update, transaction);
                    break;
                case UpdateRelationDescriptionOperation update:
                    ApplyUpdateRelationDescription(update, transaction);
                    break;
                case CreateSubWorldOperation create:
                    ApplyCreateSubWorld(create, transaction);
                    break;
                case RemoveSubWorldOperation remove:
                    ApplyRemoveSubWorld(remove, transaction);
                    break;
                default:
                    throw new WorldException(WorldErrorCode.InvalidArgument, nameof(Apply), $"不支持的世界操作类型 {operation.GetType().FullName}。");
            }
        }

        private void ApplyAddAnchor(AddAnchorOperation operation, WorldTransaction transaction)
        {
            const string operationName = nameof(AddAnchorOperation);
            EnsureId(operation.AnchorId, operationName, nameof(operation.AnchorId));
            EnsureName(operation.Name, operationName, nameof(operation.Name));
            EnsureAnchorType(operation.Type, operationName);
            if (_data.Anchors.ContainsKey(operation.AnchorId))
                throw new WorldException(WorldErrorCode.Duplicate, operationName, $"Anchor {operation.AnchorId} 已存在。", operation.AnchorId);
            if (operation.Type == AnchorType.Character && _characterIdsByName.TryGetValue(operation.Name, out Guid duplicateId))
                throw new WorldException(WorldErrorCode.Duplicate, operationName, $"Character 名称“{operation.Name}”已被 Anchor {duplicateId} 使用。", operation.AnchorId);
            var anchor = new Anchor(operation.AnchorId, operation.Name, operation.Description, operation.Type);
            transaction.RecordRollback(() =>
            {
                _data.Anchors.Remove(anchor.Id);
                if (anchor.Type == AnchorType.Character && _characterIdsByName.GetValueOrDefault(anchor.Name) == anchor.Id)
                    _characterIdsByName.Remove(anchor.Name);
            });
            _data.Anchors.Add(anchor.Id, anchor);
            if (anchor.Type == AnchorType.Character)
                _characterIdsByName.Add(anchor.Name, anchor.Id);
            transaction.RecordApplied(operation, [new RemoveAnchorOperation(anchor.Id)]);
        }

        private void ApplyRemoveAnchor(RemoveAnchorOperation operation, WorldTransaction transaction)
        {
            const string operationName = nameof(RemoveAnchorOperation);
            Anchor anchor = GetAnchorForOperation(operation.AnchorId, operationName, "Anchor");
            SubWorldSnapshot? ownedSubWorld = anchor.Type == AnchorType.Character ? FindSubWorld(anchor.Id) : null;
            RelationLocation[] externalRelations = EnumerateRelationLocations()
                .Where(location => !ReferenceEquals(location.Owner, ownedSubWorld?.Relations) && (location.Relation.SourceId == anchor.Id || location.Relation.TargetId == anchor.Id))
                .ToArray();
            RelationLocation[] removedRelations = externalRelations
                .Concat(ownedSubWorld is null ? [] : ownedSubWorld.Relations.Values.Select(relation => new RelationLocation(ownedSubWorld.Relations, relation, ownedSubWorld.DomainId)))
                .ToArray();
            transaction.RecordRollback(() =>
            {
                _data.Anchors[anchor.Id] = anchor;
                if (anchor.Type == AnchorType.Character)
                    _characterIdsByName[anchor.Name] = anchor.Id;
                if (ownedSubWorld is not null && !_data.SubWorlds.Contains(ownedSubWorld))
                    _data.SubWorlds.Add(ownedSubWorld);
                foreach (RelationLocation location in externalRelations)
                    location.Owner[location.Relation.Id] = location.Relation;
            });
            foreach (RelationLocation location in externalRelations)
                location.Owner.Remove(location.Relation.Id);
            if (ownedSubWorld is not null)
                _data.SubWorlds.Remove(ownedSubWorld);
            if (anchor.Type == AnchorType.Character)
                _characterIdsByName.Remove(anchor.Name);
            _data.Anchors.Remove(anchor.Id);

            var inverse = new List<WorldOperation> { new AddAnchorOperation(anchor.Id, anchor.Name, anchor.Description, anchor.Type) };
            if (ownedSubWorld is not null)
                inverse.Add(new CreateSubWorldOperation(ownedSubWorld.Id, anchor.Id));
            inverse.AddRange(removedRelations.Select(ToAddRelationOperation));
            transaction.RecordApplied(operation, inverse);
        }

        private void ApplyUpdateAnchorName(UpdateAnchorNameOperation operation, WorldTransaction transaction)
        {
            const string operationName = nameof(UpdateAnchorNameOperation);
            EnsureName(operation.Name, operationName, nameof(operation.Name));
            Anchor anchor = GetAnchorForOperation(operation.AnchorId, operationName, "Anchor");
            if (string.Equals(anchor.Name, operation.Name, StringComparison.Ordinal))
                return;
            if (anchor.Type == AnchorType.Character && _characterIdsByName.TryGetValue(operation.Name, out Guid duplicateId) && duplicateId != anchor.Id)
                throw new WorldException(WorldErrorCode.Duplicate, operationName, $"Character 名称“{operation.Name}”已被 Anchor {duplicateId} 使用。", anchor.Id);
            string previousName = anchor.Name;
            transaction.RecordRollback(() =>
            {
                if (anchor.Type == AnchorType.Character)
                {
                    _characterIdsByName.Remove(operation.Name);
                    _characterIdsByName[previousName] = anchor.Id;
                }
                anchor.UpdateName(previousName);
            });
            if (anchor.Type == AnchorType.Character)
            {
                _characterIdsByName.Remove(previousName);
                _characterIdsByName.Add(operation.Name, anchor.Id);
            }
            anchor.UpdateName(operation.Name);
            transaction.RecordApplied(operation, [new UpdateAnchorNameOperation(anchor.Id, previousName)]);
        }

        private void ApplyUpdateAnchorDescription(UpdateAnchorDescriptionOperation operation, WorldTransaction transaction)
        {
            const string operationName = nameof(UpdateAnchorDescriptionOperation);
            Anchor anchor = GetAnchorForOperation(operation.AnchorId, operationName, "Anchor");
            if (string.Equals(anchor.Description, operation.Description, StringComparison.Ordinal))
                return;
            string previousDescription = anchor.Description;
            transaction.RecordRollback(() => anchor.UpdateDescription(previousDescription));
            anchor.UpdateDescription(operation.Description);
            transaction.RecordApplied(operation, [new UpdateAnchorDescriptionOperation(anchor.Id, previousDescription)]);
        }

        private void ApplyUpdateAnchorType(UpdateAnchorTypeOperation operation, WorldTransaction transaction)
        {
            const string operationName = nameof(UpdateAnchorTypeOperation);
            EnsureAnchorType(operation.Type, operationName);
            Anchor anchor = GetAnchorForOperation(operation.AnchorId, operationName, "Anchor");
            if (anchor.Type == operation.Type)
                return;
            AnchorType previousType = anchor.Type;
            bool becomesCharacter = previousType != AnchorType.Character && operation.Type == AnchorType.Character;
            bool stopsBeingCharacter = previousType == AnchorType.Character && operation.Type != AnchorType.Character;
            if (becomesCharacter && _characterIdsByName.TryGetValue(anchor.Name, out Guid duplicateId))
                throw new WorldException(WorldErrorCode.Duplicate, operationName, $"Character 名称“{anchor.Name}”已被 Anchor {duplicateId} 使用。", anchor.Id);
            if (stopsBeingCharacter && FindSubWorld(anchor.Id) is { } subWorld)
                throw new WorldException(WorldErrorCode.InvalidOperation, operationName, $"Character 持有子世界 {subWorld.Id}，请先显式删除该子世界。", anchor.Id);
            transaction.RecordRollback(() =>
            {
                if (becomesCharacter)
                    _characterIdsByName.Remove(anchor.Name);
                if (stopsBeingCharacter)
                    _characterIdsByName[anchor.Name] = anchor.Id;
                anchor.UpdateType(previousType);
            });
            if (becomesCharacter)
                _characterIdsByName.Add(anchor.Name, anchor.Id);
            if (stopsBeingCharacter)
                _characterIdsByName.Remove(anchor.Name);
            anchor.UpdateType(operation.Type);
            transaction.RecordApplied(operation, [new UpdateAnchorTypeOperation(anchor.Id, previousType)]);
        }

        private void ApplyAddRelation(AddRelationOperation operation, WorldTransaction transaction)
        {
            const string operationName = nameof(AddRelationOperation);
            EnsureId(operation.RelationId, operationName, nameof(operation.RelationId));
            EnsureName(operation.Name, operationName, nameof(operation.Name));
            GetAnchorForOperation(operation.SourceId, operationName, "Source Anchor");
            GetAnchorForOperation(operation.TargetId, operationName, "Target Anchor");
            if (ContainsRelation(operation.RelationId))
                throw new WorldException(WorldErrorCode.Duplicate, operationName, $"Relation {operation.RelationId} 已存在。", operation.RelationId);
            SubWorldSnapshot? createdSubWorld = null;
            Dictionary<Guid, Relation> owner = _data.Relations;
            if (operation.DomainId.HasValue)
            {
                EnsureCharacter(operation.DomainId.Value, operationName);
                SubWorldSnapshot? existingSubWorld = FindSubWorld(operation.DomainId.Value);
                createdSubWorld = existingSubWorld is null ? new SubWorldSnapshot { Id = Guid.NewGuid(), DomainId = operation.DomainId.Value } : null;
                owner = (existingSubWorld ?? createdSubWorld!).Relations;
            }
            var relation = new Relation(operation.RelationId, operation.Name, operation.Description, operation.SourceId, operation.TargetId);
            transaction.RecordRollback(() =>
            {
                owner.Remove(relation.Id);
                if (createdSubWorld is not null)
                    _data.SubWorlds.Remove(createdSubWorld);
            });
            if (createdSubWorld is not null)
                _data.SubWorlds.Add(createdSubWorld);
            owner.Add(relation.Id, relation);
            WorldOperation[] forward = createdSubWorld is null ? [operation] : [new CreateSubWorldOperation(createdSubWorld.Id, createdSubWorld.DomainId), operation];
            WorldOperation[] inverse = createdSubWorld is null ? [new RemoveRelationOperation(relation.Id)] : [new RemoveRelationOperation(relation.Id), new RemoveSubWorldOperation(createdSubWorld.DomainId)];
            transaction.RecordApplied(forward, inverse);
        }

        private void ApplyRemoveRelation(RemoveRelationOperation operation, WorldTransaction transaction)
        {
            const string operationName = nameof(RemoveRelationOperation);
            RelationLocation location = FindRelation(operation.RelationId, operationName);
            transaction.RecordRollback(() => location.Owner[location.Relation.Id] = location.Relation);
            location.Owner.Remove(location.Relation.Id);
            transaction.RecordApplied(operation, [ToAddRelationOperation(location)]);
        }

        private void ApplyUpdateRelationName(UpdateRelationNameOperation operation, WorldTransaction transaction)
        {
            const string operationName = nameof(UpdateRelationNameOperation);
            EnsureName(operation.Name, operationName, nameof(operation.Name));
            Relation relation = FindRelation(operation.RelationId, operationName).Relation;
            if (string.Equals(relation.Name, operation.Name, StringComparison.Ordinal))
                return;
            string previousName = relation.Name;
            transaction.RecordRollback(() => relation.UpdateName(previousName));
            relation.UpdateName(operation.Name);
            transaction.RecordApplied(operation, [new UpdateRelationNameOperation(relation.Id, previousName)]);
        }

        private void ApplyUpdateRelationDescription(UpdateRelationDescriptionOperation operation, WorldTransaction transaction)
        {
            const string operationName = nameof(UpdateRelationDescriptionOperation);
            Relation relation = FindRelation(operation.RelationId, operationName).Relation;
            if (string.Equals(relation.Description, operation.Description, StringComparison.Ordinal))
                return;
            string previousDescription = relation.Description;
            transaction.RecordRollback(() => relation.UpdateDescription(previousDescription));
            relation.UpdateDescription(operation.Description);
            transaction.RecordApplied(operation, [new UpdateRelationDescriptionOperation(relation.Id, previousDescription)]);
        }

        private void ApplyCreateSubWorld(CreateSubWorldOperation operation, WorldTransaction transaction)
        {
            const string operationName = nameof(CreateSubWorldOperation);
            EnsureId(operation.SubWorldId, operationName, nameof(operation.SubWorldId));
            EnsureCharacter(operation.CharacterId, operationName);
            if (_data.SubWorlds.Any(subWorld => subWorld.Id == operation.SubWorldId))
                throw new WorldException(WorldErrorCode.Duplicate, operationName, $"SubWorld {operation.SubWorldId} 已存在。", operation.SubWorldId);
            if (FindSubWorld(operation.CharacterId) is { } duplicate)
                throw new WorldException(WorldErrorCode.Duplicate, operationName, $"Character {operation.CharacterId} 已持有子世界 {duplicate.Id}。", operation.CharacterId);
            var subWorld = new SubWorldSnapshot { Id = operation.SubWorldId, DomainId = operation.CharacterId };
            transaction.RecordRollback(() => _data.SubWorlds.Remove(subWorld));
            _data.SubWorlds.Add(subWorld);
            transaction.RecordApplied(operation, [new RemoveSubWorldOperation(operation.CharacterId)]);
        }

        private void ApplyRemoveSubWorld(RemoveSubWorldOperation operation, WorldTransaction transaction)
        {
            const string operationName = nameof(RemoveSubWorldOperation);
            EnsureCharacter(operation.CharacterId, operationName);
            SubWorldSnapshot subWorld = FindSubWorld(operation.CharacterId) ?? throw NotFound(operationName, "SubWorldSnapshot", operation.CharacterId);
            transaction.RecordRollback(() =>
            {
                if (!_data.SubWorlds.Contains(subWorld))
                    _data.SubWorlds.Add(subWorld);
            });
            _data.SubWorlds.Remove(subWorld);
            var inverse = new List<WorldOperation> { new CreateSubWorldOperation(subWorld.Id, subWorld.DomainId) };
            inverse.AddRange(subWorld.Relations.Values.Select(relation => new AddRelationOperation(relation.Id, relation.Name, relation.Description, relation.SourceId, relation.TargetId, subWorld.DomainId)));
            transaction.RecordApplied(operation, inverse);
        }

        private IEnumerable<Relation> EnumerateAllRelations()
        {
            foreach (Relation relation in _data.Relations.Values)
                yield return relation;
            foreach (SubWorldSnapshot subWorld in _data.SubWorlds)
            {
                foreach (Relation relation in subWorld.Relations.Values)
                    yield return relation;
            }
        }

        private IEnumerable<RelationLocation> EnumerateRelationLocations()
        {
            foreach (Relation relation in _data.Relations.Values)
                yield return new RelationLocation(_data.Relations, relation, null);
            foreach (SubWorldSnapshot subWorld in _data.SubWorlds)
            {
                foreach (Relation relation in subWorld.Relations.Values)
                    yield return new RelationLocation(subWorld.Relations, relation, subWorld.DomainId);
            }
        }

        private bool ContainsRelation(Guid relationId)
        {
            return _data.Relations.ContainsKey(relationId) || _data.SubWorlds.Any(subWorld => subWorld.Relations.ContainsKey(relationId));
        }

        private static AddRelationOperation ToAddRelationOperation(RelationLocation location)
        {
            Relation relation = location.Relation;
            return new AddRelationOperation(relation.Id, relation.Name, relation.Description, relation.SourceId, relation.TargetId, location.DomainId);
        }

        private Anchor GetAnchorForOperation(Guid anchorId, string operation, string entityName)
        {
            EnsureId(anchorId, operation, nameof(anchorId));
            if (_data.Anchors.TryGetValue(anchorId, out var anchor))
                return anchor;
            throw NotFound(operation, entityName, anchorId);
        }

        private Anchor EnsureCharacter(Guid characterId, string operation)
        {
            Anchor anchor = GetAnchorForOperation(characterId, operation, "Domain Anchor");
            if (anchor.Type != AnchorType.Character)
            {
                throw new WorldException(
                    WorldErrorCode.InvalidOperation,
                    operation,
                    $"Anchor {characterId} 的类型是 {anchor.Type}，只有 Character 可以持有子世界。",
                    characterId
                );
            }

            return anchor;
        }

        private SubWorldSnapshot? FindSubWorld(Guid characterId)
        {
            return _data.SubWorlds.Find(subWorld => subWorld.DomainId == characterId);
        }

        private RelationLocation FindRelation(Guid relationId, string operation)
        {
            EnsureId(relationId, operation, nameof(relationId));
            if (_data.Relations.TryGetValue(relationId, out Relation? relation))
                return new RelationLocation(_data.Relations, relation, null);
            foreach (SubWorldSnapshot subWorld in _data.SubWorlds)
            {
                if (subWorld.Relations.TryGetValue(relationId, out relation))
                    return new RelationLocation(subWorld.Relations, relation, subWorld.DomainId);
            }

            throw NotFound(operation, "Relation", relationId);
        }

        private static void EnsureId(Guid id, string operation, string parameterName)
        {
            if (id == Guid.Empty)
            {
                throw new WorldException(
                    WorldErrorCode.InvalidArgument,
                    operation,
                    $"参数 {parameterName} 不能是空 Guid。"
                );
            }
        }

        private static void EnsureName(string value, string operation, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new WorldException(
                    WorldErrorCode.InvalidArgument,
                    operation,
                    $"参数 {parameterName} 不能为空或只包含空白字符。"
                );
            }
        }

        private static void EnsureAnchorType(AnchorType type, string operation)
        {
            if (!Enum.IsDefined(type))
            {
                throw new WorldException(
                    WorldErrorCode.InvalidArgument,
                    operation,
                    $"AnchorType 值 {Convert.ToInt32(type)} 未定义。"
                );
            }
        }

        private static WorldException NotFound(string operation, string entityName, Guid entityId)
        {
            return new WorldException(
                WorldErrorCode.NotFound,
                operation,
                $"未找到 {entityName}。",
                entityId
            );
        }

        private static void ValidateWorldSnapshot(WorldSnapshot data)
        {
            const string operation = nameof(Create);
            var errors = new List<string>();
            if (data.Id == Guid.Empty)
                errors.Add("WorldSnapshot.Id 不能是空 Guid。");

            Dictionary<Guid, Anchor>? anchors = data.Anchors;
            Dictionary<Guid, Relation> worldRelations = data.Relations;
            List<SubWorldSnapshot> subWorlds = data.SubWorlds;

            var characterNames = new Dictionary<string, Guid>(StringComparer.Ordinal);
            foreach ((Guid key, Anchor anchor) in anchors)
            {
                string path = $"Anchors[{key}]";
                ValidateAnchor(key, anchor, path, characterNames, errors);
            }

            var relationIds = new HashSet<Guid>();
            ValidateRelations(worldRelations, "Relations", anchors, relationIds, errors);

            var subWorldIds = new HashSet<Guid>();
            var domainIds = new HashSet<Guid>();
            for (int index = 0; index < subWorlds.Count; index++)
            {
                SubWorldSnapshot subWorld = subWorlds[index];
                string path = $"SubWorlds[{index}]";
                if (subWorld.Id == Guid.Empty)
                    errors.Add($"{path}.Id 不能是空 Guid。");
                else if (!subWorldIds.Add(subWorld.Id))
                    errors.Add($"{path}.Id {subWorld.Id} 重复。");
                if (subWorld.DomainId == Guid.Empty)
                {
                    errors.Add($"{path}.DomainId 不能是空 Guid。");
                }
                else
                {
                    if (!domainIds.Add(subWorld.DomainId))
                    {
                        errors.Add(
                            $"{path}.DomainId {subWorld.DomainId} 重复，一个 Character 最多只能持有一个子世界。"
                        );
                    }

                    if (anchors is null || !anchors.TryGetValue(subWorld.DomainId, out Anchor? domain))
                    {
                        errors.Add(
                            $"{path}.DomainId {subWorld.DomainId} 不指向任何 Anchor。"
                        );
                    }
                    else if (domain.Type != AnchorType.Character)
                    {
                        errors.Add(
                            $"{path}.DomainId {subWorld.DomainId} 指向类型 {domain.Type}，只有 Character 可以持有子世界。"
                        );
                    }
                }

                ValidateRelations(subWorld.Relations, $"{path}.Relations", anchors, relationIds, errors);
            }

            if (errors.Count > 0)
            {
                throw new WorldException(
                    WorldErrorCode.InvalidWorldSnapshot,
                    operation,
                    $"WorldSnapshot 初始化校验发现 {errors.Count} 个错误。",
                    validationErrors: errors
                );
            }
        }

        private static void ValidateAnchor(Guid key, Anchor anchor, string path,
            Dictionary<string, Guid> characterNames, List<string> errors)
        {
            if (key == Guid.Empty)
                errors.Add($"{path} 的字典键不能是空 Guid。");
            if (anchor.Id == Guid.Empty)
                errors.Add($"{path}.Id 不能是空 Guid。");
            if (key != anchor.Id)
            {
                errors.Add($"{path} 的字典键与 Anchor.Id {anchor.Id} 不一致。");
            }

            if (string.IsNullOrWhiteSpace(anchor.Name))
                errors.Add($"{path}.Name 不能为空或只包含空白字符。");
            if (!Enum.IsDefined(anchor.Type))
                errors.Add($"{path}.Type 值 {Convert.ToInt32(anchor.Type)} 未定义。");

            if (anchor.Type == AnchorType.Character && !string.IsNullOrWhiteSpace(anchor.Name))
            {
                if (characterNames.TryGetValue(anchor.Name, out Guid duplicateId))
                {
                    errors.Add($"{path}.Name“{anchor.Name}”与 Character {duplicateId} 重复。");
                }
                else
                {
                    characterNames.Add(anchor.Name, anchor.Id);
                }
            }
        }

        private static void ValidateRelations(Dictionary<Guid, Relation> relations, string path,
            Dictionary<Guid, Anchor>? anchors, HashSet<Guid> relationIds, List<string> errors)
        {
            foreach ((Guid key, Relation relation) in relations)
            {
                string relationPath = $"{path}[{key}]";

                if (key == Guid.Empty)
                    errors.Add($"{relationPath} 的字典键不能是空 Guid。");
                if (relation.Id == Guid.Empty)
                    errors.Add($"{relationPath}.Id 不能是空 Guid。");
                if (key != relation.Id)
                {
                    errors.Add($"{relationPath} 的字典键与 Relation.Id {relation.Id} 不一致。");
                }

                if (relation.Id != Guid.Empty && !relationIds.Add(relation.Id))
                {
                    errors.Add($"{relationPath}.Id {relation.Id} 在世界图中重复。");
                }

                if (string.IsNullOrWhiteSpace(relation.Name))
                    errors.Add($"{relationPath}.Name 不能为空或只包含空白字符。");
                if (anchors is null || !anchors.ContainsKey(relation.SourceId))
                {
                    errors.Add($"{relationPath}.SourceId {relation.SourceId} 不指向任何 Anchor。");
                }

                if (anchors is null || !anchors.ContainsKey(relation.TargetId))
                {
                    errors.Add($"{relationPath}.TargetId {relation.TargetId} 不指向任何 Anchor。");
                }
            }
        }

        private static WorldSnapshot CloneWorldSnapshot(WorldSnapshot source)
        {
            return new WorldSnapshot
            {
                Id = source.Id,
                Anchors = source.Anchors.ToDictionary(
                    pair => pair.Key,
                    pair => CloneAnchor(pair.Value)
                ),
                Relations = source.Relations.ToDictionary(
                    pair => pair.Key,
                    pair => CloneRelation(pair.Value)
                ),
                SubWorlds = source.SubWorlds
                    .Select(CloneSubWorldSnapshot)
                    .ToList()
            };
        }

        private static Anchor CloneAnchor(Anchor source)
        {
            return new Anchor(
                source.Id,
                source.Name,
                source.Description,
                source.Type
            );
        }

        private static Relation CloneRelation(Relation source)
        {
            return new Relation(
                source.Id,
                source.Name,
                source.Description,
                source.SourceId,
                source.TargetId
            );
        }

        private static SubWorldSnapshot CloneSubWorldSnapshot(SubWorldSnapshot source)
        {
            return new SubWorldSnapshot
            {
                Id = source.Id,
                DomainId = source.DomainId,
                Relations = source.Relations.ToDictionary(
                    pair => pair.Key,
                    pair => CloneRelation(pair.Value)
                )
            };
        }

        private sealed record RelationLocation(
            Dictionary<Guid, Relation> Owner,
            Relation Relation,
            Guid? DomainId
        );

        /// <summary>
        /// 事务日志只保存精确的内部恢复动作；它不经过领域校验，不触发事件，也不改变 revision。
        /// </summary>
        private sealed class WorldTransaction
        {
            private readonly Stack<Action> _rollback = new();
            private readonly List<WorldOperation> _forward = [];
            private readonly List<WorldOperation> _inverse = [];

            internal bool HasChanges => _forward.Count > 0;

            internal void RecordRollback(Action rollback)
            {
                _rollback.Push(rollback);
            }

            internal void RecordApplied(WorldOperation forward, IEnumerable<WorldOperation> inverse)
            {
                RecordApplied([forward], inverse);
            }

            internal void RecordApplied(IEnumerable<WorldOperation> forward, IEnumerable<WorldOperation> inverse)
            {
                _forward.AddRange(forward);
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

            internal AppliedWorldChangeSet CreateAppliedChangeSet()
            {
                return new AppliedWorldChangeSet(new WorldChangeSet(_forward), new WorldChangeSet(_inverse));
            }
        }
    }

    internal sealed record WorldApplyResult(long PreviousRevision, long Revision, AppliedWorldChangeSet? ChangeSet)
    {
        internal bool Changed => ChangeSet is not null;
        internal static WorldApplyResult Unchanged(long revision) => new(revision, revision, null);
    }
}
