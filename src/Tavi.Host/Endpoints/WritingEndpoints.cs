using Tavi.Application.Writing;
using Tavi.Domain.Story;
using Tavi.Host.Mapping;
using Tavi.Host.Runtime;
using Tavi.Host.ViewModels;

namespace Tavi.Host.Endpoints;

internal static class WritingEndpoints
{
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
        return endpoints;
    }

    private static Task<WritingWorkspaceViewModel> GetWorkspaceAsync(WritingRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(async service => new WritingWorkspaceViewModel((await service.ListAsync(cancellationToken)).Select(WritingViewModelMapper.ToSummary).ToArray(), WritingViewModelMapper.ToSnapshot(service.GetSnapshot())), cancellationToken);
    private static Task<WritingSnapshotViewModel> CreateAsync(CreateManuscriptRequest request, WritingRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(async service => WritingViewModelMapper.ToSnapshot(await service.CreateAsync(request.Title, cancellationToken)), cancellationToken);
    private static Task<ManuscriptViewModel> GetManuscriptAsync(Guid manuscriptId, WritingRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(async service => WritingViewModelMapper.ToManuscript(await service.GetAsync(manuscriptId, cancellationToken)), cancellationToken);
    private static Task<ManuscriptViewModel> RenameArchivedAsync(Guid manuscriptId, RenameManuscriptRequest request, WritingRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(async service => WritingViewModelMapper.ToManuscript(await service.RenameArchivedAsync(manuscriptId, request.Title, cancellationToken)), cancellationToken);
    private static Task<IResult> DeleteArchivedAsync(Guid manuscriptId, WritingRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(async service => { await service.DeleteArchivedAsync(manuscriptId, cancellationToken); return Results.NoContent(); }, cancellationToken);

    private static Task<WritingSnapshotViewModel> RenameActiveAsync(RenameManuscriptRequest request, WritingRuntime runtime, CancellationToken cancellationToken)
    {
        if (request.ExpectedStateId is null)
            throw new ArgumentException("修改活动手稿名称必须提供 expectedStateId。", nameof(request));
        return ApplyAsync(runtime, new RenameManuscriptOperation(request.Title), request.ExpectedStateId.Value, cancellationToken);
    }

    private static Task<WritingSnapshotViewModel> InsertParagraphAsync(InsertParagraphRequest request, WritingRuntime runtime, CancellationToken cancellationToken) => ApplyAsync(runtime, new InsertParagraphOperation(Guid.NewGuid(), request.Index, request.Text), request.ExpectedStateId, cancellationToken);
    private static Task<WritingSnapshotViewModel> UpdateParagraphAsync(Guid paragraphId, UpdateParagraphRequest request, WritingRuntime runtime, CancellationToken cancellationToken) => ApplyAsync(runtime, new UpdateParagraphOperation(paragraphId, request.Text), request.ExpectedStateId, cancellationToken);
    private static Task<WritingSnapshotViewModel> RemoveParagraphAsync(Guid paragraphId, Guid expectedStateId, WritingRuntime runtime, CancellationToken cancellationToken) => ApplyAsync(runtime, new RemoveParagraphOperation(paragraphId), expectedStateId, cancellationToken);
    private static Task<WritingSnapshotViewModel> UndoAsync(WritingStateRequest request, WritingRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(service => { service.Undo(request.ExpectedStateId); return WritingViewModelMapper.ToSnapshot(service.GetSnapshot()); }, cancellationToken);
    private static Task<WritingSnapshotViewModel> RedoAsync(WritingStateRequest request, WritingRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(service => { service.Redo(request.ExpectedStateId); return WritingViewModelMapper.ToSnapshot(service.GetSnapshot()); }, cancellationToken);
    private static Task<WritingSnapshotViewModel> SaveAsync(WritingRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(async service => { await service.SaveAsync(cancellationToken); return WritingViewModelMapper.ToSnapshot(service.GetSnapshot()); }, cancellationToken);
    private static Task<ManuscriptViewModel> ArchiveAsync(WritingStateRequest request, WritingRuntime runtime, CancellationToken cancellationToken) => runtime.ExecuteAsync(async service => WritingViewModelMapper.ToManuscript(await service.ArchiveAsync(request.ExpectedStateId, cancellationToken)), cancellationToken);
    private static Task<WritingSnapshotViewModel> ApplyAsync(WritingRuntime runtime, ManuscriptOperation operation, Guid expectedStateId, CancellationToken cancellationToken) => runtime.ExecuteAsync(service => { service.Apply(operation, expectedStateId); return WritingViewModelMapper.ToSnapshot(service.GetSnapshot()); }, cancellationToken);
}
