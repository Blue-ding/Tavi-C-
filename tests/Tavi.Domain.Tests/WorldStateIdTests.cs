using Tavi.Domain.World;
using Xunit;
using RuntimeWorld = Tavi.Domain.World.World;

namespace Tavi.Domain.Tests;

/// <summary>验证 World 状态标识只随实际领域写入变化。</summary>
public sealed class WorldStateIdTests
{
    /// <summary>验证实际写入生成新状态标识，而空操作和无变化更新保留原标识。</summary>
    [Fact]
    public void StateIdChangesOnlyWhenWorldActuallyChanges()
    {
        Guid elementId = Guid.NewGuid();
        WorldSnapshot initial = new();
        RuntimeWorld world = RuntimeWorld.Create(initial);

        world.Apply(new WorldChangeSet([]));
        Assert.Equal(initial.Id, world.StateId);
        world.Apply(WorldOperations.Single(new AddElementOperation(elementId, "Alice", "", ElementType.None)));
        Guid changedStateId = world.StateId;
        Assert.NotEqual(initial.Id, changedStateId);
        world.Apply(WorldOperations.Single(new UpdateElementNameOperation(elementId, "Alice")));
        Assert.Equal(changedStateId, world.StateId);
        Assert.Equal(changedStateId, world.CreateSnapshot().Id);
    }
}
