using Tavi.Domain.World;
using RuntimeWorld = Tavi.Domain.World.World;

namespace Tavi.Application.World;

/// <summary>指定暂存 World 操作经过当前投影校验后的状态。</summary>
public enum WorldStagedChangeStatus
{
    /// <summary>操作可以参与临时 World 投影并允许提交。</summary>
    Valid,

    /// <summary>多个暂存项修改同一类别和标识的实体，解决冲突前均不参与投影。</summary>
    Conflict,

    /// <summary>操作缺少依赖或不再适用于当前临时 World，只能查看或删除。</summary>
    Invalid
}

/// <summary>指定暂存 World 操作的来源。</summary>
public enum WorldStagedChangeSource
{
    /// <summary>操作由玩家直接编辑产生。</summary>
    Player,

    /// <summary>操作由 Guidance 提案产生。</summary>
    Guidance,

    /// <summary>操作由活动 Module 的组合式 World Authoring 能力产生。</summary>
    Module
}

/// <summary>描述不可变的暂存 World 操作组及其当前校验结果。</summary>
public sealed record WorldStagedChange
{
    /// <summary>获取暂存日志项的系统标识。</summary>
    public required Guid Id { get; init; }

    /// <summary>获取操作来源。</summary>
    public required WorldStagedChangeSource Source { get; init; }

    /// <summary>获取作为单一暂存项原子执行的不可变 World 操作组。</summary>
    public required WorldChangeSet ChangeSet { get; init; }

    /// <summary>获取当前状态。</summary>
    public required WorldStagedChangeStatus Status { get; init; }

    /// <summary>获取状态说明；有效项为空。</summary>
    public string? Issue { get; init; }

    /// <summary>获取与本项修改相同类别和标识实体的其他暂存日志项标识。</summary>
    public IReadOnlyList<Guid> ConflictingChangeIds { get; init; } = [];
}

/// <summary>表示特定时刻的 World 暂存区和临时 World 投影。</summary>
public sealed record WorldStagingSnapshot
{
    /// <summary>获取暂存区 revision；每次追加、删除或消费暂存项后递增。</summary>
    public required long Revision { get; init; }

    /// <summary>获取生成临时投影时真实 World 的状态标识。</summary>
    public required Guid WorldStateId { get; init; }

    /// <summary>获取按追加顺序排列的暂存项。</summary>
    public IReadOnlyList<WorldStagedChange> Changes { get; init; } = [];

    /// <summary>获取排除冲突和无效操作后形成的临时 World 快照。</summary>
    public required WorldSnapshot ProjectedWorld { get; init; }
}

/// <summary>表示一次暂存区提交的结果。</summary>
public sealed record WorldStagingCommitResult
{
    /// <summary>获取真实 World 的提交结果。</summary>
    public required WorldCommitResult Commit { get; init; }

    /// <summary>获取已从暂存区消费的日志项标识。</summary>
    public IReadOnlyList<Guid> ConsumedChangeIds { get; init; } = [];
}

/// <summary>维护不可变暂存日志，并从真实 World 快照重放有效项形成临时投影。</summary>
internal sealed class WorldStagingArea
{
    private readonly List<Entry> _entries = [];
    private long _revision;

    internal long Revision => _revision;

    internal Guid Stage(WorldOperation operation, WorldStagedChangeSource source)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ValidateOperation(operation);
        Guid id = Guid.NewGuid();
        _entries.Add(new Entry(id, source, WorldOperations.Single(operation)));
        _revision++;
        return id;
    }

    internal Guid Stage(WorldChangeSet changeSet, WorldStagedChangeSource source)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        ValidateChangeSet(changeSet);
        Guid id = Guid.NewGuid();
        _entries.Add(new Entry(id, source, changeSet));
        _revision++;
        return id;
    }

    internal IReadOnlyList<Guid> Stage(IEnumerable<WorldOperation> operations, WorldStagedChangeSource source)
    {
        ArgumentNullException.ThrowIfNull(operations);
        WorldOperation[] copied = operations.ToArray();
        if (copied.Length == 0)
            return [];
        foreach (WorldOperation operation in copied)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ValidateOperation(operation);
        }
        Guid[] ids = copied.Select(operation =>
        {
            Guid id = Guid.NewGuid();
            _entries.Add(new Entry(id, source, WorldOperations.Single(operation)));
            return id;
        }).ToArray();
        _revision++;
        return ids;
    }

    internal bool Delete(Guid changeId)
    {
        EnsureId(changeId, nameof(changeId));
        int removed = _entries.RemoveAll(entry => entry.Id == changeId);
        if (removed > 0)
            _revision++;
        return removed > 0;
    }

    internal int DeleteInvalid(WorldSnapshot worldSnapshot)
    {
        Evaluation evaluation = Evaluate(worldSnapshot);
        HashSet<Guid> invalidIds = evaluation.Changes.Where(change => change.Status == WorldStagedChangeStatus.Invalid).Select(change => change.Id).ToHashSet();
        int removed = _entries.RemoveAll(entry => invalidIds.Contains(entry.Id));
        if (removed > 0)
            _revision++;
        return removed;
    }

    internal WorldStagingSnapshot CreateSnapshot(WorldSnapshot worldSnapshot, Guid worldStateId)
    {
        Evaluation evaluation = Evaluate(worldSnapshot);
        return new WorldStagingSnapshot { Revision = _revision, WorldStateId = worldStateId, Changes = evaluation.Changes, ProjectedWorld = evaluation.ProjectedWorld };
    }

    internal (WorldChangeSet ChangeSet, Guid[] ChangeIds) PrepareCommit(IEnumerable<Guid> selectedChangeIds, WorldSnapshot worldSnapshot)
    {
        ArgumentNullException.ThrowIfNull(selectedChangeIds);
        Guid[] selected = selectedChangeIds.ToArray();
        if (selected.Length == 0)
            throw new ArgumentException("至少需要选择一个暂存项。", nameof(selectedChangeIds));
        if (selected.Any(id => id == Guid.Empty) || selected.Distinct().Count() != selected.Length)
            throw new ArgumentException("暂存项标识不能为空或重复。", nameof(selectedChangeIds));
        Evaluation evaluation = Evaluate(worldSnapshot);
        Dictionary<Guid, WorldStagedChange> changes = evaluation.Changes.ToDictionary(change => change.Id);
        Guid? missing = selected.Cast<Guid?>().FirstOrDefault(id => !changes.ContainsKey(id!.Value));
        if (missing.HasValue)
            throw new ArgumentException($"不存在暂存项 {missing.Value}。", nameof(selectedChangeIds));
        WorldStagedChange? unavailable = selected.Select(id => changes[id]).FirstOrDefault(change => change.Status != WorldStagedChangeStatus.Valid);
        if (unavailable is not null)
            throw new InvalidOperationException($"暂存项 {unavailable.Id} 当前状态为 {unavailable.Status}，不能提交。");
        HashSet<Guid> selectedSet = selected.ToHashSet();
        Entry[] selectedEntries = _entries.Where(entry => selectedSet.Contains(entry.Id)).ToArray();
        var selectedProjection = RuntimeWorld.Create(worldSnapshot);
        try
        {
            foreach (Entry entry in selectedEntries)
                selectedProjection.Apply(entry.ChangeSet);
        }
        catch (WorldException exception)
        {
            throw new InvalidOperationException($"选中的暂存项缺少未选中的依赖或组合后无效：{exception.Message}", exception);
        }
        return (new WorldChangeSet(selectedEntries.SelectMany(entry => entry.ChangeSet.Operations)), selected);
    }

    internal void Consume(IReadOnlyCollection<Guid> changeIds)
    {
        HashSet<Guid> selected = changeIds.ToHashSet();
        int removed = _entries.RemoveAll(entry => selected.Contains(entry.Id));
        if (removed != selected.Count)
            throw new InvalidOperationException("暂存区在提交期间发生了不一致变化。");
        _revision++;
    }

    private Evaluation Evaluate(WorldSnapshot worldSnapshot)
    {
        ArgumentNullException.ThrowIfNull(worldSnapshot);
        var conflicts = _entries.ToDictionary(entry => entry.Id, _ => new HashSet<Guid>());
        Dictionary<TargetKey, Entry[]> entriesByTarget = _entries.SelectMany(entry => GetTargets(entry.ChangeSet).Select(target => (Target: target, Entry: entry))).GroupBy(item => item.Target).ToDictionary(group => group.Key, group => group.Select(item => item.Entry).Distinct().ToArray());
        foreach (Entry[] entries in entriesByTarget.Values.Where(entries => entries.Length > 1))
        {
            foreach (Entry entry in entries)
                conflicts[entry.Id].UnionWith(entries.Where(other => other.Id != entry.Id).Select(other => other.Id));
        }
        var projected = RuntimeWorld.Create(worldSnapshot);
        var changes = new List<WorldStagedChange>(_entries.Count);
        foreach (Entry entry in _entries)
        {
            Guid[] conflictingIds = conflicts[entry.Id].OrderBy(id => id).ToArray();
            if (conflictingIds.Length > 0)
            {
                changes.Add(ToChange(entry, WorldStagedChangeStatus.Conflict, "多个暂存项修改同一 World 实体。", conflictingIds));
                continue;
            }
            try
            {
                projected.Apply(entry.ChangeSet);
                changes.Add(ToChange(entry, WorldStagedChangeStatus.Valid, null, []));
            }
            catch (Exception exception) when (exception is WorldException or ArgumentException or InvalidOperationException)
            {
                changes.Add(ToChange(entry, WorldStagedChangeStatus.Invalid, exception.Message, []));
            }
        }
        return new Evaluation(changes, projected.CreateSnapshot());
    }

    private static WorldStagedChange ToChange(Entry entry, WorldStagedChangeStatus status, string? issue, IReadOnlyList<Guid> conflictingIds) => new()
    {
        Id = entry.Id,
        Source = entry.Source,
        ChangeSet = entry.ChangeSet,
        Status = status,
        Issue = issue,
        ConflictingChangeIds = conflictingIds
    };

    private static IEnumerable<TargetKey> GetTargets(WorldChangeSet changeSet) => changeSet.Operations.Select(GetTarget).Distinct();

    private static TargetKey GetTarget(WorldOperation operation) => operation switch
    {
        AddElementOperation value => new("Element", value.ElementId),
        RemoveElementOperation value => new("Element", value.ElementId),
        UpdateElementNameOperation value => new("Element", value.ElementId),
        UpdateElementDescriptionOperation value => new("Element", value.ElementId),
        UpdateElementTypeOperation value => new("Element", value.ElementId),
        AddAspectOperation value => new("Aspect", value.AspectId),
        RemoveAspectOperation value => new("Aspect", value.AspectId),
        UpdateAspectQuantityOperation value => new("Aspect", value.AspectId),
        UpdateAspectTypeOperation value => new("Aspect", value.AspectId),
        AddRelationOperation value => new("Relation", value.RelationId),
        RemoveRelationOperation value => new("Relation", value.RelationId),
        UpdateRelationQuantityOperation value => new("Relation", value.RelationId),
        UpdateRelationTypeOperation value => new("Relation", value.RelationId),
        AddScopeOperation value => new("Scope", value.ScopeId),
        RemoveScopeOperation value => new("Scope", value.ScopeId),
        UpdateScopeQuantityOperation value => new("Scope", value.ScopeId),
        UpdateScopeTypeOperation value => new("Scope", value.ScopeId),
        AddLocalAspectOperation value => new("LocalAspect", value.LocalAspectId),
        RemoveLocalAspectOperation value => new("LocalAspect", value.LocalAspectId),
        UpdateLocalAspectOperation value => new("LocalAspect", value.LocalAspectId),
        AddLocalRelationOperation value => new("LocalRelation", value.LocalRelationId),
        RemoveLocalRelationOperation value => new("LocalRelation", value.LocalRelationId),
        UpdateLocalRelationOperation value => new("LocalRelation", value.LocalRelationId),
        _ => throw new ArgumentException($"不支持的 World 操作类型 {operation.GetType().FullName}。", nameof(operation))
    };

    private static void ValidateChangeSet(WorldChangeSet changeSet)
    {
        if (changeSet.IsEmpty)
            throw new ArgumentException("不能暂存空 World 操作组。", nameof(changeSet));
        foreach (WorldOperation operation in changeSet.Operations)
            ValidateOperation(operation);
    }

    private static void ValidateOperation(WorldOperation operation)
    {
        TargetKey target = GetTarget(operation);
        EnsureId(target.Id, $"{operation.GetType().Name} 目标标识");
    }

    private static void EnsureId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
            throw new ArgumentException($"{parameterName} 不能是空 Guid。", parameterName);
    }

    private sealed record Entry(Guid Id, WorldStagedChangeSource Source, WorldChangeSet ChangeSet);
    private sealed record Evaluation(IReadOnlyList<WorldStagedChange> Changes, WorldSnapshot ProjectedWorld);
    private readonly record struct TargetKey(string Kind, Guid Id);
}
