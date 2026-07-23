using Tavi.Application.Guidance;
using Tavi.Application.LanguageModel;
using Tavi.Application.World;
using Tavi.Domain.World;
using Xunit;
using RuntimeWorld = Tavi.Domain.World.World;

namespace Tavi.Application.Tests;

public sealed class GuidanceServiceTests
{
    [Fact]
    public async Task StartBuildsReviewableProposalWithoutChangingWorldAndCommitAppliesAcceptedChanges()
    {
        await using WorldSession world = await CreateWorldSession();
        var languageModels = new FakeLanguageModelService(async (request, cancellationToken) =>
        {
            await Execute(request, "propose_anchor", """{"ChangeId":"character","Rationale":"建立视角","Name":"Alice","Description":"寻找失落钟声的人","Type":"Character"}""", cancellationToken);
            await Execute(request, "propose_anchor", """{"ChangeId":"bell","Rationale":"建立谜团","Name":"Bell","Description":"只在雨夜响起","Type":"Item"}""", cancellationToken);
            await Execute(request, "propose_relation", """{"ChangeId":"hears","Rationale":"连接角色与谜团","Name":"hears","Description":"Alice 听见钟声","SourceKind":"Proposed","Source":"character","TargetKind":"Proposed","Target":"bell","Scope":"World","CharacterKind":"Existing","Character":""}""", cancellationToken);
            await Execute(request, "set_guidance_summary", """{"Summary":"一名角色追寻雨夜钟声。"}""", cancellationToken);
            return "我整理了一份可审阅的世界草稿。";
        });
        IGuidanceService service = new GuidanceService(world, languageModels);

        GuidanceOperation operation = service.Start(new NarrativePotential("雨夜里传来不存在的钟声。"));
        GuidanceSnapshot snapshot = await operation.Completion;

        Assert.Equal(GuidanceState.ReadyForReview, snapshot.State);
        Assert.NotNull(snapshot.Proposal);
        Assert.Equal(snapshot.SessionId, snapshot.Proposal.Id);
        Assert.Equal(3, snapshot.Proposal.Changes.Count);
        Assert.Empty(world.Queries.GetAnchors());

        GuidanceCommitResult invalid = service.Commit(snapshot.SessionId, ["character", "hears"]);
        Assert.Equal(GuidanceCommitStatus.InvalidSelection, invalid.Status);
        Assert.Equal(GuidanceState.ReadyForReview, service.GetSnapshot(snapshot.SessionId).State);
        Assert.Empty(world.Queries.GetAnchors());

        GuidanceCommitResult committed = service.Commit(snapshot.SessionId, ["character", "bell", "hears"]);
        Assert.Equal(GuidanceCommitStatus.Committed, committed.Status);
        Assert.Equal(GuidanceState.Completed, service.GetSnapshot(snapshot.SessionId).State);
        Assert.Equal(2, world.Queries.GetAnchors().Count);
        Assert.Single(world.Queries.QueryRelations(["hears"], RelationQueryScope.World, string.Empty));
    }

    [Fact]
    public async Task CommitReportsWorldRevisionConflictWithoutFailingGuidanceSession()
    {
        await using WorldSession world = await CreateWorldSession();
        var languageModels = new FakeLanguageModelService(async (request, cancellationToken) =>
        {
            await Execute(request, "propose_anchor", """{"ChangeId":"bell","Rationale":"建立谜团","Name":"Bell","Description":"雨夜响起","Type":"Item"}""", cancellationToken);
            return "草稿已经准备好。";
        });
        IGuidanceService service = new GuidanceService(world, languageModels);
        GuidanceSnapshot snapshot = await service.Start(new NarrativePotential("一声钟响。")).Completion;
        world.Apply(WorldOperations.Single(WorldOperations.AddAnchor("Rain", "天气", AnchorType.Item)), world.Revision);

        GuidanceCommitResult result = service.Commit(snapshot.SessionId, ["bell"]);

        Assert.Equal(GuidanceCommitStatus.WorldConflict, result.Status);
        Assert.Equal(snapshot.BaseWorldRevision, result.ExpectedWorldRevision);
        Assert.Equal(world.Revision, result.ActualWorldRevision);
        Assert.Equal(GuidanceState.ReadyForReview, service.GetSnapshot(snapshot.SessionId).State);
    }

    [Fact]
    public async Task ModelFailureIsWrappedInDetailedGuidanceException()
    {
        await using WorldSession world = await CreateWorldSession();
        var providerFailure = new LanguageModelProviderException(
            LanguageModelErrorCodes.RateLimited,
            TaviErrorCategory.ExternalService,
            "请求过于频繁。",
            true,
            new LanguageModelErrorDetails { Provider = "Fake" });
        var languageModels = new FakeLanguageModelService((_, _) => Task.FromException<string>(providerFailure));
        IGuidanceService service = new GuidanceService(world, languageModels);
        GuidanceOperation operation = service.Start(new NarrativePotential("一个未完成的约定。"));

        GuidanceException exception = await Assert.ThrowsAsync<GuidanceException>(() => operation.Completion);

        Assert.Equal(GuidanceErrorCodes.GenerationFailed, exception.ErrorCode);
        Assert.Equal("Generate", exception.Operation);
        Assert.Equal(operation.SessionId, exception.SessionId);
        Assert.True(exception.IsTransient);
        Assert.Equal(LanguageModelErrorCodes.RateLimited, exception.Details["LanguageModelErrorCode"]);
        Assert.Same(providerFailure, exception.InnerException);
        Assert.Equal(GuidanceState.Failed, service.GetSnapshot(operation.SessionId).State);
    }

    [Fact]
    public async Task ContinueKeepsSessionAndProposalIdentity()
    {
        await using WorldSession world = await CreateWorldSession();
        var responses = new Queue<Func<LanguageModelRunRequest, CancellationToken, Task<string>>>();
        responses.Enqueue((_, _) => Task.FromResult("你希望钟声来自现实还是记忆？"));
        responses.Enqueue(async (request, cancellationToken) =>
        {
            await Execute(request, "propose_anchor", """{"ChangeId":"bell","Rationale":"承载记忆","Name":"Memory Bell","Description":"只存在于回忆中","Type":"Item"}""", cancellationToken);
            return "我按照“记忆”生成了草稿。";
        });
        var languageModels = new FakeLanguageModelService((request, cancellationToken) => responses.Dequeue()(request, cancellationToken));
        IGuidanceService service = new GuidanceService(world, languageModels);
        GuidanceSnapshot first = await service.Start(new NarrativePotential("远处传来钟声。")).Completion;

        Assert.Equal(GuidanceState.AwaitingPlayer, first.State);
        GuidanceSnapshot second = await service.Continue(first.SessionId, new GuidanceMessage("来自记忆。")).Completion;

        Assert.Equal(first.SessionId, second.SessionId);
        Assert.Equal(first.SessionId, second.Proposal?.Id);
        Assert.Equal(GuidanceState.ReadyForReview, second.State);
    }

    private static async Task<WorldSession> CreateWorldSession()
    {
        var session = new WorldSession(new SnapshotWorldStore(RuntimeWorld.Create(new WorldSnapshot()).CreateSnapshot()), autoSaveDelay: TimeSpan.FromMinutes(1));
        await session.InitializeAsync();
        return session;
    }

    private static Task<string> Execute(LanguageModelRunRequest request, string toolName, string arguments, CancellationToken cancellationToken)
    {
        ITool tool = request.Tools.Single(candidate => candidate.name == toolName);
        return tool.Execute(BinaryData.FromString(arguments), cancellationToken);
    }

    private sealed class FakeLanguageModelService(Func<LanguageModelRunRequest, CancellationToken, Task<string>> response) : ILanguageModelService
    {
        public LanguageModelCapabilities Capabilities { get; } = new()
        {
            Provider = "Fake",
            SupportsToolCalls = true,
            SupportsRequiredToolChoice = true
        };

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

        public bool ForgetOperation(Guid runId) => false;

        private async Task CompleteAsync(LanguageModelOperation operation, LanguageModelRunRequest request, CancellationToken cancellationToken)
        {
            operation.SetStatus(LanguageModelRunStatus.Running);
            try
            {
                string output = await response(request, cancellationToken);
                LanguageModelConversation conversation = request.Conversation.Append(ModelMessage.Assistant(output));
                operation.SetStatus(LanguageModelRunStatus.Completed);
                operation.Complete(new LanguageModelRunResult(operation.Id, output, conversation, 0, 0));
            }
            catch (OperationCanceledException)
            {
                operation.SetStatus(LanguageModelRunStatus.Cancelled);
                operation.Cancel(cancellationToken);
            }
            catch (Exception exception)
            {
                operation.SetStatus(LanguageModelRunStatus.Failed);
                operation.Fail(exception);
            }
        }
    }

    private sealed class SnapshotWorldStore(WorldSnapshot snapshot) : IWorldStore
    {
        public Task<WorldSnapshot?> LoadAsync(string slot, CancellationToken cancellationToken = default) => Task.FromResult<WorldSnapshot?>(snapshot);
        public Task SaveAsync(string slot, WorldSnapshot savedSnapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
