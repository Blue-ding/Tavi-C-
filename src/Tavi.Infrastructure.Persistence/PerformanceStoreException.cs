namespace Tavi.Infrastructure.Persistence;

/// <summary>表示 Performance 文件的读取、原子写入或归档失败。</summary>
public sealed class PerformanceStoreException : StorageException
{
    public PerformanceStoreException(string errorCode, string operation, string message, IReadOnlyDictionary<string, string>? details = null, Exception? innerException = null)
        : base(errorCode, operation, message, false, details, innerException) { }
}
