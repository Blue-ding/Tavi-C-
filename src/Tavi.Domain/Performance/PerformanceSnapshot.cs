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

/// <summary>记录 Performance 创建时冻结的 Scene 槽位绑定。</summary>
public sealed record PerformanceSourceSceneBinding
{
    public PerformanceSourceSceneBinding(string slotId, IEnumerable<Guid> elementIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(elementIds);
        Guid[] copied = elementIds.ToArray();
        if (copied.Any(value => value == Guid.Empty) || copied.Distinct().Count() != copied.Length)
            throw new ArgumentException("Performance 来源 Scene 槽位不能包含空或重复的 Element 标识。", nameof(elementIds));
        SlotId = slotId;
        ElementIds = Array.AsReadOnly(copied);
    }

    public string SlotId { get; }
    public IReadOnlyList<Guid> ElementIds { get; }
}

/// <summary>记录恢复和结算 Performance 所需的冻结 Scene 身份与绑定。</summary>
public sealed record PerformanceSourceScene
{
    public required Guid Id { get; init; }
    public required string DefinitionId { get; init; }
    public required string ModuleId { get; init; }
    public required string ModuleVersion { get; init; }
    public required Guid BasedOnScenarioStateId { get; init; }
    public required string State { get; init; }
    public IReadOnlyList<PerformanceSourceSceneBinding> Bindings { get; init; } = [];
    public IReadOnlyList<Element> Elements { get; init; } = [];
    public IReadOnlyList<Scope> Scopes { get; init; } = [];
    public IReadOnlyList<Aspect> Aspects { get; init; } = [];
    public IReadOnlyList<Relation> Relations { get; init; } = [];
}

/// <summary>表示 Performance 临时 EARS 图、Beat 与来源标识的独立领域快照。</summary>
public sealed record PerformanceSnapshot
{
    /// <summary>获取或设置 Performance 聚合的稳定身份。</summary>
    public Guid PerformanceId { get; set; } = Guid.NewGuid();

    /// <summary>获取或设置当前乐观并发状态标识；每次实际领域写入后更新。</summary>
    public Guid StateId { get; set; } = Guid.NewGuid();

    public Guid SourceScenarioStateId { get; set; }
    public required PerformanceSourceScene SourceScene { get; set; }
    public PerformanceStatus Status { get; set; } = PerformanceStatus.Active;
    public List<PerformanceModuleReference> Modules { get; set; } = [];
    public HashSet<Guid> ImportedElementIds { get; set; } = [];
    public Dictionary<Guid, Element> Elements { get; set; } = new();
    public Dictionary<Guid, Scope> Scopes { get; set; } = new();
    public Dictionary<Guid, Aspect> Aspects { get; set; } = new();
    public Dictionary<Guid, Relation> Relations { get; set; } = new();
    public Dictionary<Guid, Beat> Beats { get; set; } = new();
}
