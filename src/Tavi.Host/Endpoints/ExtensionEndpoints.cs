using Tavi.Host.Mapping;
using Tavi.Host.ViewModels;
using Tavi.Runtime;

namespace Tavi.Host.Endpoints;

/// <summary>映射 Extension Catalog、活动状态和期望配置 HTTP 边界。</summary>
internal static class ExtensionEndpoints
{
    internal static IEndpointRouteBuilder MapExtensionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder extensions = endpoints.MapGroup("/api/v1/extensions");
        extensions.MapGet("/", GetAsync);
        extensions.MapPut("/settings", UpdateAsync);
        return endpoints;
    }

    private static async Task<ExtensionWorkspaceViewModel> GetAsync(
        ExtensionRuntime runtime,
        CancellationToken cancellationToken) =>
        ExtensionViewModelMapper.ToViewModel(
            await runtime.GetSnapshotAsync(cancellationToken));

    private static async Task<ExtensionWorkspaceViewModel> UpdateAsync(
        UpdateExtensionsRequest request,
        ExtensionRuntime runtime,
        CancellationToken cancellationToken) =>
        ExtensionViewModelMapper.ToViewModel(
            await runtime.UpdateAsync(
                ExtensionViewModelMapper.ToUpdate(request),
                cancellationToken));
}
