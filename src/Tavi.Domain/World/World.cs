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
        /// 获取当前运行时世界版本；每次实际修改成功后递增。
        /// </summary>
        public long Revision { get; private set; }

        /// <summary>
        /// 在运行时世界发生实际修改后触发。
        /// </summary>
        public event EventHandler<WorldChangedEventArgs>? Changed;

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
                return anchor;
            throw NotFound(nameof(GetAnchor), "Anchor", anchorId);
        }

        /// <summary>
        /// 根据 Id 获取主世界或任一子世界中的 Relation。
        /// </summary>
        public Relation GetRelation(Guid relationId)
        {
            return FindRelation(relationId, nameof(GetRelation)).Relation;
        }

        /// <summary>
        /// 获取全部 Anchor。
        /// </summary>
        public IReadOnlyCollection<Anchor> GetAnchors()
        {
            return _data.Anchors.Values.ToArray();
        }

        /// <summary>
        /// 获取全部 Character 类型的 Anchor。
        /// </summary>
        public IReadOnlyCollection<Anchor> GetCharacters()
        {
            return _characterIdsByName.Values
                .Select(characterId => _data.Anchors[characterId])
                .ToArray();
        }

        /// <summary>
        /// 获取全部主世界 Relation。
        /// </summary>
        public IReadOnlyCollection<Relation> GetWorldRelations()
        {
            return _data.Relations.Values.ToArray();
        }

        /// <summary>
        /// 获取所有以指定 Anchor 为目标的 Relation。
        /// </summary>
        public IReadOnlyCollection<Relation> GetIncomingRelations(Guid anchorId)
        {
            GetAnchor(anchorId);
            return EnumerateAllRelations()
                .Where(relation => relation.TargetId == anchorId)
                .ToArray();
        }

        /// <summary>
        /// 获取所有以指定 Anchor 为来源的 Relation。
        /// </summary>
        public IReadOnlyCollection<Relation> GetOutgoingRelations(Guid anchorId)
        {
            GetAnchor(anchorId);
            return EnumerateAllRelations()
                .Where(relation => relation.SourceId == anchorId)
                .ToArray();
        }

        /// <summary>
        /// 获取所有与指定 Anchor 相连的 Relation。
        /// </summary>
        public IReadOnlyCollection<Relation> GetRelations(Guid anchorId)
        {
            GetAnchor(anchorId);
            return EnumerateAllRelations()
                .Where(relation => relation.SourceId == anchorId || relation.TargetId == anchorId)
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
            return subWorld.Relations.Values.ToArray();
        }

        /// <summary>
        /// 添加 Anchor 并返回其 Id。
        /// </summary>
        public Guid AddAnchor(string name, string description, AnchorType type)
        {
            const string operation = nameof(AddAnchor);
            EnsureName(name, operation, nameof(name));
            EnsureAnchorType(type, operation);
            if (type == AnchorType.Character && _characterIdsByName.TryGetValue(name, out Guid duplicateId))
            {
                throw new WorldException(WorldErrorCode.Duplicate, operation,
                    $"Character 名称“{name}”已被 Anchor {duplicateId} 使用。");
            }

            Anchor anchor = Anchor.Create(name, description, type);
            _data.Anchors.Add(anchor.Id, anchor);
            if (type == AnchorType.Character)
                _characterIdsByName.Add(anchor.Name, anchor.Id);
            MarkChanged(operation);
            return anchor.Id;
        }

        /// <summary>
        /// 添加主世界或指定 Character 子世界的 Relation，并返回其 Id。
        /// </summary>
        public Guid AddRelation(string name, string description, Guid sourceId, Guid targetId, Guid? domainId = null)
        {
            const string operation = nameof(AddRelation);
            EnsureName(name, operation, nameof(name));
            GetAnchorForOperation(sourceId, operation, "Source Anchor");
            GetAnchorForOperation(targetId, operation, "Target Anchor");

            Relation relation = Relation.Create(name, description, sourceId, targetId);
            if (!domainId.HasValue)
            {
                _data.Relations.Add(relation.Id, relation);
                MarkChanged(operation);
                return relation.Id;
            }

            EnsureCharacter(domainId.Value, operation);
            SubWorldSnapshot subWorld = FindSubWorld(domainId.Value) ?? CreateSubWorldSnapshot(domainId.Value);
            subWorld.Relations.Add(relation.Id, relation);
            MarkChanged(operation);
            return relation.Id;
        }

        /// <summary>
        /// 为指定 Character 创建子世界并返回子世界 Id。
        /// </summary>
        public Guid CreateSubWorld(Guid characterId)
        {
            const string operation = nameof(CreateSubWorld);
            EnsureCharacter(characterId, operation);
            if (FindSubWorld(characterId) is { } duplicate)
            {
                throw new WorldException(
                    WorldErrorCode.Duplicate,
                    operation,
                    $"Character {characterId} 已持有子世界 {duplicate.Id}。",
                    characterId
                );
            }
            Guid subWorldId = CreateSubWorldSnapshot(characterId).Id;
            MarkChanged(operation);
            return subWorldId;
        }

        /// <summary>
        /// 删除 Anchor、相连 Relation，以及该 Character 持有的子世界。
        /// </summary>
        public void RemoveAnchor(Guid anchorId)
        {
            const string operation = nameof(RemoveAnchor);
            Anchor anchor = GetAnchorForOperation(anchorId, operation, "Anchor");

            Guid[] connectedRelationIds = EnumerateAllRelations()
                .Where(relation => relation.SourceId == anchorId || relation.TargetId == anchorId)
                .Select(relation => relation.Id)
                .ToArray();
            foreach (Guid relationId in connectedRelationIds)
                RemoveRelationCore(relationId, operation);

            if (anchor.Type == AnchorType.Character)
            {
                _characterIdsByName.Remove(anchor.Name);
                SubWorldSnapshot? subWorld = FindSubWorld(anchorId);
                if (subWorld is not null)
                    _data.SubWorlds.Remove(subWorld);
            }
            _data.Anchors.Remove(anchorId);
            MarkChanged(operation);
        }

        /// <summary>
        /// 删除主世界或子世界中的 Relation。
        /// </summary>
        public void RemoveRelation(Guid relationId)
        {
            const string operation = nameof(RemoveRelation);
            RemoveRelationCore(relationId, operation);
            MarkChanged(operation);
        }

        /// <summary>
        /// 删除指定 Character 持有的子世界及其中的 Relation。
        /// </summary>
        public void RemoveSubWorld(Guid characterId)
        {
            const string operation = nameof(RemoveSubWorld);
            EnsureCharacter(characterId, operation);
            SubWorldSnapshot subWorld = FindSubWorld(characterId) ??
                                        throw NotFound(operation, "SubWorldSnapshot", characterId);
            _data.SubWorlds.Remove(subWorld);
            MarkChanged(operation);
        }

        /// <summary>
        /// 更新 Anchor 名称，并维护 Character 名称唯一性。
        /// </summary>
        public void UpdateAnchorName(Guid anchorId, string name)
        {
            const string operation = nameof(UpdateAnchorName);
            EnsureName(name, operation, nameof(name));
            Anchor anchor = GetAnchorForOperation(anchorId, operation, "Anchor");
            if (string.Equals(anchor.Name, name, StringComparison.Ordinal))
                return;
            if (anchor.Type == AnchorType.Character
                && _characterIdsByName.TryGetValue(name, out Guid duplicateId)
                && duplicateId != anchorId)
            {
                throw new WorldException(
                    WorldErrorCode.Duplicate,
                    operation,
                    $"Character 名称“{name}”已被 Anchor {duplicateId} 使用。",
                    anchorId
                );
            }

            if (anchor.Type == AnchorType.Character)
            {
                _characterIdsByName.Remove(anchor.Name);
                _characterIdsByName.Add(name, anchor.Id);
            }
            anchor.UpdateName(name);
            MarkChanged(operation);
        }

        /// <summary>
        /// 更新 Anchor 描述。
        /// </summary>
        public void UpdateAnchorDescription(Guid anchorId, string description)
        {
            const string operation = nameof(UpdateAnchorDescription);
            Anchor anchor = GetAnchorForOperation(anchorId, operation, "Anchor");
            if (string.Equals(anchor.Description, description, StringComparison.Ordinal))
                return;
            anchor.UpdateDescription(description);
            MarkChanged(operation);
        }

        /// <summary>
        /// 更新 Anchor 类型，并维护 Character 的特殊领域约束。
        /// </summary>
        public void UpdateAnchorType(Guid anchorId, AnchorType type)
        {
            const string operation = nameof(UpdateAnchorType);
            EnsureAnchorType(type, operation);
            Anchor anchor = GetAnchorForOperation(anchorId, operation, "Anchor");
            if (anchor.Type == type)
                return;

            bool becomesCharacter = anchor.Type != AnchorType.Character && type == AnchorType.Character;
            bool stopsBeingCharacter = anchor.Type == AnchorType.Character && type != AnchorType.Character;

            if (becomesCharacter && _characterIdsByName.TryGetValue(anchor.Name, out Guid duplicateId))
            {
                throw new WorldException(
                    WorldErrorCode.Duplicate,
                    operation,
                    $"Character 名称“{anchor.Name}”已被 Anchor {duplicateId} 使用。",
                    anchorId
                );
            }
            if (stopsBeingCharacter && FindSubWorld(anchorId) is { } subWorld)
            {
                throw new WorldException(
                    WorldErrorCode.InvalidOperation,
                    operation,
                    $"Character 持有子世界 {subWorld.Id}，请先显式删除该子世界。",
                    anchorId
                );
            }

            if (becomesCharacter)
                _characterIdsByName.Add(anchor.Name, anchor.Id);
            if (stopsBeingCharacter)
                _characterIdsByName.Remove(anchor.Name);
            anchor.UpdateType(type);
            MarkChanged(operation);
        }

        /// <summary>
        /// 更新 Relation 名称。其它 Relation 结构变化应删除后重建。
        /// </summary>
        public void UpdateRelationName(Guid relationId, string name)
        {
            const string operation = nameof(UpdateRelationName);
            EnsureName(name, operation, nameof(name));
            Relation relation = FindRelation(relationId, operation).Relation;
            if (string.Equals(relation.Name, name, StringComparison.Ordinal))
                return;
            relation.UpdateName(name);
            MarkChanged(operation);
        }

        /// <summary>
        /// 更新 Relation 描述。其它 Relation 结构变化应删除后重建。
        /// </summary>
        public void UpdateRelationDescription(Guid relationId, string description)
        {
            const string operation = nameof(UpdateRelationDescription);
            Relation relation = FindRelation(relationId, operation).Relation;
            if (string.Equals(relation.Description, description, StringComparison.Ordinal))
                return;
            relation.UpdateDescription(description);
            MarkChanged(operation);
        }

        private void RemoveRelationCore(Guid relationId, string operation)
        {
            RelationLocation location = FindRelation(relationId, operation);
            location.Owner.Remove(relationId);
        }

        private void MarkChanged(string operation)
        {
            Revision++;
            Changed?.Invoke(this, new WorldChangedEventArgs(Revision, operation));
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

        private SubWorldSnapshot CreateSubWorldSnapshot(Guid characterId)
        {
            var subWorld = new SubWorldSnapshot
            {
                Id = Guid.NewGuid(),
                DomainId = characterId
            };
            _data.SubWorlds.Add(subWorld);
            return subWorld;
        }

        private RelationLocation FindRelation(Guid relationId, string operation)
        {
            EnsureId(relationId, operation, nameof(relationId));
            if (_data.Relations.TryGetValue(relationId, out Relation? relation))
                return new RelationLocation(_data.Relations, relation);
            foreach (SubWorldSnapshot subWorld in _data.SubWorlds)
            {
                if (subWorld.Relations.TryGetValue(relationId, out relation))
                    return new RelationLocation(subWorld.Relations, relation);
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
            if (anchors is null)
                errors.Add("WorldSnapshot.Anchors 不能为 null。");
            Dictionary<Guid, Relation>? worldRelations = data.Relations;
            if (worldRelations is null)
                errors.Add("WorldSnapshot.Relations 不能为 null。");
            List<SubWorldSnapshot>? subWorlds = data.SubWorlds;
            if (subWorlds is null)
                errors.Add("WorldSnapshot.SubWorlds 不能为 null。");

            var characterNames = new Dictionary<string, Guid>(StringComparer.Ordinal);
            if (anchors is not null)
            {
                foreach ((Guid key, Anchor? anchor) in anchors)
                {
                    string path = $"Anchors[{key}]";
                    if (anchor is null)
                    {
                        errors.Add($"{path} 不能为 null。");
                        continue;
                    }
                    ValidateAnchor(key, anchor, path, characterNames, errors);
                }
            }

            var relationIds = new HashSet<Guid>();
            if (worldRelations is not null)
            {
                ValidateRelations(worldRelations, "Relations", anchors, relationIds, errors);
            }

            var subWorldIds = new HashSet<Guid>();
            var domainIds = new HashSet<Guid>();
            if (subWorlds is not null)
            {
                for (int index = 0; index < subWorlds.Count; index++)
                {
                    SubWorldSnapshot? subWorld = subWorlds[index];
                    string path = $"SubWorlds[{index}]";
                    if (subWorld is null)
                    {
                        errors.Add($"{path} 不能为 null。");
                        continue;
                    }
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

                    if (subWorld.Relations is null)
                    {
                        errors.Add($"{path}.Relations 不能为 null。");
                    }
                    else
                    {
                        ValidateRelations(subWorld.Relations, $"{path}.Relations", anchors, relationIds, errors);
                    }
                }
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
            foreach ((Guid key, Relation? relation) in relations)
            {
                string relationPath = $"{path}[{key}]";
                if (relation is null)
                {
                    errors.Add($"{relationPath} 不能为 null。");
                    continue;
                }
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
            Relation Relation
        );
    }
}
