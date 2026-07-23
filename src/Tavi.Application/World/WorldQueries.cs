using Tavi.Domain.World;
using RuntimeWorld = Tavi.Domain.World.World;

namespace Tavi.Application.World;

/// <summary>
/// 提供绑定到 WorldSession 的只读查询。实例不缓存 World 数据；每个公开查询都会在同一次会话锁内完成并返回已物化副本。
/// </summary>
public sealed class WorldQueries
{
    private readonly WorldSession _session;

    internal WorldQueries(WorldSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <summary>
    /// 创建当前 World 的独立快照。
    /// </summary>
    public WorldSnapshot CreateSnapshot() => _session.ExecuteQuery(world => world.CreateSnapshot());

    /// <summary>
    /// 根据 Id 获取 Anchor 副本。
    /// </summary>
    public Anchor GetAnchor(Guid anchorId) => _session.ExecuteQuery(world => world.GetAnchor(anchorId));

    /// <summary>
    /// 根据 Id 获取 Relation 副本。
    /// </summary>
    public Relation GetRelation(Guid relationId) => _session.ExecuteQuery(world => world.GetRelation(relationId));

    /// <summary>
    /// 获取全部 Anchor 副本。
    /// </summary>
    public IReadOnlyList<Anchor> GetAnchors() => _session.ExecuteQuery(world => world.GetAnchors().ToArray());

    /// <summary>
    /// 获取按名称排序的全部 Character 副本。
    /// </summary>
    public IReadOnlyList<Anchor> GetCharacters() => _session.ExecuteQuery(GetCharactersCore);

    /// <summary>
    /// 按名称查询 Anchor，忽略名称大小写。
    /// </summary>
    public IReadOnlyList<Anchor> FindAnchors(string name) => _session.ExecuteQuery(world => FindAnchorsCore(world, name));

    /// <summary>
    /// 查询同时包含全部字符串线索的 Anchor。
    /// </summary>
    public IReadOnlyList<Anchor> QueryAnchors(IEnumerable<string> clues) => _session.ExecuteQuery(world => QueryAnchorsCore(world, clues));

    /// <summary>
    /// 获取全部事实世界 Relation 及其端点副本。
    /// </summary>
    public IReadOnlyList<ScopedRelation> GetWorldRelations() => _session.ExecuteQuery(GetWorldRelationsCore);

    /// <summary>
    /// 获取指定 Character 子世界中的全部 Relation 及其端点副本。
    /// </summary>
    public IReadOnlyList<ScopedRelation> GetSubWorldRelations(string characterName) => _session.ExecuteQuery(world => GetSubWorldRelationsCore(world, characterName));

    /// <summary>
    /// 获取事实世界和全部角色子世界中的 Relation 及其端点副本。
    /// </summary>
    public IReadOnlyList<ScopedRelation> GetAllRelations() => _session.ExecuteQuery(GetAllRelationsCore);

    /// <summary>
    /// 获取指定范围中的 Relation 及其端点副本。
    /// </summary>
    public IReadOnlyList<ScopedRelation> GetRelationsByScope(RelationQueryScope scope, string characterName) => _session.ExecuteQuery(world => GetRelationsByScopeCore(world, scope, characterName));

    /// <summary>
    /// 在指定范围中按名称查询 Relation，忽略名称大小写。
    /// </summary>
    public IReadOnlyList<ScopedRelation> FindRelations(string name, RelationQueryScope scope, string characterName) => _session.ExecuteQuery(world => FindRelationsCore(world, name, scope, characterName));

    /// <summary>
    /// 查询指定范围中同时包含全部字符串线索的 Relation。
    /// </summary>
    public IReadOnlyList<ScopedRelation> QueryRelations(IEnumerable<string> clues, RelationQueryScope scope, string characterName) => _session.ExecuteQuery(world => QueryRelationsCore(world, clues, scope, characterName));

    /// <summary>
    /// 按方向查询与指定名称 Anchor 相连的 Relation。
    /// </summary>
    public IReadOnlyList<ScopedRelation> GetAnchorRelations(string anchorName, RelationDirection direction, RelationQueryScope scope, string characterName) => _session.ExecuteQuery(world => GetAnchorRelationsCore(world, anchorName, direction, scope, characterName));

    /// <summary>
    /// 查询两个名称对应的 Anchor 集合之间的 Relation。
    /// </summary>
    public IReadOnlyList<ScopedRelation> GetRelationsBetweenAnchors(string firstAnchorName, string secondAnchorName, RelationQueryScope scope, string characterName) => _session.ExecuteQuery(world => GetRelationsBetweenAnchorsCore(world, firstAnchorName, secondAnchorName, scope, characterName));

    /// <summary>
    /// 使用相同线索分别查询事实世界与指定 Character 子世界。
    /// </summary>
    public WorldRelationComparison CompareWorldWithSubWorld(string characterName, IEnumerable<string> clues) => _session.ExecuteQuery(world => CompareWorldWithSubWorldCore(world, characterName, clues));

    /// <summary>
    /// 返回名称唯一匹配的 Anchor 副本；没有结果或存在歧义时抛出异常。
    /// </summary>
    public Anchor RequireSingleAnchor(string name) => _session.ExecuteQuery(world => RequireSingleAnchorCore(world, name));

    /// <summary>
    /// 返回选择条件唯一匹配的 Relation；没有结果或存在歧义时抛出异常。
    /// </summary>
    public ScopedRelation RequireSingleRelation(string name, string sourceAnchorName, string targetAnchorName, RelationQueryScope scope, string characterName) => _session.ExecuteQuery(world => RequireSingleRelationCore(world, name, sourceAnchorName, targetAnchorName, scope, characterName));

    private static IReadOnlyList<Anchor> GetCharactersCore(RuntimeWorld world)
    {
        return world.GetCharacters().OrderBy(anchor => anchor.Name, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<Anchor> FindAnchorsCore(RuntimeWorld world, string name)
    {
        RequireText(name, nameof(name));
        return world.GetAnchors().Where(anchor => string.Equals(anchor.Name, name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(anchor => anchor.Type).ThenBy(anchor => anchor.Description, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<Anchor> QueryAnchorsCore(RuntimeWorld world, IEnumerable<string> clues)
    {
        string[] normalized = NormalizeClues(clues);
        return world.GetAnchors().Where(anchor => MatchesAll($"{anchor.Name}\n{anchor.Description}\n{anchor.Type}", normalized))
            .OrderBy(anchor => anchor.Name, StringComparer.Ordinal).ThenBy(anchor => anchor.Type).ToArray();
    }

    private static IReadOnlyList<ScopedRelation> GetWorldRelationsCore(RuntimeWorld world)
    {
        return world.GetWorldRelations().Select(relation => CreateScopedRelation(world, relation, null)).ToArray();
    }

    private static IReadOnlyList<ScopedRelation> GetSubWorldRelationsCore(RuntimeWorld world, string characterName)
    {
        Anchor character = RequireCharacterCore(world, characterName);
        SubWorldSnapshot? subWorld = world.GetSubWorlds().SingleOrDefault(item => item.DomainId == character.Id);
        return subWorld is null ? [] : subWorld.Relations.Values.Select(relation => CreateScopedRelation(world, relation, character)).ToArray();
    }

    private static IReadOnlyList<ScopedRelation> GetAllRelationsCore(RuntimeWorld world)
    {
        var relations = new List<ScopedRelation>(GetWorldRelationsCore(world));
        foreach (SubWorldSnapshot subWorld in world.GetSubWorlds())
        {
            Anchor character = world.GetAnchor(subWorld.DomainId);
            relations.AddRange(subWorld.Relations.Values.Select(relation => CreateScopedRelation(world, relation, character)));
        }
        return relations;
    }

    private static IReadOnlyList<ScopedRelation> GetRelationsByScopeCore(RuntimeWorld world, RelationQueryScope scope, string characterName)
    {
        return scope switch
        {
            RelationQueryScope.World => GetWorldRelationsCore(world),
            RelationQueryScope.SubWorld => GetSubWorldRelationsCore(world, characterName),
            RelationQueryScope.All => GetAllRelationsCore(world),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "不支持的 Relation 查询范围。")
        };
    }

    private static IReadOnlyList<ScopedRelation> FindRelationsCore(RuntimeWorld world, string name, RelationQueryScope scope, string characterName)
    {
        RequireText(name, nameof(name));
        return SortRelations(GetRelationsByScopeCore(world, scope, characterName)
            .Where(item => string.Equals(item.Relation.Name, name, StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    private static IReadOnlyList<ScopedRelation> QueryRelationsCore(RuntimeWorld world, IEnumerable<string> clues, RelationQueryScope scope, string characterName)
    {
        string[] normalized = NormalizeClues(clues);
        return SortRelations(GetRelationsByScopeCore(world, scope, characterName).Where(item => MatchesRelation(item, normalized))).ToArray();
    }

    private static IReadOnlyList<ScopedRelation> GetAnchorRelationsCore(RuntimeWorld world, string anchorName, RelationDirection direction, RelationQueryScope scope, string characterName)
    {
        HashSet<Guid> anchorIds = FindAnchorsCore(world, anchorName).Select(anchor => anchor.Id).ToHashSet();
        IEnumerable<ScopedRelation> relations = GetRelationsByScopeCore(world, scope, characterName).Where(item => direction switch
        {
            RelationDirection.Incoming => anchorIds.Contains(item.Relation.TargetId),
            RelationDirection.Outgoing => anchorIds.Contains(item.Relation.SourceId),
            RelationDirection.Both => anchorIds.Contains(item.Relation.SourceId) || anchorIds.Contains(item.Relation.TargetId),
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "不支持的 Relation 方向。")
        });
        return SortRelations(relations).ToArray();
    }

    private static IReadOnlyList<ScopedRelation> GetRelationsBetweenAnchorsCore(RuntimeWorld world, string firstAnchorName, string secondAnchorName, RelationQueryScope scope, string characterName)
    {
        HashSet<Guid> firstIds = FindAnchorsCore(world, firstAnchorName).Select(anchor => anchor.Id).ToHashSet();
        HashSet<Guid> secondIds = FindAnchorsCore(world, secondAnchorName).Select(anchor => anchor.Id).ToHashSet();
        IEnumerable<ScopedRelation> relations = GetRelationsByScopeCore(world, scope, characterName).Where(item =>
            firstIds.Contains(item.Relation.SourceId) && secondIds.Contains(item.Relation.TargetId)
            || secondIds.Contains(item.Relation.SourceId) && firstIds.Contains(item.Relation.TargetId));
        return SortRelations(relations).ToArray();
    }

    private static WorldRelationComparison CompareWorldWithSubWorldCore(RuntimeWorld world, string characterName, IEnumerable<string> clues)
    {
        string[] normalized = NormalizeClues(clues);
        IReadOnlyList<ScopedRelation> worldRelations = SortRelations(GetWorldRelationsCore(world).Where(item => MatchesRelation(item, normalized))).ToArray();
        IReadOnlyList<ScopedRelation> subWorldRelations = SortRelations(GetSubWorldRelationsCore(world, characterName).Where(item => MatchesRelation(item, normalized))).ToArray();
        return new WorldRelationComparison(worldRelations, subWorldRelations);
    }

    private static Anchor RequireSingleAnchorCore(RuntimeWorld world, string name)
    {
        IReadOnlyList<Anchor> anchors = FindAnchorsCore(world, name);
        return anchors.Count switch
        {
            1 => anchors[0],
            0 => throw new InvalidOperationException($"不存在名称为“{name}”的 Anchor。"),
            _ => throw new InvalidOperationException($"名称“{name}”匹配到多个 Anchor，请提供可唯一识别的名称。")
        };
    }

    private static ScopedRelation RequireSingleRelationCore(RuntimeWorld world, string name, string sourceAnchorName, string targetAnchorName, RelationQueryScope scope, string characterName)
    {
        HashSet<Guid> sourceIds = FindAnchorsCore(world, sourceAnchorName).Select(anchor => anchor.Id).ToHashSet();
        HashSet<Guid> targetIds = FindAnchorsCore(world, targetAnchorName).Select(anchor => anchor.Id).ToHashSet();
        ScopedRelation[] relations = GetRelationsByScopeCore(world, scope, characterName).Where(item =>
            sourceIds.Contains(item.Relation.SourceId) && targetIds.Contains(item.Relation.TargetId)
            && string.Equals(item.Relation.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        return relations.Length switch
        {
            1 => relations[0],
            0 => throw new InvalidOperationException($"没有符合选择条件且名称为“{name}”的 Relation。"),
            _ => throw new InvalidOperationException($"选择条件匹配到多个名称为“{name}”的 Relation。")
        };
    }

    private static Anchor RequireCharacterCore(RuntimeWorld world, string characterName)
    {
        RequireText(characterName, nameof(characterName));
        Anchor? exactCharacter = world.GetCharacters().SingleOrDefault(anchor => string.Equals(anchor.Name, characterName, StringComparison.Ordinal));
        if (exactCharacter is not null)
            return exactCharacter;
        Anchor[] characters = world.GetCharacters().Where(anchor => string.Equals(anchor.Name, characterName, StringComparison.OrdinalIgnoreCase)).ToArray();
        return characters.Length switch
        {
            1 => characters[0],
            0 => throw new InvalidOperationException($"不存在名称为“{characterName}”的 Character。"),
            _ => throw new InvalidOperationException($"Character 名称“{characterName}”存在大小写歧义，请使用准确大小写。")
        };
    }

    private static ScopedRelation CreateScopedRelation(RuntimeWorld world, Relation relation, Anchor? domain)
    {
        return new ScopedRelation(relation, world.GetAnchor(relation.SourceId), world.GetAnchor(relation.TargetId), domain);
    }

    private static string[] NormalizeClues(IEnumerable<string> clues)
    {
        ArgumentNullException.ThrowIfNull(clues);
        string[] normalized = clues.Where(clue => !string.IsNullOrWhiteSpace(clue)).Select(clue => clue.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (normalized.Length == 0)
            throw new ArgumentException("clues 至少需要包含一个非空字符串。", nameof(clues));
        return normalized;
    }

    private static bool MatchesRelation(ScopedRelation scopedRelation, IReadOnlyCollection<string> clues)
    {
        string domainName = scopedRelation.Domain?.Name ?? "World";
        return MatchesAll($"{scopedRelation.Relation.Name}\n{scopedRelation.Relation.Description}\n{scopedRelation.Source.Name}\n{scopedRelation.Target.Name}\n{domainName}", clues);
    }

    private static bool MatchesAll(string candidate, IReadOnlyCollection<string> clues)
    {
        return clues.All(clue => candidate.Contains(clue, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<ScopedRelation> SortRelations(IEnumerable<ScopedRelation> relations)
    {
        return relations.OrderBy(item => item.Relation.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Domain?.Name ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(item => item.Source.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Target.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Relation.Description, StringComparer.Ordinal);
    }

    private static void RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"参数 {parameterName} 不能为空或只包含空白字符。", parameterName);
    }
}

/// <summary>
/// 表示 Relation 及其所在范围和端点的独立查询副本。
/// </summary>
public sealed record ScopedRelation(Relation Relation, Anchor Source, Anchor Target, Anchor? Domain);

/// <summary>
/// 表示事实世界与指定子世界的 Relation 查询结果。
/// </summary>
public sealed record WorldRelationComparison(IReadOnlyList<ScopedRelation> World, IReadOnlyList<ScopedRelation> SubWorld);

/// <summary>
/// 指定 Relation 相对于 Anchor 的方向。
/// </summary>
public enum RelationDirection
{
    Incoming,
    Outgoing,
    Both
}

/// <summary>
/// 指定 Relation 查询覆盖的世界范围。
/// </summary>
public enum RelationQueryScope
{
    World,
    SubWorld,
    All
}
