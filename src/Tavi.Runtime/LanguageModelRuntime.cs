using Tavi.Application.LanguageModel;
using Tavi.Infrastructure.OpenAI;
using Tavi.Infrastructure.Persistence;
using Tavi.Runtime.Logging;

namespace Tavi.Runtime;

/// <summary>
/// 持有进程级稳定 LanguageModel Interface，并负责本地设置加载与热替换。
/// Application Session 只接收稳定代理，不依赖 Guidance Runtime。
/// </summary>
public sealed class LanguageModelRuntime : IHostedService
{
    private readonly IConfiguration _configuration;
    private readonly ApplicationLoggerAdapter _applicationLogger;
    private readonly ILogger<LanguageModelRuntime> _logger;
    private readonly IReadOnlyList<ILanguageModelService> _providedLanguageModels;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private readonly ReloadableLanguageModelService _reloadable = new();
    private ILanguageModelService? _service;
    private string _message = "语言模型尚未初始化。";
    private string? _provider;

    public LanguageModelRuntime(
        IConfiguration configuration,
        ApplicationLoggerAdapter applicationLogger,
        ILogger<LanguageModelRuntime> logger,
        IEnumerable<ILanguageModelService> providedLanguageModels)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _applicationLogger = applicationLogger ?? throw new ArgumentNullException(nameof(applicationLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _providedLanguageModels = providedLanguageModels?.ToArray() ??
            throw new ArgumentNullException(nameof(providedLanguageModels));
    }

    public bool Available => _provider is not null;
    public string? Provider => _provider;
    public string Message => _message;

    /// <summary>
    /// 获取长期稳定的模型 Interface。尚未配置时仅在真正启动模型运行时失败。
    /// </summary>
    public ILanguageModelService Service => _service ?? _reloadable;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            ILanguageModelService? languageModels =
                _providedLanguageModels.LastOrDefault() ??
                await CreateConfiguredAsync(cancellationToken);
            if (_providedLanguageModels.Count > 0)
            {
                _service = languageModels;
            }
            else
            {
                if (languageModels is not null)
                    _reloadable.Replace(languageModels);
                _service = _reloadable;
            }
            if (languageModels is null)
            {
                _message = "尚未配置语言模型。未找到本地 OpenAI 配置 openai.json。";
                return;
            }
            _provider = languageModels.Capabilities.Provider;
            _message = $"语言模型已就绪：{_provider}。";
        }
        catch (Exception exception) when (
            exception is LanguageModelException or
            LanguageModelSettingsStoreException or
            OpenAIConfigurationStoreException or
            ArgumentException)
        {
            _service = _providedLanguageModels.Count > 0 ? null : _reloadable;
            _message = exception.Message;
            _logger.LogWarning(exception, "语言模型初始化失败。");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>重新加载本地设置；显式注入的模型不受本地设置管理。</summary>
    public async Task ReloadSettingsAsync(CancellationToken cancellationToken)
    {
        if (_providedLanguageModels.Count != 0)
            return;
        await _reloadLock.WaitAsync(cancellationToken);
        try
        {
            ILanguageModelService? replacement =
                await CreateConfiguredAsync(cancellationToken);
            if (replacement is null)
            {
                _reloadable.Clear();
                _provider = null;
                _message = "尚未配置语言模型。请填写并保存 OpenAI 连接配置。";
                return;
            }
            _reloadable.Replace(replacement);
            _service = _reloadable;
            _provider = replacement.Capabilities.Provider;
            _message = $"语言模型已就绪：{_provider}。设置已即时生效。";
        }
        catch (Exception exception) when (
            exception is LanguageModelException or
            LanguageModelSettingsStoreException or
            OpenAIConfigurationStoreException or
            ArgumentException)
        {
            _message = $"新设置无法应用：{exception.Message}";
            _logger.LogWarning(exception, "语言模型设置热加载失败。");
            throw;
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private async Task<ILanguageModelService?> CreateConfiguredAsync(
        CancellationToken cancellationToken)
    {
        string defaultSettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tavi",
            "Settings");
        string settingsDirectory =
            ReadConfiguration("Tavi:SettingsDirectory", "TAVI_SETTINGS_DIRECTORY") ??
            defaultSettingsDirectory;
        string openAIConfigurationPath =
            ReadConfiguration(
                "Tavi:OpenAI:ConfigurationPath",
                "TAVI_OPENAI_CONFIGURATION_PATH") ??
            Path.Combine(settingsDirectory, "openai.json");
        using var openAIStore =
            new JsonFileOpenAIConfigurationStore(openAIConfigurationPath);
        var openAIConfigurationService =
            new OpenAIConfigurationService(openAIStore);
        OpenAILanguageModelOptions? options =
            await openAIConfigurationService.LoadOptionsAsync(cancellationToken);
        if (options is null)
            return null;
        using var store = new JsonFileLanguageModelSettingsStore(
            Path.Combine(settingsDirectory, "language-model.json"));
        var settingsService = new LanguageModelSettingsService(store);
        LanguageModelSettings settings =
            await settingsService.LoadOrDefaultAsync(cancellationToken);
        if (!File.Exists(store.Path))
            await settingsService.SaveAsync(settings, cancellationToken);
        return new LanguageModelRunner(
            new OpenAILanguageModelClient(options),
            settings,
            _applicationLogger);
    }

    private string? ReadConfiguration(string key, string environmentVariable) =>
        _configuration[key] ?? Environment.GetEnvironmentVariable(environmentVariable);
}
