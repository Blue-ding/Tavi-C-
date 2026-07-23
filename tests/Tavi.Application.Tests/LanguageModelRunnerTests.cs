using System.Diagnostics;
using Tavi.Application.LanguageModel;
using Xunit;

namespace Tavi.Application.Tests;

public sealed class LanguageModelRunnerTests
{
    [Fact]
    public async Task TimeoutDoesNotFireBeforeConfiguredDuration()
    {
        var runner = new LanguageModelRunner(
            new DelayedClient(TimeSpan.FromSeconds(5)),
            Settings(TimeSpan.FromMilliseconds(200)));
        Stopwatch stopwatch = Stopwatch.StartNew();

        LanguageModelTimeoutException exception = await Assert.ThrowsAsync<LanguageModelTimeoutException>(
            () => runner.Start(Request()).Completion);

        stopwatch.Stop();
        Assert.Contains("00:00:00.2000000", exception.Message, StringComparison.Ordinal);
        Assert.True(
            stopwatch.Elapsed >= TimeSpan.FromMilliseconds(150),
            $"超时提前触发：实际仅运行 {stopwatch.Elapsed:c}。");
    }

    private static LanguageModelRunRequest Request() => new()
    {
        Conversation = new LanguageModelConversation
        {
            Messages = [ModelMessage.User("test")]
        },
        ToolCallMode = ToolCallMode.None
    };

    private static LanguageModelSettings Settings(TimeSpan timeout) => new()
    {
        OverallTimeout = timeout,
        ToolCalls = FeaturePolicy.Preferred,
        NativeJsonOutput = FeaturePolicy.Preferred,
        Streaming = FeaturePolicy.Disabled
    };

    private sealed class DelayedClient(TimeSpan delay) : ILanguageModelClient
    {
        public LanguageModelCapabilities Capabilities { get; } = new()
        {
            Provider = "DelayedTest"
        };

        public ILanguageModelSession CreateSession(LanguageModelSessionOptions options) =>
            new DelayedSession(delay);
    }

    private sealed class DelayedSession(TimeSpan delay) : ILanguageModelSession
    {
        public async Task<ModelTurnResult> CompleteAsync(
            IReadOnlyList<ModelMessage> messages,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken);
            return ModelTurnResult.Completed("done");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
