using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Tavi.Application.Writing;
using Tavi.Host.ViewModels;
using Xunit;

namespace Tavi.Host.Tests;

/// <summary>验证 Writing Host 接口的创建、段落编辑、并发、保存和归档流程。</summary>
public sealed class WritingEndpointTests
{
    /// <summary>验证空白手稿可以编辑段落、保存、归档并进入只读手稿库。</summary>
    [Fact]
    public async Task ManuscriptCanBeEditedSavedAndArchived()
    {
        using var factory = new WritingHostFactory();
        using HttpClient client = factory.CreateClient();
        WritingWorkspaceViewModel empty = await RequireJsonAsync<WritingWorkspaceViewModel>(await client.GetAsync("/api/v1/writing/"));
        Assert.Null(empty.Session.Manuscript);

        WritingSnapshotViewModel created = await RequireJsonAsync<WritingSnapshotViewModel>(await client.PostAsJsonAsync("/api/v1/writing/manuscripts", new CreateManuscriptRequest("旅途")));
        WritingSnapshotViewModel inserted = await RequireJsonAsync<WritingSnapshotViewModel>(await client.PostAsJsonAsync("/api/v1/writing/session/paragraphs", new InsertParagraphRequest(created.Manuscript!.StateId, 0, "")));
        Guid paragraphId = inserted.Manuscript!.Paragraphs.Single().Id;
        WritingSnapshotViewModel updated = await RequireJsonAsync<WritingSnapshotViewModel>(await client.PatchAsJsonAsync($"/api/v1/writing/session/paragraphs/{paragraphId}", new UpdateParagraphRequest(inserted.Manuscript.StateId, "故事从雨夜开始。")));
        WritingSnapshotViewModel saved = await RequireJsonAsync<WritingSnapshotViewModel>(await client.PostAsJsonAsync("/api/v1/writing/session/save", new { }));
        Assert.False(saved.IsDirty);

        ManuscriptViewModel archived = await RequireJsonAsync<ManuscriptViewModel>(await client.PostAsJsonAsync("/api/v1/writing/session/archive", new WritingStateRequest(updated.Manuscript!.StateId)));
        Assert.Equal("Archived", archived.Status);
        WritingWorkspaceViewModel library = await RequireJsonAsync<WritingWorkspaceViewModel>(await client.GetAsync("/api/v1/writing/"));
        Assert.Null(library.Session.Manuscript);
        Assert.Equal("故事从雨夜开始。", Assert.Single((await RequireJsonAsync<ManuscriptViewModel>(await client.GetAsync($"/api/v1/writing/manuscripts/{archived.Id}"))).Paragraphs).Text);
    }

    /// <summary>验证旧版本段落请求返回稳定的 HTTP 409。</summary>
    [Fact]
    public async Task StaleParagraphEditReturnsConflict()
    {
        using var factory = new WritingHostFactory();
        using HttpClient client = factory.CreateClient();
        WritingSnapshotViewModel created = await RequireJsonAsync<WritingSnapshotViewModel>(await client.PostAsJsonAsync("/api/v1/writing/manuscripts", new CreateManuscriptRequest("并发测试")));
        Guid staleState = created.Manuscript!.StateId;
        await RequireJsonAsync<WritingSnapshotViewModel>(await client.PostAsJsonAsync("/api/v1/writing/session/paragraphs", new InsertParagraphRequest(staleState, 0)));
        HttpResponseMessage response = await client.PatchAsJsonAsync("/api/v1/writing/session/title", new RenameManuscriptRequest("过期", staleState));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(WritingErrorCodes.StateConflict, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static async Task<T> RequireJsonAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new InvalidOperationException("响应没有包含 JSON 内容。");
    }

    private sealed class WritingHostFactory : WebApplicationFactory<Program>
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tavi-writing-host-{Guid.NewGuid():N}");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Tavi:SaveDirectory"] = Path.Combine(_directory, "world"), ["Tavi:ManuscriptDirectory"] = Path.Combine(_directory, "manuscripts"), ["Tavi:OpenAI:ConfigurationPath"] = Path.Combine(_directory, "missing-openai.json"), ["Tavi:Writing:AutoSave"] = "false" }));
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(_directory))
                Directory.Delete(_directory, true);
        }
    }
}
