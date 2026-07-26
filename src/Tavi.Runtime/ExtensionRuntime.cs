using Tavi.Application.Extensions;
using Tavi.Extensibility;
using Tavi.Infrastructure.Persistence;

namespace Tavi.Runtime;

/// <summary>加载 Extension 设置与 Module Package，并为所有 Application Session 持有同一份冻结 Runtime。</summary>
public sealed class ExtensionRuntime : IHostedService
{
    private readonly IConfiguration _configuration;
    private readonly IReadOnlyList<ITaviPlugin> _plugins;
    private JsonFileExtensionSettingsStore? _store;
    private ExtensionService? _service;
    private int _stopped;

    /// <summary>创建使用宿主配置和显式注入受信 Plugin 的 Extension Runtime。</summary>
    public ExtensionRuntime(IConfiguration configuration, IEnumerable<ITaviPlugin> plugins)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _plugins = plugins?.ToArray() ?? throw new ArgumentNullException(nameof(plugins));
    }

    /// <summary>获取当前期望配置会话。</summary>
    public ExtensionSession Session { get; private set; } = null!;
    /// <summary>获取供 World、Scenario、Guidance 和 Writing 共用的冻结 Module Runtime。</summary>
    public FrozenModuleRuntime Frozen { get; private set; } = null!;

    /// <summary>加载持久化设置、Package 和受信 Plugin，并冻结活动 Runtime。</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        string settingsDirectory = _configuration["Tavi:SettingsDirectory"] ?? Environment.GetEnvironmentVariable("TAVI_SETTINGS_DIRECTORY") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tavi", "Settings");
        string modulesDirectory = _configuration["Tavi:ModulesDirectory"] ?? Environment.GetEnvironmentVariable("TAVI_MODULES_DIRECTORY") ?? FindModulesDirectory() ?? Path.Combine(AppContext.BaseDirectory, "Modules");
        _store = new JsonFileExtensionSettingsStore(Path.Combine(settingsDirectory, "extensions.json"));
        _service = new ExtensionService(_store, _plugins);
        Session = Directory.Exists(modulesDirectory) ? await _service.CreateSessionAsync(modulesDirectory, cancellationToken) : new ExtensionSession([], await _store.LoadAsync(cancellationToken), _plugins);
        Frozen = Session.Freeze();
    }

    /// <summary>幂等释放设置 Store；设置变更由显式保存用例持久化。</summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return Task.CompletedTask;
        _store?.Dispose();
        return Task.CompletedTask;
    }

    private static string? FindModulesDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "Modules");
            if (Directory.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        return null;
    }
}
