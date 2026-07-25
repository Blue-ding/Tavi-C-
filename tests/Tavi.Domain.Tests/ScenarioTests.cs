using Tavi.Domain.Scenario;
using Xunit;
using RuntimeScenario = Tavi.Domain.Scenario.Scenario;

namespace Tavi.Domain.Tests;

/// <summary>验证 Scenario 的事务隔离、Scene 生命周期和 Element 独占归属。</summary>
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
        var changes = new ScenarioChangeSet([new AddElementOperation(Guid.NewGuid(), "Alice", "", new ElementType("test:character")), new AddScopeOperation(Guid.NewGuid(), "Identity", "", 1, new ScopeType("test:identity"), Guid.NewGuid())]);
        Assert.Throws<ScenarioException>(() => scenario.Apply(changes));
        Assert.Equal(stateId, scenario.StateId);
        Assert.Empty(scenario.GetElements());
    }

    /// <summary>验证不完整绑定会保存在快照中且同一 Element 不能进入另一个活动 Scene。</summary>
    [Fact]
    public void BindingIsPersistentAndExclusive()
    {
        RuntimeScenario scenario = RuntimeScenario.Create(Empty());
        Guid elementId = Guid.NewGuid();
        Guid firstSceneId = Guid.NewGuid();
        Guid secondSceneId = Guid.NewGuid();
        scenario.Apply(new ScenarioChangeSet([new AddElementOperation(elementId, "Alice", "", new ElementType("test:character")), AddScene(firstSceneId, scenario.StateId), AddScene(secondSceneId, scenario.StateId), new SetSceneSlotBindingOperation(firstSceneId, new SceneSlotBinding("actor", [elementId]))]));
        Assert.Equal(elementId, scenario.CreateSnapshot().Scenes[firstSceneId].FindBinding("actor")!.ElementIds.Single());
        Assert.Throws<ScenarioException>(() => scenario.Apply(new ScenarioChangeSet([new SetSceneSlotBindingOperation(secondSceneId, new SceneSlotBinding("actor", [elementId]))])));
    }

    /// <summary>验证 Processing Scene 的绑定、内部 Element 和 Scene 本身均被冻结。</summary>
    [Fact]
    public void ProcessingSceneCannotReleaseElementOrBeDeleted()
    {
        RuntimeScenario scenario = RuntimeScenario.Create(Empty());
        Guid elementId = Guid.NewGuid();
        Guid sceneId = Guid.NewGuid();
        scenario.Apply(new ScenarioChangeSet([new AddElementOperation(elementId, "Alice", "", new ElementType("test:character")), AddScene(sceneId, scenario.StateId), new SetSceneSlotBindingOperation(sceneId, new SceneSlotBinding("actor", [elementId])), new UpdateSceneStateOperation(sceneId, SceneState.Processing)]));
        Assert.Throws<ScenarioException>(() => scenario.Apply(new ScenarioChangeSet([new ClearSceneSlotBindingOperation(sceneId, "actor")])));
        Assert.Throws<ScenarioException>(() => scenario.Apply(new ScenarioChangeSet([new UpdateElementOperation(elementId, "Changed", "", new ElementType("test:character"))])));
        Assert.Throws<ScenarioException>(() => scenario.Apply(new ScenarioChangeSet([new RemoveElementOperation(elementId)])));
        Assert.Throws<ScenarioException>(() => scenario.Apply(new ScenarioChangeSet([new RemoveSceneOperation(sceneId)])));
    }

    /// <summary>验证结算释放活动归属、允许删除内部 Element，并保留 Settled Scene 的历史绑定。</summary>
    [Fact]
    public void SettlementReleasesOwnershipAndPreservesHistory()
    {
        RuntimeScenario scenario = RuntimeScenario.Create(Empty());
        Guid elementId = Guid.NewGuid();
        Guid settledSceneId = Guid.NewGuid();
        Guid nextSceneId = Guid.NewGuid();
        scenario.Apply(new ScenarioChangeSet([new AddElementOperation(elementId, "Alice", "", new ElementType("test:character")), AddScene(settledSceneId, scenario.StateId), new SetSceneSlotBindingOperation(settledSceneId, new SceneSlotBinding("actor", [elementId])), new UpdateSceneStateOperation(settledSceneId, SceneState.Processing)]));
        scenario.Apply(new ScenarioChangeSet([new UpdateSceneStateOperation(settledSceneId, SceneState.Settled), new RemoveElementOperation(elementId)]));
        Assert.Equal(elementId, scenario.GetScene(settledSceneId).FindBinding("actor")!.ElementIds.Single());
        Guid replacementId = Guid.NewGuid();
        scenario.Apply(new ScenarioChangeSet([new AddElementOperation(replacementId, "Bob", "", new ElementType("test:character")), AddScene(nextSceneId, scenario.StateId), new SetSceneSlotBindingOperation(nextSceneId, new SceneSlotBinding("actor", [replacementId]))]));
        Assert.Equal(replacementId, scenario.GetScene(nextSceneId).FindBinding("actor")!.ElementIds.Single());
    }

    /// <summary>验证批量清理只删除已结算 Scene。</summary>
    [Fact]
    public void ClearSettledScenesLeavesBindingScenes()
    {
        RuntimeScenario scenario = RuntimeScenario.Create(Empty());
        Guid settledId = Guid.NewGuid();
        Guid bindingId = Guid.NewGuid();
        scenario.Apply(new ScenarioChangeSet([AddScene(settledId, scenario.StateId), AddScene(bindingId, scenario.StateId), new UpdateSceneStateOperation(settledId, SceneState.Processing), new UpdateSceneStateOperation(settledId, SceneState.Settled), new ClearSettledScenesOperation()]));
        Assert.Equal(bindingId, Assert.Single(scenario.GetScenes()).Id);
    }

    private static AddSceneOperation AddScene(Guid id, Guid stateId) => new(id, new SceneDefinitionType("test:act"), "test", "1.0.0", stateId, "Act", "", SceneSettlementOptions.Rules, [new SceneSlotSpecification("actor", "Actor", "", 0, 1, ["test:character"])]);
    private static ScenarioSnapshot Empty() => new() { SourceWorldStateId = Guid.NewGuid() };
}
