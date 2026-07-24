using System.Text.Json;
using Tavi.Application.World;
using Tavi.Domain.World;
using Tavi.Host.Mapping;
using Tavi.Host.Runtime;
using Tavi.Host.ViewModels;

namespace Tavi.Host.Endpoints;

internal static class WorldEndpoints
{
    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web);

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
        world.MapGet("/staging", GetWorldAsync);
        world.MapPost("/staging/commit", CommitStagedAsync);
        world.MapDelete("/staging/invalid", DeleteInvalidStagedAsync);
        world.MapDelete("/staging/{changeId:guid}", DeleteStagedAsync);
        world.MapGet("/events", StreamEventsAsync);
        return endpoints;
    }

    private static Task<WorldGraphViewModel> GetWorldAsync(WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(WorldViewModelMapper.ToGraph, cancellationToken);

    private static Task<WorldStagingResultViewModel> AddAnchorAsync(AddAnchorRequest request, WorldRuntime runtime, CancellationToken cancellationToken)
    {
        return runtime.ExecuteAsync(session =>
        {
            AnchorType type = ParseAnchorType(request.Type);
            AddAnchorOperation operation = WorldOperations.AddAnchor(request.Name, request.Description, type);
            Guid id = session.Stage(operation);
            return new WorldStagingResultViewModel([id], WorldViewModelMapper.ToGraph(session));
        }, cancellationToken);
    }

    private static Task<WorldStagingResultViewModel> UpdateAnchorAsync(Guid anchorId, UpdateAnchorRequest request, WorldRuntime runtime, CancellationToken cancellationToken)
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
            Guid id = session.Stage(new WorldChangeSet(operations));
            return new WorldStagingResultViewModel([id], WorldViewModelMapper.ToGraph(session));
        }, cancellationToken);
    }

    private static Task<WorldStagingResultViewModel> RemoveAnchorAsync(Guid anchorId, Guid expectedStateId, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session => Stage(session, new RemoveAnchorOperation(anchorId)), cancellationToken);

    private static Task<WorldStagingResultViewModel> AddRelationAsync(AddRelationRequest request, WorldRuntime runtime, CancellationToken cancellationToken)
    {
        return runtime.ExecuteAsync(session =>
        {
            AddRelationOperation operation = WorldOperations.AddRelation(request.Name, request.Description, request.SourceId, request.TargetId, request.DomainCharacterId);
            return Stage(session, operation);
        }, cancellationToken);
    }

    private static Task<WorldStagingResultViewModel> UpdateRelationAsync(Guid relationId, UpdateRelationRequest request, WorldRuntime runtime, CancellationToken cancellationToken)
    {
        return runtime.ExecuteAsync(session =>
        {
            var operations = new List<WorldOperation>(2);
            if (request.Name is not null)
                operations.Add(new UpdateRelationNameOperation(relationId, request.Name));
            if (request.Description is not null)
                operations.Add(new UpdateRelationDescriptionOperation(relationId, request.Description));
            RequireOperations(operations);
            Guid id = session.Stage(new WorldChangeSet(operations));
            return new WorldStagingResultViewModel([id], WorldViewModelMapper.ToGraph(session));
        }, cancellationToken);
    }

    private static Task<WorldStagingResultViewModel> RemoveRelationAsync(Guid relationId, Guid expectedStateId, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session => Stage(session, new RemoveRelationOperation(relationId)), cancellationToken);

    private static Task<WorldStagingResultViewModel> CreateSubWorldAsync(CreateSubWorldRequest request, WorldRuntime runtime, CancellationToken cancellationToken)
    {
        return runtime.ExecuteAsync(session =>
        {
            CreateSubWorldOperation operation = WorldOperations.CreateSubWorld(request.CharacterId);
            return Stage(session, operation);
        }, cancellationToken);
    }

    private static Task<WorldStagingResultViewModel> RemoveSubWorldAsync(Guid characterId, Guid expectedStateId, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session => Stage(session, new RemoveSubWorldOperation(characterId)), cancellationToken);

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
        int removed = session.DeleteInvalidStaged();
        return new WorldStagingResultViewModel([], WorldViewModelMapper.ToGraph(session));
    }, cancellationToken);

    private static Task<WorldCommitViewModel> UndoAsync(WorldStateRequest request, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session => WorldViewModelMapper.ToCommit(session.Undo(request.ExpectedStateId)), cancellationToken);

    private static Task<WorldCommitViewModel> RedoAsync(WorldStateRequest request, WorldRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(session => WorldViewModelMapper.ToCommit(session.Redo(request.ExpectedStateId)), cancellationToken);

    private static Task<SaveWorldViewModel> SaveAsync(WorldRuntime runtime, CancellationToken cancellationToken)
    {
        return runtime.ExecuteAsync(async session =>
        {
            await session.SaveAsync(cancellationToken);
            return new SaveWorldViewModel(session.StateId, session.IsDirty);
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
            await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(worldEvent, EventJsonOptions)}\n\n", cancellationToken);
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

    private static WorldStagingResultViewModel Stage(WorldSession session, WorldOperation operation)
    {
        Guid id = session.Stage(operation);
        return new WorldStagingResultViewModel([id], WorldViewModelMapper.ToGraph(session));
    }
}
