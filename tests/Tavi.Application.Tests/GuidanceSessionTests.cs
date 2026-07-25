using Tavi.Application.Guidance;
using Tavi.Application.Extensions;
using Tavi.Application.LanguageModel;
using Tavi.Application.World;
using Tavi.Domain.World;
using Xunit;

namespace Tavi.Application.Tests;

public sealed class GuidanceSessionTests
{
    /// <summary>验证 Guidance 捏造的格式合法类型会在进入 World 暂存区前被注册策略拒绝。</summary>
    [Fact]
    public async Task FabricatedOpenTypeIsRejectedBeforeStaging()
    {
        ModuleCatalog catalog = ModuleCatalog.Create([]);
        await using var world = new WorldSession(new MemoryWorldStore(), catalog);
        await world.InitializeAsync();
        var session = new GuidanceSession(world, new FabricatingLanguageModelService(), extensions: new ExtensionSession([]).Freeze());

        GuidanceException exception = await Assert.ThrowsAsync<GuidanceException>(() => session.Start(new NarrativePotential("创造一件遗物")).Completion);

        Assert.Equal(GuidanceErrorCodes.GenerationFailed, exception.ErrorCode);
        Assert.Empty(world.CreateStagingSnapshot().Changes);
    }

    [Fact]
    public async Task RecoverableFailurePreservesEditableMessageAndRetryContinuesSession()
    {
        await using var world = new WorldSession(new MemoryWorldStore(), AnyWorldTypePolicy.Instance);
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

    private sealed class FabricatingLanguageModelService : ILanguageModelService
    {
        public LanguageModelCapabilities Capabilities { get; } = new() { Provider = "Test", SupportsToolCalls = true };

        public LanguageModelOperation Start(LanguageModelRunRequest request, CancellationToken cancellationToken = default)
        {
            var operation = new LanguageModelOperation(Guid.NewGuid());
            _ = CompleteAsync(operation, request, cancellationToken);
            return operation;
        }

        public bool TryGetOperation(Guid runId, out LanguageModelOperation? operation)
        {
            operation = null;
            return false;
        }

        public bool ForgetOperation(Guid runId) => true;

        private static async Task CompleteAsync(LanguageModelOperation operation, LanguageModelRunRequest request, CancellationToken cancellationToken)
        {
            operation.SetStatus(LanguageModelRunStatus.Running);
            try
            {
                ITool tool = request.Tools.Single(candidate => candidate.name == "propose_element");
                await tool.Execute(BinaryData.FromString("""{"Rationale":"发展谜团","Name":"遗物","Description":"","Type":"story:fabricated"}"""), cancellationToken);
                operation.SetStatus(LanguageModelRunStatus.Completed);
                operation.Complete(new LanguageModelRunResult(operation.Id, "完成", request.Conversation, 1, 0));
            }
            catch (Exception exception)
            {
                operation.SetStatus(LanguageModelRunStatus.Failed);
                operation.Fail(exception);
            }
        }
    }

    private sealed class MemoryWorldStore : IWorldStore
    {
        public Task<WorldSnapshot?> LoadAsync(string slot, CancellationToken cancellationToken = default) => Task.FromResult<WorldSnapshot?>(new WorldSnapshot());
        public Task SaveAsync(string slot, WorldSnapshot snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
