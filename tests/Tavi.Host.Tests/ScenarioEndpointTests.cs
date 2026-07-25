using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Tavi.Host.ViewModels;
using Xunit;

namespace Tavi.Host.Tests;

/// <summary>验证独立 Scenario 页面所需的 Host 查询与 Scene 生命周期入口。</summary>
public sealed class ScenarioEndpointTests
{
    /// <summary>验证 Host 可以返回 Magic Definition 并创建 Binding Scene。</summary>
    [Fact]
    public async Task MagicSceneCanBeCreated()
    {
        using var factory = new ScenarioHostFactory();
        using HttpClient client = factory.CreateClient();
        ScenarioWorkspaceViewModel initial = await RequireJsonAsync<ScenarioWorkspaceViewModel>(await client.GetAsync("/api/v1/scenario/"));
        SceneDefinitionViewModel definition = Assert.Single(initial.Definitions, value => value.Id == "magic:cast-spell");
        ScenarioWorkspaceViewModel created = await RequireJsonAsync<ScenarioWorkspaceViewModel>(await client.PostAsJsonAsync("/api/v1/scenario/scenes", new CreateSceneRequest(initial.StateId, definition.Id, 0)));
        SceneViewModel scene = Assert.Single(created.Scenes);
        Assert.Equal("Binding", scene.State);
        Assert.Equal(["caster", "spell"], scene.Slots.Select(value => value.Id));
    }

    /// <summary>验证独立 Scenario 路径由前端入口承载。</summary>
    [Fact]
    public async Task ScenarioPageUsesWebEntryPoint()
    {
        using var factory = new ScenarioHostFactory();
        using HttpClient client = factory.CreateClient();
        string html = await client.GetStringAsync("/scenario");
        Assert.Contains("/assets/app.js", html, StringComparison.Ordinal);
    }

    private static async Task<T> RequireJsonAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new InvalidOperationException("响应没有包含 JSON 内容。");
    }

    private sealed class ScenarioHostFactory : WebApplicationFactory<Program>
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tavi-scenario-host-{Guid.NewGuid():N}");
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Tavi:SaveDirectory"] = _directory, ["Tavi:ScenarioDirectory"] = _directory, ["Tavi:OpenAI:ConfigurationPath"] = Path.Combine(_directory, "missing-openai.json") }));
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(_directory))
                Directory.Delete(_directory, true);
        }
    }
}
