using System.Collections.ObjectModel;

namespace Tavi.Domain.Scenario;

/// <summary>记录 Scenario 存档解释所需的 Module 及版本。</summary>
public sealed record ScenarioModuleReference
{
    /// <summary>使用稳定 Module 标识和兼容版本创建引用。</summary>
    public ScenarioModuleReference(string id, string version, IReadOnlyDictionary<string, string>? parameters = null)
    {
        Id = id;
        Version = version;
        Parameters = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(parameters ?? new Dictionary<string, string>(), StringComparer.Ordinal));
    }

    /// <summary>获取 Module 标识。</summary>
    public string Id { get; }

    /// <summary>获取 Module 兼容版本。</summary>
    public string Version { get; }

    /// <summary>获取创建 ScenarioSession 时冻结的 Module 行为参数。</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; }
}

/// <summary>表示当前演绎世界状态的独立领域快照。</summary>
public sealed record ScenarioSnapshot
{
    /// <summary>获取或设置不透明的 Scenario 状态标识。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>获取或设置创建该 Scenario 时所依据的 World StateId。</summary>
    public Guid SourceWorldStateId { get; set; }

    /// <summary>获取或设置解释该 Scenario 所需的 Module。</summary>
    public List<ScenarioModuleReference> Modules { get; set; } = [];

    /// <summary>获取或设置全部 Element。</summary>
    public Dictionary<Guid, Element> Elements { get; set; } = new();

    /// <summary>获取或设置全部 Aspect。</summary>
    public Dictionary<Guid, Aspect> Aspects { get; set; } = new();

    /// <summary>获取或设置全部 Relation。</summary>
    public Dictionary<Guid, Relation> Relations { get; set; } = new();

    /// <summary>获取或设置全部 Scope。</summary>
    public Dictionary<Guid, Scope> Scopes { get; set; } = new();

    /// <summary>获取或设置全部活跃或待清理 Scene。</summary>
    public Dictionary<Guid, Scene> Scenes { get; set; } = new();
}
