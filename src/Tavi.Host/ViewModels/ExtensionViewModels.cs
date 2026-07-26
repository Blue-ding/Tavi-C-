using System.Text.Json;

namespace Tavi.Host.ViewModels;

/// <summary>表示一个 Module 对另一个 Module 的最低版本依赖。</summary>
public sealed record ExtensionDependencyViewModel(
    string Id,
    string MinimumVersion);

/// <summary>表示前端配置页需要的一个完整 Module 状态。</summary>
public sealed record ExtensionModuleViewModel(
    string Id,
    string Version,
    string Name,
    string Description,
    bool ActiveEnabled,
    bool DesiredEnabled,
    IReadOnlyList<ExtensionDependencyViewModel> Dependencies,
    JsonElement SettingsSchema,
    JsonElement ActiveSettings,
    JsonElement DesiredSettings);

/// <summary>表示 Extension 配置页的完整权威状态。</summary>
public sealed record ExtensionWorkspaceViewModel(
    long Revision,
    bool RestartRequired,
    string Message,
    IReadOnlyList<ExtensionModuleViewModel> Modules);

/// <summary>表示候选配置中的一个完整 Module 状态。</summary>
public sealed record UpdateExtensionModuleRequest(
    string Id,
    bool Enabled,
    JsonElement Settings);

/// <summary>请求以乐观并发条件替换完整 Extension 期望配置。</summary>
public sealed record UpdateExtensionsRequest(
    long ExpectedRevision,
    IReadOnlyList<UpdateExtensionModuleRequest> Modules);
