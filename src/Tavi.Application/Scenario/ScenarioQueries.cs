using Tavi.Domain.Scenario;
using RuntimeScenario = Tavi.Domain.Scenario.Scenario;

namespace Tavi.Application.Scenario;

/// <summary>提供始终通过 ScenarioSession 同步边界读取最新 Scenario 的查询。</summary>
public sealed class ScenarioQueries
{
    private readonly ScenarioSession _session;

    internal ScenarioQueries(ScenarioSession session) => _session = session;

    /// <summary>创建当前 Scenario 的独立完整快照。</summary>
    public ScenarioSnapshot CreateSnapshot() => _session.ExecuteQuery(scenario => scenario.CreateSnapshot());

    /// <summary>根据标识获取独立 Element 副本。</summary>
    public Element GetElement(Guid id) => _session.ExecuteQuery(scenario => scenario.GetElement(id));

    /// <summary>获取全部独立 Element 副本。</summary>
    public IReadOnlyList<Element> GetElements() => _session.ExecuteQuery(scenario => scenario.GetElements().OrderBy(value => value.Name, StringComparer.Ordinal).ThenBy(value => value.Id).ToArray());

    /// <summary>根据标识获取独立 Aspect 副本。</summary>
    public Aspect GetAspect(Guid id) => _session.ExecuteQuery(scenario => scenario.GetAspect(id));

    /// <summary>获取全部独立 Aspect 副本。</summary>
    public IReadOnlyList<Aspect> GetAspects() => _session.ExecuteQuery(scenario => scenario.GetAspects().OrderBy(value => value.Name, StringComparer.Ordinal).ThenBy(value => value.Id).ToArray());

    /// <summary>根据标识获取独立 Relation 副本。</summary>
    public Relation GetRelation(Guid id) => _session.ExecuteQuery(scenario => scenario.GetRelation(id));

    /// <summary>获取全部独立 Relation 副本。</summary>
    public IReadOnlyList<Relation> GetRelations() => _session.ExecuteQuery(scenario => scenario.GetRelations().OrderBy(value => value.Name, StringComparer.Ordinal).ThenBy(value => value.Id).ToArray());

    /// <summary>根据标识获取独立 Scope 副本。</summary>
    public Scope GetScope(Guid id) => _session.ExecuteQuery(scenario => scenario.GetScope(id));

    /// <summary>获取全部独立 Scope 副本。</summary>
    public IReadOnlyList<Scope> GetScopes() => _session.ExecuteQuery(scenario => scenario.GetScopes().OrderBy(value => value.Name, StringComparer.Ordinal).ThenBy(value => value.Id).ToArray());

    /// <summary>根据标识获取独立 Scene 副本。</summary>
    public Scene GetScene(Guid id) => _session.ExecuteQuery(scenario => scenario.GetScene(id));

    /// <summary>获取全部独立 Scene 副本。</summary>
    public IReadOnlyList<Scene> GetScenes() => _session.ExecuteQuery(scenario => scenario.GetScenes().OrderBy(value => value.Name, StringComparer.Ordinal).ThenBy(value => value.Id).ToArray());
}
