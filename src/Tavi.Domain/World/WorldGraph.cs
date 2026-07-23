namespace Tavi.Domain.World
{
    /// <summary>
    /// 运行时世界图。
    /// 负责基于 Id 和图结构的查询，并维护世界图的领域不变量。
    /// </summary>
    public sealed class WorldGraph
    {
        private readonly WorldData _data;
        private readonly Dictionary<string, Guid> _characterIdsByName;

        private WorldGraph(WorldData data)
        {
            _data = CloneWorldData(data);
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
        public static WorldGraph Create(WorldData data)
        {
            ValidateWorldData(data);
            return new WorldGraph(data);
        }

        /// <summary>
        /// 创建当前世界图的独立、可序列化快照。
        /// </summary>
        public WorldData CreateSnapshot()
        {
            return CloneWorldData(_data);
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
        public SubWorldData GetSubWorld(Guid characterId)
        {
            EnsureCharacter(characterId, nameof(GetSubWorld));
            SubWorldData subWorld = FindSubWorld(characterId) ??
                                    throw NotFound(nameof(GetSubWorld), "SubWorldData", characterId);
            return CloneSubWorldData(subWorld);
        }

        /// <summary>
        /// 获取全部子世界快照。
        /// </summary>
        public IReadOnlyCollection<SubWorldData> GetSubWorlds()
        {
            return _data.SubWorldData
                .Select(CloneSubWorldData)
                .ToArray();
        }

        /// <summary>
        /// 获取指定 Character 子世界中的全部 Relation。
        /// </summary>
        public IReadOnlyCollection<Relation> GetSubWorldRelations(Guid characterId)
        {
            EnsureCharacter(characterId, nameof(GetSubWorldRelations));
            SubWorldData subWorld = FindSubWorld(characterId) ??
                                    throw NotFound(nameof(GetSubWorldRelations), "SubWorldData", characterId);
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
                throw new WorldGraphException(WorldGraphErrorCode.Duplicate, operation,
                    $"Character 名称“{name}”已被 Anchor {duplicateId} 使用。");
            }

            Anchor anchor = Anchor.Create(name, description, type);
            _data.Anchors.Add(anchor.Id, anchor);
            if (type == AnchorType.Character)
                _characterIdsByName.Add(anchor.Name, anchor.Id);
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
                return relation.Id;
            }

            EnsureCharacter(domainId.Value, operation);
            SubWorldData subWorld = FindSubWorld(domainId.Value) ?? CreateSubWorldData(domainId.Value);
            subWorld.Relations.Add(relation.Id, relation);
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
                throw new WorldGraphException(
                    WorldGraphErrorCode.Duplicate,
                    operation,
                    $"Character {characterId} 已持有子世界 {duplicate.Id}。",
                    characterId
                );
            }
            return CreateSubWorldData(characterId).Id;
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
                RemoveRelation(relationId);

            if (anchor.Type == AnchorType.Character)
            {
                _characterIdsByName.Remove(anchor.Name);
                SubWorldData? subWorld = FindSubWorld(anchorId);
                if (subWorld is not null)
                    _data.SubWorldData.Remove(subWorld);
            }
            _data.Anchors.Remove(anchorId);
        }

        /// <summary>
        /// 删除主世界或子世界中的 Relation。
        /// </summary>
        public void RemoveRelation(Guid relationId)
        {
            RelationLocation location = FindRelation(relationId, nameof(RemoveRelation));
            location.Owner.Remove(relationId);
        }

        /// <summary>
        /// 删除指定 Character 持有的子世界及其中的 Relation。
        /// </summary>
        public void RemoveSubWorld(Guid characterId)
        {
            const string operation = nameof(RemoveSubWorld);
            EnsureCharacter(characterId, operation);
            SubWorldData subWorld = FindSubWorld(characterId) ??
                                    throw NotFound(operation, "SubWorldData", characterId);
            _data.SubWorldData.Remove(subWorld);
        }

        /// <summary>
        /// 更新 Anchor 名称，并维护 Character 名称唯一性。
        /// </summary>
        public void UpdateAnchorName(Guid anchorId, string name)
        {
            const string operation = nameof(UpdateAnchorName);
            EnsureName(name, operation, nameof(name));
            Anchor anchor = GetAnchorForOperation(anchorId, operation, "Anchor");
            if (anchor.Type == AnchorType.Character
                && _characterIdsByName.TryGetValue(name, out Guid duplicateId)
                && duplicateId != anchorId)
            {
                throw new WorldGraphException(
                    WorldGraphErrorCode.Duplicate,
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
        }

        /// <summary>
        /// 更新 Anchor 描述。
        /// </summary>
        public void UpdateAnchorDescription(Guid anchorId, string description)
        {
            const string operation = nameof(UpdateAnchorDescription);
            Anchor anchor = GetAnchorForOperation(anchorId, operation, "Anchor");
            anchor.UpdateDescription(description);
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
                throw new WorldGraphException(
                    WorldGraphErrorCode.Duplicate,
                    operation,
                    $"Character 名称“{anchor.Name}”已被 Anchor {duplicateId} 使用。",
                    anchorId
                );
            }
            if (stopsBeingCharacter && FindSubWorld(anchorId) is { } subWorld)
            {
                throw new WorldGraphException(
                    WorldGraphErrorCode.InvalidOperation,
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
        }

        /// <summary>
        /// 更新 Relation 名称。其它 Relation 结构变化应删除后重建。
        /// </summary>
        public void UpdateRelationName(Guid relationId, string name)
        {
            const string operation = nameof(UpdateRelationName);
            FindRelation(relationId, operation).Relation.UpdateName(name);
        }

        /// <summary>
        /// 更新 Relation 描述。其它 Relation 结构变化应删除后重建。
        /// </summary>
        public void UpdateRelationDescription(Guid relationId, string description)
        {
            const string operation = nameof(UpdateRelationDescription);
            FindRelation(relationId, operation).Relation.UpdateDescription(description);
        }

        private IEnumerable<Relation> EnumerateAllRelations()
        {
            foreach (Relation relation in _data.Relations.Values)
                yield return relation;
            foreach (SubWorldData subWorld in _data.SubWorldData)
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
                throw new WorldGraphException(
                    WorldGraphErrorCode.InvalidOperation,
                    operation,
                    $"Anchor {characterId} 的类型是 {anchor.Type}，只有 Character 可以持有子世界。",
                    characterId
                );
            }
            return anchor;
        }

        private SubWorldData? FindSubWorld(Guid characterId)
        {
            return _data.SubWorldData.Find(subWorld => subWorld.DomainId == characterId);
        }

        private SubWorldData CreateSubWorldData(Guid characterId)
        {
            var subWorld = new SubWorldData
            {
                Id = Guid.NewGuid(),
                DomainId = characterId
            };
            _data.SubWorldData.Add(subWorld);
            return subWorld;
        }

        private RelationLocation FindRelation(Guid relationId, string operation)
        {
            EnsureId(relationId, operation, nameof(relationId));
            if (_data.Relations.TryGetValue(relationId, out Relation? relation))
                return new RelationLocation(_data.Relations, relation);
            foreach (SubWorldData subWorld in _data.SubWorldData)
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
                throw new WorldGraphException(
                    WorldGraphErrorCode.InvalidArgument,
                    operation,
                    $"参数 {parameterName} 不能是空 Guid。"
                );
            }
        }

        private static void EnsureName(string value, string operation, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new WorldGraphException(
                    WorldGraphErrorCode.InvalidArgument,
                    operation,
                    $"参数 {parameterName} 不能为空或只包含空白字符。"
                );
            }
        }

        private static void EnsureAnchorType(AnchorType type, string operation)
        {
            if (!Enum.IsDefined(type))
            {
                throw new WorldGraphException(
                    WorldGraphErrorCode.InvalidArgument,
                    operation,
                    $"AnchorType 值 {Convert.ToInt32(type)} 未定义。"
                );
            }
        }

        private static WorldGraphException NotFound(string operation, string entityName, Guid entityId)
        {
            return new WorldGraphException(
                WorldGraphErrorCode.NotFound,
                operation,
                $"未找到 {entityName}。",
                entityId
            );
        }

        private static void ValidateWorldData(WorldData data)
        {
            const string operation = nameof(Create);
            var errors = new List<string>();
            if (data.Id == Guid.Empty)
                errors.Add("WorldData.Id 不能是空 Guid。");

            Dictionary<Guid, Anchor>? anchors = data.Anchors;
            if (anchors is null)
                errors.Add("WorldData.Anchors 不能为 null。");
            Dictionary<Guid, Relation>? worldRelations = data.Relations;
            if (worldRelations is null)
                errors.Add("WorldData.Relations 不能为 null。");
            List<SubWorldData>? subWorlds = data.SubWorldData;
            if (subWorlds is null)
                errors.Add("WorldData.SubWorldData 不能为 null。");

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
                    SubWorldData? subWorld = subWorlds[index];
                    string path = $"SubWorldData[{index}]";
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
                throw new WorldGraphException(
                    WorldGraphErrorCode.InvalidWorldData,
                    operation,
                    $"WorldData 初始化校验发现 {errors.Count} 个错误。",
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

        private static WorldData CloneWorldData(WorldData source)
        {
            return new WorldData
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
                SubWorldData = source.SubWorldData
                    .Select(CloneSubWorldData)
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

        private static SubWorldData CloneSubWorldData(SubWorldData source)
        {
            return new SubWorldData
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
