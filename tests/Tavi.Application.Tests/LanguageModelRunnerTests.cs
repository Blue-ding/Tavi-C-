using Tavi.Application.LanguageModel;
using Xunit;

namespace Tavi.Application.Tests;

public sealed class LanguageModelRunnerTests
{
    [Fact]
    public async Task CompletedTurnReturnsCanonicalConversationAndTracksOperation()
    {
        var client = new FakeClient(ModelTurnResult.Completed("answer"));
        var runner = new LanguageModelRunner(client, new LanguageModelSettings());
        LanguageModelOperation operation = runner.Start(Request("question"));

        LanguageModelRunResult result = await operation.Completion;

        Assert.Equal("answer", result.Output);
        Assert.Equal(LanguageModelRunStatus.Completed, operation.Status);
        Assert.Equal(ModelMessageRole.Assistant, result.Conversation.Messages[^1].Role);
        Assert.Equal("answer", result.Conversation.Messages[^1].Text);
        Assert.True(runner.TryGetOperation(operation.Id, out LanguageModelOperation? tracked));
        Assert.Same(operation, tracked);
        Assert.True(runner.ForgetOperation(operation.Id));
        Assert.False(runner.TryGetOperation(operation.Id, out _));
    }

    [Fact]
    public async Task ToolCallsAreExecutedAndReturnedToNextTurn()
    {
        var call = new ModelToolCall(
            "call-1",
            "echo",
            BinaryData.FromString("""{"Value":"hello"}"""));
        var client = new FakeClient(
            ModelTurnResult.CallingTools([call]),
            ModelTurnResult.Completed("done"));
        var tool = new EchoTool();
        var runner = new LanguageModelRunner(client, new LanguageModelSettings());

        LanguageModelRunResult result = await runner.Start(
            Request("question") with { Tools = [tool] }).Completion;

        Assert.Equal("done", result.Output);
        Assert.Equal(1, result.ToolRounds);
        Assert.Equal("hello", tool.LastValue);
        Assert.Equal(2, client.Session.Received.Count);
        ModelMessage toolMessage = Assert.Single(client.Session.Received[1]);
        Assert.Equal(ModelMessageRole.Tool, toolMessage.Role);
        Assert.Equal("call-1", toolMessage.ToolCallId);
        Assert.Equal("echo:hello", toolMessage.Text);
    }

    [Fact]
    public async Task InvalidJsonIsRepairedWithIndependentRetryCounter()
    {
        var client = new FakeClient(
            ModelTurnResult.Completed("not-json"),
            ModelTurnResult.Completed("""{"value":1}"""));
        var runner = new LanguageModelRunner(
            client,
            new LanguageModelSettings { MaxOutputRepairAttempts = 1 });

        LanguageModelRunResult result = await runner.Start(
            Request("json") with { OutputValidator = new JsonOutputValidator() }).Completion;

        Assert.Equal("""{"value":1}""", result.Output);
        Assert.Equal(1, result.OutputRepairAttempts);
        Assert.Equal(0, result.ToolRounds);
        ModelMessage repair = Assert.Single(client.Session.Received[1]);
        Assert.Equal(ModelMessageRole.User, repair.Role);
        Assert.Contains("未通过校验", repair.Text);
    }

    [Fact]
    public async Task ExhaustedJsonRepairsUseStableErrorCode()
    {
        var client = new FakeClient(ModelTurnResult.Completed("not-json"));
        var runner = new LanguageModelRunner(
            client,
            new LanguageModelSettings { MaxOutputRepairAttempts = 0 });

        ModelOutputValidationException exception =
            await Assert.ThrowsAsync<ModelOutputValidationException>(
                () => runner.Start(
                    Request("json") with
                    {
                        OutputValidator = new JsonOutputValidator()
                    }).Completion);

        Assert.Equal(LanguageModelErrorCodes.OutputValidationFailed, exception.ErrorCode);
        Assert.Equal(LanguageModelErrorCategory.OutputValidation, exception.Category);
    }

    [Fact]
    public async Task MissingToolUsesStableErrorCodeAndDetails()
    {
        var call = new ModelToolCall("call-7", "missing", BinaryData.FromString("{}"));
        var client = new FakeClient(ModelTurnResult.CallingTools([call]));
        var runner = new LanguageModelRunner(client, new LanguageModelSettings());

        ModelToolException exception = await Assert.ThrowsAsync<ModelToolException>(
            () => runner.Start(Request("question")).Completion);

        Assert.Equal(LanguageModelErrorCodes.ToolNotFound, exception.ErrorCode);
        Assert.Equal("missing", exception.Details.ToolName);
        Assert.Equal("call-7", exception.Details.ToolCallId);
    }

    [Fact]
    public async Task ToolRoundLimitIsIndependentFromOutputRepairs()
    {
        var call = new ModelToolCall("call-1", "echo", BinaryData.FromString("""{"Value":"x"}"""));
        var client = new FakeClient(ModelTurnResult.CallingTools([call]));
        var runner = new LanguageModelRunner(
            client,
            new LanguageModelSettings { MaxToolRounds = 0 });

        ModelRoundLimitException exception =
            await Assert.ThrowsAsync<ModelRoundLimitException>(
                () => runner.Start(
                    Request("question") with { Tools = [new EchoTool()] }).Completion);

        Assert.Equal(LanguageModelErrorCodes.RoundLimit, exception.ErrorCode);
    }

    [Fact]
    public void RequiredUnsupportedCapabilityFailsBeforeFirstCall()
    {
        var client = new FakeClient(
            new LanguageModelCapabilities
            {
                Provider = "Fake",
                SupportsToolCalls = true,
                SupportsRequiredToolChoice = true,
                SupportsNativeJsonOutput = false,
                SupportsJsonSchema = false,
                SupportsStreaming = false
            },
            ModelTurnResult.Completed("unused"));

        LanguageModelConfigurationException exception =
            Assert.Throws<LanguageModelConfigurationException>(
                () => new LanguageModelRunner(
                    client,
                    new LanguageModelSettings
                    {
                        NativeJsonOutput = FeaturePolicy.Required
                    }));

        Assert.Equal(LanguageModelErrorCodes.UnsupportedCapability, exception.ErrorCode);
    }

    [Fact]
    public async Task CallerCancellationMarksOnlyThatOperationCancelled()
    {
        using var source = new CancellationTokenSource();
        var client = new FakeClient(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return ModelTurnResult.Completed("never");
        });
        var runner = new LanguageModelRunner(client, new LanguageModelSettings());
        LanguageModelOperation operation = runner.Start(Request("question"), source.Token);

        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.Completion);
        Assert.Equal(LanguageModelRunStatus.Cancelled, operation.Status);
    }

    [Fact]
    public async Task OverallTimeoutUsesDedicatedException()
    {
        var client = new FakeClient(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return ModelTurnResult.Completed("never");
        });
        var runner = new LanguageModelRunner(
            client,
            new LanguageModelSettings { OverallTimeout = TimeSpan.FromMilliseconds(20) });

        LanguageModelTimeoutException exception =
            await Assert.ThrowsAsync<LanguageModelTimeoutException>(
                () => runner.Start(Request("question")).Completion);

        Assert.Equal(LanguageModelErrorCodes.Timeout, exception.ErrorCode);
        Assert.True(exception.IsTransient);
    }

    private static LanguageModelRunRequest Request(string text) =>
        new()
        {
            Conversation = new LanguageModelConversation
            {
                Messages = [ModelMessage.User(text)]
            }
        };

    private sealed class FakeClient : ILanguageModelClient
    {
        internal FakeClient(params ModelTurnResult[] turns)
            : this(DefaultCapabilities(), turns)
        {
        }

        internal FakeClient(
            LanguageModelCapabilities capabilities,
            params ModelTurnResult[] turns)
        {
            Capabilities = capabilities;
            Session = new FakeSession(
                turns.Select<ModelTurnResult, Func<CancellationToken, Task<ModelTurnResult>>>(
                    turn => _ => Task.FromResult(turn)));
        }

        internal FakeClient(Func<CancellationToken, Task<ModelTurnResult>> turn)
        {
            Capabilities = DefaultCapabilities();
            Session = new FakeSession([turn]);
        }

        public LanguageModelCapabilities Capabilities { get; }
        internal FakeSession Session { get; }

        public ILanguageModelSession CreateSession(LanguageModelSessionOptions options)
        {
            Session.Options = options;
            return Session;
        }

        private static LanguageModelCapabilities DefaultCapabilities() =>
            new()
            {
                Provider = "Fake",
                SupportsToolCalls = true,
                SupportsRequiredToolChoice = true,
                SupportsParallelToolCalls = false,
                SupportsNativeJsonOutput = false,
                SupportsJsonSchema = false,
                SupportsStreaming = false
            };
    }

    private sealed class FakeSession : ILanguageModelSession
    {
        private readonly Queue<Func<CancellationToken, Task<ModelTurnResult>>> _turns;

        internal FakeSession(
            IEnumerable<Func<CancellationToken, Task<ModelTurnResult>>> turns)
        {
            _turns = new Queue<Func<CancellationToken, Task<ModelTurnResult>>>(turns);
        }

        internal LanguageModelSessionOptions? Options { get; set; }
        internal List<IReadOnlyList<ModelMessage>> Received { get; } = [];

        public Task<ModelTurnResult> CompleteAsync(
            IReadOnlyList<ModelMessage> messages,
            CancellationToken cancellationToken = default)
        {
            Received.Add(messages.ToArray());
            if (_turns.Count == 0)
                throw new InvalidOperationException("Fake 没有更多响应。");
            return _turns.Dequeue()(cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed record EchoArguments : IToolArgument
    {
        public string Value { get; init; } = string.Empty;
    }

    private sealed class EchoTool : Tool<EchoArguments>
    {
        public override string name => "echo";
        public override string description => "echo";
        internal string? LastValue { get; private set; }

        protected override Task<string> Execute(
            EchoArguments arguments,
            CancellationToken cancellationToken)
        {
            LastValue = arguments.Value;
            return Task.FromResult($"echo:{arguments.Value}");
        }
    }
}
