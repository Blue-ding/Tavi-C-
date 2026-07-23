namespace Tavi.Infrastructure.Persistence;

internal sealed class SaveDocumentV1
{
    internal const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public DateTimeOffset SavedAtUtc { get; set; }
    public WorldSaveDataV1 World { get; set; } = new();
}

internal sealed class WorldSaveDataV1
{
    public Guid Id { get; set; }
    public List<AnchorSaveDataV1> Anchors { get; set; } = new();
    public List<RelationSaveDataV1> Relations { get; set; } = new();
    public List<SubWorldSaveDataV1> SubWorlds { get; set; } = new();
}

internal sealed class AnchorSaveDataV1
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

internal sealed class RelationSaveDataV1
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public Guid TargetId { get; set; }
}

internal sealed class SubWorldSaveDataV1
{
    public Guid Id { get; set; }
    public Guid DomainId { get; set; }
    public List<RelationSaveDataV1> Relations { get; set; } = new();
}
