using Tavi.Domain.World;
using Tavi.Infrastructure.Persistence;
using Xunit;
using RuntimeWorld = Tavi.Domain.World.World;

namespace Tavi.Infrastructure.Persistence.Tests;

public sealed class JsonFileWorldStoreTests
{
    [Fact]
    public async Task CompleteWorldCanRoundTrip()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonFileWorldStore(directory.Path);
        RuntimeWorld world = RuntimeWorld.Create(new WorldSnapshot());
        Guid characterId = world.AddAnchor("Alice", "Character", AnchorType.Character);
        Guid itemId = world.AddAnchor("Sword", "Item", AnchorType.Item);
        Guid worldRelationId = world.AddRelation("owns", "World relation", characterId, itemId);
        Guid subWorldRelationId = world.AddRelation("believes", "Sub-world relation", characterId, itemId, characterId);

        await store.SaveAsync("slot", world.CreateSnapshot());
        WorldSnapshot? restoredSnapshot = await store.LoadAsync("slot");
        RuntimeWorld restored = RuntimeWorld.Create(Assert.IsType<WorldSnapshot>(restoredSnapshot));

        Assert.Equal(characterId, restored.GetAnchor(characterId).Id);
        Assert.Equal(worldRelationId, restored.GetRelation(worldRelationId).Id);
        Assert.Equal(subWorldRelationId, restored.GetRelation(subWorldRelationId).Id);
        Assert.Single(restored.GetSubWorlds());
    }

    [Fact]
    public async Task CorruptPrimaryFallsBackToPreviousBackup()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonFileWorldStore(directory.Path);
        RuntimeWorld world = RuntimeWorld.Create(new WorldSnapshot());
        Guid firstId = world.AddAnchor("First", "", AnchorType.Item);
        await store.SaveAsync("slot", world.CreateSnapshot());
        world.AddAnchor("Second", "", AnchorType.Item);
        await store.SaveAsync("slot", world.CreateSnapshot());
        string primaryPath = Path.Combine(directory.Path, "slot.save.json");
        string backupPath = Path.Combine(directory.Path, "slot.save.bak.json");
        await File.WriteAllTextAsync(primaryPath, "{ invalid json");

        WorldSnapshot? restoredSnapshot = await store.LoadAsync("slot");
        RuntimeWorld restored = RuntimeWorld.Create(Assert.IsType<WorldSnapshot>(restoredSnapshot));

        Assert.True(File.Exists(backupPath));
        Assert.Equal(firstId, restored.GetAnchor(firstId).Id);
        Assert.Single(restored.GetAnchors());
    }

    [Fact]
    public async Task InvalidPrimaryAndBackupReportFailure()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonFileWorldStore(directory.Path);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "slot.save.json"), "{ invalid");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "slot.save.bak.json"), "{ invalid");

        WorldStoreException exception = await Assert.ThrowsAsync<WorldStoreException>(() => store.LoadAsync("slot"));

        Assert.Equal(StorageErrorCodes.WorldReadFailed, exception.ErrorCode);
        Assert.Equal(TaviErrorCategory.Storage, exception.Category);
        Assert.Equal("Load", exception.Operation);
        Assert.Equal("slot", exception.Details["Slot"]);
    }

    [Fact]
    public async Task UnsupportedVersionReportsFailure()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonFileWorldStore(directory.Path);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "slot.save.json"), """{"version":999,"saved_at_utc":"2026-01-01T00:00:00Z","world":{}}""");

        WorldStoreException exception = await Assert.ThrowsAsync<WorldStoreException>(() => store.LoadAsync("slot"));

        Assert.Contains("无法读取", exception.Message);
        Assert.Equal(StorageErrorCodes.WorldReadFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task MissingSlotReturnsNull()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonFileWorldStore(directory.Path);

        Assert.Null(await store.LoadAsync("missing"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Tavi.Tests", Guid.NewGuid().ToString("N"));
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
