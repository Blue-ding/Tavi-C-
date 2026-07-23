namespace Tavi.Infrastructure.Persistence;

/// <summary>
/// 表示世界存档无法读取或写入。
/// </summary>
public sealed class WorldStoreException : Exception
{
    /// <summary>
    /// 使用指定消息创建世界存档异常。
    /// </summary>
    public WorldStoreException(string message) : base(message)
    {
    }

    /// <summary>
    /// 使用指定消息和内部异常创建世界存档异常。
    /// </summary>
    public WorldStoreException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
