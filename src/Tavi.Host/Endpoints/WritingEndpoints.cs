using System.Text.Json;
using Tavi.Application.Writing;
using Tavi.Domain.Story;
using Tavi.Host.Mapping;
using Tavi.Host.ViewModels;
using Tavi.Runtime;

namespace Tavi.Host.Endpoints;

internal static class WritingEndpoints
{
    private static readonly JsonSerializerOptions EventJsonOptions =
        new(JsonSerializerDefaults.Web);

    internal static IEndpointRouteBuilder MapWritingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder writing = endpoints.MapGroup("/api/v1/writing");
        writing.MapGet("/", GetWorkspaceAsync);
        writing.MapPost("/manuscripts", CreateAsync);
        writing.MapGet("/manuscripts/{manuscriptId:guid}", GetManuscriptAsync);
        writing.MapPatch("/manuscripts/{manuscriptId:guid}/title", RenameArchivedAsync);
        writing.MapDelete("/manuscripts/{manuscriptId:guid}", DeleteArchivedAsync);
        writing.MapPatch("/session/title", RenameActiveAsync);
        writing.MapPost("/session/paragraphs", InsertParagraphAsync);
        writing.MapPatch("/session/paragraphs/{paragraphId:guid}", UpdateParagraphAsync);
        writing.MapDelete("/session/paragraphs/{paragraphId:guid}", RemoveParagraphAsync);
        writing.MapPost("/session/undo", UndoAsync);
        writing.MapPost("/session/redo", RedoAsync);
        writing.MapPost("/session/save", SaveAsync);
        writing.MapPost("/session/archive", ArchiveAsync);
        writing.MapGet("/events", StreamEventsAsync);
        return endpoints;
    }

    private static Task<WritingWorkspaceViewModel> GetWorkspaceAsync(WritingRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(async workspace => new WritingWorkspaceViewModel((await workspace.Queries.ListAsync(cancellationToken)).Select(WritingViewModelMapper.ToSummary).ToArray(), WritingViewModelMapper.ToSnapshot(workspace.Queries.CreateSnapshot())), cancellationToken);
    private static Task<WritingSnapshotViewModel> CreateAsync(CreateManuscriptRequest request, WritingRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(async workspace => WritingViewModelMapper.ToSnapshot(await workspace.Commands.CreateAsync(request.Title, cancellationToken)), cancellationToken);
    private static Task<ManuscriptViewModel> GetManuscriptAsync(Guid manuscriptId, WritingRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(async workspace => WritingViewModelMapper.ToManuscript(await workspace.Queries.GetAsync(manuscriptId, cancellationToken)), cancellationToken);
    private static Task<ManuscriptViewModel> RenameArchivedAsync(Guid manuscriptId, RenameManuscriptRequest request, WritingRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(async workspace => WritingViewModelMapper.ToManuscript(await workspace.Commands.RenameArchivedAsync(manuscriptId, request.Title, cancellationToken)), cancellationToken);
    private static Task<IResult> DeleteArchivedAsync(Guid manuscriptId, WritingRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(async workspace => { await workspace.Commands.DeleteArchivedAsync(manuscriptId, cancellationToken); return Results.NoContent(); }, cancellationToken);

    private static Task<WritingSnapshotViewModel> RenameActiveAsync(RenameManuscriptRequest request, WritingRuntime runtime, CancellationToken cancellationToken)
    {
        if (request.ExpectedStateId is null)
            throw new ArgumentException("修改活动手稿名称必须提供 expectedStateId。", nameof(request));
        return ApplyAsync(runtime, new RenameManuscriptOperation(request.Title), request.ExpectedStateId.Value, cancellationToken);
    }

    private static Task<WritingSnapshotViewModel> InsertParagraphAsync(InsertParagraphRequest request, WritingRuntime runtime, CancellationToken cancellationToken) => ApplyAsync(runtime, new InsertParagraphOperation(Guid.NewGuid(), request.Index, request.Text), request.ExpectedStateId, cancellationToken);
    private static Task<WritingSnapshotViewModel> UpdateParagraphAsync(Guid paragraphId, UpdateParagraphRequest request, WritingRuntime runtime, CancellationToken cancellationToken) => ApplyAsync(runtime, new UpdateParagraphOperation(paragraphId, request.Text), request.ExpectedStateId, cancellationToken);
    private static Task<WritingSnapshotViewModel> RemoveParagraphAsync(Guid paragraphId, Guid expectedStateId, WritingRuntime runtime, CancellationToken cancellationToken) => ApplyAsync(runtime, new RemoveParagraphOperation(paragraphId), expectedStateId, cancellationToken);
    private static Task<WritingSnapshotViewModel> UndoAsync(WritingStateRequest request, WritingRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(workspace => { workspace.Commands.Undo(request.ExpectedStateId); return WritingViewModelMapper.ToSnapshot(workspace.Queries.CreateSnapshot()); }, cancellationToken);
    private static Task<WritingSnapshotViewModel> RedoAsync(WritingStateRequest request, WritingRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(workspace => { workspace.Commands.Redo(request.ExpectedStateId); return WritingViewModelMapper.ToSnapshot(workspace.Queries.CreateSnapshot()); }, cancellationToken);
    private static Task<WritingSnapshotViewModel> SaveAsync(WritingRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(async workspace => { await workspace.Commands.SaveAsync(cancellationToken); return WritingViewModelMapper.ToSnapshot(workspace.Queries.CreateSnapshot()); }, cancellationToken);
    private static Task<ManuscriptViewModel> ArchiveAsync(WritingStateRequest request, WritingRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(async workspace => WritingViewModelMapper.ToManuscript(await workspace.Commands.ArchiveAsync(request.ExpectedStateId, cancellationToken)), cancellationToken);
    private static Task<WritingSnapshotViewModel> ApplyAsync(WritingRuntime runtime, ManuscriptOperation operation, Guid expectedStateId, CancellationToken cancellationToken) => runtime.ExecuteAsync(workspace => { workspace.Commands.Apply(operation, expectedStateId); return WritingViewModelMapper.ToSnapshot(workspace.Queries.CreateSnapshot()); }, cancellationToken);

    private static async Task StreamEventsAsync(
        HttpContext context,
        WritingEventBroker broker,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        await foreach (WritingRuntimeEvent runtimeEvent in
            broker.SubscribeAsync(cancellationToken))
        {
            var viewModel = new WritingEventViewModel(
                runtimeEvent.Type,
                runtimeEvent.StateId,
                runtimeEvent.IsDirty,
                runtimeEvent.CommitId,
                runtimeEvent.Operation,
                runtimeEvent.Error);
            await context.Response.WriteAsync(
                $"event: {viewModel.Type}\n",
                cancellationToken);
            await context.Response.WriteAsync(
                $"data: {JsonSerializer.Serialize(viewModel, EventJsonOptions)}\n\n",
                cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
        }
    }
}
