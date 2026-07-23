using Tavi.Application;
using Tavi.Application.Guidance;
using Tavi.Application.LanguageModel;
using Tavi.Host.Logging;
using Tavi.Host.Mapping;
using Tavi.Host.ViewModels;
using Tavi.Infrastructure.OpenAI;
using Tavi.Infrastructure.Persistence;

namespace Tavi.Host.Runtime;

internal sealed class GuidanceRuntime : IHostedService
{
    private readonly WorldRuntime _world;
    private readonly IConfiguration _configuration;
    private readonly ApplicationLoggerAdapter _applicationLogger;
    private readonly ILogger<GuidanceRuntime> _logger;
    private readonly GuidanceEventBroker _events;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IReadOnlyList<ILanguageModelService> _providedLanguageModels;
    private IGuidanceService? _service;
    private string _availabilityMessage = "Guidance 尚未初始化。";
    private string? _provider;

    public GuidanceRuntime(WorldRuntime world, IConfiguration configuration, ApplicationLoggerAdapter applicationLogger, ILogger<GuidanceRuntime> logger, GuidanceEventBroker events, IHostApplicationLifetime lifetime, IEnumerable<ILanguageModelService> providedLanguageModels)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _applicationLogger = applicationLogger ?? throw new ArgumentNullException(nameof(applicationLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _providedLanguageModels = providedLanguageModels?.ToArray() ?? throw new ArgumentNullException(nameof(providedLanguageModels));
    }

    internal GuidanceAvailabilityViewModel Availability => new(_service is not null, _provider, _availabilityMessage);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            ILanguageModelService? languageModels = _providedLanguageModels.LastOrDefault() ?? await CreateConfiguredLanguageModelsAsync(cancellationToken);
            if (languageModels is null)
            {
                _availabilityMessage = "尚未配置语言模型。未找到临时配置 SelfCongif.md。";
                return;
            }
            _provider = languageModels.Capabilities.Provider;
            _service = new TaviCore(languageModels, _applicationLogger).CreateGuidanceService(_world.Session);
            _availabilityMessage = $"Guidance 已就绪：{_provider}。";
        }
        catch (Exception exception) when (exception is LanguageModelException or LanguageModelSettingsStoreException or ArgumentException)
        {
            _availabilityMessage = exception.Message;
            _logger.LogWarning(exception, "Guidance 初始化失败。");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal GuidanceOperationViewModel Start(string potential)
    {
        IGuidanceService service = RequireService();
        GuidanceOperation operation = service.Start(new NarrativePotential(potential), _lifetime.ApplicationStopping);
        TrackOperation(service, operation);
        return GuidanceViewModelMapper.ToOperation(operation, service.GetSnapshot(operation.SessionId));
    }

    internal GuidanceOperationViewModel Continue(Guid sessionId, string message)
    {
        IGuidanceService service = RequireService();
        GuidanceOperation operation = service.Continue(sessionId, new GuidanceMessage(message), _lifetime.ApplicationStopping);
        TrackOperation(service, operation);
        return GuidanceViewModelMapper.ToOperation(operation, service.GetSnapshot(sessionId));
    }

    internal GuidanceSnapshotViewModel GetSnapshot(Guid sessionId) => GuidanceViewModelMapper.ToSnapshot(RequireService().GetSnapshot(sessionId));

    internal GuidanceCommitViewModel Commit(Guid sessionId, IReadOnlyCollection<string> acceptedChangeIds)
    {
        IGuidanceService service = RequireService();
        GuidanceCommitResult result = service.Commit(sessionId, acceptedChangeIds);
        GuidanceSnapshot snapshot = service.GetSnapshot(sessionId);
        GuidanceSnapshotViewModel mappedSnapshot = GuidanceViewModelMapper.ToSnapshot(snapshot);
        _events.Publish(new GuidanceEventViewModel("guidance.session.changed", sessionId, null, null, mappedSnapshot, null));
        return GuidanceViewModelMapper.ToCommit(result, snapshot);
    }

    internal GuidanceSnapshotViewModel Cancel(Guid sessionId)
    {
        IGuidanceService service = RequireService();
        service.Cancel(sessionId);
        GuidanceSnapshotViewModel snapshot = GuidanceViewModelMapper.ToSnapshot(service.GetSnapshot(sessionId));
        _events.Publish(new GuidanceEventViewModel("guidance.cancelled", sessionId, null, null, snapshot, null));
        return snapshot;
    }

    internal void Forget(Guid sessionId)
    {
        IGuidanceService service = RequireService();
        GuidanceSnapshot snapshot = service.GetSnapshot(sessionId);
        if (!service.Forget(sessionId))
            throw new InvalidOperationException($"Guidance 会话当前状态为 {snapshot.State}，不能遗忘。");
    }

    private void TrackOperation(IGuidanceService service, GuidanceOperation operation)
    {
        operation.TextReceived += (_, text) => _events.Publish(new GuidanceEventViewModel("guidance.text.delta", operation.SessionId, operation.Id, text, null, null));
        GuidanceSnapshotViewModel snapshot = GuidanceViewModelMapper.ToSnapshot(service.GetSnapshot(operation.SessionId));
        _events.Publish(new GuidanceEventViewModel("guidance.operation.started", operation.SessionId, operation.Id, null, snapshot, null));
        _ = ObserveOperationAsync(service, operation);
    }

    private async Task ObserveOperationAsync(IGuidanceService service, GuidanceOperation operation)
    {
        try
        {
            GuidanceSnapshot snapshot = await operation.Completion;
            _events.Publish(new GuidanceEventViewModel("guidance.operation.completed", operation.SessionId, operation.Id, null, GuidanceViewModelMapper.ToSnapshot(snapshot), null));
        }
        catch (OperationCanceledException)
        {
            GuidanceSnapshotViewModel snapshot = GuidanceViewModelMapper.ToSnapshot(service.GetSnapshot(operation.SessionId));
            _events.Publish(new GuidanceEventViewModel("guidance.operation.cancelled", operation.SessionId, operation.Id, null, snapshot, null));
        }
        catch (Exception exception)
        {
            GuidanceSnapshotViewModel snapshot = GuidanceViewModelMapper.ToSnapshot(service.GetSnapshot(operation.SessionId));
            _events.Publish(new GuidanceEventViewModel("guidance.operation.failed", operation.SessionId, operation.Id, null, snapshot, snapshot.Failure?.Message ?? "Guidance 生成失败。"));
            _logger.LogWarning(exception, "Guidance 操作失败，SessionId={SessionId}，OperationId={OperationId}。", operation.SessionId, operation.Id);
        }
    }

    private async Task<ILanguageModelService?> CreateConfiguredLanguageModelsAsync(CancellationToken cancellationToken)
    {
        OpenAILanguageModelOptions? options = SelfConfigOpenAILanguageModelOptionsLoader.TryLoad(_configuration["Tavi:OpenAI:SelfConfigPath"]);
        if (options is null)
            return null;
        string defaultSettingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tavi", "Settings");
        string settingsDirectory = ReadConfiguration("Tavi:SettingsDirectory", "TAVI_SETTINGS_DIRECTORY") ?? defaultSettingsDirectory;
        using var store = new JsonFileLanguageModelSettingsStore(Path.Combine(settingsDirectory, "language-model.json"));
        var settingsService = new LanguageModelSettingsService(store);
        LanguageModelSettings settings = await settingsService.LoadOrDefaultAsync(cancellationToken);
        if (!File.Exists(store.Path))
            await settingsService.SaveAsync(settings, cancellationToken);
        var client = new OpenAILanguageModelClient(options);
        return new LanguageModelRunner(client, settings, _applicationLogger);
    }

    private string? ReadConfiguration(string key, string environmentVariable) => _configuration[key] ?? Environment.GetEnvironmentVariable(environmentVariable);

    private IGuidanceService RequireService() => _service ?? throw LanguageModelConfigurationException.Invalid(_availabilityMessage);
}
