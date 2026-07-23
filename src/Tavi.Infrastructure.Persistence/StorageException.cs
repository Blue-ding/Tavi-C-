namespace Tavi.Infrastructure.Persistence;

/// <summary>定义遵循 <c>TAVI.&lt;AREA&gt;.&lt;SUBJECT&gt;.&lt;REASON&gt;</c> 约定的持久化稳定错误码。</summary>
public static class StorageErrorCodes
{
    /// <summary>读取 World 存档失败。</summary>
    public const string WorldReadFailed = "TAVI.STORAGE.WORLD.READ_FAILED";

    /// <summary>写入 World 存档失败。</summary>
    public const string WorldWriteFailed = "TAVI.STORAGE.WORLD.WRITE_FAILED";

    /// <summary>读取语言模型设置失败。</summary>
    public const string LanguageModelSettingsReadFailed = "TAVI.STORAGE.LM_SETTINGS.READ_FAILED";

    /// <summary>写入语言模型设置失败。</summary>
    public const string LanguageModelSettingsWriteFailed = "TAVI.STORAGE.LM_SETTINGS.WRITE_FAILED";
}

/// <summary>表示持久化介质的读取、写入或恢复失败。</summary>
public abstract class StorageException : TaviException
{
    /// <summary>创建具有稳定错误契约的持久化异常。</summary>
    protected StorageException(string errorCode, string operation, string message, bool isTransient = false, IReadOnlyDictionary<string, string>? details = null, Exception? innerException = null)
        : base(errorCode, TaviErrorCategory.Storage, message, operation, isTransient, details, innerException)
    {
    }
}
