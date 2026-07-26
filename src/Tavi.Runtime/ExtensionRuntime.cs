using Tavi.Application.Extensions;
using Tavi.Extensibility;
using Tavi.Infrastructure.Persistence;

namespace Tavi.Runtime;

/// <summary>
/// 持有进程级冻结 Extension 配置，并串行管理可由前端替换的期望设置。
/// 活动配置在进程启动时冻结；期望配置持久化成功后才替换。
/// </summary>
public sealed class ExtensionRuntime : IHostedService
{
    private readonly IConfiguration _configuration;
    private readonly IReadOnlyList<ITaviPlugin> _plugins;
    private readonly SemaphoreSlim _accessGate = new(1, 1);
    private JsonFileExtensionSettingsStore? _store;
    private ExtensionService? _service;
    private ExtensionSession? _activeSession;
    private ExtensionSession? _desiredSession;
    private long _revision;
    private int _stopped;

    public ExtensionRuntime(IConfiguration configuration, IEnumerable<ITaviPlugin> plugins)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _plugins = plugins?.ToArray() ?? throw new ArgumentNullException(nameof(plugins));
    }

    /// <summary>获取供进程内 Application Session 使用的启动期冻结 Runtime。</summary>
    public FrozenModuleRuntime Frozen { get; private set; } = null!;

    /// <summary>从固定部署目录加载 Module，并恢复、校验和冻结活动配置。</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        string settingsDirectory =
            _configuration["Tavi:SettingsDirectory"]
            ?? Environment.GetEnvironmentVariable("TAVI_SETTINGS_DIRECTORY")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tavi", "Settings");
        string modulesDirectory = Path.Combine(AppContext.BaseDirectory, "Modules");
        _store = new JsonFileExtensionSettingsStore(Path.Combine(settingsDirectory, "extensions.json"));
        _service = new ExtensionService(_store, _plugins);
        ExtensionSession session = Directory.Exists(modulesDirectory)
            ? await _service.CreateSessionAsync(modulesDirectory, cancellationToken)
            : new ExtensionSession([], await _store.LoadAsync(cancellationToken), _plugins);
        Frozen = session.Freeze();
        _activeSession = session;
        _desiredSession = session;
        _revision = 1;
    }

    /// <summary>读取活动与期望 Extension 配置的完整权威快照。</summary>
    public async Task<ExtensionRuntimeSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _accessGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfStopped();
            return CreateSnapshot();
        }
        finally
        {
            _accessGate.Release();
        }
    }

    /// <summary>
    /// 校验并原子保存完整候选配置；保存失败时内存期望状态保持不变。
    /// 当前活动 Config/Catalog 不在进程内替换，因此实际差异会明确报告需要重启。
    /// </summary>
    public async Task<ExtensionRuntimeSnapshot> UpdateAsync(
        ExtensionRuntimeUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(update.Modules);
        await _accessGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfStopped();
            if (update.ExpectedRevision != _revision)
            {
                throw new ExtensionRuntimeException(
                    "TAVI.EXTENSIONS.SETTINGS.REVISION_CONFLICT",
                    TaviErrorCategory.Conflict,
                    $"Extension 设置版本冲突：期望 {update.ExpectedRevision}，实际 {_revision}。",
                    nameof(UpdateAsync));
            }

            ExtensionSession current = RequireDesiredSession();
            ExtensionSession candidate = current.CreateCandidate(
                update.Modules.Select(module =>
                    new ExtensionModuleConfiguration(module.Module, module.Enabled, module.Settings)));
            if (Equivalent(current, candidate))
                return CreateSnapshot();

            try
            {
                await RequireService().SaveAsync(candidate, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new ExtensionRuntimeException(
                    "TAVI.EXTENSIONS.SETTINGS.SAVE_FAILED",
                    TaviErrorCategory.Storage,
                    "Extension 设置无法保存。",
                    nameof(UpdateAsync),
                    isTransient: true,
                    innerException: exception);
            }

            _desiredSession = candidate;
            _revision = checked(_revision + 1);
            return CreateSnapshot();
        }
        finally
        {
            _accessGate.Release();
        }
    }

    /// <summary>幂等停止 Runtime 并释放设置 Store。</summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _accessGate.WaitAsync(cancellationToken);
        try
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0)
                return;
            _store?.Dispose();
        }
        finally
        {
            _accessGate.Release();
        }
    }

    private ExtensionRuntimeSnapshot CreateSnapshot()
    {
        ExtensionSession active = RequireActiveSession();
        ExtensionSession desired = RequireDesiredSession();
        bool restartRequired = !Equivalent(active, desired);
        ExtensionModuleRuntimeSnapshot[] modules = desired.Modules.Select(manifest =>
        {
            ModuleId module = manifest.Id;
            return new ExtensionModuleRuntimeSnapshot(
                manifest,
                active.IsEnabled(module),
                desired.IsEnabled(module),
                desired.GetSettingsSchema(module).Document,
                active.GetEffectiveSettings(module),
                desired.GetEffectiveSettings(module));
        }).ToArray();
        string message = restartRequired
            ? "期望配置已保存；当前进程继续使用启动期配置，重启后生效。"
            : "期望配置与当前活动配置一致。";
        return new ExtensionRuntimeSnapshot(_revision, restartRequired, message, modules);
    }

    private static bool Equivalent(ExtensionSession left, ExtensionSession right)
    {
        ModuleManifest[] leftModules = left.Modules.OrderBy(module => module.Id.Value, StringComparer.Ordinal).ToArray();
        ModuleManifest[] rightModules = right.Modules.OrderBy(module => module.Id.Value, StringComparer.Ordinal).ToArray();
        if (leftModules.Length != rightModules.Length ||
            !leftModules.Select(module => module.Id).SequenceEqual(rightModules.Select(module => module.Id)))
            return false;
        foreach (ModuleManifest manifest in leftModules)
        {
            ModuleId module = manifest.Id;
            if (left.IsEnabled(module) != right.IsEnabled(module) ||
                left.GetEffectiveSettings(module).GetRawText() != right.GetEffectiveSettings(module).GetRawText())
                return false;
        }
        return true;
    }

    private ExtensionService RequireService() =>
        _service ?? throw new InvalidOperationException("Extension Runtime 尚未初始化。");

    private ExtensionSession RequireActiveSession() =>
        _activeSession ?? throw new InvalidOperationException("Extension Runtime 尚未初始化。");

    private ExtensionSession RequireDesiredSession() =>
        _desiredSession ?? throw new InvalidOperationException("Extension Runtime 尚未初始化。");

    private void ThrowIfStopped()
    {
        if (Volatile.Read(ref _stopped) != 0)
            throw new ObjectDisposedException(nameof(ExtensionRuntime));
    }
}
