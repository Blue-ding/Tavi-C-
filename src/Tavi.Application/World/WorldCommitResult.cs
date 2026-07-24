using Tavi.Domain.World;

namespace Tavi.Application.World;

/// <summary>定义遵循 <c>TAVI.&lt;AREA&gt;.&lt;SUBJECT&gt;.&lt;REASON&gt;</c> 约定的 WorldSession 稳定错误码。</summary>
public static class WorldSessionErrorCodes
{
    /// <summary>提交所基于的状态标识与当前 World 状态标识不一致。</summary>
    public const string StateConflict = "TAVI.WORLD.STATE.CONFLICT";
}

/// <summary>表示 WorldSession 的一次原子提交结果；状态标识只用于识别提交前后的完整状态，不表达顺序。</summary>
public sealed record WorldCommitResult(Guid CommitId, Guid PreviousStateId, Guid StateId, AppliedWorldChangeSet? ChangeSet)
{
    /// <summary>获取操作组是否产生了实际修改。</summary>
    public bool Changed => ChangeSet is not null;

    internal static WorldCommitResult Unchanged(Guid stateId) => new(Guid.Empty, stateId, stateId, null);
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

/// <summary>表示提交所基于的状态标识与当前 World 状态标识不一致。</summary>
public sealed class WorldStateConflictException : TaviException
{
    /// <summary>使用调用方期望状态和当前实际状态创建可稳定映射的并发冲突。</summary>
    /// <param name="expectedStateId">调用方观察到的 World 状态标识。</param>
    /// <param name="actualStateId">World 当前状态标识。</param>
    public WorldStateConflictException(Guid expectedStateId, Guid actualStateId)
        : base(
            WorldSessionErrorCodes.StateConflict,
            TaviErrorCategory.Conflict,
            $"World 状态冲突：期望 {expectedStateId}，实际 {actualStateId}。",
            "Commit",
            details: new Dictionary<string, string>
            {
                [nameof(ExpectedStateId)] = expectedStateId.ToString(),
                [nameof(ActualStateId)] = actualStateId.ToString()
            })
    {
        ExpectedStateId = expectedStateId;
        ActualStateId = actualStateId;
    }

    /// <summary>获取调用方提交时使用的 World 状态标识。</summary>
    public Guid ExpectedStateId { get; }

    /// <summary>获取提交时 World 的实际状态标识。</summary>
    public Guid ActualStateId { get; }
}

/// <summary>
/// 指定 WorldSession 的内存状态是否仍然可靠。
/// </summary>
public enum WorldSessionHealth
{
    Healthy,
    Faulted
}
