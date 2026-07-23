using Tavi.Domain.World;
using Xunit;
using RuntimeWorld = Tavi.Domain.World.World;

namespace Tavi.Domain.Tests;

public sealed class WorldTests
{
    [Fact]
    public void AddAndQueryGraphStructure()
    {
        RuntimeWorld world = RuntimeWorld.Create(new WorldSnapshot());

        Guid characterId = world.AddAnchor(
            "Alice",
            "Character",
            AnchorType.Character
        );
        Guid itemId = world.AddAnchor("Sword", "Item", AnchorType.Item);
        Guid outgoingId = world.AddRelation(
            "owns",
            "Alice owns the sword.",
            characterId,
            itemId
        );
        Guid incomingId = world.AddRelation(
            "protects",
            "The sword protects Alice.",
            itemId,
            characterId,
            characterId
        );

        Assert.Equal(characterId, world.GetAnchor(characterId).Id);
        Assert.Equal(outgoingId, world.GetRelation(outgoingId).Id);
        Assert.Single(world.GetOutgoingRelations(characterId));
        Assert.Single(world.GetIncomingRelations(characterId));
        Assert.Equal(2, world.GetRelations(characterId).Count);
        Assert.Single(world.GetWorldRelations());
        Assert.Single(world.GetSubWorldRelations(characterId));
        Assert.Single(world.GetSubWorlds());
        Assert.Equal(characterId, world.GetSubWorlds().Single().DomainId);
        Assert.Equal(2, world.GetAnchors().Count);
        Assert.Single(world.GetCharacters());
        Assert.Equal(characterId, world.GetCharacters().Single().Id);
        Assert.Equal(incomingId, world.GetSubWorldRelations(characterId).Single().Id);
    }

    [Fact]
    public void RemoveRelationWorksForSubWorld()
    {
        RuntimeWorld graph = RuntimeWorld.Create(new WorldSnapshot());
        Guid characterId = graph.AddAnchor(
            "Alice",
            "Character",
            AnchorType.Character
        );
        Guid itemId = graph.AddAnchor("Sword", "Item", AnchorType.Item);
        Guid relationId = graph.AddRelation(
            "owns",
            "Description",
            characterId,
            itemId,
            characterId
        );

        graph.RemoveRelation(relationId);

        Assert.Empty(graph.GetSubWorldRelations(characterId));
        WorldException exception = Assert.Throws<WorldException>(
            () => graph.GetRelation(relationId)
        );
        Assert.Equal(WorldErrorCode.NotFound, exception.ErrorCode);
        Assert.Equal(relationId, exception.EntityId);
    }

    [Fact]
    public void RemovingCharacterCascadesRelationsAndSubWorld()
    {
        RuntimeWorld graph = RuntimeWorld.Create(new WorldSnapshot());
        Guid characterId = graph.AddAnchor(
            "Alice",
            "Character",
            AnchorType.Character
        );
        Guid itemId = graph.AddAnchor("Sword", "Item", AnchorType.Item);
        Guid relationId = graph.AddRelation(
            "owns",
            "Description",
            characterId,
            itemId,
            characterId
        );

        graph.RemoveAnchor(characterId);

        Assert.Throws<WorldException>(() => graph.GetAnchor(characterId));
        Assert.Throws<WorldException>(() => graph.GetRelation(relationId));
        Assert.Throws<WorldException>(
            () => graph.GetSubWorld(characterId)
        );
    }

    [Fact]
    public void RelationOnlyAllowsDescriptionUpdate()
    {
        RuntimeWorld graph = RuntimeWorld.Create(new WorldSnapshot());
        Guid sourceId = graph.AddAnchor("Source", "", AnchorType.Item);
        Guid targetId = graph.AddAnchor("Target", "", AnchorType.Item);
        Guid relationId = graph.AddRelation(
            "relation",
            "Before",
            sourceId,
            targetId
        );

        graph.UpdateRelationDescription(relationId, "After");

        Relation relation = graph.GetRelation(relationId);
        Assert.Equal("After", relation.Description);
        Assert.Equal("relation", relation.Name);
        Assert.Equal(sourceId, relation.SourceId);
        Assert.Equal(targetId, relation.TargetId);
    }

    [Fact]
    public void CharacterNameInvariantAppliesToUpdates()
    {
        RuntimeWorld graph = RuntimeWorld.Create(new WorldSnapshot());
        Guid firstId = graph.AddAnchor(
            "Alice",
            "",
            AnchorType.Character
        );
        Guid secondId = graph.AddAnchor(
            "Bob",
            "",
            AnchorType.Character
        );

        WorldException exception = Assert.Throws<WorldException>(
            () => graph.UpdateAnchorName(secondId, "Alice")
        );

        Assert.Equal(WorldErrorCode.Duplicate, exception.ErrorCode);
        Assert.Equal("Alice", graph.GetAnchor(firstId).Name);
        Assert.Equal("Bob", graph.GetAnchor(secondId).Name);
    }

    [Fact]
    public void CharacterMustRemoveSubWorldBeforeChangingType()
    {
        RuntimeWorld graph = RuntimeWorld.Create(new WorldSnapshot());
        Guid characterId = graph.AddAnchor(
            "Alice",
            "",
            AnchorType.Character
        );
        graph.CreateSubWorld(characterId);

        WorldException exception = Assert.Throws<WorldException>(
            () => graph.UpdateAnchorType(characterId, AnchorType.Item)
        );
        Assert.Equal(
            WorldErrorCode.InvalidOperation,
            exception.ErrorCode
        );

        graph.RemoveSubWorld(characterId);
        graph.UpdateAnchorType(characterId, AnchorType.Item);

        Assert.Equal(AnchorType.Item, graph.GetAnchor(characterId).Type);
    }

    [Fact]
    public void CharacterTraversalTracksTypeChangesAndRemoval()
    {
        RuntimeWorld graph = RuntimeWorld.Create(new WorldSnapshot());
        Guid anchorId = graph.AddAnchor("Alice", "", AnchorType.Item);

        Assert.Empty(graph.GetCharacters());

        graph.UpdateAnchorType(anchorId, AnchorType.Character);
        Assert.Equal(anchorId, graph.GetCharacters().Single().Id);

        graph.UpdateAnchorType(anchorId, AnchorType.Item);
        Assert.Empty(graph.GetCharacters());

        graph.UpdateAnchorType(anchorId, AnchorType.Character);
        graph.RemoveAnchor(anchorId);
        Assert.Empty(graph.GetCharacters());
    }

    [Fact]
    public void InitializationReportsAllValidationErrors()
    {
        Guid characterId = Guid.NewGuid();
        Guid wrongKey = Guid.NewGuid();
        Guid relationId = Guid.NewGuid();
        var data = new WorldSnapshot
        {
            Id = Guid.Empty,
            Anchors =
            {
                [wrongKey] = new Anchor(
                    characterId,
                    "Alice",
                    "",
                    AnchorType.Character
                )
            },
            Relations =
            {
                [relationId] = new Relation(
                    relationId,
                    "broken",
                    "",
                    characterId,
                    Guid.NewGuid()
                )
            },
            SubWorlds =
            {
                new SubWorldSnapshot
                {
                    DomainId = Guid.NewGuid()
                }
            }
        };

        WorldException exception = Assert.Throws<WorldException>(
            () => RuntimeWorld.Create(data)
        );

        Assert.Equal(
            WorldErrorCode.InvalidWorldSnapshot,
            exception.ErrorCode
        );
        Assert.True(exception.ValidationErrors.Count >= 4);
        Assert.Contains(
            exception.ValidationErrors,
            error => error.Contains("WorldSnapshot.Id")
        );
        Assert.Contains(
            exception.ValidationErrors,
            error => error.Contains("字典键与 Anchor.Id")
        );
        Assert.Contains(
            exception.ValidationErrors,
            error => error.Contains("TargetId")
        );
        Assert.Contains(
            exception.ValidationErrors,
            error => error.Contains("DomainId")
        );
    }

    [Fact]
    public void InitializationReportsCrossScopeDuplicates()
    {
        Guid firstCharacterId = Guid.NewGuid();
        Guid secondCharacterId = Guid.NewGuid();
        Guid relationId = Guid.NewGuid();
        var worldRelation = new Relation(
            relationId,
            "relation",
            "",
            firstCharacterId,
            secondCharacterId
        );
        var subWorldRelation = new Relation(
            relationId,
            "relation",
            "",
            secondCharacterId,
            firstCharacterId
        );
        var data = new WorldSnapshot
        {
            Anchors =
            {
                [firstCharacterId] = new Anchor(
                    firstCharacterId,
                    "Alice",
                    "",
                    AnchorType.Character
                ),
                [secondCharacterId] = new Anchor(
                    secondCharacterId,
                    "Alice",
                    "",
                    AnchorType.Character
                )
            },
            Relations =
            {
                [relationId] = worldRelation
            },
            SubWorlds =
            {
                new SubWorldSnapshot
                {
                    DomainId = firstCharacterId,
                    Relations =
                    {
                        [relationId] = subWorldRelation
                    }
                },
                new SubWorldSnapshot
                {
                    DomainId = firstCharacterId
                }
            }
        };

        WorldException exception = Assert.Throws<WorldException>(
            () => RuntimeWorld.Create(data)
        );

        Assert.Contains(
            exception.ValidationErrors,
            error => error.Contains("Character") && error.Contains("重复")
        );
        Assert.Contains(
            exception.ValidationErrors,
            error => error.Contains("一个 Character 最多只能持有一个子世界")
        );
        Assert.Contains(
            exception.ValidationErrors,
            error => error.Contains("Relation") && error.Contains("重复")
        );
    }

    [Fact]
    public void SnapshotIsDetachedFromGraph()
    {
        RuntimeWorld graph = RuntimeWorld.Create(new WorldSnapshot());
        Guid anchorId = graph.AddAnchor("Anchor", "Before", AnchorType.Item);
        WorldSnapshot snapshot = graph.CreateSnapshot();
        snapshot.Anchors.Clear();

        Assert.Equal(anchorId, graph.GetAnchor(anchorId).Id);
    }

    [Fact]
    public void SuccessfulChangesIncrementRevisionAndRaiseOneEvent()
    {
        RuntimeWorld world = RuntimeWorld.Create(new WorldSnapshot());
        var changes = new List<WorldChangedEventArgs>();
        world.Changed += (_, eventArgs) => changes.Add(eventArgs);
        Guid sourceId = world.AddAnchor("Source", "", AnchorType.Item);
        Guid targetId = world.AddAnchor("Target", "", AnchorType.Item);
        world.AddRelation("relation", "", sourceId, targetId);

        changes.Clear();
        world.RemoveAnchor(sourceId);

        WorldChangedEventArgs change = Assert.Single(changes);
        Assert.Equal(4, world.Revision);
        Assert.Equal(world.Revision, change.Revision);
        Assert.Equal(nameof(RuntimeWorld.RemoveAnchor), change.Operation);
    }

    [Fact]
    public void NoOpAndFailedChangesDoNotRaiseEvents()
    {
        RuntimeWorld world = RuntimeWorld.Create(new WorldSnapshot());
        Guid anchorId = world.AddAnchor("Anchor", "Description", AnchorType.Item);
        long revision = world.Revision;
        int changeCount = 0;
        world.Changed += (_, _) => changeCount++;

        world.UpdateAnchorDescription(anchorId, "Description");
        Assert.Throws<WorldException>(() => world.UpdateAnchorName(anchorId, ""));

        Assert.Equal(revision, world.Revision);
        Assert.Equal(0, changeCount);
    }
}
