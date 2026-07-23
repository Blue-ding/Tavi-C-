using System.Text.Json;
using Tavi.Application.World;
using Tavi.Domain.World;
using Tavi.Host.Mapping;
using Tavi.Host.Runtime;
using Tavi.Host.ViewModels;

namespace Tavi.Host.Endpoints;

internal static class WorldEndpoints
{
    internal static IEndpointRouteBuilder MapWorldEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder world = endpoints.MapGroup("/api/v1/world");
        world.MapGet("/", GetWorldAsync);
        world.MapPost("/anchors", AddAnchorAsync);
        world.MapPatch("/anchors/{anchorId:guid}", UpdateAnchorAsync);
        world.MapDelete("/anchors/{anchorId:guid}", RemoveAnchorAsync);
        world.MapPost("/relations", AddRelationAsync);
        world.MapPatch("/relations/{relationId:guid}", UpdateRelationAsync);
        world.MapDelete("/relations/{relationId:guid}", RemoveRelationAsync);
        world.MapPost("/subworlds", CreateSubWorldAsync);
        world.MapDelete("/subworlds/{characterId:guid}", RemoveSubWorldAsync);
        world.MapPost("/undo", UndoAsync);
        world.MapPost("/redo", RedoAsync);
        world.MapPost("/save", SaveAsync);
        world.MapGet("/events", StreamEventsAsync);
        return endpoints;
    }

    private static Task<WorldGraphViewModel> GetWorldAsync(WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(WorldViewModelMapper.ToGraph, cancellationToken);

    private static Task<WorldCommitViewModel> AddAnchorAsync(AddAnchorRequest request, WorldRuntime runtime, CancellationToken cancellationToken)
    {
        return runtime.ExecuteAsync(session =>
        {
            AnchorType type = ParseAnchorType(request.Type);
            AddAnchorOperation operation = WorldOperations.AddAnchor(request.Name, request.Description, type);
            return WorldViewModelMapper.ToCommit(session.Apply(WorldOperations.Single(operation), request.ExpectedRevision), operation.AnchorId);
        }, cancellationToken);
    }

    private static Task<WorldCommitViewModel> UpdateAnchorAsync(Guid anchorId, UpdateAnchorRequest request, WorldRuntime runtime, CancellationToken cancellationToken)
    {
        return runtime.ExecuteAsync(session =>
        {
            var operations = new List<WorldOperation>(3);
            if (request.Name is not null)
                operations.Add(new UpdateAnchorNameOperation(anchorId, request.Name));
            if (request.Description is not null)
                operations.Add(new UpdateAnchorDescriptionOperation(anchorId, request.Description));
            if (request.Type is not null)
                operations.Add(new UpdateAnchorTypeOperation(anchorId, ParseAnchorType(request.Type)));
            RequireOperations(operations);
            return WorldViewModelMapper.ToCommit(session.Apply(new WorldChangeSet(operations), request.ExpectedRevision), anchorId);
        }, cancellationToken);
    }

    private static Task<WorldCommitViewModel> RemoveAnchorAsync(Guid anchorId, long expectedRevision, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session => WorldViewModelMapper.ToCommit(session.Apply(WorldOperations.Single(new RemoveAnchorOperation(anchorId)), expectedRevision), anchorId), cancellationToken);

    private static Task<WorldCommitViewModel> AddRelationAsync(AddRelationRequest request, WorldRuntime runtime, CancellationToken cancellationToken)
    {
        return runtime.ExecuteAsync(session =>
        {
            AddRelationOperation operation = WorldOperations.AddRelation(request.Name, request.Description, request.SourceId, request.TargetId, request.DomainCharacterId);
            return WorldViewModelMapper.ToCommit(session.Apply(WorldOperations.Single(operation), request.ExpectedRevision), operation.RelationId);
        }, cancellationToken);
    }

    private static Task<WorldCommitViewModel> UpdateRelationAsync(Guid relationId, UpdateRelationRequest request, WorldRuntime runtime, CancellationToken cancellationToken)
    {
        return runtime.ExecuteAsync(session =>
        {
            var operations = new List<WorldOperation>(2);
            if (request.Name is not null)
                operations.Add(new UpdateRelationNameOperation(relationId, request.Name));
            if (request.Description is not null)
                operations.Add(new UpdateRelationDescriptionOperation(relationId, request.Description));
            RequireOperations(operations);
            return WorldViewModelMapper.ToCommit(session.Apply(new WorldChangeSet(operations), request.ExpectedRevision), relationId);
        }, cancellationToken);
    }

    private static Task<WorldCommitViewModel> RemoveRelationAsync(Guid relationId, long expectedRevision, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session => WorldViewModelMapper.ToCommit(session.Apply(WorldOperations.Single(new RemoveRelationOperation(relationId)), expectedRevision), relationId), cancellationToken);

    private static Task<WorldCommitViewModel> CreateSubWorldAsync(CreateSubWorldRequest request, WorldRuntime runtime, CancellationToken cancellationToken)
    {
        return runtime.ExecuteAsync(session =>
        {
            CreateSubWorldOperation operation = WorldOperations.CreateSubWorld(request.CharacterId);
            return WorldViewModelMapper.ToCommit(session.Apply(WorldOperations.Single(operation), request.ExpectedRevision), operation.SubWorldId);
        }, cancellationToken);
    }

    private static Task<WorldCommitViewModel> RemoveSubWorldAsync(Guid characterId, long expectedRevision, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session => WorldViewModelMapper.ToCommit(session.Apply(WorldOperations.Single(new RemoveSubWorldOperation(characterId)), expectedRevision), characterId), cancellationToken);

    private static Task<WorldCommitViewModel> UndoAsync(RevisionRequest request, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session => WorldViewModelMapper.ToCommit(session.Undo(request.ExpectedRevision)), cancellationToken);

    private static Task<WorldCommitViewModel> RedoAsync(RevisionRequest request, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session => WorldViewModelMapper.ToCommit(session.Redo(request.ExpectedRevision)), cancellationToken);

    private static Task<SaveWorldViewModel> SaveAsync(WorldRuntime runtime, CancellationToken cancellationToken)
    {
        return runtime.ExecuteAsync(async session =>
        {
            await session.SaveAsync(cancellationToken);
            return new SaveWorldViewModel(session.Revision, session.IsDirty);
        }, cancellationToken);
    }

    private static async Task StreamEventsAsync(HttpContext context, WorldEventBroker broker, CancellationToken cancellationToken)
    {
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        await foreach (WorldEventViewModel worldEvent in broker.SubscribeAsync(cancellationToken))
        {
            await context.Response.WriteAsync($"event: {worldEvent.Type}\n", cancellationToken);
            await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(worldEvent)}\n\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
        }
    }

    private static AnchorType ParseAnchorType(string type)
    {
        if (!Enum.TryParse(type, true, out AnchorType parsed) || !Enum.IsDefined(parsed))
            throw new ArgumentException("Anchor 类型只允许 Character 或 Item。", nameof(type));
        return parsed;
    }

    private static void RequireOperations(IReadOnlyCollection<WorldOperation> operations)
    {
        if (operations.Count == 0)
            throw new ArgumentException("更新请求至少需要包含一个可修改属性。");
    }
}
