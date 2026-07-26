using Tavi.Application;
using Tavi.Application.Guidance;
using Tavi.Application.LanguageModel;
using Tavi.Infrastructure.OpenAI;
using Tavi.Infrastructure.Persistence;
using Tavi.Runtime.Logging;

namespace Tavi.Runtime;

public sealed class GuidanceRuntime : IHostedService
{
    private readonly WorldRuntime _world;
    private readonly ExtensionRuntime _extensions;
    private readonly IConfiguration _configuration;
    private readonly ApplicationLoggerAdapter _applicationLogger;
    private readonly ILogger<GuidanceRuntime> _logger;
    private readonly GuidanceEventBroker _events;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IReadOnlyList<ILanguageModelService> _providedLanguageModels;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private ReloadableLanguageModelService? _reloadableLanguageModels;
    private IGuidanceService? _service;
    private string _availabilityMessage = "Guidance 尚未初始化。";
    private string? _provider;

    public GuidanceRuntime(WorldRuntime world, ExtensionRuntime extensions, IConfiguration configuration, ApplicationLoggerAdapter applicationLogger, ILogger<GuidanceRuntime> logger, GuidanceEventBroker events, IHostApplicationLifetime lifetime, IEnumerable<ILanguageModelService> providedLanguageModels)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _extensions = extensions ?? throw new ArgumentNullException(nameof(extensions));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _applicationLogger = applicationLogger ?? throw new ArgumentNullException(nameof(applicationLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _providedLanguageModels = providedLanguageModels?.ToArray() ?? throw new ArgumentNullException(nameof(providedLanguageModels));
    }

    public GuidanceAvailability Availability => new(_service is not null, _provider, _availabilityMessage);

    /// <summary>获取唯一 Guidance Session 当前是否正在生成。</summary>
    public bool IsGenerating
    {
        get
        {
            if (_service is null)
                return false;
            return _service.GetSnapshot(_service.Id).State == GuidanceState.Generating;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            ILanguageModelService? languageModels = _providedLanguageModels.LastOrDefault()
                ?? await CreateConfiguredLanguageModelsAsync(cancellationToken);
            if (languageModels is null)
            {
                _availabilityMessage = "尚未配置语言模型。未找到本地 OpenAI 配置 openai.json。";
                return;
            }
            if (_providedLanguageModels.Count == 0)
            {
                _reloadableLanguageModels = new ReloadableLanguageModelService(languageModels);
                languageModels = _reloadableLanguageModels;
            }
            InitializeService(languageModels);
            _availabilityMessage = $"Guidance 已就绪：{_provider}。";
        }
        catch (Exception exception) when (exception is LanguageModelException or LanguageModelSettingsStoreException or OpenAIConfigurationStoreException or ArgumentException)
        {
            _availabilityMessage = exception.Message;
            _logger.LogWarning(exception, "Guidance 初始化失败。");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// 重新加载持久化配置。已有 Guidance Session 保持不变，之后启动的模型运行使用新配置。
    /// </summary>
    public async Task ReloadSettingsAsync(CancellationToken cancellationToken)
    {
        // 显式注入的模型服务不由本地 OpenAI 设置管理。
        if (_providedLanguageModels.Count != 0)
            return;

        await _reloadLock.WaitAsync(cancellationToken);
        try
        {
            ILanguageModelService? languageModels = await CreateConfiguredLanguageModelsAsync(cancellationToken);
            if (languageModels is null)
            {
                _availabilityMessage = "尚未配置语言模型。请填写并保存 OpenAI 连接配置。";
                return;
            }

            if (_reloadableLanguageModels is null)
            {
                _reloadableLanguageModels = new ReloadableLanguageModelService(languageModels);
                InitializeService(_reloadableLanguageModels);
            }
            else
            {
                _reloadableLanguageModels.Replace(languageModels);
                _provider = languageModels.Capabilities.Provider;
            }

            _availabilityMessage = $"Guidance 已就绪：{_provider}。设置已即时生效，无需重建 Session。";
        }
        catch (Exception exception) when (exception is LanguageModelException or LanguageModelSettingsStoreException or OpenAIConfigurationStoreException or ArgumentException)
        {
            _availabilityMessage = $"新设置无法应用：{exception.Message}";
            _logger.LogWarning(exception, "Guidance 设置热加载失败。");
            throw;
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    public GuidanceRuntimeOperation Start(string potential)
    {
        IGuidanceService service = RequireService();
        GuidanceOperation operation = service.Start(new NarrativePotential(potential), _lifetime.ApplicationStopping);
        TrackOperation(service, operation);
        return ToRuntimeOperation(operation, service.GetSnapshot(operation.SessionId));
    }

    public GuidanceRuntimeOperation Continue(Guid sessionId, string message)
    {
        IGuidanceService service = RequireService();
        GuidanceOperation operation = service.Continue(sessionId, new GuidanceMessage(message), _lifetime.ApplicationStopping);
        TrackOperation(service, operation);
        return ToRuntimeOperation(operation, service.GetSnapshot(sessionId));
    }

    public GuidanceSnapshot GetSnapshot(Guid sessionId) => RequireService().GetSnapshot(sessionId);

    public GuidanceSnapshot GetCurrentSnapshot()
    {
        IGuidanceService service = RequireService();
        return service.GetSnapshot(service.Id);
    }

    public GuidanceRuntimeCommit Commit(Guid sessionId, IReadOnlyCollection<string> acceptedChangeIds)
    {
        IGuidanceService service = RequireService();
        GuidanceCommitResult result = service.Commit(sessionId, acceptedChangeIds);
        GuidanceSnapshot snapshot = service.GetSnapshot(sessionId);
        _events.Publish(new GuidanceRuntimeEvent("guidance.session.changed", sessionId, null, null, snapshot, null));
        return new GuidanceRuntimeCommit(result, snapshot);
    }

    public GuidanceSnapshot Cancel(Guid sessionId)
    {
        IGuidanceService service = RequireService();
        service.Cancel(sessionId);
        GuidanceSnapshot snapshot = service.GetSnapshot(sessionId);
        _events.Publish(new GuidanceRuntimeEvent("guidance.cancelled", sessionId, null, null, snapshot, null));
        return snapshot;
    }

    public void Forget(Guid sessionId)
    {
        IGuidanceService service = RequireService();
        GuidanceSnapshot snapshot = service.GetSnapshot(sessionId);
        if (!service.Forget(sessionId))
            throw new InvalidOperationException($"Guidance 会话当前状态为 {snapshot.State}，不能遗忘。");
    }

    public GuidanceRuntimeOperation Retry(Guid sessionId, string message)
    {
        IGuidanceService service = RequireService();
        GuidanceOperation operation = service.Retry(sessionId, new GuidanceMessage(message), _lifetime.ApplicationStopping);
        TrackOperation(service, operation);
        return ToRuntimeOperation(operation, service.GetSnapshot(sessionId));
    }

    public GuidanceSnapshot Refresh(Guid sessionId)
    {
        IGuidanceService service = RequireService();
        service.Refresh(sessionId);
        GuidanceSnapshot snapshot = service.GetSnapshot(sessionId);
        _events.Publish(new GuidanceRuntimeEvent("guidance.refreshed", sessionId, null, null, snapshot, null));
        return snapshot;
    }

    private void TrackOperation(IGuidanceService service, GuidanceOperation operation)
    {
        operation.TextReceived += (_, text) => _events.Publish(new GuidanceRuntimeEvent("guidance.text.delta", operation.SessionId, operation.Id, text, null, null));
        GuidanceSnapshot snapshot = service.GetSnapshot(operation.SessionId);
        _events.Publish(new GuidanceRuntimeEvent("guidance.operation.started", operation.SessionId, operation.Id, null, snapshot, null));
        _ = ObserveOperationAsync(service, operation);
    }

    private async Task ObserveOperationAsync(IGuidanceService service, GuidanceOperation operation)
    {
        try
        {
            GuidanceSnapshot snapshot = await operation.Completion;
            _events.Publish(new GuidanceRuntimeEvent("guidance.operation.completed", operation.SessionId, operation.Id, null, snapshot, null));
        }
        catch (OperationCanceledException)
        {
            GuidanceSnapshot snapshot = service.GetSnapshot(operation.SessionId);
            _events.Publish(new GuidanceRuntimeEvent("guidance.operation.cancelled", operation.SessionId, operation.Id, null, snapshot, null));
        }
        catch (Exception exception)
        {
            GuidanceSnapshot snapshot = service.GetSnapshot(operation.SessionId);
            _events.Publish(new GuidanceRuntimeEvent("guidance.operation.failed", operation.SessionId, operation.Id, null, snapshot, snapshot.Failure?.Message ?? "Guidance 生成失败。"));
            _logger.LogWarning(exception, "Guidance 操作失败，SessionId={SessionId}，OperationId={OperationId}。", operation.SessionId, operation.Id);
        }
    }

    private async Task<ILanguageModelService?> CreateConfiguredLanguageModelsAsync(CancellationToken cancellationToken)
    {
        string defaultSettingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tavi", "Settings");
        string settingsDirectory = ReadConfiguration("Tavi:SettingsDirectory", "TAVI_SETTINGS_DIRECTORY") ?? defaultSettingsDirectory;
        string openAIConfigurationPath = ReadConfiguration("Tavi:OpenAI:ConfigurationPath", "TAVI_OPENAI_CONFIGURATION_PATH") ?? Path.Combine(settingsDirectory, "openai.json");
        using var openAIStore = new JsonFileOpenAIConfigurationStore(openAIConfigurationPath);
        var openAIConfigurationService = new OpenAIConfigurationService(openAIStore);
        OpenAILanguageModelOptions? options = await openAIConfigurationService.LoadOptionsAsync(cancellationToken);
        if (options is null)
            return null;
        using var store = new JsonFileLanguageModelSettingsStore(Path.Combine(settingsDirectory, "language-model.json"));
        var settingsService = new LanguageModelSettingsService(store);
        LanguageModelSettings settings = await settingsService.LoadOrDefaultAsync(cancellationToken);
        if (!File.Exists(store.Path))
            await settingsService.SaveAsync(settings, cancellationToken);
        var client = new OpenAILanguageModelClient(options);
        return new LanguageModelRunner(client, settings, _applicationLogger);
    }

    private string? ReadConfiguration(string key, string environmentVariable) => _configuration[key] ?? Environment.GetEnvironmentVariable(environmentVariable);

    private void InitializeService(ILanguageModelService languageModels)
    {
        _provider = languageModels.Capabilities.Provider;
        _service = new TaviCore(languageModels, _applicationLogger).CreateGuidanceService(_world.Service, _extensions.Frozen);
    }

    private IGuidanceService RequireService() => _service ?? throw LanguageModelConfigurationException.Invalid(_availabilityMessage);

    private static GuidanceRuntimeOperation ToRuntimeOperation(
        GuidanceOperation operation,
        GuidanceSnapshot snapshot) =>
        new(operation.Id, operation.SessionId, operation.State, snapshot);
}
