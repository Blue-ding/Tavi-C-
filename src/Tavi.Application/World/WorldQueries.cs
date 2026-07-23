using Tavi.Domain.World;
using RuntimeWorld = Tavi.Domain.World.World;

namespace Tavi.Application.World;

/// <summary>
/// 提供不依赖具体前端和序列化格式的标准世界查询。
/// </summary>
public static class WorldQueries
{
    /// <summary>
    /// 获取按名称排序的全部 Character。
    /// </summary>
    public static IReadOnlyList<Anchor> GetCharacters(RuntimeWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.GetCharacters().OrderBy(anchor => anchor.Name, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// 按名称查询 Anchor，忽略名称大小写。
    /// </summary>
    public static IReadOnlyList<Anchor> FindAnchors(RuntimeWorld world, string name)
    {
        ArgumentNullException.ThrowIfNull(world);
        RequireText(name, nameof(name));
        return world.GetAnchors().Where(anchor => string.Equals(anchor.Name, name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(anchor => anchor.Type).ThenBy(anchor => anchor.Description, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// 查询同时包含全部字符串线索的 Anchor。
    /// </summary>
    public static IReadOnlyList<Anchor> QueryAnchors(RuntimeWorld world, IEnumerable<string> clues)
    {
        ArgumentNullException.ThrowIfNull(world);
        string[] normalized = NormalizeClues(clues);
        return world.GetAnchors().Where(anchor => MatchesAll($"{anchor.Name}\n{anchor.Description}\n{anchor.Type}", normalized))
            .OrderBy(anchor => anchor.Name, StringComparer.Ordinal).ThenBy(anchor => anchor.Type).ToArray();
    }

    /// <summary>
    /// 获取全部主世界 Relation 及其范围信息。
    /// </summary>
    public static IReadOnlyList<ScopedRelation> GetWorldRelations(RuntimeWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.GetWorldRelations().Select(relation => new ScopedRelation(relation, null)).ToArray();
    }

    /// <summary>
    /// 获取指定 Character 子世界中的全部 Relation。
    /// </summary>
    public static IReadOnlyList<ScopedRelation> GetSubWorldRelations(RuntimeWorld world, string characterName)
    {
        ArgumentNullException.ThrowIfNull(world);
        Anchor character = RequireCharacter(world, characterName);
        SubWorldSnapshot? subWorld = world.GetSubWorlds().SingleOrDefault(item => item.DomainId == character.Id);
        return subWorld is null ? Array.Empty<ScopedRelation>() : subWorld.Relations.Values.Select(relation => new ScopedRelation(relation, character)).ToArray();
    }

    /// <summary>
    /// 获取主世界和全部子世界中的 Relation。
    /// </summary>
    public static IReadOnlyList<ScopedRelation> GetAllRelations(RuntimeWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var relations = new List<ScopedRelation>(GetWorldRelations(world));
        foreach (SubWorldSnapshot subWorld in world.GetSubWorlds())
        {
            Anchor character = world.GetAnchor(subWorld.DomainId);
            relations.AddRange(subWorld.Relations.Values.Select(relation => new ScopedRelation(relation, character)));
        }
        return relations;
    }

    /// <summary>
    /// 获取指定世界范围中的 Relation。
    /// </summary>
    public static IReadOnlyList<ScopedRelation> GetRelationsByScope(RuntimeWorld world, RelationQueryScope scope, string characterName)
    {
        return scope switch
        {
            RelationQueryScope.World => GetWorldRelations(world),
            RelationQueryScope.SubWorld => GetSubWorldRelations(world, characterName),
            RelationQueryScope.All => GetAllRelations(world),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "不支持的 Relation 查询范围。")
        };
    }

    /// <summary>
    /// 在指定范围中按名称查询 Relation，忽略名称大小写。
    /// </summary>
    public static IReadOnlyList<ScopedRelation> FindRelations(RuntimeWorld world, string name, RelationQueryScope scope, string characterName)
    {
        RequireText(name, nameof(name));
        return SortRelations(world, GetRelationsByScope(world, scope, characterName)
            .Where(item => string.Equals(item.Relation.Name, name, StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    /// <summary>
    /// 查询指定范围中同时包含全部字符串线索的 Relation。
    /// </summary>
    public static IReadOnlyList<ScopedRelation> QueryRelations(RuntimeWorld world, IEnumerable<string> clues, RelationQueryScope scope, string characterName)
    {
        string[] normalized = NormalizeClues(clues);
        return SortRelations(world, GetRelationsByScope(world, scope, characterName).Where(item => MatchesRelation(world, item, normalized))).ToArray();
    }

    /// <summary>
    /// 按方向查询与指定名称 Anchor 相连的 Relation。
    /// </summary>
    public static IReadOnlyList<ScopedRelation> GetAnchorRelations(RuntimeWorld world, string anchorName, RelationDirection direction, RelationQueryScope scope, string characterName)
    {
        HashSet<Guid> anchorIds = FindAnchors(world, anchorName).Select(anchor => anchor.Id).ToHashSet();
        IEnumerable<ScopedRelation> relations = GetRelationsByScope(world, scope, characterName).Where(item => direction switch
        {
            RelationDirection.Incoming => anchorIds.Contains(item.Relation.TargetId),
            RelationDirection.Outgoing => anchorIds.Contains(item.Relation.SourceId),
            RelationDirection.Both => anchorIds.Contains(item.Relation.SourceId) || anchorIds.Contains(item.Relation.TargetId),
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "不支持的 Relation 方向。")
        });
        return SortRelations(world, relations).ToArray();
    }

    /// <summary>
    /// 查询两个名称对应的 Anchor 集合之间的 Relation。
    /// </summary>
    public static IReadOnlyList<ScopedRelation> GetRelationsBetweenAnchors(RuntimeWorld world, string firstAnchorName, string secondAnchorName, RelationQueryScope scope, string characterName)
    {
        HashSet<Guid> firstIds = FindAnchors(world, firstAnchorName).Select(anchor => anchor.Id).ToHashSet();
        HashSet<Guid> secondIds = FindAnchors(world, secondAnchorName).Select(anchor => anchor.Id).ToHashSet();
        IEnumerable<ScopedRelation> relations = GetRelationsByScope(world, scope, characterName).Where(item =>
            firstIds.Contains(item.Relation.SourceId) && secondIds.Contains(item.Relation.TargetId)
            || secondIds.Contains(item.Relation.SourceId) && firstIds.Contains(item.Relation.TargetId));
        return SortRelations(world, relations).ToArray();
    }

    /// <summary>
    /// 使用相同线索分别查询主世界与指定 Character 子世界。
    /// </summary>
    public static WorldRelationComparison CompareWorldWithSubWorld(RuntimeWorld world, string characterName, IEnumerable<string> clues)
    {
        string[] normalized = NormalizeClues(clues);
        IReadOnlyList<ScopedRelation> worldRelations = SortRelations(world, GetWorldRelations(world).Where(item => MatchesRelation(world, item, normalized))).ToArray();
        IReadOnlyList<ScopedRelation> subWorldRelations = SortRelations(world, GetSubWorldRelations(world, characterName).Where(item => MatchesRelation(world, item, normalized))).ToArray();
        return new WorldRelationComparison(worldRelations, subWorldRelations);
    }

    /// <summary>
    /// 返回名称唯一匹配的 Anchor；没有结果或存在歧义时抛出异常。
    /// </summary>
    public static Anchor RequireSingleAnchor(RuntimeWorld world, string name)
    {
        IReadOnlyList<Anchor> anchors = FindAnchors(world, name);
        return anchors.Count switch
        {
            1 => anchors[0],
            0 => throw new InvalidOperationException($"不存在名称为“{name}”的 Anchor。"),
            _ => throw new InvalidOperationException($"名称“{name}”匹配到多个 Anchor，请提供可唯一识别的名称。")
        };
    }

    /// <summary>
    /// 返回选择条件唯一匹配的 Relation；没有结果或存在歧义时抛出异常。
    /// </summary>
    public static ScopedRelation RequireSingleRelation(RuntimeWorld world, string name, string sourceAnchorName, string targetAnchorName, RelationQueryScope scope, string characterName)
    {
        HashSet<Guid> sourceIds = FindAnchors(world, sourceAnchorName).Select(anchor => anchor.Id).ToHashSet();
        HashSet<Guid> targetIds = FindAnchors(world, targetAnchorName).Select(anchor => anchor.Id).ToHashSet();
        ScopedRelation[] relations = GetRelationsByScope(world, scope, characterName).Where(item =>
            sourceIds.Contains(item.Relation.SourceId) && targetIds.Contains(item.Relation.TargetId)
            && string.Equals(item.Relation.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        return relations.Length switch
        {
            1 => relations[0],
            0 => throw new InvalidOperationException($"没有符合选择条件且名称为“{name}”的 Relation。"),
            _ => throw new InvalidOperationException($"选择条件匹配到多个名称为“{name}”的 Relation。")
        };
    }

    private static Anchor RequireCharacter(RuntimeWorld world, string characterName)
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

    private static string[] NormalizeClues(IEnumerable<string> clues)
    {
        ArgumentNullException.ThrowIfNull(clues);
        string[] normalized = clues.Where(clue => !string.IsNullOrWhiteSpace(clue)).Select(clue => clue.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (normalized.Length == 0)
            throw new ArgumentException("clues 至少需要包含一个非空字符串。", nameof(clues));
        return normalized;
    }

    private static bool MatchesRelation(RuntimeWorld world, ScopedRelation scopedRelation, IReadOnlyCollection<string> clues)
    {
        Relation relation = scopedRelation.Relation;
        Anchor source = world.GetAnchor(relation.SourceId);
        Anchor target = world.GetAnchor(relation.TargetId);
        string domainName = scopedRelation.Domain?.Name ?? "World";
        return MatchesAll($"{relation.Name}\n{relation.Description}\n{source.Name}\n{target.Name}\n{domainName}", clues);
    }

    private static bool MatchesAll(string candidate, IReadOnlyCollection<string> clues)
    {
        return clues.All(clue => candidate.Contains(clue, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<ScopedRelation> SortRelations(RuntimeWorld world, IEnumerable<ScopedRelation> relations)
    {
        return relations.OrderBy(item => item.Relation.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Domain?.Name ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(item => world.GetAnchor(item.Relation.SourceId).Name, StringComparer.Ordinal)
            .ThenBy(item => world.GetAnchor(item.Relation.TargetId).Name, StringComparer.Ordinal)
            .ThenBy(item => item.Relation.Description, StringComparer.Ordinal);
    }

    private static void RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"参数 {parameterName} 不能为空或只包含空白字符。", parameterName);
    }
}

/// <summary>
/// 表示 Relation 所在的主世界或 Character 子世界。
/// </summary>
public sealed record ScopedRelation(Relation Relation, Anchor? Domain);

/// <summary>
/// 表示主世界与指定子世界的 Relation 查询结果。
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
