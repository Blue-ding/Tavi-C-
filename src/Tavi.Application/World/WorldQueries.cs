using Tavi.Domain.World;
using RuntimeWorld = Tavi.Domain.World.World;

namespace Tavi.Application.World;

/// <summary>提供始终通过 WorldBuildSession 同步 seam 读取最新 EARS 与 Local 事实的查询。</summary>
public sealed class WorldQueries
{
    private readonly WorldBuildSession _session;

    internal WorldQueries(WorldBuildSession session) => _session = session;

    /// <summary>创建当前 World 的独立完整快照。</summary>
    public WorldSnapshot CreateSnapshot() => _session.ExecuteQuery(world => world.CreateSnapshot());

    /// <summary>根据标识获取 Element。</summary>
    public Element GetElement(Guid elementId) => _session.ExecuteQuery(world => world.GetElement(elementId));

    /// <summary>获取按名称稳定排序的全部 Element。</summary>
    public IReadOnlyList<Element> GetElements() => _session.ExecuteQuery(world => world.GetElements().OrderBy(value => value.Name, StringComparer.Ordinal).ThenBy(value => value.Id).ToArray());

    /// <summary>查询名称、说明和类型中同时包含全部线索的 Element。</summary>
    public IReadOnlyList<Element> QueryElements(IEnumerable<string> clues) => _session.ExecuteQuery(world =>
    {
        string[] normalized = NormalizeClues(clues);
        return world.GetElements().Where(value => Matches($"{value.Name}\n{value.Description}\n{value.Type}", normalized)).OrderBy(value => value.Name, StringComparer.Ordinal).ThenBy(value => value.Id).ToArray();
    });

    /// <summary>根据标识获取解析后的 Scope。</summary>
    public ResolvedScope GetScope(Guid scopeId) => _session.ExecuteQuery(world => Resolve(world, world.GetScope(scopeId)));

    /// <summary>获取全部解析后的 Scope。</summary>
    public IReadOnlyList<ResolvedScope> GetScopes() => _session.ExecuteQuery(world => Sort(world.GetScopes().Select(value => Resolve(world, value))).ToArray());

    /// <summary>获取由指定 Element 持有的 Scope。</summary>
    public IReadOnlyList<ResolvedScope> GetScopesOwnedByElement(Guid elementId) => _session.ExecuteQuery(world => Sort(world.GetScopesOwnedByElement(elementId).Select(value => Resolve(world, value))).ToArray());

    /// <summary>查询类型、数量和 Owner 文本中同时包含全部线索的 Scope。</summary>
    public IReadOnlyList<ResolvedScope> QueryScopes(IEnumerable<string> clues) => _session.ExecuteQuery(world =>
    {
        string[] normalized = NormalizeClues(clues);
        return Sort(world.GetScopes().Select(value => Resolve(world, value)).Where(value => Matches($"{value.Scope.Type}\n{value.Scope.Quantity}\n{value.Owner.Name}\n{value.Owner.Description}", normalized))).ToArray();
    });

    /// <summary>根据标识获取解析后的 Aspect。</summary>
    public ResolvedAspect GetAspect(Guid aspectId) => _session.ExecuteQuery(world => Resolve(world, world.GetAspect(aspectId)));

    /// <summary>获取全部解析后的 Aspect。</summary>
    public IReadOnlyList<ResolvedAspect> GetAspects() => _session.ExecuteQuery(world => Sort(world.GetAspects().Select(value => Resolve(world, value))).ToArray());

    /// <summary>获取针对指定 Element 的 Aspect。</summary>
    public IReadOnlyList<ResolvedAspect> GetAspectsForElement(Guid elementId) => _session.ExecuteQuery(world => Sort(world.GetAspectsForElement(elementId).Select(value => Resolve(world, value))).ToArray());

    /// <summary>获取指定 Scope 中的 Aspect。</summary>
    public IReadOnlyList<ResolvedAspect> GetAspectsInScope(Guid scopeId) => _session.ExecuteQuery(world => Sort(world.GetAspectsInScope(scopeId).Select(value => Resolve(world, value))).ToArray());

    /// <summary>查询规则化类型、数量及相关 Element 文本中同时包含全部线索的 Aspect。</summary>
    public IReadOnlyList<ResolvedAspect> QueryAspects(IEnumerable<string> clues, Guid? scopeId = null) => _session.ExecuteQuery(world =>
    {
        string[] normalized = NormalizeClues(clues);
        IEnumerable<Aspect> values = scopeId.HasValue ? world.GetAspectsInScope(scopeId.Value) : world.GetAspects();
        return Sort(values.Select(value => Resolve(world, value)).Where(value => Matches($"{value.Aspect.Type}\n{value.Aspect.Quantity}\n{value.Element.Name}\n{value.ScopeOwner.Name}", normalized))).ToArray();
    });

    /// <summary>根据标识获取解析后的 Relation。</summary>
    public ResolvedRelation GetRelation(Guid relationId) => _session.ExecuteQuery(world => Resolve(world, world.GetRelation(relationId)));

    /// <summary>获取全部解析后的 Relation。</summary>
    public IReadOnlyList<ResolvedRelation> GetRelations() => _session.ExecuteQuery(world => Sort(world.GetRelations().Select(value => Resolve(world, value))).ToArray());

    /// <summary>获取指定 Scope 中的 Relation。</summary>
    public IReadOnlyList<ResolvedRelation> GetRelationsInScope(Guid scopeId) => _session.ExecuteQuery(world => Sort(world.GetRelationsInScope(scopeId).Select(value => Resolve(world, value))).ToArray());

    /// <summary>查询规则化类型、数量及相关 Element 文本中同时包含全部线索的 Relation。</summary>
    public IReadOnlyList<ResolvedRelation> QueryRelations(IEnumerable<string> clues, Guid? scopeId = null) => _session.ExecuteQuery(world =>
    {
        string[] normalized = NormalizeClues(clues);
        IEnumerable<Relation> values = scopeId.HasValue ? world.GetRelationsInScope(scopeId.Value) : world.GetRelations();
        return Sort(values.Select(value => Resolve(world, value)).Where(value => Matches($"{value.Relation.Type}\n{value.Relation.Quantity}\n{value.Source.Name}\n{value.Target.Name}\n{value.ScopeOwner.Name}", normalized))).ToArray();
    });

    /// <summary>根据标识获取解析后的 LocalAspect。</summary>
    public ResolvedLocalAspect GetLocalAspect(Guid localAspectId) => _session.ExecuteQuery(world => Resolve(world, world.GetLocalAspect(localAspectId)));

    /// <summary>获取全部解析后的 LocalAspect。</summary>
    public IReadOnlyList<ResolvedLocalAspect> GetLocalAspects() => _session.ExecuteQuery(world => Sort(world.GetLocalAspects().Select(value => Resolve(world, value))).ToArray());

    /// <summary>获取指定 Scope 中的 LocalAspect。</summary>
    public IReadOnlyList<ResolvedLocalAspect> GetLocalAspectsInScope(Guid scopeId) => _session.ExecuteQuery(world => Sort(world.GetLocalAspectsInScope(scopeId).Select(value => Resolve(world, value))).ToArray());

    /// <summary>查询自由谓词文本、数量及相关 Element 文本中同时包含全部线索的 LocalAspect。</summary>
    public IReadOnlyList<ResolvedLocalAspect> QueryLocalAspects(IEnumerable<string> clues, Guid? scopeId = null) => _session.ExecuteQuery(world =>
    {
        string[] normalized = NormalizeClues(clues);
        IEnumerable<LocalAspect> values = scopeId.HasValue ? world.GetLocalAspectsInScope(scopeId.Value) : world.GetLocalAspects();
        return Sort(values.Select(value => Resolve(world, value)).Where(value => Matches($"{value.LocalAspect.Name}\n{value.LocalAspect.Description}\n{value.LocalAspect.Quantity}\n{value.Element.Name}\n{value.ScopeOwner.Name}", normalized))).ToArray();
    });

    /// <summary>根据标识获取解析后的 LocalRelation。</summary>
    public ResolvedLocalRelation GetLocalRelation(Guid localRelationId) => _session.ExecuteQuery(world => Resolve(world, world.GetLocalRelation(localRelationId)));

    /// <summary>获取全部解析后的 LocalRelation。</summary>
    public IReadOnlyList<ResolvedLocalRelation> GetLocalRelations() => _session.ExecuteQuery(world => Sort(world.GetLocalRelations().Select(value => Resolve(world, value))).ToArray());

    /// <summary>获取指定 Scope 中的 LocalRelation。</summary>
    public IReadOnlyList<ResolvedLocalRelation> GetLocalRelationsInScope(Guid scopeId) => _session.ExecuteQuery(world => Sort(world.GetLocalRelationsInScope(scopeId).Select(value => Resolve(world, value))).ToArray());

    /// <summary>查询自由谓词文本、数量及相关 Element 文本中同时包含全部线索的 LocalRelation。</summary>
    public IReadOnlyList<ResolvedLocalRelation> QueryLocalRelations(IEnumerable<string> clues, Guid? scopeId = null) => _session.ExecuteQuery(world =>
    {
        string[] normalized = NormalizeClues(clues);
        IEnumerable<LocalRelation> values = scopeId.HasValue ? world.GetLocalRelationsInScope(scopeId.Value) : world.GetLocalRelations();
        return Sort(values.Select(value => Resolve(world, value)).Where(value => Matches($"{value.LocalRelation.Name}\n{value.LocalRelation.Description}\n{value.LocalRelation.Quantity}\n{value.Source.Name}\n{value.Target.Name}\n{value.ScopeOwner.Name}", normalized))).ToArray();
    });

    private static ResolvedScope Resolve(RuntimeWorld world, Scope value) => new(value, world.GetElement(value.OwnerElementId));

    private static ResolvedAspect Resolve(RuntimeWorld world, Aspect value)
    {
        Scope scope = world.GetScope(value.ScopeId);
        return new ResolvedAspect(value, world.GetElement(value.ElementId), scope, world.GetElement(scope.OwnerElementId));
    }

    private static ResolvedRelation Resolve(RuntimeWorld world, Relation value)
    {
        Scope scope = world.GetScope(value.ScopeId);
        return new ResolvedRelation(value, world.GetElement(value.SourceElementId), world.GetElement(value.TargetElementId), scope, world.GetElement(scope.OwnerElementId));
    }

    private static ResolvedLocalAspect Resolve(RuntimeWorld world, LocalAspect value)
    {
        Scope scope = world.GetScope(value.ScopeId);
        return new ResolvedLocalAspect(value, world.GetElement(value.ElementId), scope, world.GetElement(scope.OwnerElementId));
    }

    private static ResolvedLocalRelation Resolve(RuntimeWorld world, LocalRelation value)
    {
        Scope scope = world.GetScope(value.ScopeId);
        return new ResolvedLocalRelation(value, world.GetElement(value.SourceElementId), world.GetElement(value.TargetElementId), scope, world.GetElement(scope.OwnerElementId));
    }

    private static IEnumerable<ResolvedScope> Sort(IEnumerable<ResolvedScope> values) => values.OrderBy(value => value.Scope.Type.Value, StringComparer.Ordinal).ThenBy(value => value.Owner.Name, StringComparer.Ordinal).ThenBy(value => value.Scope.Id);
    private static IEnumerable<ResolvedAspect> Sort(IEnumerable<ResolvedAspect> values) => values.OrderBy(value => value.Aspect.Type.Value, StringComparer.Ordinal).ThenBy(value => value.Element.Name, StringComparer.Ordinal).ThenBy(value => value.Aspect.Id);
    private static IEnumerable<ResolvedRelation> Sort(IEnumerable<ResolvedRelation> values) => values.OrderBy(value => value.Relation.Type.Value, StringComparer.Ordinal).ThenBy(value => value.Source.Name, StringComparer.Ordinal).ThenBy(value => value.Target.Name, StringComparer.Ordinal).ThenBy(value => value.Relation.Id);
    private static IEnumerable<ResolvedLocalAspect> Sort(IEnumerable<ResolvedLocalAspect> values) => values.OrderBy(value => value.LocalAspect.Name, StringComparer.Ordinal).ThenBy(value => value.Element.Name, StringComparer.Ordinal).ThenBy(value => value.LocalAspect.Id);
    private static IEnumerable<ResolvedLocalRelation> Sort(IEnumerable<ResolvedLocalRelation> values) => values.OrderBy(value => value.LocalRelation.Name, StringComparer.Ordinal).ThenBy(value => value.Source.Name, StringComparer.Ordinal).ThenBy(value => value.Target.Name, StringComparer.Ordinal).ThenBy(value => value.LocalRelation.Id);
    private static bool Matches(string candidate, IEnumerable<string> clues) => clues.All(clue => candidate.Contains(clue, StringComparison.OrdinalIgnoreCase));

    private static string[] NormalizeClues(IEnumerable<string> clues)
    {
        ArgumentNullException.ThrowIfNull(clues);
        string[] normalized = clues.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (normalized.Length == 0)
            throw new ArgumentException("clues 至少需要包含一个非空字符串。", nameof(clues));
        return normalized;
    }
}

/// <summary>表示 Scope 及其 Owner Element 的独立查询副本。</summary>
public sealed record ResolvedScope(Scope Scope, Element Owner);

/// <summary>表示 Aspect 及其结构引用的独立查询副本。</summary>
public sealed record ResolvedAspect(Aspect Aspect, Element Element, Scope Scope, Element ScopeOwner);

/// <summary>表示 Relation 及其结构引用的独立查询副本。</summary>
public sealed record ResolvedRelation(Relation Relation, Element Source, Element Target, Scope Scope, Element ScopeOwner);

/// <summary>表示 LocalAspect 及其结构引用的独立查询副本。</summary>
public sealed record ResolvedLocalAspect(LocalAspect LocalAspect, Element Element, Scope Scope, Element ScopeOwner);

/// <summary>表示 LocalRelation 及其结构引用的独立查询副本。</summary>
public sealed record ResolvedLocalRelation(LocalRelation LocalRelation, Element Source, Element Target, Scope Scope, Element ScopeOwner);
