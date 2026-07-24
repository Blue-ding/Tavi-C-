using System.Security.Cryptography;
using System.Text;
using Tavi.Domain.World;

namespace Tavi.Infrastructure.Persistence;

/// <summary>在版本化 JSON DTO 与当前 WorldSnapshot 之间执行无语义猜测的映射和显式 V1 兼容迁移。</summary>
internal static class WorldSaveMapper
{
    private static readonly ElementType LegacyCharacterType = new("legacy:character");
    private static readonly ElementType LegacyItemType = new("legacy:item");
    private static readonly ElementType LegacyWorldElementType = new("legacy:world");
    private static readonly ScopeType LegacyWorldScopeType = new("legacy:world");
    private static readonly ScopeType LegacySubWorldScopeType = new("legacy:character-subworld");

    internal static WorldSaveDataV2 FromDomain(WorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new WorldSaveDataV2
        {
            Id = snapshot.Id,
            Elements = snapshot.Elements.Values.Select(value => new ElementSaveDataV2 { Id = value.Id, Name = value.Name, Description = value.Description, Type = value.Type.Value }).ToList(),
            Aspects = snapshot.Aspects.Values.Select(value => new AspectSaveDataV2 { Id = value.Id, Name = value.Name, Description = value.Description, Quantity = value.Quantity, Type = value.Type.Value, ElementId = value.ElementId, ScopeId = value.ScopeId }).ToList(),
            Relations = snapshot.Relations.Values.Select(value => new RelationSaveDataV2 { Id = value.Id, Name = value.Name, Description = value.Description, Quantity = value.Quantity, Type = value.Type.Value, SourceElementId = value.SourceElementId, TargetElementId = value.TargetElementId, ScopeId = value.ScopeId }).ToList(),
            Scopes = snapshot.Scopes.Values.Select(value => new ScopeSaveDataV2 { Id = value.Id, Name = value.Name, Description = value.Description, Quantity = value.Quantity, Type = value.Type.Value, OwnerElementId = value.OwnerElementId }).ToList()
        };
    }

    internal static WorldSnapshot ToDomain(WorldSaveDataV2 data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Elements is null || data.Aspects is null || data.Relations is null || data.Scopes is null)
            throw new InvalidDataException("V2 世界存档集合不能为 null。");
        var snapshot = new WorldSnapshot { Id = data.Id };
        foreach (ElementSaveDataV2 value in data.Elements)
        {
            if (value is null)
                throw new InvalidDataException("Element 存档项不能为 null。");
            var element = new Element(value.Id, RequireText(value.Name, "Element.Name"), RequireValue(value.Description, "Element.Description"), ParseElementType(value.Type));
            AddUnique(snapshot.Elements, element.Id, element, "Element");
        }
        foreach (ScopeSaveDataV2 value in data.Scopes)
        {
            if (value is null)
                throw new InvalidDataException("Scope 存档项不能为 null。");
            var scope = new Scope(value.Id, RequireText(value.Name, "Scope.Name"), RequireValue(value.Description, "Scope.Description"), RequireFinite(value.Quantity, "Scope.Quantity"), ParseScopeType(value.Type), value.OwnerElementId);
            AddUnique(snapshot.Scopes, scope.Id, scope, "Scope");
        }
        foreach (AspectSaveDataV2 value in data.Aspects)
        {
            if (value is null)
                throw new InvalidDataException("Aspect 存档项不能为 null。");
            var aspect = new Aspect(value.Id, RequireText(value.Name, "Aspect.Name"), RequireValue(value.Description, "Aspect.Description"), RequireFinite(value.Quantity, "Aspect.Quantity"), ParseAspectType(value.Type), value.ElementId, value.ScopeId);
            AddUnique(snapshot.Aspects, aspect.Id, aspect, "Aspect");
        }
        foreach (RelationSaveDataV2 value in data.Relations)
        {
            if (value is null)
                throw new InvalidDataException("Relation 存档项不能为 null。");
            var relation = new Relation(value.Id, RequireText(value.Name, "Relation.Name"), RequireValue(value.Description, "Relation.Description"), RequireFinite(value.Quantity, "Relation.Quantity"), ParseRelationType(value.Type), value.SourceElementId, value.TargetElementId, value.ScopeId);
            AddUnique(snapshot.Relations, relation.Id, relation, "Relation");
        }
        return snapshot;
    }

    internal static WorldSnapshot MigrateFromV1(WorldSaveDataV1 data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Anchors is null || data.Relations is null || data.SubWorlds is null)
            throw new InvalidDataException("V1 世界存档集合不能为 null。");
        var snapshot = new WorldSnapshot { Id = data.Id };
        foreach (AnchorSaveDataV1 value in data.Anchors)
        {
            if (value is null)
                throw new InvalidDataException("V1 Anchor 存档项不能为 null。");
            ElementType type = value.Type switch
            {
                "Character" => LegacyCharacterType,
                "Item" => LegacyItemType,
                _ => throw new InvalidDataException($"V1 AnchorType“{value.Type}”无效。")
            };
            var element = new Element(value.Id, RequireText(value.Name, "V1 Anchor.Name"), RequireValue(value.Description, "V1 Anchor.Description"), type);
            AddUnique(snapshot.Elements, element.Id, element, "V1 Anchor");
        }
        if (data.Relations.Count > 0)
            MigrateV1WorldRelations(data, snapshot);
        foreach (SubWorldSaveDataV1 subWorld in data.SubWorlds)
            MigrateV1SubWorld(subWorld, snapshot);
        return snapshot;
    }

    private static void MigrateV1WorldRelations(WorldSaveDataV1 data, WorldSnapshot snapshot)
    {
        Guid ownerId = DeriveUniqueGuid(data.Id, "v1-world-owner", snapshot.Elements.Keys);
        Guid scopeId = DeriveUniqueGuid(data.Id, "v1-world-scope", snapshot.Scopes.Keys);
        snapshot.Elements.Add(ownerId, new Element(ownerId, "Legacy World", "由 V1 主世界 Relation 迁移生成的兼容 Owner Element。", LegacyWorldElementType));
        snapshot.Scopes.Add(scopeId, new Scope(scopeId, "Legacy World", "由 V1 主世界 Relation 迁移生成的兼容断言域。", 1, LegacyWorldScopeType, ownerId));
        foreach (RelationSaveDataV1 value in data.Relations)
            AddMigratedRelation(value, scopeId, snapshot);
    }

    private static void MigrateV1SubWorld(SubWorldSaveDataV1 value, WorldSnapshot snapshot)
    {
        if (value is null || value.Relations is null)
            throw new InvalidDataException("V1 SubWorld 存档项及其 Relations 不能为 null。");
        string ownerName = snapshot.Elements.GetValueOrDefault(value.DomainId)?.Name ?? value.DomainId.ToString();
        var scope = new Scope(value.Id, $"{ownerName} Legacy SubWorld", "由 V1 Character SubWorld 迁移生成的兼容断言域。", 1, LegacySubWorldScopeType, value.DomainId);
        AddUnique(snapshot.Scopes, scope.Id, scope, "V1 SubWorld");
        foreach (RelationSaveDataV1 relation in value.Relations)
            AddMigratedRelation(relation, scope.Id, snapshot);
    }

    private static void AddMigratedRelation(RelationSaveDataV1 value, Guid scopeId, WorldSnapshot snapshot)
    {
        if (value is null)
            throw new InvalidDataException("V1 Relation 存档项不能为 null。");
        var relation = new Relation(value.Id, RequireText(value.Name, "V1 Relation.Name"), RequireValue(value.Description, "V1 Relation.Description"), 1, RelationType.None, value.SourceId, value.TargetId, scopeId);
        AddUnique(snapshot.Relations, relation.Id, relation, "V1 Relation");
    }

    /// <summary>从旧 StateId、稳定类别标签和固定 salt 确定性派生合成实体 Guid；同一输入在所有加载中得到相同结果。</summary>
    private static Guid DeriveUniqueGuid(Guid worldId, string category, IEnumerable<Guid> occupied)
    {
        HashSet<Guid> used = occupied.ToHashSet();
        for (int salt = 0; ; salt++)
        {
            byte[] input = Encoding.UTF8.GetBytes($"{worldId:D}|{category}|{salt}");
            byte[] bytes = SHA256.HashData(input)[..16];
            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
            var candidate = new Guid(bytes);
            if (candidate != Guid.Empty && !used.Contains(candidate))
                return candidate;
        }
    }

    private static ElementType ParseElementType(string? value) => ParseType(value, "Element.Type", text => new ElementType(text));
    private static AspectType ParseAspectType(string? value) => ParseType(value, "Aspect.Type", text => new AspectType(text));
    private static RelationType ParseRelationType(string? value) => ParseType(value, "Relation.Type", text => new RelationType(text));
    private static ScopeType ParseScopeType(string? value) => ParseType(value, "Scope.Type", text => new ScopeType(text));

    private static T ParseType<T>(string? value, string path, Func<string, T> factory)
    {
        string text = RequireText(value, path);
        try
        {
            return factory(text);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"{path}“{text}”无效。", exception);
        }
    }

    private static void AddUnique<T>(IDictionary<Guid, T> values, Guid id, T value, string entityName)
    {
        if (!values.TryAdd(id, value))
            throw new InvalidDataException($"{entityName} Id {id} 重复。");
    }

    private static double RequireFinite(double value, string path) => double.IsFinite(value) ? value : throw new InvalidDataException($"{path} 必须是有限 double。");
    private static string RequireText(string? value, string path) => !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException($"{path} 不能为空。");
    private static string RequireValue(string? value, string path) => value ?? throw new InvalidDataException($"{path} 不能为 null。");
}
