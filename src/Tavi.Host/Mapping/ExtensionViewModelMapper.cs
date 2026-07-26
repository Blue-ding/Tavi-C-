using Tavi.Extensibility;
using Tavi.Host.ViewModels;
using Tavi.Runtime;

namespace Tavi.Host.Mapping;

/// <summary>在 Extension HTTP 契约与 Runtime 契约之间转换。</summary>
internal static class ExtensionViewModelMapper
{
    internal static ExtensionWorkspaceViewModel ToViewModel(ExtensionRuntimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new ExtensionWorkspaceViewModel(
            snapshot.Revision,
            snapshot.RestartRequired,
            snapshot.Message,
            snapshot.Modules.Select(module => new ExtensionModuleViewModel(
                module.Manifest.Id.Value,
                module.Manifest.Version.Value,
                module.Manifest.Name,
                module.Manifest.Description,
                module.ActiveEnabled,
                module.DesiredEnabled,
                module.Manifest.Dependencies.Select(dependency =>
                    new ExtensionDependencyViewModel(
                        dependency.Id.Value,
                        dependency.MinimumVersion.Value)).ToArray(),
                module.SettingsSchema.Clone(),
                module.ActiveSettings.Clone(),
                module.DesiredSettings.Clone())).ToArray());
    }

    internal static ExtensionRuntimeUpdate ToUpdate(UpdateExtensionsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Modules);
        return new ExtensionRuntimeUpdate(
            request.ExpectedRevision,
            request.Modules.Select(module =>
            {
                ArgumentNullException.ThrowIfNull(module);
                return new ExtensionModuleRuntimeUpdate(
                    new ModuleId(module.Id),
                    module.Enabled,
                    module.Settings.Clone());
            }).ToArray());
    }
}
