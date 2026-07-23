using Tavi.Domain.World;

namespace Tavi.Infrastructure.Persistence;

internal static class WorldSaveMapper
{
    internal static WorldSaveDataV1 FromDomain(WorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new WorldSaveDataV1
        {
            Id = snapshot.Id,
            Anchors = snapshot.Anchors.Values.Select(FromDomain).ToList(),
            Relations = snapshot.Relations.Values.Select(FromDomain).ToList(),
            SubWorlds = snapshot.SubWorlds.Select(FromDomain).ToList()
        };
    }

    internal static WorldSnapshot ToDomain(WorldSaveDataV1 data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Anchors is null || data.Relations is null || data.SubWorlds is null)
            throw new InvalidDataException("世界存档集合不能为 null。");
        var snapshot = new WorldSnapshot { Id = data.Id };
        foreach (AnchorSaveDataV1 anchorData in data.Anchors)
        {
            Anchor anchor = ToDomain(anchorData);
            if (!snapshot.Anchors.TryAdd(anchor.Id, anchor))
                throw new InvalidDataException($"Anchor Id {anchor.Id} 重复。");
        }
        foreach (RelationSaveDataV1 relationData in data.Relations)
        {
            Relation relation = ToDomain(relationData);
            if (!snapshot.Relations.TryAdd(relation.Id, relation))
                throw new InvalidDataException($"主世界 Relation Id {relation.Id} 重复。");
        }
        foreach (SubWorldSaveDataV1 subWorldData in data.SubWorlds)
            snapshot.SubWorlds.Add(ToDomain(subWorldData));
        return snapshot;
    }

    private static AnchorSaveDataV1 FromDomain(Anchor anchor)
    {
        return new AnchorSaveDataV1 { Id = anchor.Id, Name = anchor.Name, Description = anchor.Description, Type = anchor.Type.ToString() };
    }

    private static RelationSaveDataV1 FromDomain(Relation relation)
    {
        return new RelationSaveDataV1 { Id = relation.Id, Name = relation.Name, Description = relation.Description, SourceId = relation.SourceId, TargetId = relation.TargetId };
    }

    private static SubWorldSaveDataV1 FromDomain(SubWorldSnapshot subWorld)
    {
        return new SubWorldSaveDataV1 { Id = subWorld.Id, DomainId = subWorld.DomainId, Relations = subWorld.Relations.Values.Select(FromDomain).ToList() };
    }

    private static Anchor ToDomain(AnchorSaveDataV1 data)
    {
        if (data is null)
            throw new InvalidDataException("Anchor 存档项不能为 null。");
        if (!Enum.TryParse(data.Type, false, out AnchorType type) || !Enum.IsDefined(type))
            throw new InvalidDataException($"AnchorType“{data.Type}”无效。");
        return new Anchor(data.Id, RequireText(data.Name, "Anchor.Name"), RequireValue(data.Description, "Anchor.Description"), type);
    }

    private static Relation ToDomain(RelationSaveDataV1 data)
    {
        if (data is null)
            throw new InvalidDataException("Relation 存档项不能为 null。");
        return new Relation(data.Id, RequireText(data.Name, "Relation.Name"), RequireValue(data.Description, "Relation.Description"), data.SourceId, data.TargetId);
    }

    private static SubWorldSnapshot ToDomain(SubWorldSaveDataV1 data)
    {
        if (data is null || data.Relations is null)
            throw new InvalidDataException("子世界存档项及其 Relations 不能为 null。");
        var snapshot = new SubWorldSnapshot { Id = data.Id, DomainId = data.DomainId };
        foreach (RelationSaveDataV1 relationData in data.Relations)
        {
            Relation relation = ToDomain(relationData);
            if (!snapshot.Relations.TryAdd(relation.Id, relation))
                throw new InvalidDataException($"子世界 Relation Id {relation.Id} 重复。");
        }
        return snapshot;
    }

    private static string RequireText(string? value, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"{path} 不能为空。");
        return value;
    }

    private static string RequireValue(string? value, string path)
    {
        return value ?? throw new InvalidDataException($"{path} 不能为 null。");
    }
}
