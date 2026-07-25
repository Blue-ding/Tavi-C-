using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Tavi.Host.ViewModels;
using Xunit;

namespace Tavi.Host.Tests;

/// <summary>验证 Host 日志可以安全持久化、关联错误响应并维护保留期限。</summary>
public sealed class LoggingPersistenceTests
{
    /// <summary>验证嵌入桌面应用的 Host 在写出首条日志前统一使用 UTF-8 控制台编码。</summary>
    [Fact]
    public async Task EmbeddedHostBuildConfiguresUtf8ConsoleEncoding()
    {
        Encoding originalEncoding = Console.OutputEncoding;
        string directory = Path.Combine(Path.GetTempPath(), $"tavi-console-encoding-tests-{Guid.NewGuid():N}");
        try
        {
            Console.OutputEncoding = Encoding.Latin1;
            WebApplication application = TaviHost.Build(
                [
                    "--Tavi:SaveDirectory", Path.Combine(directory, "saves"),
                    "--Tavi:SettingsDirectory", Path.Combine(directory, "settings"),
                    "--Tavi:Logging:Directory", Path.Combine(directory, "logs")
                ]);
            await using (application)
                Assert.Equal(Encoding.UTF8.CodePage, Console.OutputEncoding.CodePage);
        }
        finally
        {
            Console.OutputEncoding = originalEncoding;
            await DeleteDirectoryEventuallyAsync(directory);
        }
    }

    /// <summary>验证无控制台句柄的桌面进程不会因 UTF-8 初始化失败而中止 Host 启动。</summary>
    [Fact]
    public void MissingConsoleHandleDoesNotPreventHostInitialization()
    {
        Exception? exception = Record.Exception(() =>
            TaviHost.ConfigureConsoleEncoding(_ => throw new IOException("句柄无效。")));

        Assert.Null(exception);
    }

    /// <summary>验证被拒绝的请求会将错误码和 TraceId 写入滚动日志，并清理过期日志。</summary>
    [Fact]
    public async Task RejectedRequestIsPersistedWithCorrelationData()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"tavi-logging-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string expiredLog = Path.Combine(directory, "tavi-20000101.jsonl");
        await File.WriteAllTextAsync(expiredLog, "{}");
        File.SetLastWriteTimeUtc(expiredLog, DateTime.UtcNow.AddDays(-30));
        string? traceId;
        try
        {
            using (var factory = new LoggingHostFactory(directory))
            using (HttpClient client = factory.CreateClient())
            {
                WorldGraphViewModel initial = await RequireJsonAsync<WorldGraphViewModel>(await client.GetAsync("/api/v1/world/"));
                var configuration = new UpdateOpenAIConfigurationRequest("https://example.test/v1", "test-model", "must-not-leak", "Chat", false, null);
                await RequireJsonAsync<OpenAIConfigurationViewModel>(await client.PutAsJsonAsync("/api/v1/settings/openai", configuration));
                HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/world/elements", new AddElementRequest(initial.StateId, "Place", "", "Location"));
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                traceId = problem.RootElement.GetProperty("error").GetProperty("traceId").GetString();
            }

            Assert.False(File.Exists(expiredLog));
            string[] persistedLines = Directory.EnumerateFiles(directory, "tavi-*.jsonl").SelectMany(ReadAllLinesShared).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            foreach (string line in persistedLines)
                using (JsonDocument.Parse(line)) { }
            string persistedLog = string.Join(Environment.NewLine, persistedLines);
            Assert.Contains("Tavi Host", persistedLog, StringComparison.Ordinal);
            Assert.Contains("TAVI.HOST.REQUEST.INVALID_ARGUMENT", persistedLog, StringComparison.Ordinal);
            Assert.Contains(Assert.IsType<string>(traceId), persistedLog, StringComparison.Ordinal);
            Assert.DoesNotContain("must-not-leak", persistedLog, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(directory);
        }
    }

    /// <summary>验证无效日志目录只关闭文件输出而不会阻止 Host 提供服务。</summary>
    [Fact]
    public async Task InvalidLogDirectoryDoesNotPreventHostStartup()
    {
        string rootDirectory = Path.Combine(Path.GetTempPath(), $"tavi-logging-fallback-tests-{Guid.NewGuid():N}");
        try
        {
            using var factory = new LoggingHostFactory(rootDirectory, "\0");
            using HttpClient client = factory.CreateClient();
            WorldGraphViewModel world = await RequireJsonAsync<WorldGraphViewModel>(await client.GetAsync("/api/v1/world/"));
            Assert.NotEqual(Guid.Empty, world.StateId);
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(rootDirectory);
        }
    }

    private static async Task<T> RequireJsonAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new InvalidOperationException("响应没有包含 JSON 内容。");
    }

    private static IReadOnlyList<string> ReadAllLinesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
            lines.Add(line);
        return lines;
    }

    private static async Task DeleteDirectoryEventuallyAsync(string directory)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (!Directory.Exists(directory))
                return;
            try
            {
                Directory.Delete(directory, true);
                return;
            }
            catch (IOException) when (attempt < 19)
            {
                await Task.Delay(50);
            }
        }
    }

    private sealed class LoggingHostFactory : WebApplicationFactory<Program>
    {
        private readonly string _rootDirectory;
        private readonly string _logDirectory;

        internal LoggingHostFactory(string rootDirectory, string? logDirectory = null)
        {
            _rootDirectory = rootDirectory;
            _logDirectory = logDirectory ?? rootDirectory;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tavi:SaveDirectory"] = Path.Combine(_rootDirectory, "saves"),
                ["Tavi:SettingsDirectory"] = Path.Combine(_rootDirectory, "settings"),
                ["Tavi:Logging:Directory"] = _logDirectory
            }));
        }
    }
}
