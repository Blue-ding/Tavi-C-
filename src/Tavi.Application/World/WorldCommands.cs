using Tavi.Domain.World;

namespace Tavi.Application.World;

/// <summary>提供通过 WorldSession 同步和事务边界修改当前 World 的命令。</summary>
public sealed class WorldCommands
{
    private readonly WorldSession _session;

    internal WorldCommands(WorldSession session) => _session = session;

    /// <summary>以预期状态标识为乐观并发条件原子提交操作组。</summary>
    public WorldCommitResult Apply(WorldChangeSet changeSet, Guid expectedStateId)
        => _session.Apply(changeSet, expectedStateId);

    /// <summary>将一项不可变 World 操作追加到暂存日志。</summary>
    public Guid Stage(WorldOperation operation, WorldStagedChangeSource source = WorldStagedChangeSource.Player)
        => _session.Stage(operation, source);

    /// <summary>将一个不可变 World 操作组作为单项记录追加到暂存日志。</summary>
    public Guid Stage(WorldChangeSet changeSet, WorldStagedChangeSource source = WorldStagedChangeSource.Player)
        => _session.Stage(changeSet, source);

    /// <summary>将一组不可变 World 操作按顺序追加到暂存日志。</summary>
    public IReadOnlyList<Guid> Stage(IEnumerable<WorldOperation> operations, WorldStagedChangeSource source)
        => _session.Stage(operations, source);

    /// <summary>删除指定暂存日志项；不存在时返回 false。</summary>
    public bool DeleteStaged(Guid changeId) => _session.DeleteStaged(changeId);

    /// <summary>删除全部无效暂存项并返回删除数量。</summary>
    public int DeleteInvalidStaged() => _session.DeleteInvalidStaged();

    /// <summary>原子提交选中的有效暂存项并在成功后消费它们。</summary>
    public WorldStagingCommitResult CommitStaged(IEnumerable<Guid> selectedChangeIds, Guid expectedStateId)
        => _session.CommitStaged(selectedChangeIds, expectedStateId);

    /// <summary>原子应用最近一次提交的反向操作。</summary>
    public WorldCommitResult Undo(Guid expectedStateId) => _session.Undo(expectedStateId);

    /// <summary>原子重新应用最近一次撤销的正向操作。</summary>
    public WorldCommitResult Redo(Guid expectedStateId) => _session.Redo(expectedStateId);

    /// <summary>立即保存当前 World，无论当前是否为脏状态。</summary>
    public Task SaveAsync(CancellationToken cancellationToken = default)
        => _session.SaveAsync(cancellationToken);
}
