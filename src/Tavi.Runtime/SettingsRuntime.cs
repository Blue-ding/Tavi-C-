using Tavi.Application.LanguageModel;
using Tavi.Infrastructure.OpenAI;
using Tavi.Infrastructure.Persistence;

namespace Tavi.Runtime;

/// <summary>协调 Runtime 设置与 Application、Infrastructure 设置持久化端口。</summary>
public sealed class SettingsRuntime : IDisposable
{
    private readonly JsonFileLanguageModelSettingsStore _languageModelStore;
    private readonly LanguageModelSettingsService _languageModelService;
    private readonly JsonFileOpenAIConfigurationStore _openAIStore;
    private readonly OpenAIConfigurationService _openAIService;

    public SettingsRuntime(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string defaultDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tavi",
            "Settings");
        string settingsDirectory =
            configuration["Tavi:SettingsDirectory"]
            ?? Environment.GetEnvironmentVariable("TAVI_SETTINGS_DIRECTORY")
            ?? defaultDirectory;
        string openAIConfigurationPath =
            configuration["Tavi:OpenAI:ConfigurationPath"]
            ?? Environment.GetEnvironmentVariable("TAVI_OPENAI_CONFIGURATION_PATH")
            ?? Path.Combine(settingsDirectory, "openai.json");
        _languageModelStore = new JsonFileLanguageModelSettingsStore(Path.Combine(settingsDirectory, "language-model.json"));
        _languageModelService = new LanguageModelSettingsService(_languageModelStore);
        _openAIStore = new JsonFileOpenAIConfigurationStore(openAIConfigurationPath);
        _openAIService = new OpenAIConfigurationService(_openAIStore);
    }

    public Task<LanguageModelSettings> LoadLanguageModelAsync(CancellationToken cancellationToken) =>
        _languageModelService.LoadOrDefaultAsync(cancellationToken);

    public async Task<LanguageModelSettings> SaveLanguageModelAsync(
        LanguageModelSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _languageModelService.SaveAsync(settings, cancellationToken);
        return settings;
    }

    public async Task<OpenAIConfigurationSnapshot> LoadOpenAIAsync(CancellationToken cancellationToken)
    {
        OpenAIConfiguration? configuration = await _openAIService.LoadAsync(cancellationToken);
        return configuration is null
            ? new OpenAIConfigurationSnapshot(new Uri("https://api.openai.com/v1"), string.Empty, OpenAIClientType.Chat, true, null, false)
            : ToSnapshot(configuration);
    }

    public async Task<OpenAIConfigurationSnapshot> SaveOpenAIAsync(
        OpenAIConfigurationUpdate update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (string.IsNullOrWhiteSpace(update.Model))
            throw new ArgumentException("OpenAI 模型名称不能为空。", nameof(update.Model));
        OpenAIConfiguration? existing = await _openAIService.LoadAsync(cancellationToken);
        string apiKey = string.IsNullOrWhiteSpace(update.ApiKey)
            ? existing?.ApiKey ?? string.Empty
            : update.ApiKey.Trim();
        var configuration = new OpenAIConfiguration
        {
            Endpoint = update.Endpoint,
            Model = update.Model.Trim(),
            ApiKey = apiKey,
            ClientType = update.ClientType,
            SupportsRequiredToolChoice = update.SupportsRequiredToolChoice,
            EnableThinking = update.EnableThinking
        };
        await _openAIService.SaveAsync(configuration, cancellationToken);
        return ToSnapshot(configuration);
    }

    public void Dispose()
    {
        _languageModelStore.Dispose();
        _openAIStore.Dispose();
    }

    private static OpenAIConfigurationSnapshot ToSnapshot(OpenAIConfiguration configuration) => new(
        configuration.Endpoint,
        configuration.Model,
        configuration.ClientType,
        configuration.SupportsRequiredToolChoice,
        configuration.EnableThinking,
        !string.IsNullOrWhiteSpace(configuration.ApiKey));
}
