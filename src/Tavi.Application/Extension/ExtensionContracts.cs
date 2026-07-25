using Tavi.Application.Scenario;
using Tavi.Extensibility;

namespace Tavi.Application.Extension;

/// <summary>保存一个 Module 的期望启用状态和规范参数文本。</summary>
public sealed record ExtensionModuleSettings
{
    /// <summary>获取 Module 标识。</summary>
    public required ModuleId Module { get; init; }
    /// <summary>获取下一次 ScenarioSession 是否启用该 Module。</summary>
    public required bool Enabled { get; init; }
    /// <summary>获取覆盖 Module 默认值的参数；键不包含 Module 前缀。</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
}

/// <summary>表示可持久化的 Extension 设置文档。</summary>
public sealed record ExtensionSettings
{
    /// <summary>获取设置文档版本。</summary>
    public int Version { get; init; } = 1;
    /// <summary>获取各 Module 设置。</summary>
    public IReadOnlyList<ExtensionModuleSettings> Modules { get; init; } = [];
}

/// <summary>定义 Extension 启用状态和参数的异步持久化边界。</summary>
public interface IExtensionSettingsStore
{
    /// <summary>读取设置；尚未保存时返回 null。</summary>
    Task<ExtensionSettings?> LoadAsync(CancellationToken cancellationToken = default);
    /// <summary>保存完整设置。</summary>
    Task SaveAsync(ExtensionSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>保存创建 ScenarioSession 时使用的不可变 Module Catalog 和有效参数。</summary>
public sealed record FrozenExtensionSnapshot
{
    /// <summary>获取只包含已启用 Module 和受信 Plugin 的 Catalog。</summary>
    public required ScenarioModuleCatalog Catalog { get; init; }
    /// <summary>获取按 Module ID 索引的规范有效参数。</summary>
    public IReadOnlyDictionary<ModuleId, IReadOnlyDictionary<string, string>> Parameters { get; init; } = new Dictionary<ModuleId, IReadOnlyDictionary<string, string>>();
}
