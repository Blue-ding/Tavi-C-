namespace Tavi.Infrastructure.Persistence;

/// <summary>表示世界存档无法读取或写入。</summary>
public sealed class WorldStoreException : StorageException
{
    /// <summary>创建世界存档异常。</summary>
    public WorldStoreException(string errorCode, string operation, string message, IReadOnlyDictionary<string, string>? details = null, Exception? innerException = null)
        : base(errorCode, operation, message, details: details, innerException: innerException)
    {
    }
}
