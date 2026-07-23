using Tavi.Domain.World;

namespace Tavi.Application.World;

/// <summary>定义遵循 <c>TAVI.&lt;AREA&gt;.&lt;SUBJECT&gt;.&lt;REASON&gt;</c> 约定的 WorldSession 稳定错误码。</summary>
public static class WorldSessionErrorCodes
{
    /// <summary>提交时的期望 revision 与当前 World revision 不一致。</summary>
    public const string RevisionConflict = "TAVI.WORLD.REVISION.CONFLICT";
}

/// <summary>
/// 表示 WorldSession 的一次原子提交结果。
/// </summary>
public sealed record WorldCommitResult(Guid CommitId, long PreviousRevision, long Revision, AppliedWorldChangeSet? ChangeSet)
{
    /// <summary>
    /// 获取操作组是否产生了实际修改。
    /// </summary>
    public bool Changed => ChangeSet is not null;

    internal static WorldCommitResult Unchanged(long revision) => new(Guid.Empty, revision, revision, null);
}

/// <summary>
/// 指定 WorldSession 已提交操作的来源。
/// </summary>
public enum WorldSessionOperation
{
    Apply,
    Undo,
    Redo
}

/// <summary>
/// 表示提交时的 expectedRevision 与当前 World revision 不一致。
/// </summary>
public sealed class WorldRevisionConflictException : TaviException
{
    internal WorldRevisionConflictException(long expectedRevision, long actualRevision)
        : base(
            WorldSessionErrorCodes.RevisionConflict,
            TaviErrorCategory.Conflict,
            $"World revision 冲突：期望 {expectedRevision}，实际 {actualRevision}。",
            "Commit",
            details: new Dictionary<string, string>
            {
                [nameof(ExpectedRevision)] = expectedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [nameof(ActualRevision)] = actualRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })
    {
        ExpectedRevision = expectedRevision;
        ActualRevision = actualRevision;
    }

    /// <summary>
    /// 获取调用方提交时使用的 revision。
    /// </summary>
    public long ExpectedRevision { get; }

    /// <summary>
    /// 获取提交时 World 的实际 revision。
    /// </summary>
    public long ActualRevision { get; }
}

/// <summary>
/// 指定 WorldSession 的内存状态是否仍然可靠。
/// </summary>
public enum WorldSessionHealth
{
    Healthy,
    Faulted
}
