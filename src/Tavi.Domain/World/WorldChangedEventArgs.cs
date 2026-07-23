namespace Tavi.Domain.World;

/// <summary>
/// 提供运行时世界发生实际变化后的版本和操作信息。
/// </summary>
public sealed class WorldChangedEventArgs : EventArgs
{
    /// <summary>
    /// 创建世界变化事件参数。
    /// </summary>
    public WorldChangedEventArgs(long revision, string operation)
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
