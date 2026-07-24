using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Tavi.Host.ViewModels;
using Xunit;

namespace Tavi.Host.Tests;

/// <summary>验证世界展示层端点、并发契约和全局异常边界。</summary>
public sealed class WorldEndpointTests
{
    /// <summary>验证空世界可以通过 Host 加载并添加 Anchor。</summary>
    [Fact]
    public async Task EmptyWorldCanBeLoadedAndEdited()
    {
        using var factory = new TaviHostFactory();
        using HttpClient client = factory.CreateClient();
        WorldGraphViewModel initial = await RequireJsonAsync<WorldGraphViewModel>(await client.GetAsync("/api/v1/world/"));
        Assert.Empty(initial.Nodes);
        HttpResponseMessage addResponse = await client.PostAsJsonAsync("/api/v1/world/anchors", new AddAnchorRequest(initial.Revision, "Alice", "旅行者", "Character"));
        WorldStagingResultViewModel staged = await RequireJsonAsync<WorldStagingResultViewModel>(addResponse);
        WorldCommitViewModel commit = await RequireJsonAsync<WorldCommitViewModel>(await client.PostAsJsonAsync("/api/v1/world/staging/commit", new CommitStagedRequest(initial.Revision, staged.AffectedChangeIds)));
        WorldGraphViewModel updated = await RequireJsonAsync<WorldGraphViewModel>(await client.GetAsync("/api/v1/world/"));
        Assert.True(commit.Changed);
        Assert.Equal(commit.Revision, updated.Revision);
        Assert.Collection(updated.Nodes, anchor => Assert.Equal("Alice", anchor.Name));
    }

    /// <summary>验证过期 revision 被转换为稳定的 HTTP 409 错误。</summary>
    [Fact]
    public async Task RevisionConflictUsesStableProblemResponse()
    {
        using var factory = new TaviHostFactory();
        using HttpClient client = factory.CreateClient();
        WorldGraphViewModel initial = await RequireJsonAsync<WorldGraphViewModel>(await client.GetAsync("/api/v1/world/"));
        WorldStagingResultViewModel first = await RequireJsonAsync<WorldStagingResultViewModel>(await client.PostAsJsonAsync("/api/v1/world/anchors", new AddAnchorRequest(initial.Revision, "Alice", "", "Character")));
        await RequireJsonAsync<WorldCommitViewModel>(await client.PostAsJsonAsync("/api/v1/world/staging/commit", new CommitStagedRequest(initial.Revision, first.AffectedChangeIds)));
        WorldStagingResultViewModel second = await RequireJsonAsync<WorldStagingResultViewModel>(await client.PostAsJsonAsync("/api/v1/world/anchors", new AddAnchorRequest(initial.Revision, "Key", "", "Item")));
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/world/staging/commit", new CommitStagedRequest(initial.Revision, second.AffectedChangeIds));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        Assert.Contains("TAVI.WORLD.REVISION.CONFLICT", content, StringComparison.Ordinal);
    }

    /// <summary>验证非 Tavi 参数异常同样由全局异常边界处理。</summary>
    [Fact]
    public async Task InvalidAnchorTypeIsHandledByGlobalExceptionBoundary()
    {
        using var factory = new TaviHostFactory();
        using HttpClient client = factory.CreateClient();
        WorldGraphViewModel initial = await RequireJsonAsync<WorldGraphViewModel>(await client.GetAsync("/api/v1/world/"));
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/world/anchors", new AddAnchorRequest(initial.Revision, "Place", "", "Location"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        Assert.Contains("TAVI.HOST.REQUEST.INVALID_ARGUMENT", content, StringComparison.Ordinal);
    }

    /// <summary>验证 Character 子世界、Relation 和撤销可以组成完整编辑流程。</summary>
    [Fact]
    public async Task RelationAndSubWorldCanBeCreatedAndUndone()
    {
        using var factory = new TaviHostFactory();
        using HttpClient client = factory.CreateClient();
        WorldGraphViewModel graph = await RequireJsonAsync<WorldGraphViewModel>(await client.GetAsync("/api/v1/world/"));
        WorldStagingResultViewModel characterStage = await RequireJsonAsync<WorldStagingResultViewModel>(await client.PostAsJsonAsync("/api/v1/world/anchors", new AddAnchorRequest(graph.Revision, "Alice", "", "Character")));
        Guid characterId = characterStage.World.Nodes.Single().Id;
        WorldStagingResultViewModel itemStage = await RequireJsonAsync<WorldStagingResultViewModel>(await client.PostAsJsonAsync("/api/v1/world/anchors", new AddAnchorRequest(graph.Revision, "Key", "", "Item")));
        Guid itemId = itemStage.World.Nodes.Single(node => node.Name == "Key").Id;
        WorldStagingResultViewModel subWorldStage = await RequireJsonAsync<WorldStagingResultViewModel>(await client.PostAsJsonAsync("/api/v1/world/subworlds", new CreateSubWorldRequest(graph.Revision, characterId)));
        var relationRequest = new AddRelationRequest(graph.Revision, "寻找", "", characterId, itemId, characterId);
        WorldStagingResultViewModel relationStage = await RequireJsonAsync<WorldStagingResultViewModel>(await client.PostAsJsonAsync("/api/v1/world/relations", relationRequest));
        Guid[] allChanges = relationStage.World.StagedChanges.Where(change => change.Status == "Valid").Select(change => change.Id).ToArray();
        WorldCommitViewModel relation = await RequireJsonAsync<WorldCommitViewModel>(await client.PostAsJsonAsync("/api/v1/world/staging/commit", new CommitStagedRequest(graph.Revision, allChanges)));
        WorldGraphViewModel withRelation = await RequireJsonAsync<WorldGraphViewModel>(await client.GetAsync("/api/v1/world/"));
        Assert.Collection(withRelation.Edges, edge => Assert.Equal(characterId, edge.DomainCharacterId));
        await RequireJsonAsync<WorldCommitViewModel>(await client.PostAsJsonAsync("/api/v1/world/undo", new RevisionRequest(relation.Revision)));
        WorldGraphViewModel undone = await RequireJsonAsync<WorldGraphViewModel>(await client.GetAsync("/api/v1/world/"));
        Assert.Empty(undone.Edges);
        Assert.True(undone.CanRedo);
    }

    /// <summary>验证 Host 可以直接提供已构建的前端入口页面。</summary>
    [Fact]
    public async Task HostServesWorldWorkbench()
    {
        using var factory = new TaviHostFactory();
        using HttpClient client = factory.CreateClient();
        string html = await client.GetStringAsync("/");
        Assert.Contains("Tavi · 世界图工作台", html, StringComparison.Ordinal);
        Assert.Contains("/assets/app.js", html, StringComparison.Ordinal);
    }

    private static async Task<T> RequireJsonAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new InvalidOperationException("响应没有包含 JSON 内容。");
    }

    private sealed class TaviHostFactory : WebApplicationFactory<Program>
    {
        private readonly string _saveDirectory = Path.Combine(Path.GetTempPath(), $"tavi-host-tests-{Guid.NewGuid():N}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Tavi:SaveDirectory"] = _saveDirectory }));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(_saveDirectory))
                Directory.Delete(_saveDirectory, true);
        }
    }
}
