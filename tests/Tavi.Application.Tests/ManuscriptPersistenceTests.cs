using System.Text.Json;
using Tavi.Application.Writing;
using Tavi.Domain.Performance;
using Tavi.Infrastructure.Persistence;
using Xunit;

namespace Tavi.Application.Tests;

public sealed class ManuscriptPersistenceTests
{
    [Fact]
    public async Task BeatPublicationSurvivesStoreRoundTrip()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"tavi-manuscript-{Guid.NewGuid():N}");
        try
        {
            using var store = new JsonFileManuscriptStore(directory);
            await using var session = new WritingSession(store);
            await session.InitializeAsync();
            Guid stateId = (await session.CreateAsync("Story")).Manuscript!.StateId;
            var changedOperations = new List<string>();
            var stateChanges =
                new List<(SessionStateChange Change, bool IsDirty)>();
            session.Changed += (_, eventArgs) =>
                changedOperations.Add(eventArgs.Operation);
            session.StateChanged += (_, eventArgs) =>
                stateChanges.Add((eventArgs.Change, eventArgs.IsDirty));
            Guid performanceId = Guid.NewGuid();
            Guid beatId = Guid.NewGuid();
            session.PublishBeat(performanceId, beatId, [new BeatParagraph(Guid.NewGuid(), "Text")], stateId);
            await session.SaveAsync();

            Assert.Contains("PublishBeat", changedOperations);
            Assert.Contains(
                (SessionStateChange.DirtyChanged, true),
                stateChanges);
            Assert.Contains(
                (SessionStateChange.DirtyChanged, false),
                stateChanges);
            Assert.Contains(stateChanges, value =>
                value.Change == SessionStateChange.SaveStarted);
            Assert.Contains(stateChanges, value =>
                value.Change == SessionStateChange.SaveCompleted);

            using var secondStore = new JsonFileManuscriptStore(directory);
            await using var restored = new WritingSession(secondStore);
            await restored.InitializeAsync();

            Assert.Equal(beatId, Assert.Single(restored.GetSnapshot().Manuscript!.BeatPublications).BeatId);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task VersionOneManuscriptLoadsWithEmptyPublicationHistory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"tavi-manuscript-v1-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string json = JsonSerializer.Serialize(new
            {
                version = 1,
                id = Guid.NewGuid(),
                state_id = Guid.NewGuid(),
                title = "Legacy",
                status = 0,
                created_at_utc = now,
                updated_at_utc = now,
                paragraphs = Array.Empty<object>()
            });
            await File.WriteAllTextAsync(Path.Combine(directory, "active.json"), json);
            using var store = new JsonFileManuscriptStore(directory);

            var loaded = await store.LoadActiveAsync();

            Assert.NotNull(loaded);
            Assert.Empty(loaded!.BeatPublications);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}
