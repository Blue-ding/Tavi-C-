using Tavi.Application.Guidance;
using Tavi.Application.LanguageModel;
using Tavi.Application.World;
using Tavi.Domain.World;
using Xunit;

namespace Tavi.Application.Tests;

public sealed class GuidanceSessionTests
{
    [Fact]
    public async Task RecoverableFailurePreservesEditableMessageAndRetryContinuesSession()
    {
        await using var world = new WorldSession(new MemoryWorldStore());
        await world.InitializeAsync();
        var models = new RecoveringLanguageModelService();
        var session = new GuidanceSession(world, models);
        GuidanceOperation failed = session.Start(new NarrativePotential("原始消息"));
        await Assert.ThrowsAsync<GuidanceException>(() => failed.Completion);
        GuidanceSnapshot failure = session.GetSnapshot(session.Id);
        Assert.Equal(GuidanceState.Idle, failure.State);
        Assert.Equal("原始消息", failure.RetryMessage);

        models.Fail = false;
        GuidanceSnapshot recovered = await session.Retry(session.Id, new GuidanceMessage("编辑后的消息")).Completion;

        Assert.Null(recovered.RetryMessage);
        Assert.Null(recovered.Failure);
        Assert.Equal(["编辑后的消息", "已恢复"], recovered.Messages.Select(message => message.Text));
    }

    private sealed class RecoveringLanguageModelService : ILanguageModelService
    {
        public bool Fail { get; set; } = true;
        public LanguageModelCapabilities Capabilities { get; } = new() { Provider = "Test" };

        public LanguageModelOperation Start(LanguageModelRunRequest request, CancellationToken cancellationToken = default)
        {
            var operation = new LanguageModelOperation(Guid.NewGuid());
            operation.SetStatus(LanguageModelRunStatus.Running);
            if (Fail)
            {
                operation.SetStatus(LanguageModelRunStatus.Failed);
                operation.Fail(new LanguageModelProviderException(LanguageModelErrorCodes.ServiceUnavailable, TaviErrorCategory.ExternalService, "暂时不可用。", true, new LanguageModelErrorDetails { Provider = "Test" }));
            }
            else
            {
                const string output = "已恢复";
                operation.SetStatus(LanguageModelRunStatus.Completed);
                operation.Complete(new LanguageModelRunResult(operation.Id, output, request.Conversation.Append(ModelMessage.Assistant(output)), 0, 0));
            }
            return operation;
        }

        public bool TryGetOperation(Guid runId, out LanguageModelOperation? operation)
        {
            operation = null;
            return false;
        }

        public bool ForgetOperation(Guid runId) => true;
    }

    private sealed class MemoryWorldStore : IWorldStore
    {
        public Task<WorldSnapshot?> LoadAsync(string slot, CancellationToken cancellationToken = default) => Task.FromResult<WorldSnapshot?>(new WorldSnapshot());
        public Task SaveAsync(string slot, WorldSnapshot snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
