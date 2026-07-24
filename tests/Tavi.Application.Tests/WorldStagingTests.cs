using Tavi.Application.World;
using Tavi.Domain.World;
using Xunit;

namespace Tavi.Application.Tests;

/// <summary>验证新断言图模型的暂存冲突、依赖、投影和原子提交行为。</summary>
public sealed class WorldStagingTests
{
    /// <summary>验证修改同一 Element 的不同暂存项在删除其中一项前都不会参与投影。</summary>
    [Fact]
    public async Task ConflictingElementOperationsAreExcludedUntilOneIsDeleted()
    {
        await using WorldSession session = await CreateSessionAsync();
        Guid elementId = Guid.NewGuid();
        Guid addId = session.Stage(new AddElementOperation(elementId, "Alice", "", ElementType.None));
        Guid deleteId = session.Stage(new RemoveElementOperation(elementId));

        WorldStagingSnapshot conflict = session.CreateStagingSnapshot();

        Assert.All(conflict.Changes, change => Assert.Equal(WorldStagedChangeStatus.Conflict, change.Status));
        Assert.DoesNotContain(elementId, conflict.ProjectedWorld.Elements.Keys);
        Assert.True(session.DeleteStaged(deleteId));
        WorldStagingSnapshot resolved = session.CreateStagingSnapshot();
        Assert.Equal(WorldStagedChangeStatus.Valid, Assert.Single(resolved.Changes).Status);
        Assert.Contains(elementId, resolved.ProjectedWorld.Elements.Keys);
        Assert.Equal(addId, resolved.Changes[0].Id);
    }

    /// <summary>验证 Relation 引用的临时 Scope 发生冲突时，该 Relation 会变为 Invalid。</summary>
    [Fact]
    public async Task RelationBecomesInvalidWhenItsStagedScopeIsConflicted()
    {
        await using WorldSession session = await CreateSessionAsync();
        Guid ownerId = Guid.NewGuid();
        Guid targetId = Guid.NewGuid();
        Guid scopeId = Guid.NewGuid();
        session.Stage(new AddElementOperation(ownerId, "Alice", "", ElementType.None));
        session.Stage(new AddElementOperation(targetId, "Key", "", ElementType.None));
        session.Stage(new AddScopeOperation(scopeId, "Alice belief", "", 1, ScopeType.None, ownerId));
        session.Stage(new RemoveScopeOperation(scopeId));
        Guid relationChangeId = session.Stage(new AddRelationOperation(Guid.NewGuid(), "holds", "", 1, RelationType.None, ownerId, targetId, scopeId));

        WorldStagedChange relation = session.CreateStagingSnapshot().Changes.Single(change => change.Id == relationChangeId);

        Assert.Equal(WorldStagedChangeStatus.Invalid, relation.Status);
        Assert.NotNull(relation.Issue);
    }

    /// <summary>验证只选择 Relation 而不选择其临时 Element 和 Scope 依赖时提交会被拒绝。</summary>
    [Fact]
    public async Task PartialCommitRejectsRelationWithoutSelectedDependencies()
    {
        await using WorldSession session = await CreateSessionAsync();
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        Guid scopeId = Guid.NewGuid();
        session.Stage(new AddElementOperation(firstId, "Alice", "", ElementType.None));
        session.Stage(new AddElementOperation(secondId, "Key", "", ElementType.None));
        session.Stage(new AddScopeOperation(scopeId, "Default", "", 1, ScopeType.None, firstId));
        Guid relationChangeId = session.Stage(new AddRelationOperation(Guid.NewGuid(), "seeks", "", 0.5, RelationType.None, firstId, secondId, scopeId));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => session.CommitStaged([relationChangeId], session.StateId));

        Assert.Contains("依赖", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>验证同一原子暂存项可以组合更新 Element 的多个非结构字段。</summary>
    [Fact]
    public async Task CompoundUpdateIsOneValidStagedItem()
    {
        Guid elementId = Guid.NewGuid();
        var initial = new WorldSnapshot();
        initial.Elements.Add(elementId, new Element(elementId, "Alice", "", ElementType.None));
        await using WorldSession session = await CreateSessionAsync(initial);
        Guid changeId = session.Stage(new WorldChangeSet([new UpdateElementNameOperation(elementId, "Alicia"), new UpdateElementDescriptionOperation(elementId, "旅行者")]));

        WorldStagingSnapshot snapshot = session.CreateStagingSnapshot();

        Assert.Equal(changeId, Assert.Single(snapshot.Changes).Id);
        Assert.Equal(WorldStagedChangeStatus.Valid, snapshot.Changes[0].Status);
        Assert.Equal("Alicia", snapshot.ProjectedWorld.Elements[elementId].Name);
        Assert.Equal("旅行者", snapshot.ProjectedWorld.Elements[elementId].Description);
    }

    /// <summary>验证选中完整依赖链后可以原子提交并消费对应暂存项。</summary>
    [Fact]
    public async Task CompleteAssertionDependencyChainCommitsAtomically()
    {
        await using WorldSession session = await CreateSessionAsync();
        Guid ownerId = Guid.NewGuid();
        Guid scopeId = Guid.NewGuid();
        Guid elementChangeId = session.Stage(new AddElementOperation(ownerId, "Alice", "", ElementType.None));
        Guid scopeChangeId = session.Stage(new AddScopeOperation(scopeId, "Alice belief", "", 1, ScopeType.None, ownerId));
        Guid aspectChangeId = session.Stage(new AddAspectOperation(Guid.NewGuid(), "afraid", "", 0.8, AspectType.None, ownerId, scopeId));

        WorldStagingCommitResult result = session.CommitStaged([elementChangeId, scopeChangeId, aspectChangeId], session.StateId);

        Assert.True(result.Commit.Changed);
        Assert.Equal(3, result.ConsumedChangeIds.Count);
        Assert.Empty(session.CreateStagingSnapshot().Changes);
        Assert.Single(session.Queries.GetAspectsInScope(scopeId));
    }

    private static async Task<WorldSession> CreateSessionAsync(WorldSnapshot? snapshot = null)
    {
        var session = new WorldSession(new MemoryWorldStore(snapshot ?? new WorldSnapshot()));
        await session.InitializeAsync();
        return session;
    }

    private sealed class MemoryWorldStore(WorldSnapshot snapshot) : IWorldStore
    {
        Task<WorldSnapshot?> IWorldStore.LoadAsync(string slot, CancellationToken cancellationToken) => Task.FromResult<WorldSnapshot?>(snapshot);
        Task IWorldStore.SaveAsync(string slot, WorldSnapshot value, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
