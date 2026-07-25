using Tavi.Domain.Scenario;
using Xunit;
using RuntimeScenario = Tavi.Domain.Scenario.Scenario;

namespace Tavi.Domain.Tests;

/// <summary>验证 Scenario 的深复制、事务恢复、级联解绑和 Scene 状态隔离。</summary>
public sealed class ScenarioTests
{
    /// <summary>验证 Scenario 创建和查询均不会泄露可修改的快照引用。</summary>
    [Fact]
    public void SnapshotAndQueriesAreDefensiveCopies()
    {
        Guid elementId = Guid.NewGuid();
        var snapshot = Empty();
        snapshot.Elements.Add(elementId, new Element(elementId, "Alice", "", new ElementType("test:character")));
        RuntimeScenario scenario = RuntimeScenario.Create(snapshot);

        snapshot.Elements.Clear();
        ScenarioSnapshot copied = scenario.CreateSnapshot();
        copied.Elements.Clear();

        Assert.Equal("Alice", scenario.GetElement(elementId).Name);
        Assert.Single(scenario.GetElements());
    }

    /// <summary>验证原子操作组中任一步失败时完整恢复数据和 StateId。</summary>
    [Fact]
    public void FailedChangeSetRestoresCompleteState()
    {
        RuntimeScenario scenario = RuntimeScenario.Create(Empty());
        Guid stateId = scenario.StateId;
        Guid elementId = Guid.NewGuid();
        var changes = new ScenarioChangeSet([
            new AddElementOperation(elementId, "Alice", "", new ElementType("test:character")),
            new AddScopeOperation(Guid.NewGuid(), "Identity", "", 1, new ScopeType("test:identity"), Guid.NewGuid())
        ]);

        Assert.Throws<ScenarioException>(() => scenario.Apply(changes));

        Assert.Equal(stateId, scenario.StateId);
        Assert.Empty(scenario.GetElements());
    }

    /// <summary>验证删除 Element 会级联删除 EARS 依赖并从 Scene 槽位解绑。</summary>
    [Fact]
    public void RemovingElementAlsoRemovesBindingsAndStructuralDependencies()
    {
        Guid elementId = Guid.NewGuid();
        Guid scopeId = Guid.NewGuid();
        Guid sceneId = Guid.NewGuid();
        RuntimeScenario scenario = RuntimeScenario.Create(Empty());
        scenario.Apply(new ScenarioChangeSet([
            new AddElementOperation(elementId, "Alice", "", new ElementType("test:character")),
            new AddScopeOperation(scopeId, "Identity", "", 1, new ScopeType("test:identity"), elementId),
            new AddAspectOperation(Guid.NewGuid(), "Gender", "", 1, new AspectType("test:gender"), elementId, scopeId),
            new AddSceneOperation(sceneId, new SceneDefinitionType("test:act"), "test", "1.0.0", scenario.StateId, "Act", "", SceneSettlementOptions.Rules),
            new SetSceneSlotBindingOperation(sceneId, new SceneSlotBinding("actor", [elementId]))
        ]));

        scenario.Apply(new ScenarioChangeSet([new RemoveElementOperation(elementId)]));

        Assert.Empty(scenario.GetElements());
        Assert.Empty(scenario.GetScopes());
        Assert.Empty(scenario.GetAspects());
        Assert.Empty(scenario.GetScene(sceneId).FindBinding("actor")!.ElementIds);
    }

    private static ScenarioSnapshot Empty() => new() { SourceWorldStateId = Guid.NewGuid() };
}
