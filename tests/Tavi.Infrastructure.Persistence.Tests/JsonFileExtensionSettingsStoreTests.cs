using Tavi.Application.Extensions;
using Tavi.Extensibility;
using Tavi.Infrastructure.Persistence;
using Xunit;

namespace Tavi.Infrastructure.Persistence.Tests;

/// <summary>验证 Extension 设置 JSON Store 的完整往返。</summary>
public sealed class JsonFileExtensionSettingsStoreTests
{
    /// <summary>验证 Module 启用状态和参数使用稳定文本保存。</summary>
    [Fact]
    public async Task SettingsRoundTrip()
    {
        string directory = Path.Combine(Path.GetTempPath(), "tavi-extension-tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "extensions.json");
        try
        {
            using var store = new JsonFileExtensionSettingsStore(path);
            await store.SaveAsync(new ExtensionSettings { Modules = [new ExtensionModuleSettings { Module = new ModuleId("character"), Enabled = true, Parameters = new Dictionary<string, string> { ["mode"] = "story" } }] });
            ExtensionSettings loaded = Assert.IsType<ExtensionSettings>(await store.LoadAsync());
            ExtensionModuleSettings module = Assert.Single(loaded.Modules);
            Assert.True(module.Enabled);
            Assert.Equal("story", module.Parameters["mode"]);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}
