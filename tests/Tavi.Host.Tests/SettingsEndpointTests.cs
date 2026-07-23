using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Tavi.Host.ViewModels;
using Tavi.Infrastructure.OpenAI;
using Xunit;

namespace Tavi.Host.Tests;

/// <summary>验证本地设置接口可以安全读写两类持久化配置。</summary>
public sealed class SettingsEndpointTests
{
    [Fact]
    public async Task LanguageModelSettingsCanRoundTrip()
    {
        using var factory = new SettingsHostFactory();
        using HttpClient client = factory.CreateClient();

        LanguageModelSettingsViewModel defaults = await RequireJsonAsync<LanguageModelSettingsViewModel>(
            await client.GetAsync("/api/v1/settings/language-model"));
        Assert.Equal(8, defaults.MaxToolRounds);

        var request = new UpdateLanguageModelSettingsRequest(4, 1, 45, "Required", "Preferred", "Disabled");
        LanguageModelSettingsViewModel saved = await RequireJsonAsync<LanguageModelSettingsViewModel>(
            await client.PutAsJsonAsync("/api/v1/settings/language-model", request));

        Assert.Equal(4, saved.MaxToolRounds);
        Assert.Equal(45, saved.OverallTimeoutSeconds);
        Assert.True(File.Exists(Path.Combine(factory.Directory, "language-model.json")));
    }

    [Fact]
    public async Task OpenAIKeyIsNeverReturnedAndBlankUpdatePreservesIt()
    {
        using var factory = new SettingsHostFactory();
        using HttpClient client = factory.CreateClient();
        var initial = new UpdateOpenAIConfigurationRequest(
            "https://example.test/v1",
            "test-model",
            "secret-value",
            "Responses",
            false,
            true);

        OpenAIConfigurationViewModel saved = await RequireJsonAsync<OpenAIConfigurationViewModel>(
            await client.PutAsJsonAsync("/api/v1/settings/openai", initial));
        Assert.True(saved.HasApiKey);
        Assert.DoesNotContain("secret-value", await client.GetStringAsync("/api/v1/settings/openai"), StringComparison.Ordinal);

        var update = initial with { Model = "next-model", ApiKey = string.Empty };
        await RequireJsonAsync<OpenAIConfigurationViewModel>(
            await client.PutAsJsonAsync("/api/v1/settings/openai", update));
        using var store = new JsonFileOpenAIConfigurationStore(Path.Combine(factory.Directory, "openai.json"));
        OpenAIConfiguration restored = Assert.IsType<OpenAIConfiguration>(await store.LoadAsync());
        Assert.Equal("secret-value", restored.ApiKey);
        Assert.Equal("next-model", restored.Model);
    }

    [Fact]
    public async Task SavedOpenAIConfigurationBecomesAvailableWithoutRestart()
    {
        using var factory = new SettingsHostFactory();
        using HttpClient client = factory.CreateClient();

        GuidanceAvailabilityViewModel initial = await RequireJsonAsync<GuidanceAvailabilityViewModel>(
            await client.GetAsync("/api/v1/guidance/"));
        Assert.False(initial.Available);

        var request = new UpdateSettingsRequest(
            new UpdateLanguageModelSettingsRequest(6, 2, 90, "Preferred", "Preferred", "Preferred"),
            new UpdateOpenAIConfigurationRequest(
                "https://example.test/v1",
                "hot-loaded-model",
                "secret-value",
                "Chat",
                true,
                null));
        SettingsSaveResultViewModel saved = await RequireJsonAsync<SettingsSaveResultViewModel>(
            await client.PutAsJsonAsync("/api/v1/settings/", request));
        Assert.False(saved.SessionRecreationRequired);
        Assert.Contains("无需重建", saved.Message, StringComparison.Ordinal);

        GuidanceAvailabilityViewModel updated = await RequireJsonAsync<GuidanceAvailabilityViewModel>(
            await client.GetAsync("/api/v1/guidance/"));
        Assert.True(updated.Available);
        Assert.Contains("即时生效", updated.Message, StringComparison.Ordinal);
        Assert.Contains("无需重建 Session", updated.Message, StringComparison.Ordinal);
    }

    private static async Task<T> RequireJsonAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<T>()
            ?? throw new InvalidOperationException("响应没有包含 JSON 内容。");
    }

    private sealed class SettingsHostFactory : WebApplicationFactory<Program>
    {
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), $"tavi-settings-host-tests-{Guid.NewGuid():N}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Tavi:SaveDirectory"] = Path.Combine(Directory, "saves"),
                    ["Tavi:SettingsDirectory"] = Directory
                }));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, true);
        }
    }
}
