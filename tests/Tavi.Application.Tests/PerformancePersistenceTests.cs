using Tavi.Domain;
using Tavi.Domain.Performance;
using Tavi.Infrastructure.Persistence;
using Xunit;

namespace Tavi.Application.Tests;

public sealed class PerformancePersistenceTests
{
    [Fact]
    public async Task JsonStoreRoundTripsFrozenOriginBeatStateAndArchive()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"tavi-performance-tests-{Guid.NewGuid():N}");
        try
        {
            Guid performanceId = Guid.NewGuid();
            Guid stateId = Guid.NewGuid();
            Guid scenarioStateId = Guid.NewGuid();
            Guid sceneId = Guid.NewGuid();
            Guid elementId = Guid.NewGuid();
            Guid beatId = Guid.NewGuid();
            var snapshot = new PerformanceSnapshot
            {
                PerformanceId = performanceId,
                StateId = stateId,
                SourceScenarioStateId = scenarioStateId,
                SourceScene = new PerformanceSourceScene
                {
                    Id = sceneId,
                    DefinitionId = "test:scene",
                    ModuleId = "test",
                    ModuleVersion = "1.0.0",
                    BasedOnScenarioStateId = scenarioStateId,
                    State = "Processing",
                    Bindings = [new PerformanceSourceSceneBinding("subject", [elementId])],
                    Elements = [new Element(elementId, "Hero", string.Empty, ElementType.None)]
                },
                Modules = [new PerformanceModuleReference("test", "1.0.0", new Dictionary<string, string> { ["tone"] = "quiet" })],
                ImportedElementIds = [elementId],
                Elements = new Dictionary<Guid, Element> { [elementId] = new Element(elementId, "Hero", string.Empty, ElementType.None) },
                Beats = new Dictionary<Guid, Beat>
                {
                    [beatId] = new Beat(
                        beatId,
                        new BeatDefinitionType("test:beat"),
                        "test",
                        "1.0.0",
                        stateId,
                        "Beat",
                        string.Empty,
                        BeatState.Binding,
                        [new BeatSlotSpecification("subject", "Subject", string.Empty, 1, 1)],
                        [new BeatSlotBinding("subject", [elementId])])
                }
            };

            using var store = new JsonFilePerformanceStore(directory);
            await store.SaveActiveAsync(snapshot);
            PerformanceSnapshot loaded = Assert.IsType<PerformanceSnapshot>(await store.LoadActiveAsync());

            Assert.Equal(performanceId, loaded.PerformanceId);
            Assert.Equal(stateId, loaded.StateId);
            Assert.Equal(sceneId, loaded.SourceScene.Id);
            Assert.Equal(elementId, Assert.Single(Assert.Single(loaded.Beats.Values).GetBindings()).ElementIds.Single());
            Assert.Equal("quiet", Assert.Single(loaded.Modules).Parameters["tone"]);

            loaded.Status = PerformanceStatus.Abandoned;
            loaded.StateId = Guid.NewGuid();
            await store.SaveActiveAsync(loaded);
            await store.ArchiveAsync(loaded);

            Assert.Null(await store.LoadActiveAsync());
            Assert.Equal(performanceId, (await store.LoadArchivedAsync(performanceId))!.PerformanceId);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}
