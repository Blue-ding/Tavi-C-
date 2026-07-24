namespace Tavi.Infrastructure.Persistence;

/// <summary>表示手稿文件的读取、原子写入、归档或删除失败。</summary>
public sealed class ManuscriptStoreException : StorageException
{
    /// <summary>创建具有稳定错误码和脱敏上下文的手稿存储异常。</summary>
    public ManuscriptStoreException(string errorCode, string operation, string message, IReadOnlyDictionary<string, string>? details = null, Exception? innerException = null)
        : base(errorCode, operation, message, false, details, innerException) { }
}
