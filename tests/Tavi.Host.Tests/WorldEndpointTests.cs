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
    /// <summary>验证空世界可以通过 Host 加载并添加 Element。</summary>
    [Fact]
    public async Task EmptyWorldCanBeLoadedAndEdited()
    {
        using var factory = new TaviHostFactory();
        using HttpClient client = factory.CreateClient();
        WorldGraphViewModel initial = await RequireJsonAsync<WorldGraphViewModel>(await client.GetAsync("/api/v1/world/"));
        Assert.Empty(initial.Elements);
        HttpResponseMessage addResponse = await client.PostAsJsonAsync("/api/v1/world/elements", new AddElementRequest(initial.StateId, "Alice", "旅行者", "story:person"));
        WorldStagingResultViewModel staged = await RequireJsonAsync<WorldStagingResultViewModel>(addResponse);
        WorldCommitViewModel commit = await RequireJsonAsync<WorldCommitViewModel>(await client.PostAsJsonAsync("/api/v1/world/staging/commit", new CommitStagedRequest(initial.StateId, staged.AffectedChangeIds)));
        WorldGraphViewModel updated = await RequireJsonAsync<WorldGraphViewModel>(await client.GetAsync("/api/v1/world/"));
        Assert.True(commit.Changed);
        Assert.NotEqual(commit.PreviousStateId, commit.StateId);
        Assert.Equal(commit.StateId, updated.StateId);
        Assert.Collection(updated.Elements, element => Assert.Equal("Alice", element.Name));
    }

    /// <summary>验证过期 World 状态标识被转换为稳定的 HTTP 409 错误。</summary>
    [Fact]
    public async Task StateConflictUsesStableProblemResponse()
    {
        using var factory = new TaviHostFactory();
        using HttpClient client = factory.CreateClient();
        WorldGraphViewModel initial = await RequireJsonAsync<WorldGraphViewModel>(await client.GetAsync("/api/v1/world/"));
        WorldStagingResultViewModel first = await RequireJsonAsync<WorldStagingResultViewModel>(await client.PostAsJsonAsync("/api/v1/world/elements", new AddElementRequest(initial.StateId, "Alice", "", "story:person")));
        await RequireJsonAsync<WorldCommitViewModel>(await client.PostAsJsonAsync("/api/v1/world/staging/commit", new CommitStagedRequest(initial.StateId, first.AffectedChangeIds)));
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/world/elements", new AddElementRequest(initial.StateId, "Key", "", "story:item"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        Assert.Contains("TAVI.WORLD.STATE.CONFLICT", content, StringComparison.Ordinal);
    }

    /// <summary>验证非 Tavi 参数异常同样由全局异常边界处理。</summary>
    [Fact]
    public async Task InvalidElementTypeIsHandledByGlobalExceptionBoundary()
    {
        using var factory = new TaviHostFactory();
        using HttpClient client = factory.CreateClient();
        WorldGraphViewModel initial = await RequireJsonAsync<WorldGraphViewModel>(await client.GetAsync("/api/v1/world/"));
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/world/elements", new AddElementRequest(initial.StateId, "Place", "", "Location"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        Assert.Contains("TAVI.HOST.REQUEST.INVALID_ARGUMENT", content, StringComparison.Ordinal);
    }

    /// <summary>验证 Element、Scope、Relation 和撤销可以组成完整编辑流程。</summary>
    [Fact]
    public async Task ScopedRelationCanBeCreatedAndUndone()
    {
        using var factory = new TaviHostFactory();
        using HttpClient client = factory.CreateClient();
        WorldGraphViewModel graph = await RequireJsonAsync<WorldGraphViewModel>(await client.GetAsync("/api/v1/world/"));
        WorldStagingResultViewModel ownerStage = await RequireJsonAsync<WorldStagingResultViewModel>(await client.PostAsJsonAsync("/api/v1/world/elements", new AddElementRequest(graph.StateId, "Alice", "", "story:person")));
        Guid ownerId = ownerStage.World.Elements.Single().Id;
        WorldStagingResultViewModel itemStage = await RequireJsonAsync<WorldStagingResultViewModel>(await client.PostAsJsonAsync("/api/v1/world/elements", new AddElementRequest(graph.StateId, "Key", "", "story:item")));
        Guid itemId = itemStage.World.Elements.Single(node => node.Name == "Key").Id;
        WorldStagingResultViewModel scopeStage = await RequireJsonAsync<WorldStagingResultViewModel>(await client.PostAsJsonAsync("/api/v1/world/scopes", new AddScopeRequest(graph.StateId, "Alice belief", "", 1, "epistemic:belief", ownerId)));
        Guid scopeId = scopeStage.World.Scopes.Single().Id;
        var relationRequest = new AddRelationRequest(graph.StateId, "寻找", "", 0.6, "story:seeks", ownerId, itemId, scopeId);
        WorldStagingResultViewModel relationStage = await RequireJsonAsync<WorldStagingResultViewModel>(await client.PostAsJsonAsync("/api/v1/world/relations", relationRequest));
        Guid[] allChanges = relationStage.World.StagedChanges.Where(change => change.Status == "Valid").Select(change => change.Id).ToArray();
        WorldCommitViewModel relation = await RequireJsonAsync<WorldCommitViewModel>(await client.PostAsJsonAsync("/api/v1/world/staging/commit", new CommitStagedRequest(graph.StateId, allChanges)));
        WorldGraphViewModel withRelation = await RequireJsonAsync<WorldGraphViewModel>(await client.GetAsync("/api/v1/world/"));
        Assert.Collection(withRelation.Relations, edge => Assert.Equal(scopeId, edge.ScopeId));
        await RequireJsonAsync<WorldCommitViewModel>(await client.PostAsJsonAsync("/api/v1/world/undo", new WorldStateRequest(relation.StateId)));
        WorldGraphViewModel undone = await RequireJsonAsync<WorldGraphViewModel>(await client.GetAsync("/api/v1/world/"));
        Assert.Empty(undone.Relations);
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
