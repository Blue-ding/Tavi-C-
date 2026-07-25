using Tavi.Application.Extensions.Loading;
using Tavi.Extensibility;

namespace Tavi.Application.Extensions;

/// <summary>负责加载 Module 包与持久化设置，并向宿主提供可由前端控制的 ExtensionSession。</summary>
public sealed class ExtensionService
{
    private readonly IExtensionSettingsStore _store;
    private readonly IReadOnlyList<ITaviPlugin> _trustedPlugins;

    /// <summary>使用显式设置 Store 和受信 Plugin 创建服务；服务不会根据 Manifest 自动执行程序集。</summary>
    public ExtensionService(IExtensionSettingsStore store, IEnumerable<ITaviPlugin>? trustedPlugins = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _trustedPlugins = (trustedPlugins ?? []).ToArray();
    }

    /// <summary>加载 Module 根目录及设置并创建 ExtensionSession。</summary>
    public async Task<ExtensionSession> CreateSessionAsync(string modulesDirectory, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ModulePackageDefinition> packages = ModulePackageLoader.LoadAll(modulesDirectory);
        ExtensionSettings? settings = await _store.LoadAsync(cancellationToken);
        return new ExtensionSession(packages, settings, _trustedPlugins);
    }

    /// <summary>保存 ExtensionSession 当前期望设置；已冻结的 ScenarioSession 不会因此改变。</summary>
    public Task SaveAsync(ExtensionSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return _store.SaveAsync(session.CreateSettings(), cancellationToken);
    }
}
