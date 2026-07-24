using System.Text.Json;
using Tavi.Domain.World;
using Tavi.Infrastructure.Persistence;
using Xunit;
using RuntimeWorld = Tavi.Domain.World.World;

namespace Tavi.Infrastructure.Persistence.Tests;

/// <summary>验证版本化 World JSON 存档、备份恢复和 V1 迁移。</summary>
public sealed class JsonFileWorldStoreTests
{
    /// <summary>验证包含四类实体、开放类型和 Quantity 的 V2 World 可以完整往返。</summary>
    [Fact]
    public async Task CompleteWorldCanRoundTrip()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonFileWorldStore(directory.Path);
        RuntimeWorld world = RuntimeWorld.Create(new WorldSnapshot());
        Guid aliceId = world.AddElement("Alice", "Person", new ElementType("story:person"));
        Guid swordId = world.AddElement("Sword", "Item", new ElementType("story:item"));
        Guid scopeId = world.AddScope("Alice belief", "Epistemic scope", 0.9, new ScopeType("epistemic:belief"), aliceId);
        Guid aspectId = world.AddAspect("afraid", "Fear", 0.7, new AspectType("emotion:fear"), aliceId, scopeId);
        Guid relationId = world.AddRelation("owns", "Ownership", 1, new RelationType("social:ownership"), aliceId, swordId, scopeId);

        await store.SaveAsync("slot", world.CreateSnapshot());
        RuntimeWorld restored = RuntimeWorld.Create(Assert.IsType<WorldSnapshot>(await store.LoadAsync("slot")));

        Assert.Equal("story:person", restored.GetElement(aliceId).Type.Value);
        Assert.Equal(0.9, restored.GetScope(scopeId).Quantity);
        Assert.Equal(0.7, restored.GetAspect(aspectId).Quantity);
        Assert.Equal("social:ownership", restored.GetRelation(relationId).Type.Value);
        using JsonDocument json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory.Path, "slot.save.json")));
        Assert.Equal(2, json.RootElement.GetProperty("version").GetInt32());
    }

    /// <summary>验证主文件损坏时会读取上一个有效 V2 备份。</summary>
    [Fact]
    public async Task CorruptPrimaryFallsBackToPreviousBackup()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonFileWorldStore(directory.Path);
        RuntimeWorld world = RuntimeWorld.Create(new WorldSnapshot());
        Guid firstId = world.AddElement("First", "", ElementType.None);
        await store.SaveAsync("slot", world.CreateSnapshot());
        world.AddElement("Second", "", ElementType.None);
        await store.SaveAsync("slot", world.CreateSnapshot());
        string primaryPath = Path.Combine(directory.Path, "slot.save.json");
        string backupPath = Path.Combine(directory.Path, "slot.save.bak.json");
        await File.WriteAllTextAsync(primaryPath, "{ invalid json");

        RuntimeWorld restored = RuntimeWorld.Create(Assert.IsType<WorldSnapshot>(await store.LoadAsync("slot")));

        Assert.True(File.Exists(backupPath));
        Assert.Equal(firstId, restored.GetElement(firstId).Id);
        Assert.Single(restored.GetElements());
    }

    /// <summary>验证 V1 Anchor、主世界 Relation 和 SubWorld Relation 会无损迁移为兼容 Element、Scope 和 Relation。</summary>
    [Fact]
    public async Task VersionOneSaveMigratesDeterministically()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonFileWorldStore(directory.Path);
        Guid worldId = Guid.NewGuid();
        Guid aliceId = Guid.NewGuid();
        Guid itemId = Guid.NewGuid();
        Guid worldRelationId = Guid.NewGuid();
        Guid subWorldId = Guid.NewGuid();
        Guid subWorldRelationId = Guid.NewGuid();
        string json = $$"""
        {
          "version": 1,
          "saved_at_utc": "2026-01-01T00:00:00Z",
          "world": {
            "id": "{{worldId}}",
            "anchors": [
              { "id": "{{aliceId}}", "name": "Alice", "description": "", "type": "Character" },
              { "id": "{{itemId}}", "name": "Key", "description": "", "type": "Item" }
            ],
            "relations": [
              { "id": "{{worldRelationId}}", "name": "owns", "description": "", "source_id": "{{aliceId}}", "target_id": "{{itemId}}" }
            ],
            "sub_worlds": [
              {
                "id": "{{subWorldId}}",
                "domain_id": "{{aliceId}}",
                "relations": [
                  { "id": "{{subWorldRelationId}}", "name": "believes", "description": "", "source_id": "{{aliceId}}", "target_id": "{{itemId}}" }
                ]
              }
            ]
          }
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "slot.save.json"), json);

        WorldSnapshot first = Assert.IsType<WorldSnapshot>(await store.LoadAsync("slot"));
        WorldSnapshot second = Assert.IsType<WorldSnapshot>(await store.LoadAsync("slot"));

        Assert.Equal(worldId, first.Id);
        Assert.Equal("legacy:character", first.Elements[aliceId].Type.Value);
        Assert.Equal("legacy:item", first.Elements[itemId].Type.Value);
        Assert.Equal(subWorldId, first.Relations[subWorldRelationId].ScopeId);
        Assert.Equal("legacy:character-subworld", first.Scopes[subWorldId].Type.Value);
        Scope worldScope = first.Scopes.Values.Single(scope => scope.Id != subWorldId);
        Assert.Equal("legacy:world", worldScope.Type.Value);
        Assert.Equal(worldScope.Id, first.Relations[worldRelationId].ScopeId);
        Assert.Equal(first.Elements.Keys.Order(), second.Elements.Keys.Order());
        Assert.Equal(first.Scopes.Keys.Order(), second.Scopes.Keys.Order());
    }

    /// <summary>验证主文件和备份均损坏时返回稳定 Storage 错误。</summary>
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

    /// <summary>验证未知存档版本返回稳定读取失败。</summary>
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

    /// <summary>验证不存在主文件和备份的存档槽返回 null。</summary>
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
