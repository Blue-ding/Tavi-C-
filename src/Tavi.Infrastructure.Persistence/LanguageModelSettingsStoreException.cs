namespace Tavi.Infrastructure.Persistence;

/// <summary>表示语言模型设置文件无法读取或写入。</summary>
public sealed class LanguageModelSettingsStoreException : StorageException
{
    /// <summary>创建语言模型设置存储异常。</summary>
    public LanguageModelSettingsStoreException(string errorCode, string operation, string message, Exception innerException)
        : base(errorCode, operation, message, innerException: innerException)
    {
    }
}
