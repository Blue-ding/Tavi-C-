using Tavi.Domain.Scenario;
using Tavi.Infrastructure.Persistence;
using Xunit;

namespace Tavi.Infrastructure.Persistence.Tests;

/// <summary>验证 Scenario 独立版本化 JSON Store 的完整往返与 Scene 槽位保存。</summary>
public sealed class JsonFileScenarioStoreTests
{
    /// <summary>验证 EARS、Module 引用和 Scene 可以无损保存并重新加载。</summary>
    [Fact]
    public async Task ScenarioRoundTripsWithSceneBindings()
    {
        string directory = Path.Combine(Path.GetTempPath(), "tavi-scenario-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Guid elementId = Guid.NewGuid();
            Guid sceneId = Guid.NewGuid();
            var snapshot = new ScenarioSnapshot { SourceWorldStateId = Guid.NewGuid(), Modules = [new ScenarioModuleReference("character", "1.0.0")] };
            snapshot.Elements.Add(elementId, new Element(elementId, "Alice", "", new ElementType("character:character")));
            snapshot.Scenes.Add(sceneId, new Scene(sceneId, new SceneDefinitionType("character:test"), "character", "1.0.0", snapshot.Id, "Test", "", SceneSettlementOptions.Writing, SceneState.Processing, [new SceneSlotSpecification("actor", "Actor", "", 1, 1, ["character:character"])], [new SceneSlotBinding("actor", [elementId])]));
            using var store = new JsonFileScenarioStore(directory);

            await store.SaveAsync("default", snapshot);
            ScenarioSnapshot loaded = Assert.IsType<ScenarioSnapshot>(await store.LoadAsync("default"));

            Assert.Equal(snapshot.Id, loaded.Id);
            Assert.Equal(elementId, loaded.Scenes[sceneId].FindBinding("actor")!.ElementIds.Single());
            Assert.Equal(SceneState.Processing, loaded.Scenes[sceneId].State);
            Assert.Equal("actor", Assert.Single(loaded.Scenes[sceneId].GetSlotSpecifications()).Id);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}
