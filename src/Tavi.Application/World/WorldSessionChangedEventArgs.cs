namespace Tavi.Application.World;

/// <summary>
/// 提供世界会话观察到的运行时世界变化信息。
/// </summary>
public sealed class WorldSessionChangedEventArgs : EventArgs
{
    /// <summary>
    /// 创建世界会话变化事件参数。
    /// </summary>
    public WorldSessionChangedEventArgs(long revision, string operation)
    {
        Revision = revision;
        Operation = operation;
    }

    /// <summary>
    /// 获取变化完成后的世界版本。
    /// </summary>
    public long Revision { get; }

    /// <summary>
    /// 获取引发变化的领域操作名称。
    /// </summary>
    public string Operation { get; }
}
