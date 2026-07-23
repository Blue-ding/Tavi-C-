using Tavi.Application.LanguageModel;
using Tavi.Host.ViewModels;
using Tavi.Infrastructure.OpenAI;
using Tavi.Infrastructure.Persistence;

namespace Tavi.Host.Runtime;

/// <summary>协调 Host 设置接口与 Application 设置持久化端口。</summary>
internal sealed class SettingsRuntime : IDisposable
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

    internal async Task<LanguageModelSettingsViewModel> LoadAsync(CancellationToken cancellationToken)
    {
        LanguageModelSettings settings = await _languageModelService.LoadOrDefaultAsync(cancellationToken);
        return Map(settings);
    }

    internal async Task<LanguageModelSettingsViewModel> SaveAsync(
        UpdateLanguageModelSettingsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = new LanguageModelSettings
        {
            MaxToolRounds = request.MaxToolRounds,
            MaxOutputRepairAttempts = request.MaxOutputRepairAttempts,
            OverallTimeout = TimeSpan.FromSeconds(request.OverallTimeoutSeconds),
            ToolCalls = ParsePolicy(request.ToolCalls, nameof(request.ToolCalls)),
            NativeJsonOutput = ParsePolicy(request.NativeJsonOutput, nameof(request.NativeJsonOutput)),
            Streaming = ParsePolicy(request.Streaming, nameof(request.Streaming))
        };
        await _languageModelService.SaveAsync(settings, cancellationToken);
        return Map(settings);
    }

    internal async Task<OpenAIConfigurationViewModel> LoadOpenAIAsync(CancellationToken cancellationToken)
    {
        OpenAIConfiguration? configuration = await _openAIService.LoadAsync(cancellationToken);
        return configuration is null
            ? new OpenAIConfigurationViewModel("https://api.openai.com/v1", string.Empty, OpenAIClientType.Chat.ToString(), true, null, false)
            : Map(configuration);
    }

    internal async Task<OpenAIConfigurationViewModel> SaveOpenAIAsync(
        UpdateOpenAIConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Endpoint))
            throw new ArgumentException("OpenAI Endpoint 不能为空。", nameof(request.Endpoint));
        if (string.IsNullOrWhiteSpace(request.Model))
            throw new ArgumentException("OpenAI 模型名称不能为空。", nameof(request.Model));
        OpenAIConfiguration? existing = await _openAIService.LoadAsync(cancellationToken);
        string apiKey = string.IsNullOrWhiteSpace(request.ApiKey)
            ? existing?.ApiKey ?? string.Empty
            : request.ApiKey.Trim();
        var configuration = new OpenAIConfiguration
        {
            Endpoint = new Uri(request.Endpoint, UriKind.Absolute),
            Model = request.Model.Trim(),
            ApiKey = apiKey,
            ClientType = ParseClientType(request.ClientType),
            SupportsRequiredToolChoice = request.SupportsRequiredToolChoice,
            EnableThinking = request.EnableThinking
        };
        await _openAIService.SaveAsync(configuration, cancellationToken);
        return Map(configuration);
    }

    public void Dispose()
    {
        _languageModelStore.Dispose();
        _openAIStore.Dispose();
    }

    private static FeaturePolicy ParsePolicy(string value, string parameterName)
    {
        if (!Enum.TryParse(value, true, out FeaturePolicy policy) || !Enum.IsDefined(policy))
            throw new ArgumentException($"不支持的功能策略“{value}”。", parameterName);
        return policy;
    }

    private static OpenAIClientType ParseClientType(string value)
    {
        if (!Enum.TryParse(value, true, out OpenAIClientType clientType) || !Enum.IsDefined(clientType))
            throw new ArgumentException($"不支持的 OpenAI API 类型“{value}”。", nameof(value));
        return clientType;
    }

    private static LanguageModelSettingsViewModel Map(LanguageModelSettings settings) => new(
        settings.MaxToolRounds,
        settings.MaxOutputRepairAttempts,
        checked((int)settings.OverallTimeout.TotalSeconds),
        settings.ToolCalls.ToString(),
        settings.NativeJsonOutput.ToString(),
        settings.Streaming.ToString());

    private static OpenAIConfigurationViewModel Map(OpenAIConfiguration configuration) => new(
        configuration.Endpoint.ToString(),
        configuration.Model,
        configuration.ClientType.ToString(),
        configuration.SupportsRequiredToolChoice,
        configuration.EnableThinking,
        !string.IsNullOrWhiteSpace(configuration.ApiKey));
}
