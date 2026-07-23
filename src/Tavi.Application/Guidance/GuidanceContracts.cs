using Tavi.Domain.World;

namespace Tavi.Application.Guidance;

/// <summary>表示玩家用于启动 Guidance 的初始叙事势能。</summary>
public sealed record NarrativePotential
{
    /// <summary>创建叙事势能。</summary>
    public NarrativePotential(string text)
    {
        Text = EnsureText(text, nameof(text));
    }

    /// <summary>获取叙事势能文本。</summary>
    public string Text { get; }

    private static string EnsureText(string text, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(text, parameterName);
        return string.IsNullOrWhiteSpace(text) ? throw new ArgumentException("叙事势能文本不能为空。", parameterName) : text;
    }
}

/// <summary>表示玩家或 Guidance 在对话中提供的一段文本。</summary>
public sealed record GuidanceMessage
{
    /// <summary>创建 Guidance 对话文本。</summary>
    public GuidanceMessage(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = string.IsNullOrWhiteSpace(text) ? throw new ArgumentException("Guidance 对话文本不能为空。", nameof(text)) : text;
    }

    /// <summary>获取对话文本。</summary>
    public string Text { get; }
}

/// <summary>指定一次 Guidance 会话当前所处的应用阶段。</summary>
public enum GuidanceState
{
    /// <summary>会话已经登记但尚未开始生成。</summary>
    Created,

    /// <summary>会话正在等待模型或执行草稿工具。</summary>
    Generating,

    /// <summary>模型需要玩家继续提供文本。</summary>
    AwaitingPlayer,

    /// <summary>会话包含可供玩家选择和提交的提案。</summary>
    ReadyForReview,

    /// <summary>会话正在编译并提交玩家接受的修改。</summary>
    Committing,

    /// <summary>玩家接受的修改已经提交。</summary>
    Completed,

    /// <summary>会话已由调用方取消。</summary>
    Cancelled,

    /// <summary>会话因无法恢复的 Guidance 执行错误结束。</summary>
    Failed
}

/// <summary>表示可安全交给前端读取的 Guidance 会话快照。</summary>
public sealed record GuidanceSnapshot
{
    /// <summary>获取 Guidance 会话标识。</summary>
    public required Guid SessionId { get; init; }

    /// <summary>获取 Guidance 会话当前状态。</summary>
    public required GuidanceState State { get; init; }

    /// <summary>获取 Guidance 开始时的 World revision。</summary>
    public required long BaseWorldRevision { get; init; }

    /// <summary>获取按发生顺序保存的玩家输入和 Guidance 回复；当前文本类型不携带说话方。</summary>
    public IReadOnlyList<GuidanceMessage> Messages { get; init; } = [];

    /// <summary>获取当前可供玩家审阅的提案；尚未形成提案时为 null。</summary>
    public WorldProposal? Proposal { get; init; }

    /// <summary>获取导致会话失败的 Guidance 异常；会话未失败时为 null。</summary>
    public GuidanceException? Failure { get; init; }
}

/// <summary>指定一次异步 Guidance 操作的运行状态。</summary>
public enum GuidanceOperationState
{
    /// <summary>操作已经创建但尚未运行。</summary>
    Created,

    /// <summary>操作正在运行。</summary>
    Running,

    /// <summary>操作已经成功完成。</summary>
    Completed,

    /// <summary>操作因 GuidanceException 失败。</summary>
    Failed,

    /// <summary>操作已取消。</summary>
    Cancelled
}

/// <summary>提供 Guidance 异步操作状态变化前后的值。</summary>
public sealed class GuidanceOperationStateChangedEventArgs : EventArgs
{
    /// <summary>创建状态变化事件参数。</summary>
    public GuidanceOperationStateChangedEventArgs(GuidanceOperationState previous, GuidanceOperationState current)
    {
        Previous = previous;
        Current = current;
    }

    /// <summary>获取变化前的状态。</summary>
    public GuidanceOperationState Previous { get; }

    /// <summary>获取变化后的状态。</summary>
    public GuidanceOperationState Current { get; }
}

/// <summary>表示前端可观察并等待完成的一次 Guidance 异步操作。</summary>
public sealed class GuidanceOperation
{
    private int _state = (int)GuidanceOperationState.Created;
    private readonly TaskCompletionSource<GuidanceSnapshot> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal GuidanceOperation(Guid id, Guid sessionId)
    {
        Id = id;
        SessionId = sessionId;
    }

    /// <summary>获取本次操作标识。</summary>
    public Guid Id { get; }

    /// <summary>获取本次操作所属的 Guidance 会话标识。</summary>
    public Guid SessionId { get; }

    /// <summary>获取本次操作的当前状态。</summary>
    public GuidanceOperationState State => (GuidanceOperationState)Volatile.Read(ref _state);

    /// <summary>获取最终完成、失败或取消的任务。</summary>
    public Task<GuidanceSnapshot> Completion => _completion.Task;

    /// <summary>在操作状态变化后触发。</summary>
    public event EventHandler<GuidanceOperationStateChangedEventArgs>? StateChanged;

    /// <summary>在底层模型返回文本增量后触发。</summary>
    public event EventHandler<string>? TextReceived;

    internal void SetState(GuidanceOperationState state)
    {
        GuidanceOperationState previous = (GuidanceOperationState)Interlocked.Exchange(ref _state, (int)state);
        if (previous != state)
            StateChanged?.Invoke(this, new GuidanceOperationStateChangedEventArgs(previous, state));
    }

    internal void ReportText(string text)
    {
        if (text.Length > 0)
            TextReceived?.Invoke(this, text);
    }

    internal void Complete(GuidanceSnapshot snapshot) => _completion.TrySetResult(snapshot);
    internal void Fail(GuidanceException exception) => _completion.TrySetException(exception);
    internal void Cancel(CancellationToken cancellationToken) => _completion.TrySetCanceled(cancellationToken);
}

/// <summary>指定提交 Guidance 提案的业务结果。</summary>
public enum GuidanceCommitStatus
{
    /// <summary>玩家接受的修改已经原子提交。</summary>
    Committed,

    /// <summary>玩家的修改选择不满足提案依赖或领域约束。</summary>
    InvalidSelection,

    /// <summary>真实 World 已经偏离提案基于的 revision。</summary>
    WorldConflict,

    /// <summary>会话当前没有可提交的提案。</summary>
    SessionNotReady
}

/// <summary>描述一项阻止 Guidance 提交或需要玩家处理的问题。</summary>
public sealed record GuidanceIssue
{
    /// <summary>获取稳定的问题代码。</summary>
    public required string Code { get; init; }

    /// <summary>获取面向调用方的问题说明。</summary>
    public required string Message { get; init; }

    /// <summary>获取相关的提案修改标识；问题不对应单项修改时为 null。</summary>
    public string? ChangeId { get; init; }
}

/// <summary>表示 Guidance 提案的一次提交结果。</summary>
public sealed record GuidanceCommitResult
{
    /// <summary>获取提交状态。</summary>
    public required GuidanceCommitStatus Status { get; init; }

    /// <summary>获取成功提交后的 World revision；未提交时为 null。</summary>
    public long? WorldRevision { get; init; }

    /// <summary>获取临时 Anchor 标识到真实 World 标识的映射。</summary>
    public IReadOnlyDictionary<ProposalAnchorId, Guid> CreatedAnchorIds { get; init; } = new Dictionary<ProposalAnchorId, Guid>();

    /// <summary>获取阻止提交或需要玩家处理的问题。</summary>
    public IReadOnlyList<GuidanceIssue> Issues { get; init; } = [];

    /// <summary>获取提案期望的 World revision；没有 revision 冲突时为 null。</summary>
    public long? ExpectedWorldRevision { get; init; }

    /// <summary>获取提交时实际的 World revision；没有 revision 冲突时为 null。</summary>
    public long? ActualWorldRevision { get; init; }
}

/// <summary>定义从叙事势能生成、继续、审阅并提交 World 提案的 Application 服务。</summary>
public interface IGuidanceService
{
    /// <summary>开始一次绑定当前 World revision 的 Guidance 会话，并立即返回可观察操作。</summary>
    GuidanceOperation Start(NarrativePotential potential, CancellationToken cancellationToken = default);

    /// <summary>向可继续的 Guidance 会话追加玩家文本，并立即返回可观察操作。</summary>
    GuidanceOperation Continue(Guid sessionId, GuidanceMessage message, CancellationToken cancellationToken = default);

    /// <summary>获取指定 Guidance 会话当前的不可变快照。</summary>
    GuidanceSnapshot GetSnapshot(Guid sessionId);

    /// <summary>验证并原子提交玩家接受的提案修改。</summary>
    GuidanceCommitResult Commit(Guid sessionId, IReadOnlyCollection<string> acceptedChangeIds);

    /// <summary>取消指定 Guidance 会话及其当前模型运行。</summary>
    void Cancel(Guid sessionId);

    /// <summary>停止跟踪已经结束的 Guidance 会话；活跃会话不会被移除。</summary>
    bool Forget(Guid sessionId);
}
