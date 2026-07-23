using Tavi.Domain.World;

namespace Tavi.Application.World;

/// <summary>
/// 提供世界会话一次原子提交的信息。
/// </summary>
public sealed class WorldSessionChangedEventArgs : EventArgs
{
    /// <summary>
    /// 创建世界会话变化事件参数；ChangeSet 是已确定的正向与反向操作记录。
    /// </summary>
    public WorldSessionChangedEventArgs(Guid commitId, long revision, WorldSessionOperation operation, AppliedWorldChangeSet changeSet)
    {
        CommitId = commitId;
        Revision = revision;
        Operation = operation;
        ChangeSet = changeSet ?? throw new ArgumentNullException(nameof(changeSet));
    }

    /// <summary>
    /// 获取本次提交标识。
    /// </summary>
    public Guid CommitId { get; }

    /// <summary>
    /// 获取提交后的 World revision。
    /// </summary>
    public long Revision { get; }

    /// <summary>
    /// 获取本次提交来源。
    /// </summary>
    public WorldSessionOperation Operation { get; }

    /// <summary>
    /// 获取本次提交的正向与反向操作。
    /// </summary>
    public AppliedWorldChangeSet ChangeSet { get; }
}
