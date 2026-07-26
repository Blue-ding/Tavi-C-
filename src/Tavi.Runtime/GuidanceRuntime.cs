using Tavi.Application;
using Tavi.Application.Guidance;
using Tavi.Application.LanguageModel;
using Tavi.Runtime.Logging;

namespace Tavi.Runtime;

public sealed class GuidanceRuntime : IHostedService
{
    private readonly WorldRuntime _world;
    private readonly ExtensionRuntime _extensions;
    private readonly LanguageModelRuntime _languageModels;
    private readonly ApplicationLoggerAdapter _applicationLogger;
    private readonly ILogger<GuidanceRuntime> _logger;
    private readonly GuidanceEventBroker _events;
    private readonly IHostApplicationLifetime _lifetime;
    private IGuidanceService? _service;
    private string _availabilityMessage = "Guidance 尚未初始化。";
    private string? _provider;

    public GuidanceRuntime(
        WorldRuntime world,
        ExtensionRuntime extensions,
        LanguageModelRuntime languageModels,
        ApplicationLoggerAdapter applicationLogger,
        ILogger<GuidanceRuntime> logger,
        GuidanceEventBroker events,
        IHostApplicationLifetime lifetime)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _extensions = extensions ?? throw new ArgumentNullException(nameof(extensions));
        _languageModels = languageModels ?? throw new ArgumentNullException(nameof(languageModels));
        _applicationLogger = applicationLogger ?? throw new ArgumentNullException(nameof(applicationLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
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
        await Task.CompletedTask;
        if (!_languageModels.Available)
        {
            _availabilityMessage = _languageModels.Message;
            return;
        }
        InitializeService(_languageModels.Service);
        _availabilityMessage = $"Guidance 已就绪：{_provider}。";
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// 重新加载持久化配置。已有 Guidance Session 保持不变，之后启动的模型运行使用新配置。
    /// </summary>
    public async Task ReloadSettingsAsync(CancellationToken cancellationToken)
    {
        await _languageModels.ReloadSettingsAsync(cancellationToken);
        if (!_languageModels.Available)
        {
            _availabilityMessage = _languageModels.Message;
            return;
        }
        if (_service is null)
            InitializeService(_languageModels.Service);
        _provider = _languageModels.Provider;
        _availabilityMessage =
            $"Guidance 已就绪：{_provider}。设置已即时生效，无需重建 Session。";
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

    private void InitializeService(ILanguageModelService languageModels)
    {
        _provider = _languageModels.Provider ?? languageModels.Capabilities.Provider;
        _service = new TaviCore(languageModels, _applicationLogger).CreateGuidanceService(
            _world.View,
            _world.Contributor,
            _world.Controller,
            _extensions.Frozen);
    }

    private IGuidanceService RequireService() => _service ?? throw LanguageModelConfigurationException.Invalid(_availabilityMessage);

    private static GuidanceRuntimeOperation ToRuntimeOperation(
        GuidanceOperation operation,
        GuidanceSnapshot snapshot) =>
        new(operation.Id, operation.SessionId, operation.State, snapshot);
}
