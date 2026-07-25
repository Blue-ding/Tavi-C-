using System.Security.Cryptography;
using System.Text;
using Tavi.Domain.World;

namespace Tavi.Infrastructure.Persistence;

/// <summary>在版本化 JSON DTO 与当前 WorldSnapshot 之间执行显式映射和兼容迁移。</summary>
internal static class WorldSaveMapper
{
    private static readonly ElementType LegacyCharacterType = new("legacy:character");
    private static readonly ElementType LegacyItemType = new("legacy:item");
    private static readonly ElementType LegacyWorldElementType = new("legacy:world");
    private static readonly ScopeType LegacyWorldScopeType = new("legacy:world");
    private static readonly ScopeType LegacySubWorldScopeType = new("legacy:character-subworld");

    internal static WorldSaveDataV3 FromDomain(WorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new WorldSaveDataV3
        {
            Id = snapshot.Id,
            Elements = snapshot.Elements.Values.Select(value => new ElementSaveDataV2 { Id = value.Id, Name = value.Name, Description = value.Description, Type = value.Type.Value }).ToList(),
            Scopes = snapshot.Scopes.Values.Select(value => new ScopeSaveDataV3 { Id = value.Id, Quantity = value.Quantity, Type = value.Type.Value, OwnerElementId = value.OwnerElementId }).ToList(),
            Aspects = snapshot.Aspects.Values.Select(value => new AspectSaveDataV3 { Id = value.Id, Quantity = value.Quantity, Type = value.Type.Value, ElementId = value.ElementId, ScopeId = value.ScopeId }).ToList(),
            Relations = snapshot.Relations.Values.Select(value => new RelationSaveDataV3 { Id = value.Id, Quantity = value.Quantity, Type = value.Type.Value, SourceElementId = value.SourceElementId, TargetElementId = value.TargetElementId, ScopeId = value.ScopeId }).ToList(),
            LocalAspects = snapshot.LocalAspects.Values.Select(value => new LocalAspectSaveDataV3 { Id = value.Id, Name = value.Name, Description = value.Description, Quantity = value.Quantity, ElementId = value.ElementId, ScopeId = value.ScopeId }).ToList(),
            LocalRelations = snapshot.LocalRelations.Values.Select(value => new LocalRelationSaveDataV3 { Id = value.Id, Name = value.Name, Description = value.Description, Quantity = value.Quantity, SourceElementId = value.SourceElementId, TargetElementId = value.TargetElementId, ScopeId = value.ScopeId }).ToList()
        };
    }

    internal static WorldSnapshot ToDomain(WorldSaveDataV3 data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Elements is null || data.Scopes is null || data.Aspects is null || data.Relations is null || data.LocalAspects is null || data.LocalRelations is null)
            throw new InvalidDataException("V3 世界存档集合不能为 null。");
        var snapshot = new WorldSnapshot { Id = data.Id };
        foreach (ElementSaveDataV2 value in data.Elements)
            Add(snapshot.Elements, Require(value, "Element").Id, new Element(value.Id, RequireText(value.Name, "Element.Name"), RequireValue(value.Description, "Element.Description"), Parse(value.Type, "Element.Type", text => new ElementType(text))), "Element");
        foreach (ScopeSaveDataV3 value in data.Scopes)
            Add(snapshot.Scopes, Require(value, "Scope").Id, new Scope(value.Id, value.Quantity, Parse(value.Type, "Scope.Type", text => new ScopeType(text)), value.OwnerElementId), "Scope");
        foreach (AspectSaveDataV3 value in data.Aspects)
            Add(snapshot.Aspects, Require(value, "Aspect").Id, new Aspect(value.Id, value.Quantity, Parse(value.Type, "Aspect.Type", text => new AspectType(text)), value.ElementId, value.ScopeId), "Aspect");
        foreach (RelationSaveDataV3 value in data.Relations)
            Add(snapshot.Relations, Require(value, "Relation").Id, new Relation(value.Id, value.Quantity, Parse(value.Type, "Relation.Type", text => new RelationType(text)), value.SourceElementId, value.TargetElementId, value.ScopeId), "Relation");
        foreach (LocalAspectSaveDataV3 value in data.LocalAspects)
            Add(snapshot.LocalAspects, Require(value, "LocalAspect").Id, new LocalAspect(value.Id, RequireText(value.Name, "LocalAspect.Name"), RequireValue(value.Description, "LocalAspect.Description"), value.Quantity, value.ElementId, value.ScopeId), "LocalAspect");
        foreach (LocalRelationSaveDataV3 value in data.LocalRelations)
            Add(snapshot.LocalRelations, Require(value, "LocalRelation").Id, new LocalRelation(value.Id, RequireText(value.Name, "LocalRelation.Name"), RequireValue(value.Description, "LocalRelation.Description"), value.Quantity, value.SourceElementId, value.TargetElementId, value.ScopeId), "LocalRelation");
        return snapshot;
    }

    internal static WorldSnapshot MigrateFromV2(WorldSaveDataV2 data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Elements is null || data.Scopes is null || data.Aspects is null || data.Relations is null)
            throw new InvalidDataException("V2 世界存档集合不能为 null。");
        var snapshot = new WorldSnapshot { Id = data.Id };
        foreach (ElementSaveDataV2 value in data.Elements)
            Add(snapshot.Elements, Require(value, "Element").Id, new Element(value.Id, RequireText(value.Name, "Element.Name"), RequireValue(value.Description, "Element.Description"), Parse(value.Type, "Element.Type", text => new ElementType(text))), "Element");
        foreach (ScopeSaveDataV2 value in data.Scopes)
            Add(snapshot.Scopes, Require(value, "Scope").Id, new Scope(value.Id, RequireInteger(value.Quantity, "Scope.Quantity"), Parse(value.Type, "Scope.Type", text => new ScopeType(text)), value.OwnerElementId), "Scope");
        foreach (AspectSaveDataV2 value in data.Aspects)
        {
            Require(value, "Aspect");
            int quantity = RequireInteger(value.Quantity, "Aspect.Quantity");
            if (value.Type == AspectType.None.Value)
                Add(snapshot.LocalAspects, value.Id, new LocalAspect(value.Id, RequireText(value.Name, "Aspect.Name"), RequireValue(value.Description, "Aspect.Description"), quantity, value.ElementId, value.ScopeId), "LocalAspect");
            else
                Add(snapshot.Aspects, value.Id, new Aspect(value.Id, quantity, Parse(value.Type, "Aspect.Type", text => new AspectType(text)), value.ElementId, value.ScopeId), "Aspect");
        }
        foreach (RelationSaveDataV2 value in data.Relations)
        {
            Require(value, "Relation");
            int quantity = RequireInteger(value.Quantity, "Relation.Quantity");
            if (value.Type == RelationType.None.Value)
                Add(snapshot.LocalRelations, value.Id, new LocalRelation(value.Id, RequireText(value.Name, "Relation.Name"), RequireValue(value.Description, "Relation.Description"), quantity, value.SourceElementId, value.TargetElementId, value.ScopeId), "LocalRelation");
            else
                Add(snapshot.Relations, value.Id, new Relation(value.Id, quantity, Parse(value.Type, "Relation.Type", text => new RelationType(text)), value.SourceElementId, value.TargetElementId, value.ScopeId), "Relation");
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
            Require(value, "V1 Anchor");
            ElementType type = value.Type switch { "Character" => LegacyCharacterType, "Item" => LegacyItemType, _ => throw new InvalidDataException($"V1 AnchorType“{value.Type}”无效。") };
            Add(snapshot.Elements, value.Id, new Element(value.Id, RequireText(value.Name, "V1 Anchor.Name"), RequireValue(value.Description, "V1 Anchor.Description"), type), "V1 Anchor");
        }
        if (data.Relations.Count > 0)
            MigrateWorldRelations(data, snapshot);
        foreach (SubWorldSaveDataV1 value in data.SubWorlds)
            MigrateSubWorld(value, snapshot);
        return snapshot;
    }

    private static void MigrateWorldRelations(WorldSaveDataV1 data, WorldSnapshot snapshot)
    {
        Guid ownerId = DeriveUniqueGuid(data.Id, "v1-world-owner", snapshot.Elements.Keys);
        Guid scopeId = DeriveUniqueGuid(data.Id, "v1-world-scope", snapshot.Scopes.Keys);
        snapshot.Elements.Add(ownerId, new Element(ownerId, "Legacy World", "由 V1 主世界 Relation 迁移生成的兼容 Owner Element。", LegacyWorldElementType));
        snapshot.Scopes.Add(scopeId, new Scope(scopeId, 1, LegacyWorldScopeType, ownerId));
        foreach (RelationSaveDataV1 value in data.Relations)
            AddMigratedRelation(value, scopeId, snapshot);
    }

    private static void MigrateSubWorld(SubWorldSaveDataV1 value, WorldSnapshot snapshot)
    {
        if (value is null || value.Relations is null)
            throw new InvalidDataException("V1 SubWorld 存档项及其 Relations 不能为 null。");
        var scope = new Scope(value.Id, 1, LegacySubWorldScopeType, value.DomainId);
        Add(snapshot.Scopes, scope.Id, scope, "V1 SubWorld");
        foreach (RelationSaveDataV1 relation in value.Relations)
            AddMigratedRelation(relation, scope.Id, snapshot);
    }

    private static void AddMigratedRelation(RelationSaveDataV1 value, Guid scopeId, WorldSnapshot snapshot)
    {
        Require(value, "V1 Relation");
        var relation = new LocalRelation(value.Id, RequireText(value.Name, "V1 Relation.Name"), RequireValue(value.Description, "V1 Relation.Description"), 1, value.SourceId, value.TargetId, scopeId);
        Add(snapshot.LocalRelations, relation.Id, relation, "V1 LocalRelation");
    }

    private static Guid DeriveUniqueGuid(Guid worldId, string category, IEnumerable<Guid> occupied)
    {
        HashSet<Guid> used = occupied.ToHashSet();
        for (int salt = 0; ; salt++)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{worldId:D}|{category}|{salt}"))[..16];
            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
            var candidate = new Guid(bytes);
            if (candidate != Guid.Empty && !used.Contains(candidate))
                return candidate;
        }
    }

    private static T Parse<T>(string? value, string path, Func<string, T> factory)
    {
        string text = RequireText(value, path);
        try { return factory(text); }
        catch (ArgumentException exception) { throw new InvalidDataException($"{path}“{text}”无效。", exception); }
    }

    private static T Require<T>(T? value, string entity) where T : class => value ?? throw new InvalidDataException($"{entity} 存档项不能为 null。");
    private static void Add<T>(IDictionary<Guid, T> values, Guid id, T value, string entity)
    {
        if (!values.TryAdd(id, value))
            throw new InvalidDataException($"{entity} Id {id} 重复。");
    }

    private static int RequireInteger(double value, string path)
    {
        if (!double.IsFinite(value) || value < int.MinValue || value > int.MaxValue || value != Math.Truncate(value))
            throw new InvalidDataException($"{path} 必须是 Int32 范围内的整数；旧存档值为 {value}。");
        return (int)value;
    }

    private static string RequireText(string? value, string path) => !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException($"{path} 不能为空。");
    private static string RequireValue(string? value, string path) => value ?? throw new InvalidDataException($"{path} 不能为 null。");
}
