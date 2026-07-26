namespace Tavi.Application.World;

/// <summary>
/// 指定世界会话发生的状态变化。
/// </summary>
public enum WorldBuildStateChange
{
    DirtyChanged,
    SaveStarted,
    SaveCompleted,
    SaveFailed,
    SaveCancelled
}

/// <summary>
/// 提供世界会话的脏状态和保存状态变化信息。
/// </summary>
public sealed class WorldBuildStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 创建世界会话状态变化事件参数。
    /// </summary>
    public WorldBuildStateChangedEventArgs(
        WorldBuildStateChange change,
        bool isDirty,
        Exception? exception = null)
    {
        Change = change;
        IsDirty = isDirty;
        Exception = exception;
    }

    /// <summary>
    /// 获取发生的状态变化。
    /// </summary>
    public WorldBuildStateChange Change { get; }

    /// <summary>
    /// 获取事件发生时会话是否包含尚未保存的修改。
    /// </summary>
    public bool IsDirty { get; }

    /// <summary>
    /// 获取保存失败或取消时产生的异常。
    /// </summary>
    public Exception? Exception { get; }
}
