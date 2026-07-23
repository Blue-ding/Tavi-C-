namespace Tavi.Infrastructure.OpenAI;

/// <summary>定义包含本机 API Key 的 OpenAI 配置持久化端口。</summary>
public interface IOpenAIConfigurationStore
{
    /// <summary>加载 OpenAI 配置；配置文件不存在时返回 <see langword="null"/>。</summary>
    Task<OpenAIConfiguration?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>验证并持久化 OpenAI 配置。</summary>
    Task SaveAsync(OpenAIConfiguration configuration, CancellationToken cancellationToken = default);
}
