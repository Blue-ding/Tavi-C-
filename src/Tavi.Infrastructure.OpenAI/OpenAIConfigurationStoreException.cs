namespace Tavi.Infrastructure.OpenAI;

/// <summary>定义 OpenAI 配置存储使用的稳定错误码。</summary>
public static class OpenAIConfigurationStoreErrorCodes
{
    /// <summary>读取 OpenAI 配置失败。</summary>
    public const string ReadFailed = "TAVI.STORAGE.OPENAI_CONFIG.READ_FAILED";

    /// <summary>写入 OpenAI 配置失败。</summary>
    public const string WriteFailed = "TAVI.STORAGE.OPENAI_CONFIG.WRITE_FAILED";
}

/// <summary>表示包含本机密钥的 OpenAI 配置文件无法读取或写入。</summary>
public sealed class OpenAIConfigurationStoreException : TaviException
{
    /// <summary>创建 OpenAI 配置存储异常。</summary>
    public OpenAIConfigurationStoreException(string errorCode, string operation, string message, Exception innerException)
        : base(errorCode, TaviErrorCategory.Storage, message, operation, innerException: innerException)
    {
    }
}
