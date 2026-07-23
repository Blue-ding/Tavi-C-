namespace Tavi.Infrastructure.OpenAI;

/// <summary>协调 OpenAI 配置的加载、验证、保存和运行时选项创建。</summary>
public sealed class OpenAIConfigurationService
{
    private readonly IOpenAIConfigurationStore _store;

    /// <summary>创建使用指定持久化端口的 OpenAI 配置服务。</summary>
    public OpenAIConfigurationService(IOpenAIConfigurationStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>加载并验证 OpenAI 配置；配置不存在时返回 <see langword="null"/>。</summary>
    public async Task<OpenAIConfiguration?> LoadAsync(CancellationToken cancellationToken = default)
    {
        OpenAIConfiguration? configuration = await _store.LoadAsync(cancellationToken);
        configuration?.Validate();
        return configuration;
    }

    /// <summary>加载配置并创建运行时选项；配置不存在时返回 <see langword="null"/>。</summary>
    public async Task<OpenAILanguageModelOptions?> LoadOptionsAsync(CancellationToken cancellationToken = default)
    {
        OpenAIConfiguration? configuration = await LoadAsync(cancellationToken);
        return configuration?.ToOptions();
    }

    /// <summary>验证并保存包含本机 API Key 的 OpenAI 配置。</summary>
    public async Task SaveAsync(OpenAIConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        await _store.SaveAsync(configuration, cancellationToken);
    }
}
