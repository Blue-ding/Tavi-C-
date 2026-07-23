using Tavi.Application.LanguageModel;
using Tavi.Infrastructure.Persistence;
using Xunit;

namespace Tavi.Infrastructure.Persistence.Tests;

public sealed class JsonFileLanguageModelSettingsStoreTests
{
    [Fact]
    public async Task SettingsCanRoundTripWithoutProviderSecrets()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonFileLanguageModelSettingsStore(
            Path.Combine(directory.Path, "language-model.json"));
        var settings = new LanguageModelSettings
        {
            MaxToolRounds = 4,
            MaxOutputRepairAttempts = 3,
            OverallTimeout = TimeSpan.FromSeconds(45),
            ToolCalls = FeaturePolicy.Required,
            NativeJsonOutput = FeaturePolicy.Preferred,
            Streaming = FeaturePolicy.Disabled
        };

        await store.SaveAsync(settings);
        LanguageModelSettings? restored = await store.LoadAsync();
        string file = await File.ReadAllTextAsync(store.Path);

        Assert.Equal(settings, restored);
        Assert.DoesNotContain("api_key", file, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"tool_calls\": \"required\"", file);
    }

    [Fact]
    public async Task InvalidSettingsAreReportedByStoreBoundary()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "language-model.json");
        await File.WriteAllTextAsync(path, """{"version":999}""");
        using var store = new JsonFileLanguageModelSettingsStore(path);

        LanguageModelSettingsStoreException exception = await Assert.ThrowsAsync<LanguageModelSettingsStoreException>(() => store.LoadAsync());

        Assert.Equal(StorageErrorCodes.LanguageModelSettingsReadFailed, exception.ErrorCode);
        Assert.Equal(TaviErrorCategory.Storage, exception.Category);
        Assert.Equal("Load", exception.Operation);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Tavi.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
