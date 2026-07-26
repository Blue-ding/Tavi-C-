using System.Text.Json;
using Tavi.Application.Performance;
using Tavi.Extensibility;
using Tavi.Host.Mapping;
using Tavi.Host.ViewModels;
using Tavi.Runtime;

namespace Tavi.Host.Endpoints;

/// <summary>映射 Performance、Beat、Scenario 回写、归档和事件 HTTP seam。</summary>
internal static class PerformanceEndpoints
{
    private static readonly JsonSerializerOptions EventJsonOptions =
        new(JsonSerializerDefaults.Web);

    internal static IEndpointRouteBuilder MapPerformanceEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder performance =
            endpoints.MapGroup("/api/v1/performance");
        performance.MapGet("/", GetAsync);
        performance.MapPost("/start", StartAsync);
        performance.MapGet("/archives", ListArchivesAsync);
        performance.MapGet("/archives/{performanceId:guid}", GetArchiveAsync);
        performance.MapPost("/beats", CreateBeatAsync);
        performance.MapPut(
            "/beats/{beatId:guid}/bindings/{slotId}",
            SetBindingAsync);
        performance.MapDelete(
            "/beats/{beatId:guid}/bindings/{slotId}",
            ClearBindingAsync);
        performance.MapPost(
            "/beats/{beatId:guid}/processing",
            BeginProcessingAsync);
        performance.MapPost(
            "/beats/{beatId:guid}/resolve",
            ResolveBeatAsync);
        performance.MapPost(
            "/beats/{beatId:guid}/publish",
            PublishBeatAsync);
        performance.MapPost("/complete", CompleteAsync);
        performance.MapPost("/abandon", AbandonAsync);
        performance.MapPost("/undo", UndoAsync);
        performance.MapPost("/redo", RedoAsync);
        performance.MapPost("/save", SaveAsync);
        performance.MapPost("/archive", ArchiveAsync);
        performance.MapGet("/events", StreamEventsAsync);
        return endpoints;
    }

    private static Task<PerformanceWorkspaceViewModel> GetAsync(
        long? randomSeed,
        PerformanceRuntime runtime,
        CancellationToken cancellationToken) =>
        WorkspaceAsync(runtime, randomSeed ?? 0, cancellationToken);

    private static async Task<PerformanceWorkspaceViewModel> StartAsync(
        StartPerformanceRequest request,
        ScenarioPerformanceRuntime coordinator,
        PerformanceRuntime runtime,
        CancellationToken cancellationToken)
    {
        _ = await coordinator.StartPerformanceAsync(
            request.SceneId,
            request.ExpectedScenarioStateId,
            request.RandomSeed,
            cancellationToken);
        return await WorkspaceAsync(
            runtime,
            request.RandomSeed,
            cancellationToken);
    }

    private static async Task<PerformanceArchiveSummaryViewModel[]>
        ListArchivesAsync(
            PerformanceRuntime runtime,
            CancellationToken cancellationToken) =>
        (await runtime.ListArchivedAsync(cancellationToken))
        .OrderBy(value => value.PerformanceId)
        .Select(PerformanceViewModelMapper.ToSummary)
        .ToArray();

    private static async Task<PerformanceSnapshotViewModel> GetArchiveAsync(
        Guid performanceId,
        PerformanceRuntime runtime,
        CancellationToken cancellationToken)
    {
        Tavi.Domain.Performance.PerformanceSnapshot? snapshot =
            await runtime.LoadArchivedAsync(performanceId, cancellationToken);
        return snapshot is null
            ? throw new KeyNotFoundException(
                $"不存在已归档 Performance {performanceId}。")
            : PerformanceViewModelMapper.ToSnapshot(snapshot);
    }

    private static Task<PerformanceWorkspaceViewModel> CreateBeatAsync(
        CreateBeatRequest request,
        PerformanceRuntime runtime,
        CancellationToken cancellationToken) =>
        runtime.ExecuteAsync(async workspace =>
        {
            IReadOnlyList<BeatDefinition> definitions =
                await workspace.Commands.GetBeatDefinitionsAsync(
                    request.RandomSeed,
                    cancellationToken);
            BeatDefinition definition = definitions.SingleOrDefault(value =>
                    value.Id.Value == request.DefinitionId)
                ?? throw new KeyNotFoundException(
                    $"当前 Performance 没有可用的 BeatDefinition {request.DefinitionId}。");
            _ = workspace.Commands.CreateBeat(
                definition,
                request.ExpectedStateId);
            return await ToWorkspaceAsync(
                workspace,
                request.RandomSeed,
                cancellationToken);
        }, cancellationToken);

    private static Task<PerformanceWorkspaceViewModel> SetBindingAsync(
        Guid beatId,
        string slotId,
        SetBeatBindingRequest request,
        PerformanceRuntime runtime,
        CancellationToken cancellationToken) =>
        MutateAsync(
            runtime,
            commands => commands.SetBeatBinding(
                beatId,
                slotId,
                request.ElementIds,
                request.ExpectedStateId),
            cancellationToken);

    private static Task<PerformanceWorkspaceViewModel> ClearBindingAsync(
        Guid beatId,
        string slotId,
        Guid expectedStateId,
        PerformanceRuntime runtime,
        CancellationToken cancellationToken) =>
        MutateAsync(
            runtime,
            commands => commands.ClearBeatBinding(
                beatId,
                slotId,
                expectedStateId),
            cancellationToken);

    private static Task<PerformanceWorkspaceViewModel> BeginProcessingAsync(
        Guid beatId,
        PerformanceStateRequest request,
        PerformanceRuntime runtime,
        CancellationToken cancellationToken) =>
        MutateAsync(
            runtime,
            commands => commands.BeginBeatProcessing(
                beatId,
                request.ExpectedStateId),
            cancellationToken);

    private static Task<PerformanceWorkspaceViewModel> ResolveBeatAsync(
        Guid beatId,
        ResolveBeatRequest request,
        PerformanceRuntime runtime,
        CancellationToken cancellationToken) =>
        runtime.ExecuteAsync(async workspace =>
        {
            _ = await workspace.Commands.ResolveBeatAsync(
                beatId,
                request.Interaction,
                request.RandomSeed,
                request.ExpectedStateId,
                cancellationToken);
            return await ToWorkspaceAsync(
                workspace,
                request.RandomSeed,
                cancellationToken);
        }, cancellationToken);

    private static Task<PerformanceWorkspaceViewModel> PublishBeatAsync(
        Guid beatId,
        PublishBeatRequest request,
        PerformanceRuntime runtime,
        CancellationToken cancellationToken) =>
        MutateAsync(
            runtime,
            commands => commands.PublishBeat(
                beatId,
                request.ExpectedStateId,
                request.ExpectedManuscriptStateId),
            cancellationToken);

    private static async Task<ScenarioPerformanceCompletionViewModel>
        CompleteAsync(
            CompletePerformanceRequest request,
            ScenarioPerformanceRuntime coordinator,
            PerformanceRuntime runtime,
            CancellationToken cancellationToken)
    {
        Tavi.Application.Scenario.ScenarioPerformanceCompletion completion =
            await coordinator.CompletePerformanceAsync(
                request.ExpectedScenarioStateId,
                request.ExpectedPerformanceStateId,
                cancellationToken);
        PerformanceWorkspaceViewModel workspace =
            await WorkspaceAsync(runtime, 0, cancellationToken);
        return new ScenarioPerformanceCompletionViewModel(
            completion.SceneId,
            completion.ScenarioCommit.StateId,
            completion.PerformanceCommit.StateId,
            workspace);
    }

    private static Task<PerformanceWorkspaceViewModel> AbandonAsync(
        PerformanceStateRequest request,
        PerformanceRuntime runtime,
        CancellationToken cancellationToken) =>
        MutateAsync(
            runtime,
            commands => commands.Abandon(request.ExpectedStateId),
            cancellationToken);

    private static Task<PerformanceWorkspaceViewModel> UndoAsync(
        PerformanceStateRequest request,
        PerformanceRuntime runtime,
        CancellationToken cancellationToken) =>
        MutateAsync(
            runtime,
            commands => commands.Undo(request.ExpectedStateId),
            cancellationToken);

    private static Task<PerformanceWorkspaceViewModel> RedoAsync(
        PerformanceStateRequest request,
        PerformanceRuntime runtime,
        CancellationToken cancellationToken) =>
        MutateAsync(
            runtime,
            commands => commands.Redo(request.ExpectedStateId),
            cancellationToken);

    private static Task<PerformanceWorkspaceViewModel> SaveAsync(
        PerformanceRuntime runtime,
        CancellationToken cancellationToken) =>
        runtime.ExecuteAsync(async workspace =>
        {
            await workspace.Commands.SaveAsync(cancellationToken);
            return await ToWorkspaceAsync(workspace, 0, cancellationToken);
        }, cancellationToken);

    private static async Task<PerformanceSnapshotViewModel> ArchiveAsync(
        PerformanceRuntime runtime,
        CancellationToken cancellationToken) =>
        PerformanceViewModelMapper.ToSnapshot(
            await runtime.ArchiveEndedAsync(cancellationToken));

    private static Task<PerformanceWorkspaceViewModel> MutateAsync(
        PerformanceRuntime runtime,
        Func<PerformanceCommands, PerformanceCommitResult> operation,
        CancellationToken cancellationToken) =>
        runtime.ExecuteAsync(async workspace =>
        {
            _ = operation(workspace.Commands);
            return await ToWorkspaceAsync(workspace, 0, cancellationToken);
        }, cancellationToken);

    private static async Task<PerformanceWorkspaceViewModel> WorkspaceAsync(
        PerformanceRuntime runtime,
        long randomSeed,
        CancellationToken cancellationToken)
    {
        if (!runtime.HasSession)
            return PerformanceViewModelMapper.EmptyWorkspace();
        return await runtime.ExecuteAsync(
            workspace => ToWorkspaceAsync(
                workspace,
                randomSeed,
                cancellationToken),
            cancellationToken);
    }

    private static async Task<PerformanceWorkspaceViewModel> ToWorkspaceAsync(
        IPerformanceWorkspace workspace,
        long randomSeed,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<BeatDefinition> definitions =
            workspace.Status == Tavi.Domain.Performance.PerformanceStatus.Active
                ? await workspace.Commands.GetBeatDefinitionsAsync(
                    randomSeed,
                    cancellationToken)
                : [];
        return PerformanceViewModelMapper.ToWorkspace(workspace, definitions);
    }

    private static async Task StreamEventsAsync(
        HttpContext context,
        PerformanceEventBroker broker,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        await foreach (PerformanceRuntimeEvent runtimeEvent in
            broker.SubscribeAsync(cancellationToken))
        {
            var viewModel = new PerformanceEventViewModel(
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
