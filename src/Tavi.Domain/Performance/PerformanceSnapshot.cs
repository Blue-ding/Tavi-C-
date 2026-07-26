using System.Collections.ObjectModel;

namespace Tavi.Domain.Performance;

/// <summary>记录解释 Performance 所需的冻结 Module 身份与参数。</summary>
public sealed record PerformanceModuleReference
{
    public PerformanceModuleReference(string id, string version, IReadOnlyDictionary<string, string>? parameters = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        Id = id;
        Version = version;
        Parameters = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(parameters ?? new Dictionary<string, string>(), StringComparer.Ordinal));
    }

    public string Id { get; }
    public string Version { get; }
    public IReadOnlyDictionary<string, string> Parameters { get; }
}

/// <summary>表示 Performance 临时 EARS 图、Beat 与来源标识的独立领域快照。</summary>
public sealed record PerformanceSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceScenarioStateId { get; set; }
    public Guid SourceSceneId { get; set; }
    public PerformanceStatus Status { get; set; } = PerformanceStatus.Active;
    public List<PerformanceModuleReference> Modules { get; set; } = [];
    public HashSet<Guid> ImportedElementIds { get; set; } = [];
    public Dictionary<Guid, Element> Elements { get; set; } = new();
    public Dictionary<Guid, Scope> Scopes { get; set; } = new();
    public Dictionary<Guid, Aspect> Aspects { get; set; } = new();
    public Dictionary<Guid, Relation> Relations { get; set; } = new();
    public Dictionary<Guid, Beat> Beats { get; set; } = new();
}
