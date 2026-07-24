using System.Text.Json;
using Tavi.Application.World;
using Tavi.Domain.World;
using Tavi.Host.Mapping;
using Tavi.Host.Runtime;
using Tavi.Host.ViewModels;

namespace Tavi.Host.Endpoints;

/// <summary>映射 World 断言图、暂存、历史、保存和事件 HTTP 边界。</summary>
internal static class WorldEndpoints
{
    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web);

    internal static IEndpointRouteBuilder MapWorldEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder world = endpoints.MapGroup("/api/v1/world");
        world.MapGet("/", GetWorldAsync);
        world.MapPost("/elements", AddElementAsync);
        world.MapPatch("/elements/{elementId:guid}", UpdateElementAsync);
        world.MapDelete("/elements/{elementId:guid}", RemoveElementAsync);
        world.MapPost("/scopes", AddScopeAsync);
        world.MapPatch("/scopes/{scopeId:guid}", UpdateScopeAsync);
        world.MapDelete("/scopes/{scopeId:guid}", RemoveScopeAsync);
        world.MapPost("/aspects", AddAspectAsync);
        world.MapPatch("/aspects/{aspectId:guid}", UpdateAspectAsync);
        world.MapDelete("/aspects/{aspectId:guid}", RemoveAspectAsync);
        world.MapPost("/relations", AddRelationAsync);
        world.MapPatch("/relations/{relationId:guid}", UpdateRelationAsync);
        world.MapDelete("/relations/{relationId:guid}", RemoveRelationAsync);
        world.MapPost("/undo", UndoAsync);
        world.MapPost("/redo", RedoAsync);
        world.MapPost("/save", SaveAsync);
        world.MapGet("/staging", GetWorldAsync);
        world.MapPost("/staging/commit", CommitStagedAsync);
        world.MapDelete("/staging/invalid", DeleteInvalidStagedAsync);
        world.MapDelete("/staging/{changeId:guid}", DeleteStagedAsync);
        world.MapGet("/events", StreamEventsAsync);
        return endpoints;
    }

    private static Task<WorldGraphViewModel> GetWorldAsync(WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(WorldViewModelMapper.ToGraph, cancellationToken);

    private static Task<WorldStagingResultViewModel> AddElementAsync(AddElementRequest request, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session => Stage(session, WorldOperations.AddElement(request.Name, request.Description, new ElementType(request.Type)), request.ExpectedStateId), cancellationToken);

    private static Task<WorldStagingResultViewModel> UpdateElementAsync(Guid elementId, UpdateElementRequest request, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session =>
    {
        RequireExpectedState(session, request.ExpectedStateId);
        var operations = new List<WorldOperation>(3);
        if (request.Name is not null)
            operations.Add(new UpdateElementNameOperation(elementId, request.Name));
        if (request.Description is not null)
            operations.Add(new UpdateElementDescriptionOperation(elementId, request.Description));
        if (request.Type is not null)
            operations.Add(new UpdateElementTypeOperation(elementId, new ElementType(request.Type)));
        return Stage(session, operations);
    }, cancellationToken);

    private static Task<WorldStagingResultViewModel> RemoveElementAsync(Guid elementId, Guid expectedStateId, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session => Stage(session, new RemoveElementOperation(elementId), expectedStateId), cancellationToken);

    private static Task<WorldStagingResultViewModel> AddScopeAsync(AddScopeRequest request, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session => Stage(session, WorldOperations.AddScope(request.Name, request.Description, request.Quantity, new ScopeType(request.Type), request.OwnerElementId), request.ExpectedStateId), cancellationToken);

    private static Task<WorldStagingResultViewModel> UpdateScopeAsync(Guid scopeId, UpdateScopeRequest request, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session =>
    {
        RequireExpectedState(session, request.ExpectedStateId);
        var operations = new List<WorldOperation>(4);
        if (request.Name is not null)
            operations.Add(new UpdateScopeNameOperation(scopeId, request.Name));
        if (request.Description is not null)
            operations.Add(new UpdateScopeDescriptionOperation(scopeId, request.Description));
        if (request.Quantity.HasValue)
            operations.Add(new UpdateScopeQuantityOperation(scopeId, request.Quantity.Value));
        if (request.Type is not null)
            operations.Add(new UpdateScopeTypeOperation(scopeId, new ScopeType(request.Type)));
        return Stage(session, operations);
    }, cancellationToken);

    private static Task<WorldStagingResultViewModel> RemoveScopeAsync(Guid scopeId, Guid expectedStateId, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session => Stage(session, new RemoveScopeOperation(scopeId), expectedStateId), cancellationToken);

    private static Task<WorldStagingResultViewModel> AddAspectAsync(AddAspectRequest request, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session => Stage(session, WorldOperations.AddAspect(request.Name, request.Description, request.Quantity, new AspectType(request.Type), request.ElementId, request.ScopeId), request.ExpectedStateId), cancellationToken);

    private static Task<WorldStagingResultViewModel> UpdateAspectAsync(Guid aspectId, UpdateAspectRequest request, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session =>
    {
        RequireExpectedState(session, request.ExpectedStateId);
        var operations = new List<WorldOperation>(4);
        if (request.Name is not null)
            operations.Add(new UpdateAspectNameOperation(aspectId, request.Name));
        if (request.Description is not null)
            operations.Add(new UpdateAspectDescriptionOperation(aspectId, request.Description));
        if (request.Quantity.HasValue)
            operations.Add(new UpdateAspectQuantityOperation(aspectId, request.Quantity.Value));
        if (request.Type is not null)
            operations.Add(new UpdateAspectTypeOperation(aspectId, new AspectType(request.Type)));
        return Stage(session, operations);
    }, cancellationToken);

    private static Task<WorldStagingResultViewModel> RemoveAspectAsync(Guid aspectId, Guid expectedStateId, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session => Stage(session, new RemoveAspectOperation(aspectId), expectedStateId), cancellationToken);

    private static Task<WorldStagingResultViewModel> AddRelationAsync(AddRelationRequest request, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session => Stage(session, WorldOperations.AddRelation(request.Name, request.Description, request.Quantity, new RelationType(request.Type), request.SourceElementId, request.TargetElementId, request.ScopeId), request.ExpectedStateId), cancellationToken);

    private static Task<WorldStagingResultViewModel> UpdateRelationAsync(Guid relationId, UpdateRelationRequest request, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session =>
    {
        RequireExpectedState(session, request.ExpectedStateId);
        var operations = new List<WorldOperation>(4);
        if (request.Name is not null)
            operations.Add(new UpdateRelationNameOperation(relationId, request.Name));
        if (request.Description is not null)
            operations.Add(new UpdateRelationDescriptionOperation(relationId, request.Description));
        if (request.Quantity.HasValue)
            operations.Add(new UpdateRelationQuantityOperation(relationId, request.Quantity.Value));
        if (request.Type is not null)
            operations.Add(new UpdateRelationTypeOperation(relationId, new RelationType(request.Type)));
        return Stage(session, operations);
    }, cancellationToken);

    private static Task<WorldStagingResultViewModel> RemoveRelationAsync(Guid relationId, Guid expectedStateId, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session => Stage(session, new RemoveRelationOperation(relationId), expectedStateId), cancellationToken);

    private static Task<WorldCommitViewModel> CommitStagedAsync(CommitStagedRequest request, WorldRuntime runtime, GuidanceRuntime guidance, CancellationToken cancellationToken)
    {
        if (guidance.IsGenerating)
            throw new InvalidOperationException("Guidance 正在生成；请先停止生成，再提交真实 World。");
        return runtime.ExecuteAsync(session => WorldViewModelMapper.ToCommit(session.CommitStaged(request.ChangeIds, request.ExpectedStateId).Commit), cancellationToken);
    }

    private static Task<WorldStagingResultViewModel> DeleteStagedAsync(Guid changeId, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session =>
    {
        if (!session.DeleteStaged(changeId))
            throw new ArgumentException($"不存在暂存项 {changeId}。", nameof(changeId));
        return new WorldStagingResultViewModel([changeId], WorldViewModelMapper.ToGraph(session));
    }, cancellationToken);

    private static Task<WorldStagingResultViewModel> DeleteInvalidStagedAsync(WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session =>
    {
        session.DeleteInvalidStaged();
        return new WorldStagingResultViewModel([], WorldViewModelMapper.ToGraph(session));
    }, cancellationToken);

    private static Task<WorldCommitViewModel> UndoAsync(WorldStateRequest request, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session => WorldViewModelMapper.ToCommit(session.Undo(request.ExpectedStateId)), cancellationToken);
    private static Task<WorldCommitViewModel> RedoAsync(WorldStateRequest request, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session => WorldViewModelMapper.ToCommit(session.Redo(request.ExpectedStateId)), cancellationToken);

    private static Task<SaveWorldViewModel> SaveAsync(WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(async session =>
    {
        await session.SaveAsync(cancellationToken);
        return new SaveWorldViewModel(session.StateId, session.IsDirty);
    }, cancellationToken);

    private static async Task StreamEventsAsync(HttpContext context, WorldEventBroker broker, CancellationToken cancellationToken)
    {
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        await foreach (WorldEventViewModel worldEvent in broker.SubscribeAsync(cancellationToken))
        {
            await context.Response.WriteAsync($"event: {worldEvent.Type}\n", cancellationToken);
            await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(worldEvent, EventJsonOptions)}\n\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
        }
    }

    private static WorldStagingResultViewModel Stage(IWorldService service, WorldOperation operation, Guid expectedStateId)
    {
        RequireExpectedState(service, expectedStateId);
        return Stage(service, [operation]);
    }

    private static WorldStagingResultViewModel Stage(IWorldService service, IReadOnlyCollection<WorldOperation> operations)
    {
        if (operations.Count == 0)
            throw new ArgumentException("更新请求至少需要包含一个可修改属性。");
        Guid id = service.Stage(new WorldChangeSet(operations));
        return new WorldStagingResultViewModel([id], WorldViewModelMapper.ToGraph(service));
    }

    private static void RequireExpectedState(IWorldService service, Guid expectedStateId)
    {
        if (expectedStateId != service.StateId)
            throw new WorldStateConflictException(expectedStateId, service.StateId);
    }
}
