namespace Tavi.Infrastructure.Persistence;

/// <summary>表示 Scenario 存档读取、写入或备份恢复失败。</summary>
public sealed class ScenarioStoreException : StorageException
{
    /// <summary>创建包含存档槽上下文的 Scenario Store 异常。</summary>
    public ScenarioStoreException(string errorCode, string operation, string message, IReadOnlyDictionary<string, string>? details = null, Exception? innerException = null)
        : base(errorCode, operation, message, details: details, innerException: innerException)
    {
    }
}
