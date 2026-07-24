using System.Text.Json;
using Tavi.Host.Runtime;
using Tavi.Host.ViewModels;

namespace Tavi.Host.Endpoints;

internal static class GuidanceEndpoints
{
    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web);

    internal static IEndpointRouteBuilder MapGuidanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder guidance = endpoints.MapGroup("/api/v1/guidance");
        guidance.MapGet("/", (GuidanceRuntime runtime) => runtime.Availability);
        guidance.MapGet("/session", (GuidanceRuntime runtime) => runtime.GetCurrentSnapshot());
        guidance.MapPost("/sessions", (StartGuidanceRequest request, GuidanceRuntime runtime) => Results.Accepted(value: runtime.Start(request.Potential)));
        guidance.MapGet("/sessions/{sessionId:guid}", (Guid sessionId, GuidanceRuntime runtime) => runtime.GetSnapshot(sessionId));
        guidance.MapPost("/sessions/{sessionId:guid}/messages", (Guid sessionId, ContinueGuidanceRequest request, GuidanceRuntime runtime) => Results.Accepted(value: runtime.Continue(sessionId, request.Message)));
        guidance.MapPost("/sessions/{sessionId:guid}/retry", (Guid sessionId, RetryGuidanceRequest request, GuidanceRuntime runtime) => Results.Accepted(value: runtime.Retry(sessionId, request.Message)));
        guidance.MapPost("/sessions/{sessionId:guid}/refresh", (Guid sessionId, GuidanceRuntime runtime) => runtime.Refresh(sessionId));
        guidance.MapPost("/sessions/{sessionId:guid}/commit", (Guid sessionId, CommitGuidanceRequest request, GuidanceRuntime runtime) => runtime.Commit(sessionId, request.AcceptedChangeIds));
        guidance.MapPost("/sessions/{sessionId:guid}/cancel", (Guid sessionId, GuidanceRuntime runtime) => runtime.Cancel(sessionId));
        guidance.MapDelete("/sessions/{sessionId:guid}", ForgetAsync);
        guidance.MapGet("/sessions/{sessionId:guid}/events", StreamEventsAsync);
        return endpoints;
    }

    private static IResult ForgetAsync(Guid sessionId, GuidanceRuntime runtime)
    {
        runtime.Forget(sessionId);
        return Results.NoContent();
    }

    private static async Task StreamEventsAsync(Guid sessionId, HttpContext context, GuidanceRuntime runtime, GuidanceEventBroker broker, CancellationToken cancellationToken)
    {
        _ = runtime.GetSnapshot(sessionId);
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        await foreach (GuidanceEventViewModel guidanceEvent in broker.SubscribeAsync(sessionId, cancellationToken))
        {
            await context.Response.WriteAsync($"event: {guidanceEvent.Type}\n", cancellationToken);
            await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(guidanceEvent, EventJsonOptions)}\n\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
        }
    }
}
