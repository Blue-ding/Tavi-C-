namespace Tavi.Application;

/// <summary>指定有持久化生命周期的 Application Session 状态变化。</summary>
public enum SessionStateChange
{
    DirtyChanged,
    SaveStarted,
    SaveCompleted,
    SaveFailed,
    SaveCancelled
}

/// <summary>提供 Session 脏状态和保存状态变化信息。</summary>
public sealed class SessionStateChangedEventArgs : EventArgs
{
    public SessionStateChangedEventArgs(
        SessionStateChange change,
        bool isDirty,
        Exception? exception = null)
    {
        Change = change;
        IsDirty = isDirty;
        Exception = exception;
    }

    public SessionStateChange Change { get; }
    public bool IsDirty { get; }
    public Exception? Exception { get; }
}
