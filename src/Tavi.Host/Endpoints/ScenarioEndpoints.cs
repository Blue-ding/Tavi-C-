using Tavi.Application.Scenario;
using Tavi.Extensibility;
using Tavi.Host.Mapping;
using Tavi.Host.ViewModels;
using Tavi.Runtime;

namespace Tavi.Host.Endpoints;

internal static class ScenarioEndpoints
{
    internal static IEndpointRouteBuilder MapScenarioEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder scenario = endpoints.MapGroup("/api/v1/scenario");
        scenario.MapGet("/", GetAsync);
        scenario.MapPost("/scenes", CreateSceneAsync);
        scenario.MapPut("/scenes/{sceneId:guid}/bindings/{slotId}", SetBindingAsync);
        scenario.MapDelete("/scenes/{sceneId:guid}/bindings/{slotId}", ClearBindingAsync);
        scenario.MapPost("/scenes/{sceneId:guid}/processing", BeginProcessingAsync);
        scenario.MapPost("/scenes/{sceneId:guid}/settle-rules", SettleRulesAsync);
        scenario.MapDelete("/scenes/{sceneId:guid}", RemoveSceneAsync);
        scenario.MapPost("/scenes/clear-settled", ClearSettledAsync);
        scenario.MapPost("/undo", UndoAsync);
        scenario.MapPost("/redo", RedoAsync);
        scenario.MapPost("/save", SaveAsync);
        return endpoints;
    }

    private static Task<ScenarioWorkspaceViewModel> GetAsync(long? randomSeed, ScenarioRuntime runtime, CancellationToken cancellationToken) => WorkspaceAsync(runtime, randomSeed ?? 0, cancellationToken);

    private static Task<ScenarioWorkspaceViewModel> CreateSceneAsync(CreateSceneRequest request, ScenarioRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(async workspace =>
    {
        IReadOnlyList<SceneDefinition> definitions = await workspace.Queries.GetSceneDefinitionsAsync(request.RandomSeed, cancellationToken);
        SceneDefinition definition = definitions.SingleOrDefault(value => value.Id.Value == request.DefinitionId) ?? throw new KeyNotFoundException($"当前 Scenario 没有可用的 SceneDefinition {request.DefinitionId}。");
        _ = workspace.Commands.CreateScene(definition, request.ExpectedStateId);
        return ScenarioViewModelMapper.ToWorkspace(workspace, await workspace.Queries.GetSceneDefinitionsAsync(request.RandomSeed, cancellationToken));
    }, cancellationToken);

    private static Task<ScenarioWorkspaceViewModel> SetBindingAsync(Guid sceneId, string slotId, SetSceneBindingRequest request, ScenarioRuntime runtime, CancellationToken cancellationToken) => MutateAsync(runtime, commands => commands.SetSceneBinding(sceneId, slotId, request.ElementIds, request.ExpectedStateId), cancellationToken);
    private static Task<ScenarioWorkspaceViewModel> ClearBindingAsync(Guid sceneId, string slotId, Guid expectedStateId, ScenarioRuntime runtime, CancellationToken cancellationToken) => MutateAsync(runtime, commands => commands.ClearSceneBinding(sceneId, slotId, expectedStateId), cancellationToken);
    private static Task<ScenarioWorkspaceViewModel> BeginProcessingAsync(Guid sceneId, ScenarioStateRequest request, ScenarioRuntime runtime, CancellationToken cancellationToken) => MutateAsync(runtime, commands => commands.BeginSceneProcessing(sceneId, request.ExpectedStateId), cancellationToken);
    private static Task<ScenarioWorkspaceViewModel> SettleRulesAsync(Guid sceneId, SettleSceneRequest request, ScenarioRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(async workspace =>
    {
        _ = await workspace.Commands.SettleSceneByRulesAsync(sceneId, request.RandomSeed, request.ExpectedStateId, cancellationToken);
        return ScenarioViewModelMapper.ToWorkspace(workspace, await workspace.Queries.GetSceneDefinitionsAsync(request.RandomSeed, cancellationToken));
    }, cancellationToken);
    private static Task<ScenarioWorkspaceViewModel> RemoveSceneAsync(Guid sceneId, Guid expectedStateId, ScenarioRuntime runtime, CancellationToken cancellationToken) => MutateAsync(runtime, commands => commands.RemoveScene(sceneId, expectedStateId), cancellationToken);
    private static Task<ScenarioWorkspaceViewModel> ClearSettledAsync(ScenarioStateRequest request, ScenarioRuntime runtime, CancellationToken cancellationToken) => MutateAsync(runtime, commands => commands.ClearSettledScenes(request.ExpectedStateId), cancellationToken);
    private static Task<ScenarioWorkspaceViewModel> UndoAsync(ScenarioStateRequest request, ScenarioRuntime runtime, CancellationToken cancellationToken) => MutateAsync(runtime, commands => commands.Undo(request.ExpectedStateId), cancellationToken);
    private static Task<ScenarioWorkspaceViewModel> RedoAsync(ScenarioStateRequest request, ScenarioRuntime runtime, CancellationToken cancellationToken) => MutateAsync(runtime, commands => commands.Redo(request.ExpectedStateId), cancellationToken);

    private static Task<ScenarioWorkspaceViewModel> SaveAsync(ScenarioRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(async workspace =>
    {
        await workspace.Commands.SaveAsync(cancellationToken);
        return ScenarioViewModelMapper.ToWorkspace(workspace, await workspace.Queries.GetSceneDefinitionsAsync(0, cancellationToken));
    }, cancellationToken);

    private static Task<ScenarioWorkspaceViewModel> WorkspaceAsync(ScenarioRuntime runtime, long randomSeed, CancellationToken cancellationToken) => runtime.ExecuteAsync(async workspace => ScenarioViewModelMapper.ToWorkspace(workspace, await workspace.Queries.GetSceneDefinitionsAsync(randomSeed, cancellationToken)), cancellationToken);

    private static Task<ScenarioWorkspaceViewModel> MutateAsync(ScenarioRuntime runtime, Func<ScenarioCommands, ScenarioCommitResult> operation, CancellationToken cancellationToken) => runtime.ExecuteAsync(async workspace =>
    {
        _ = operation(workspace.Commands);
        return ScenarioViewModelMapper.ToWorkspace(workspace, await workspace.Queries.GetSceneDefinitionsAsync(0, cancellationToken));
    }, cancellationToken);
}
