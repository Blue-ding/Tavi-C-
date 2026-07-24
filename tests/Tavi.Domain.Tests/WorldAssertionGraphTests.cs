using Tavi.Domain.World;
using Xunit;
using RuntimeWorld = Tavi.Domain.World.World;

namespace Tavi.Domain.Tests;

/// <summary>验证 Element、Aspect、Relation 与 Scope 断言图的核心不变量和级联语义。</summary>
public sealed class WorldAssertionGraphTests
{
    /// <summary>验证同一结构命题可以作为不同 Scope 中强度不同的独立 Relation 存在。</summary>
    [Fact]
    public void RelationsInDifferentScopesAreIndependentAssertions()
    {
        var (snapshot, aliceId, bobId, firstScopeId, secondScopeId) = CreateScopedWorld();
        Guid firstRelationId = Guid.NewGuid();
        Guid secondRelationId = Guid.NewGuid();
        snapshot.Relations.Add(firstRelationId, new Relation(firstRelationId, "trusts", "", 0.8, new RelationType("social:trust"), aliceId, bobId, firstScopeId));
        snapshot.Relations.Add(secondRelationId, new Relation(secondRelationId, "trusts", "", 0.2, new RelationType("social:trust"), aliceId, bobId, secondScopeId));

        RuntimeWorld world = RuntimeWorld.Create(snapshot);

        Assert.Equal(0.8, world.GetRelation(firstRelationId).Quantity);
        Assert.Equal(0.2, world.GetRelation(secondRelationId).Quantity);
        Assert.Single(world.GetRelationsInScope(firstScopeId));
        Assert.Single(world.GetRelationsInScope(secondScopeId));
    }

    /// <summary>验证删除 Scope 会级联删除其中断言，并且反向操作可以精确恢复完整快照。</summary>
    [Fact]
    public void RemovingScopeCascadesAssertionsAndInverseRestoresSnapshot()
    {
        var (snapshot, aliceId, bobId, firstScopeId, _) = CreateScopedWorld();
        Guid aspectId = Guid.NewGuid();
        Guid relationId = Guid.NewGuid();
        snapshot.Aspects.Add(aspectId, new Aspect(aspectId, "afraid", "", 0.7, new AspectType("emotion:fear"), aliceId, firstScopeId));
        snapshot.Relations.Add(relationId, new Relation(relationId, "trusts", "", 0.8, new RelationType("social:trust"), aliceId, bobId, firstScopeId));
        RuntimeWorld world = RuntimeWorld.Create(snapshot);
        WorldSnapshot before = world.CreateSnapshot();

        WorldApplyResult removal = world.Apply(WorldOperations.Single(new RemoveScopeOperation(firstScopeId)));

        Assert.Empty(world.GetAspects());
        Assert.Empty(world.GetRelations());
        Assert.Throws<WorldException>(() => world.GetScope(firstScopeId));
        world.Apply(removal.ChangeSet!.Inverse);
        AssertSnapshotsEqual(before, world.CreateSnapshot());
    }

    /// <summary>验证删除 Element 会移除其持有 Scope、其中断言及其他 Scope 中直接引用该 Element 的断言。</summary>
    [Fact]
    public void RemovingElementDeletesCompleteDependencyClosure()
    {
        var (snapshot, aliceId, bobId, aliceScopeId, bobScopeId) = CreateScopedWorld();
        Guid ownedAspectId = Guid.NewGuid();
        Guid externalAspectId = Guid.NewGuid();
        Guid ownedRelationId = Guid.NewGuid();
        Guid externalRelationId = Guid.NewGuid();
        snapshot.Aspects.Add(ownedAspectId, new Aspect(ownedAspectId, "calm", "", 0.4, AspectType.None, bobId, aliceScopeId));
        snapshot.Aspects.Add(externalAspectId, new Aspect(externalAspectId, "afraid", "", 0.9, AspectType.None, aliceId, bobScopeId));
        snapshot.Relations.Add(ownedRelationId, new Relation(ownedRelationId, "knows", "", 1, RelationType.None, bobId, bobId, aliceScopeId));
        snapshot.Relations.Add(externalRelationId, new Relation(externalRelationId, "trusts", "", 0.3, RelationType.None, bobId, aliceId, bobScopeId));
        RuntimeWorld world = RuntimeWorld.Create(snapshot);
        WorldSnapshot before = world.CreateSnapshot();

        WorldApplyResult removal = world.Apply(WorldOperations.Single(new RemoveElementOperation(aliceId)));

        Assert.Single(world.GetElements());
        Assert.Single(world.GetScopes());
        Assert.Empty(world.GetAspects());
        Assert.Empty(world.GetRelations());
        world.Apply(removal.ChangeSet!.Inverse);
        AssertSnapshotsEqual(before, world.CreateSnapshot());
    }

    /// <summary>验证悬空引用、未初始化类型和非有限 Quantity 会在快照初始化时集中报告。</summary>
    [Fact]
    public void InvalidSnapshotReportsAllStructuralFailures()
    {
        Guid elementId = Guid.NewGuid();
        Guid scopeId = Guid.NewGuid();
        Guid aspectId = Guid.NewGuid();
        var snapshot = new WorldSnapshot();
        snapshot.Elements.Add(elementId, new Element(elementId, "Element", "", ElementType.None));
        snapshot.Scopes.Add(scopeId, new Scope(scopeId, "Scope", "", double.NaN, default, Guid.NewGuid()));
        snapshot.Aspects.Add(aspectId, new Aspect(aspectId, "Aspect", "", double.PositiveInfinity, AspectType.None, Guid.NewGuid(), scopeId));

        WorldException exception = Assert.Throws<WorldException>(() => RuntimeWorld.Create(snapshot));

        Assert.Equal(WorldErrorCodes.InvalidWorldSnapshot, exception.ErrorCode);
        Assert.True(exception.ValidationErrors.Count >= 4);
        Assert.Contains(exception.ValidationErrors, error => error.Contains("ScopeType", StringComparison.Ordinal));
        Assert.Contains(exception.ValidationErrors, error => error.Contains("有限 double", StringComparison.Ordinal));
        Assert.Contains(exception.ValidationErrors, error => error.Contains("不指向任何 Element", StringComparison.Ordinal));
    }

    /// <summary>验证快照使用按实体类别独立的 ID 空间。</summary>
    [Fact]
    public void DifferentEntityKindsMayShareGuid()
    {
        Guid sharedId = Guid.NewGuid();
        var snapshot = new WorldSnapshot();
        snapshot.Elements.Add(sharedId, new Element(sharedId, "Owner", "", ElementType.None));
        snapshot.Scopes.Add(sharedId, new Scope(sharedId, "Scope", "", 1, ScopeType.None, sharedId));
        snapshot.Aspects.Add(sharedId, new Aspect(sharedId, "Aspect", "", 1, AspectType.None, sharedId, sharedId));
        snapshot.Relations.Add(sharedId, new Relation(sharedId, "Relation", "", 1, RelationType.None, sharedId, sharedId, sharedId));

        RuntimeWorld world = RuntimeWorld.Create(snapshot);

        Assert.Equal(sharedId, world.GetElement(sharedId).Id);
        Assert.Equal(sharedId, world.GetScope(sharedId).Id);
        Assert.Equal(sharedId, world.GetAspect(sharedId).Id);
        Assert.Equal(sharedId, world.GetRelation(sharedId).Id);
    }

    private static (WorldSnapshot Snapshot, Guid AliceId, Guid BobId, Guid AliceScopeId, Guid BobScopeId) CreateScopedWorld()
    {
        Guid aliceId = Guid.NewGuid();
        Guid bobId = Guid.NewGuid();
        Guid aliceScopeId = Guid.NewGuid();
        Guid bobScopeId = Guid.NewGuid();
        var snapshot = new WorldSnapshot();
        snapshot.Elements.Add(aliceId, new Element(aliceId, "Alice", "", ElementType.None));
        snapshot.Elements.Add(bobId, new Element(bobId, "Bob", "", ElementType.None));
        snapshot.Scopes.Add(aliceScopeId, new Scope(aliceScopeId, "Alice belief", "", 1, new ScopeType("epistemic:belief"), aliceId));
        snapshot.Scopes.Add(bobScopeId, new Scope(bobScopeId, "Bob belief", "", 1, new ScopeType("epistemic:belief"), bobId));
        return (snapshot, aliceId, bobId, aliceScopeId, bobScopeId);
    }

    private static void AssertSnapshotsEqual(WorldSnapshot expected, WorldSnapshot actual)
    {
        Assert.Equal(expected.Elements.OrderBy(pair => pair.Key), actual.Elements.OrderBy(pair => pair.Key));
        Assert.Equal(expected.Aspects.OrderBy(pair => pair.Key), actual.Aspects.OrderBy(pair => pair.Key));
        Assert.Equal(expected.Relations.OrderBy(pair => pair.Key), actual.Relations.OrderBy(pair => pair.Key));
        Assert.Equal(expected.Scopes.OrderBy(pair => pair.Key), actual.Scopes.OrderBy(pair => pair.Key));
    }
}
