using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tavi.Application.LanguageModel;
using Tavi.Host.ViewModels;
using Xunit;

namespace Tavi.Host.Tests;

/// <summary>验证 Guidance 展示层的异步生成、提案映射和提交契约。</summary>
public sealed class GuidanceEndpointTests
{
    /// <summary>验证前端可以启动 Guidance、审阅带判别类型的提案并将其提交到世界。</summary>
    [Fact]
    public async Task ProposalCanBeGeneratedReviewedAndCommitted()
    {
        using var factory = new GuidanceHostFactory();
        using HttpClient client = factory.CreateClient();
        GuidanceAvailabilityViewModel availability = await RequireJsonAsync<GuidanceAvailabilityViewModel>(await client.GetAsync("/api/v1/guidance/"));
        Assert.True(availability.Available);
        HttpResponseMessage startResponse = await client.PostAsJsonAsync("/api/v1/guidance/sessions", new StartGuidanceRequest("雨夜里传来不存在的钟声。"));
        Assert.Equal(HttpStatusCode.Accepted, startResponse.StatusCode);
        GuidanceOperationViewModel operation = await RequireJsonAsync<GuidanceOperationViewModel>(startResponse);
        GuidanceSnapshotViewModel snapshot = await WaitForStateAsync(client, operation.SessionId, "ReadyForReview");
        Assert.Equal(["Player", "Guidance"], snapshot.Messages.Select(message => message.Role));
        Assert.NotNull(snapshot.Proposal);
        Assert.IsType<ProposeAddAnchorViewModel>(Assert.Single(snapshot.Proposal.Changes));
        var commitRequest = new CommitGuidanceRequest(snapshot.Proposal.Changes.Select(change => change.Id).ToArray());
        GuidanceCommitViewModel commit = await RequireJsonAsync<GuidanceCommitViewModel>(await client.PostAsJsonAsync($"/api/v1/guidance/sessions/{operation.SessionId}/commit", commitRequest));
        Assert.Equal("Committed", commit.Status);
        Assert.Equal("Completed", commit.Snapshot.State);
        WorldGraphViewModel world = await RequireJsonAsync<WorldGraphViewModel>(await client.GetAsync("/api/v1/world/"));
        Assert.Collection(world.Nodes, anchor => Assert.Equal("雨夜钟", anchor.Name));
    }

    /// <summary>验证未配置语言模型时，世界工作台仍可用且 Guidance 返回稳定配置错误。</summary>
    [Fact]
    public async Task UnavailableGuidanceDoesNotPreventWorldEditing()
    {
        using var factory = new GuidanceHostFactory(includeLanguageModels: false);
        using HttpClient client = factory.CreateClient();
        GuidanceAvailabilityViewModel availability = await RequireJsonAsync<GuidanceAvailabilityViewModel>(await client.GetAsync("/api/v1/guidance/"));
        Assert.False(availability.Available);
        WorldGraphViewModel world = await RequireJsonAsync<WorldGraphViewModel>(await client.GetAsync("/api/v1/world/"));
        Assert.Empty(world.Nodes);
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/guidance/sessions", new StartGuidanceRequest("一声钟响。"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains(LanguageModelErrorCodes.InvalidConfiguration, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static async Task<GuidanceSnapshotViewModel> WaitForStateAsync(HttpClient client, Guid sessionId, string state)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            GuidanceSnapshotViewModel snapshot = await RequireJsonAsync<GuidanceSnapshotViewModel>(await client.GetAsync($"/api/v1/guidance/sessions/{sessionId}"));
            if (snapshot.State == state)
                return snapshot;
            await Task.Delay(25);
        }
        throw new TimeoutException($"Guidance 会话未进入 {state} 状态。");
    }

    private static async Task<T> RequireJsonAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new InvalidOperationException("响应没有包含 JSON 内容。");
    }

    private sealed class GuidanceHostFactory(bool includeLanguageModels = true) : WebApplicationFactory<Program>
    {
        private readonly string _saveDirectory = Path.Combine(Path.GetTempPath(), $"tavi-guidance-host-tests-{Guid.NewGuid():N}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Tavi:SaveDirectory"] = _saveDirectory, ["Tavi:OpenAI:SelfConfigPath"] = Path.Combine(_saveDirectory, "missing-SelfCongif.md") }));
            if (includeLanguageModels)
                builder.ConfigureServices(services => services.AddSingleton<ILanguageModelService>(new FakeLanguageModelService()));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(_saveDirectory))
                Directory.Delete(_saveDirectory, true);
        }
    }

    private sealed class FakeLanguageModelService : ILanguageModelService
    {
        public LanguageModelCapabilities Capabilities { get; } = new() { Provider = "Fake", SupportsToolCalls = true, SupportsRequiredToolChoice = true };

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

        private static async Task CompleteAsync(LanguageModelOperation operation, LanguageModelRunRequest request, CancellationToken cancellationToken)
        {
            operation.SetStatus(LanguageModelRunStatus.Running);
            try
            {
                ITool tool = request.Tools.Single(candidate => candidate.name == "propose_anchor");
                await tool.Execute(BinaryData.FromString("""{"ChangeId":"bell","Rationale":"承载雨夜谜团","Name":"雨夜钟","Description":"只在无人看见时响起","Type":"Item"}"""), cancellationToken);
                const string output = "我整理了一项可以审阅的世界变化。";
                operation.ReportText(output);
                operation.SetStatus(LanguageModelRunStatus.Completed);
                operation.Complete(new LanguageModelRunResult(operation.Id, output, request.Conversation.Append(ModelMessage.Assistant(output)), 1, 0));
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
}
