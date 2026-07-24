using Tavi.Domain.World;
using RuntimeWorld = Tavi.Domain.World.World;

namespace Tavi.Application.World;

/// <summary>提供始终通过 WorldSession 同步边界读取最新状态的 Element、Aspect、Relation 与 Scope 查询。</summary>
public sealed class WorldQueries
{
    private readonly WorldSession _session;

    /// <summary>创建不缓存 World 数据的会话查询器。</summary>
    internal WorldQueries(WorldSession session)
    {
        _session = session;
    }

    /// <summary>创建当前 World 的独立完整快照。</summary>
    public WorldSnapshot CreateSnapshot() => _session.ExecuteQuery(world => world.CreateSnapshot());

    /// <summary>根据标识获取独立的 Element 副本。</summary>
    public Element GetElement(Guid elementId) => _session.ExecuteQuery(world => world.GetElement(elementId));

    /// <summary>获取按名称稳定排序的全部 Element。</summary>
    public IReadOnlyList<Element> GetElements() => _session.ExecuteQuery(world => SortElements(world.GetElements()).ToArray());

    /// <summary>按名称查找 Element，忽略名称大小写。</summary>
    public IReadOnlyList<Element> FindElements(string name) => _session.ExecuteQuery(world => FindElementsCore(world, name));

    /// <summary>查询同时包含全部字符串线索的 Element。</summary>
    public IReadOnlyList<Element> QueryElements(IEnumerable<string> clues) => _session.ExecuteQuery(world => QueryElementsCore(world, clues));

    /// <summary>返回名称唯一匹配的 Element；没有结果或存在歧义时抛出异常。</summary>
    public Element RequireSingleElement(string name) => _session.ExecuteQuery(world => RequireSingleElementCore(world, name));

    /// <summary>根据标识获取 Scope 及其 Owner Element 的独立副本。</summary>
    public ResolvedScope GetScope(Guid scopeId) => _session.ExecuteQuery(world => ResolveScope(world, world.GetScope(scopeId)));

    /// <summary>获取全部 Scope 及其 Owner Element 的独立副本。</summary>
    public IReadOnlyList<ResolvedScope> GetScopes() => _session.ExecuteQuery(world => SortScopes(world.GetScopes().Select(scope => ResolveScope(world, scope))).ToArray());

    /// <summary>获取由指定 Element 持有的全部 Scope。</summary>
    public IReadOnlyList<ResolvedScope> GetScopesOwnedByElement(Guid elementId) => _session.ExecuteQuery(world => SortScopes(world.GetScopesOwnedByElement(elementId).Select(scope => ResolveScope(world, scope))).ToArray());

    /// <summary>按名称查找 Scope，忽略名称大小写。</summary>
    public IReadOnlyList<ResolvedScope> FindScopes(string name) => _session.ExecuteQuery(world =>
    {
        RequireText(name, nameof(name));
        return SortScopes(world.GetScopes().Where(scope => string.Equals(scope.Name, name, StringComparison.OrdinalIgnoreCase)).Select(scope => ResolveScope(world, scope))).ToArray();
    });

    /// <summary>查询名称、说明、类型和 Owner 中同时包含全部字符串线索的 Scope。</summary>
    public IReadOnlyList<ResolvedScope> QueryScopes(IEnumerable<string> clues) => _session.ExecuteQuery(world =>
    {
        string[] normalized = NormalizeClues(clues);
        return SortScopes(world.GetScopes().Select(scope => ResolveScope(world, scope)).Where(item => MatchesAll($"{item.Scope.Name}\n{item.Scope.Description}\n{item.Scope.Type}\n{item.Owner.Name}\n{item.Owner.Description}", normalized))).ToArray();
    });

    /// <summary>根据标识获取 Aspect 及其 Element、Scope 和 Scope Owner 的独立副本。</summary>
    public ResolvedAspect GetAspect(Guid aspectId) => _session.ExecuteQuery(world => ResolveAspect(world, world.GetAspect(aspectId)));

    /// <summary>获取全部已解析 Aspect。</summary>
    public IReadOnlyList<ResolvedAspect> GetAspects() => _session.ExecuteQuery(world => SortAspects(world.GetAspects().Select(aspect => ResolveAspect(world, aspect))).ToArray());

    /// <summary>获取针对指定 Element 的全部已解析 Aspect。</summary>
    public IReadOnlyList<ResolvedAspect> GetAspectsForElement(Guid elementId) => _session.ExecuteQuery(world => SortAspects(world.GetAspectsForElement(elementId).Select(aspect => ResolveAspect(world, aspect))).ToArray());

    /// <summary>获取指定 Scope 中的全部已解析 Aspect。</summary>
    public IReadOnlyList<ResolvedAspect> GetAspectsInScope(Guid scopeId) => _session.ExecuteQuery(world => SortAspects(world.GetAspectsInScope(scopeId).Select(aspect => ResolveAspect(world, aspect))).ToArray());

    /// <summary>按名称查找 Aspect；可选 Scope 标识用于限制断言域。</summary>
    public IReadOnlyList<ResolvedAspect> FindAspects(string name, Guid? scopeId = null) => _session.ExecuteQuery(world =>
    {
        RequireText(name, nameof(name));
        IEnumerable<Aspect> aspects = GetAspectsByScope(world, scopeId);
        return SortAspects(aspects.Where(aspect => string.Equals(aspect.Name, name, StringComparison.OrdinalIgnoreCase)).Select(aspect => ResolveAspect(world, aspect))).ToArray();
    });

    /// <summary>查询同时包含全部字符串线索的 Aspect；可选 Scope 标识用于限制断言域。</summary>
    public IReadOnlyList<ResolvedAspect> QueryAspects(IEnumerable<string> clues, Guid? scopeId = null) => _session.ExecuteQuery(world =>
    {
        string[] normalized = NormalizeClues(clues);
        return SortAspects(GetAspectsByScope(world, scopeId).Select(aspect => ResolveAspect(world, aspect)).Where(item => MatchesAspect(item, normalized))).ToArray();
    });

    /// <summary>根据标识获取 Relation 及其端点、Scope 和 Scope Owner 的独立副本。</summary>
    public ResolvedRelation GetRelation(Guid relationId) => _session.ExecuteQuery(world => ResolveRelation(world, world.GetRelation(relationId)));

    /// <summary>获取全部已解析 Relation。</summary>
    public IReadOnlyList<ResolvedRelation> GetRelations() => _session.ExecuteQuery(world => SortRelations(world.GetRelations().Select(relation => ResolveRelation(world, relation))).ToArray());

    /// <summary>获取指定 Scope 中的全部已解析 Relation。</summary>
    public IReadOnlyList<ResolvedRelation> GetRelationsInScope(Guid scopeId) => _session.ExecuteQuery(world => SortRelations(world.GetRelationsInScope(scopeId).Select(relation => ResolveRelation(world, relation))).ToArray());

    /// <summary>按名称查找 Relation；可选 Scope 标识用于限制断言域。</summary>
    public IReadOnlyList<ResolvedRelation> FindRelations(string name, Guid? scopeId = null) => _session.ExecuteQuery(world =>
    {
        RequireText(name, nameof(name));
        return SortRelations(GetRelationsByScope(world, scopeId).Where(relation => string.Equals(relation.Name, name, StringComparison.OrdinalIgnoreCase)).Select(relation => ResolveRelation(world, relation))).ToArray();
    });

    /// <summary>查询同时包含全部字符串线索的 Relation；可选 Scope 标识用于限制断言域。</summary>
    public IReadOnlyList<ResolvedRelation> QueryRelations(IEnumerable<string> clues, Guid? scopeId = null) => _session.ExecuteQuery(world =>
    {
        string[] normalized = NormalizeClues(clues);
        return SortRelations(GetRelationsByScope(world, scopeId).Select(relation => ResolveRelation(world, relation)).Where(item => MatchesRelation(item, normalized))).ToArray();
    });

    /// <summary>按方向获取与指定 Element 相连的 Relation；可选 Scope 标识用于限制断言域。</summary>
    public IReadOnlyList<ResolvedRelation> GetElementRelations(Guid elementId, RelationDirection direction = RelationDirection.Both, Guid? scopeId = null) => _session.ExecuteQuery(world =>
    {
        world.GetElement(elementId);
        IEnumerable<Relation> relations = GetRelationsByScope(world, scopeId).Where(relation => direction switch
        {
            RelationDirection.Incoming => relation.TargetElementId == elementId,
            RelationDirection.Outgoing => relation.SourceElementId == elementId,
            RelationDirection.Both => relation.SourceElementId == elementId || relation.TargetElementId == elementId,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "不支持的 Relation 方向。")
        });
        return SortRelations(relations.Select(relation => ResolveRelation(world, relation))).ToArray();
    });

    /// <summary>获取两个 Element 之间任一方向的 Relation；可选 Scope 标识用于限制断言域。</summary>
    public IReadOnlyList<ResolvedRelation> GetRelationsBetweenElements(Guid firstElementId, Guid secondElementId, Guid? scopeId = null) => _session.ExecuteQuery(world =>
    {
        world.GetElement(firstElementId);
        world.GetElement(secondElementId);
        IEnumerable<Relation> relations = GetRelationsByScope(world, scopeId).Where(relation => relation.SourceElementId == firstElementId && relation.TargetElementId == secondElementId || relation.SourceElementId == secondElementId && relation.TargetElementId == firstElementId);
        return SortRelations(relations.Select(relation => ResolveRelation(world, relation))).ToArray();
    });

    /// <summary>返回选择条件唯一匹配的 Relation；没有结果或存在歧义时抛出异常。</summary>
    public ResolvedRelation RequireSingleRelation(string name, Guid sourceElementId, Guid targetElementId, Guid? scopeId = null) => _session.ExecuteQuery(world =>
    {
        RequireText(name, nameof(name));
        world.GetElement(sourceElementId);
        world.GetElement(targetElementId);
        ResolvedRelation[] matches = GetRelationsByScope(world, scopeId).Where(relation => relation.SourceElementId == sourceElementId && relation.TargetElementId == targetElementId && string.Equals(relation.Name, name, StringComparison.OrdinalIgnoreCase)).Select(relation => ResolveRelation(world, relation)).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"没有符合选择条件且名称为“{name}”的 Relation。"),
            _ => throw new InvalidOperationException($"选择条件匹配到多个名称为“{name}”的 Relation。")
        };
    });

    private static IReadOnlyList<Element> FindElementsCore(RuntimeWorld world, string name)
    {
        RequireText(name, nameof(name));
        return SortElements(world.GetElements().Where(element => string.Equals(element.Name, name, StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    private static IReadOnlyList<Element> QueryElementsCore(RuntimeWorld world, IEnumerable<string> clues)
    {
        string[] normalized = NormalizeClues(clues);
        return SortElements(world.GetElements().Where(element => MatchesAll($"{element.Name}\n{element.Description}\n{element.Type}", normalized))).ToArray();
    }

    private static Element RequireSingleElementCore(RuntimeWorld world, string name)
    {
        IReadOnlyList<Element> elements = FindElementsCore(world, name);
        return elements.Count switch
        {
            1 => elements[0],
            0 => throw new InvalidOperationException($"不存在名称为“{name}”的 Element。"),
            _ => throw new InvalidOperationException($"名称“{name}”匹配到多个 Element，请使用标识消除歧义。")
        };
    }

    private static IEnumerable<Aspect> GetAspectsByScope(RuntimeWorld world, Guid? scopeId) => scopeId.HasValue ? world.GetAspectsInScope(scopeId.Value) : world.GetAspects();
    private static IEnumerable<Relation> GetRelationsByScope(RuntimeWorld world, Guid? scopeId) => scopeId.HasValue ? world.GetRelationsInScope(scopeId.Value) : world.GetRelations();
    private static ResolvedScope ResolveScope(RuntimeWorld world, Scope scope) => new(scope, world.GetElement(scope.OwnerElementId));

    private static ResolvedAspect ResolveAspect(RuntimeWorld world, Aspect aspect)
    {
        Scope scope = world.GetScope(aspect.ScopeId);
        return new ResolvedAspect(aspect, world.GetElement(aspect.ElementId), scope, world.GetElement(scope.OwnerElementId));
    }

    private static ResolvedRelation ResolveRelation(RuntimeWorld world, Relation relation)
    {
        Scope scope = world.GetScope(relation.ScopeId);
        return new ResolvedRelation(relation, world.GetElement(relation.SourceElementId), world.GetElement(relation.TargetElementId), scope, world.GetElement(scope.OwnerElementId));
    }

    private static bool MatchesAspect(ResolvedAspect item, IReadOnlyCollection<string> clues) => MatchesAll($"{item.Aspect.Name}\n{item.Aspect.Description}\n{item.Aspect.Type}\n{item.Aspect.Quantity}\n{item.Element.Name}\n{item.Scope.Name}\n{item.ScopeOwner.Name}", clues);
    private static bool MatchesRelation(ResolvedRelation item, IReadOnlyCollection<string> clues) => MatchesAll($"{item.Relation.Name}\n{item.Relation.Description}\n{item.Relation.Type}\n{item.Relation.Quantity}\n{item.Source.Name}\n{item.Target.Name}\n{item.Scope.Name}\n{item.ScopeOwner.Name}", clues);
    private static bool MatchesAll(string candidate, IReadOnlyCollection<string> clues) => clues.All(clue => candidate.Contains(clue, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<Element> SortElements(IEnumerable<Element> values) => values.OrderBy(value => value.Name, StringComparer.Ordinal).ThenBy(value => value.Type.Value, StringComparer.Ordinal).ThenBy(value => value.Id);
    private static IEnumerable<ResolvedScope> SortScopes(IEnumerable<ResolvedScope> values) => values.OrderBy(value => value.Scope.Name, StringComparer.Ordinal).ThenBy(value => value.Owner.Name, StringComparer.Ordinal).ThenBy(value => value.Scope.Id);
    private static IEnumerable<ResolvedAspect> SortAspects(IEnumerable<ResolvedAspect> values) => values.OrderBy(value => value.Aspect.Name, StringComparer.Ordinal).ThenBy(value => value.Scope.Name, StringComparer.Ordinal).ThenBy(value => value.Element.Name, StringComparer.Ordinal).ThenBy(value => value.Aspect.Id);
    private static IEnumerable<ResolvedRelation> SortRelations(IEnumerable<ResolvedRelation> values) => values.OrderBy(value => value.Relation.Name, StringComparer.Ordinal).ThenBy(value => value.Scope.Name, StringComparer.Ordinal).ThenBy(value => value.Source.Name, StringComparer.Ordinal).ThenBy(value => value.Target.Name, StringComparer.Ordinal).ThenBy(value => value.Relation.Id);

    private static string[] NormalizeClues(IEnumerable<string> clues)
    {
        ArgumentNullException.ThrowIfNull(clues);
        string[] normalized = clues.Where(clue => !string.IsNullOrWhiteSpace(clue)).Select(clue => clue.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (normalized.Length == 0)
            throw new ArgumentException("clues 至少需要包含一个非空字符串。", nameof(clues));
        return normalized;
    }

    private static void RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"参数 {parameterName} 不能为空或只包含空白字符。", parameterName);
    }
}

/// <summary>表示 Scope 及其 Owner Element 的独立查询副本。</summary>
/// <param name="Scope">被解析的 Scope。</param>
/// <param name="Owner">持有该 Scope 的 Element。</param>
public sealed record ResolvedScope(Scope Scope, Element Owner);

/// <summary>表示 Aspect 及其目标 Element、Scope 和 Scope Owner 的独立查询副本。</summary>
/// <param name="Aspect">被解析的一元断言。</param>
/// <param name="Element">该断言指向的 Element。</param>
/// <param name="Scope">该断言唯一所属的 Scope。</param>
/// <param name="ScopeOwner">持有该 Scope 的 Element。</param>
public sealed record ResolvedAspect(Aspect Aspect, Element Element, Scope Scope, Element ScopeOwner);

/// <summary>表示 Relation 及其端点、Scope 和 Scope Owner 的独立查询副本。</summary>
/// <param name="Relation">被解析的二元断言。</param>
/// <param name="Source">来源 Element。</param>
/// <param name="Target">目标 Element。</param>
/// <param name="Scope">该断言唯一所属的 Scope。</param>
/// <param name="ScopeOwner">持有该 Scope 的 Element。</param>
public sealed record ResolvedRelation(Relation Relation, Element Source, Element Target, Scope Scope, Element ScopeOwner);

/// <summary>指定 Relation 相对于 Element 的方向。</summary>
public enum RelationDirection
{
    /// <summary>只选择以 Element 为目标的 Relation。</summary>
    Incoming,

    /// <summary>只选择以 Element 为来源的 Relation。</summary>
    Outgoing,

    /// <summary>选择任一端点引用 Element 的 Relation。</summary>
    Both
}
