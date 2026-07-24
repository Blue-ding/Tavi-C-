using Tavi.Domain.World;
using RuntimeWorld = Tavi.Domain.World.World;

namespace Tavi.Application.World;

/// <summary>指定暂存世界操作经过当前投影校验后的状态。</summary>
public enum WorldStagedChangeStatus
{
    /// <summary>操作可以参与临时 World 投影并允许提交。</summary>
    Valid,

    /// <summary>多个暂存操作指向同一个 World 项目标识，解决冲突前均不参与投影。</summary>
    Conflict,

    /// <summary>操作缺少引用或不再适用于当前临时 World，只能查看或删除。</summary>
    Invalid
}

/// <summary>指定暂存世界操作的来源。</summary>
public enum WorldStagedChangeSource
{
    /// <summary>操作由玩家直接编辑产生。</summary>
    Player,

    /// <summary>操作由 Guidance 提案产生。</summary>
    Guidance
}

/// <summary>描述不可变的暂存世界操作及其当前校验结果。</summary>
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

    /// <summary>获取与本项指向相同 World 项目的其他暂存日志项标识。</summary>
    public IReadOnlyList<Guid> ConflictingChangeIds { get; init; } = [];
}

/// <summary>表示特定时刻的 World 暂存区和临时 World 投影。</summary>
public sealed record WorldStagingSnapshot
{
    /// <summary>获取暂存区 revision；每次追加、删除或消费暂存项后递增。</summary>
    public required long Revision { get; init; }

    /// <summary>获取生成临时投影时真实 World 的 revision。</summary>
    public required long WorldRevision { get; init; }

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
        HashSet<Guid> invalid = evaluation.Changes.Where(change => change.Status == WorldStagedChangeStatus.Invalid).Select(change => change.Id).ToHashSet();
        int removed = _entries.RemoveAll(entry => invalid.Contains(entry.Id));
        if (removed > 0)
            _revision++;
        return removed;
    }

    internal WorldStagingSnapshot CreateSnapshot(WorldSnapshot worldSnapshot, long worldRevision)
    {
        Evaluation evaluation = Evaluate(worldSnapshot);
        return new WorldStagingSnapshot { Revision = _revision, WorldRevision = worldRevision, Changes = evaluation.Changes, ProjectedWorld = evaluation.ProjectedWorld };
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
        Entry[] entries = _entries.Where(entry => selectedSet.Contains(entry.Id)).ToArray();
        ValidateSelectionDependencies(entries, worldSnapshot);
        return (new WorldChangeSet(entries.SelectMany(entry => entry.ChangeSet.Operations)), selected);
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
        Dictionary<Guid, Guid[]> conflicts = _entries.GroupBy(entry => Target(entry.ChangeSet)).Where(group => group.Count() > 1)
            .SelectMany(group =>
            {
                Guid[] ids = group.Select(entry => entry.Id).ToArray();
                return ids.Select(id => new KeyValuePair<Guid, Guid[]>(id, ids.Where(other => other != id).ToArray()));
            }).ToDictionary(pair => pair.Key, pair => pair.Value);
        var statuses = new Dictionary<Guid, (WorldStagedChangeStatus Status, string? Issue)>();
        foreach (Entry entry in _entries.Where(entry => conflicts.ContainsKey(entry.Id)))
            statuses[entry.Id] = (WorldStagedChangeStatus.Conflict, "多个暂存项指向同一个 World 项目标识。");

        RuntimeWorld projected = RuntimeWorld.Create(worldSnapshot);
        foreach (Entry entry in _entries.Where(entry => !conflicts.ContainsKey(entry.Id)))
        {
            try
            {
                projected.Apply(entry.ChangeSet);
                statuses[entry.Id] = (WorldStagedChangeStatus.Valid, null);
            }
            catch (Exception exception) when (exception is WorldException or InvalidOperationException or ArgumentException)
            {
                statuses[entry.Id] = (WorldStagedChangeStatus.Invalid, exception.Message);
            }
        }

        WorldSnapshot projectedSnapshot = projected.CreateSnapshot();
        HashSet<Guid> relationIds = projectedSnapshot.Relations.Keys.Concat(projectedSnapshot.SubWorlds.SelectMany(subWorld => subWorld.Relations.Keys)).ToHashSet();
        foreach (Entry entry in _entries.Where(entry => statuses.GetValueOrDefault(entry.Id).Status == WorldStagedChangeStatus.Valid))
        {
            Guid? missingRelation = entry.ChangeSet.Operations.Select(ExpectsRelation).FirstOrDefault(id => id.HasValue && !relationIds.Contains(id.Value));
            if (missingRelation.HasValue)
            {
                Guid relationId = missingRelation.Value;
                statuses[entry.Id] = (WorldStagedChangeStatus.Invalid, $"Relation {relationId} 已因依赖的 Anchor 缺失而不再存在。");
            }
        }

        if (statuses.Values.Any(value => value.Status == WorldStagedChangeStatus.Invalid))
        {
            projected = RuntimeWorld.Create(worldSnapshot);
            foreach (Entry entry in _entries.Where(entry => statuses[entry.Id].Status == WorldStagedChangeStatus.Valid))
                projected.Apply(entry.ChangeSet);
            projectedSnapshot = projected.CreateSnapshot();
        }

        WorldStagedChange[] changes = _entries.Select(entry =>
        {
            (WorldStagedChangeStatus status, string? issue) = statuses[entry.Id];
            return new WorldStagedChange { Id = entry.Id, Source = entry.Source, ChangeSet = entry.ChangeSet, Status = status, Issue = issue, ConflictingChangeIds = conflicts.GetValueOrDefault(entry.Id) ?? [] };
        }).ToArray();
        return new Evaluation(changes, projectedSnapshot);
    }

    private static void ValidateSelectionDependencies(IReadOnlyCollection<Entry> entries, WorldSnapshot worldSnapshot)
    {
        HashSet<Guid> availableAnchors = worldSnapshot.Anchors.Keys.ToHashSet();
        foreach (Entry entry in entries)
        {
            foreach (WorldOperation operation in entry.ChangeSet.Operations)
            {
                switch (operation)
                {
                    case AddAnchorOperation add:
                        availableAnchors.Add(add.AnchorId);
                        break;
                    case RemoveAnchorOperation remove:
                        availableAnchors.Remove(remove.AnchorId);
                        break;
                    case AddRelationOperation add when !availableAnchors.Contains(add.SourceId) || !availableAnchors.Contains(add.TargetId) || add.DomainId.HasValue && !availableAnchors.Contains(add.DomainId.Value):
                        throw new InvalidOperationException($"暂存 Relation {entry.Id} 的 Anchor 依赖未包含在本次提交中。");
                }
            }
        }
    }

    private static TargetKey Target(WorldChangeSet changeSet)
    {
        ValidateChangeSet(changeSet);
        return Target(changeSet.Operations[0]);
    }

    private static TargetKey Target(WorldOperation operation) => operation switch
    {
        AddAnchorOperation value => new("Anchor", value.AnchorId),
        RemoveAnchorOperation value => new("Anchor", value.AnchorId),
        UpdateAnchorNameOperation value => new("Anchor", value.AnchorId),
        UpdateAnchorDescriptionOperation value => new("Anchor", value.AnchorId),
        UpdateAnchorTypeOperation value => new("Anchor", value.AnchorId),
        AddRelationOperation value => new("Relation", value.RelationId),
        RemoveRelationOperation value => new("Relation", value.RelationId),
        UpdateRelationNameOperation value => new("Relation", value.RelationId),
        UpdateRelationDescriptionOperation value => new("Relation", value.RelationId),
        CreateSubWorldOperation value => new("SubWorld", value.CharacterId),
        RemoveSubWorldOperation value => new("SubWorld", value.CharacterId),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), $"不支持的 WorldOperation 类型 {operation.GetType().Name}。")
    };

    private static Guid? ExpectsRelation(WorldOperation operation) => operation switch
    {
        AddRelationOperation value => value.RelationId,
        UpdateRelationNameOperation value => value.RelationId,
        UpdateRelationDescriptionOperation value => value.RelationId,
        _ => null
    };

    private static void ValidateOperation(WorldOperation operation)
    {
        TargetKey target = Target(operation);
        EnsureId(target.Id, nameof(operation));
        if (operation is AddRelationOperation relation)
        {
            EnsureId(relation.SourceId, nameof(relation.SourceId));
            EnsureId(relation.TargetId, nameof(relation.TargetId));
            if (relation.DomainId.HasValue)
                EnsureId(relation.DomainId.Value, nameof(relation.DomainId));
        }
    }

    private static void ValidateChangeSet(WorldChangeSet changeSet)
    {
        if (changeSet.IsEmpty)
            throw new ArgumentException("暂存操作组不能为空。", nameof(changeSet));
        foreach (WorldOperation operation in changeSet.Operations)
            ValidateOperation(operation);
        TargetKey[] targets = changeSet.Operations.Select(Target).Distinct().ToArray();
        if (targets.Length != 1)
            throw new ArgumentException("一个暂存项内的操作必须指向同一个 World 项目。", nameof(changeSet));
    }

    private static void EnsureId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("暂存项及其 World 项目标识不能是空 Guid。", parameterName);
    }

    private sealed record Entry(Guid Id, WorldStagedChangeSource Source, WorldChangeSet ChangeSet);
    private sealed record Evaluation(IReadOnlyList<WorldStagedChange> Changes, WorldSnapshot ProjectedWorld);
    private readonly record struct TargetKey(string Kind, Guid Id);
}
