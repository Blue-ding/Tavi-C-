using System.Text.Json;
using System.Text.Json.Serialization;
using Tavi.Domain.World;
using Xunit;

namespace Tavi.Domain.Tests;

public sealed class WorldGraphTests
{
    [Fact]
    public void AddAndQueryGraphStructure()
    {
        WorldGraph graph = WorldGraph.Create(new WorldData());

        Guid characterId = graph.AddAnchor(
            "Alice",
            "Character",
            AnchorType.Character
        );
        Guid itemId = graph.AddAnchor("Sword", "Item", AnchorType.Item);
        Guid outgoingId = graph.AddRelation(
            "owns",
            "Alice owns the sword.",
            characterId,
            itemId
        );
        Guid incomingId = graph.AddRelation(
            "protects",
            "The sword protects Alice.",
            itemId,
            characterId,
            characterId
        );

        Assert.Equal(characterId, graph.GetAnchor(characterId).Id);
        Assert.Equal(outgoingId, graph.GetRelation(outgoingId).Id);
        Assert.Single(graph.GetOutgoingRelations(characterId));
        Assert.Single(graph.GetIncomingRelations(characterId));
        Assert.Equal(2, graph.GetRelations(characterId).Count);
        Assert.Single(graph.GetWorldRelations());
        Assert.Single(graph.GetSubWorldRelations(characterId));
        Assert.Single(graph.GetSubWorlds());
        Assert.Equal(characterId, graph.GetSubWorlds().Single().DomainId);
        Assert.Equal(2, graph.GetAnchors().Count);
        Assert.Single(graph.GetCharacters());
        Assert.Equal(characterId, graph.GetCharacters().Single().Id);
        Assert.Equal(incomingId, graph.GetSubWorldRelations(characterId).Single().Id);
    }

    [Fact]
    public void RemoveRelationWorksForSubWorld()
    {
        WorldGraph graph = WorldGraph.Create(new WorldData());
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
        WorldGraphException exception = Assert.Throws<WorldGraphException>(
            () => graph.GetRelation(relationId)
        );
        Assert.Equal(WorldGraphErrorCode.NotFound, exception.ErrorCode);
        Assert.Equal(relationId, exception.EntityId);
    }

    [Fact]
    public void RemovingCharacterCascadesRelationsAndSubWorld()
    {
        WorldGraph graph = WorldGraph.Create(new WorldData());
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

        Assert.Throws<WorldGraphException>(() => graph.GetAnchor(characterId));
        Assert.Throws<WorldGraphException>(() => graph.GetRelation(relationId));
        Assert.Throws<WorldGraphException>(
            () => graph.GetSubWorld(characterId)
        );
    }

    [Fact]
    public void RelationOnlyAllowsDescriptionUpdate()
    {
        WorldGraph graph = WorldGraph.Create(new WorldData());
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
        WorldGraph graph = WorldGraph.Create(new WorldData());
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

        WorldGraphException exception = Assert.Throws<WorldGraphException>(
            () => graph.UpdateAnchorName(secondId, "Alice")
        );

        Assert.Equal(WorldGraphErrorCode.Duplicate, exception.ErrorCode);
        Assert.Equal("Alice", graph.GetAnchor(firstId).Name);
        Assert.Equal("Bob", graph.GetAnchor(secondId).Name);
    }

    [Fact]
    public void CharacterMustRemoveSubWorldBeforeChangingType()
    {
        WorldGraph graph = WorldGraph.Create(new WorldData());
        Guid characterId = graph.AddAnchor(
            "Alice",
            "",
            AnchorType.Character
        );
        graph.CreateSubWorld(characterId);

        WorldGraphException exception = Assert.Throws<WorldGraphException>(
            () => graph.UpdateAnchorType(characterId, AnchorType.Item)
        );
        Assert.Equal(
            WorldGraphErrorCode.InvalidOperation,
            exception.ErrorCode
        );

        graph.RemoveSubWorld(characterId);
        graph.UpdateAnchorType(characterId, AnchorType.Item);

        Assert.Equal(AnchorType.Item, graph.GetAnchor(characterId).Type);
    }

    [Fact]
    public void CharacterTraversalTracksTypeChangesAndRemoval()
    {
        WorldGraph graph = WorldGraph.Create(new WorldData());
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
        var data = new WorldData
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
            SubWorldData =
            {
                new SubWorldData
                {
                    DomainId = Guid.NewGuid()
                }
            }
        };

        WorldGraphException exception = Assert.Throws<WorldGraphException>(
            () => WorldGraph.Create(data)
        );

        Assert.Equal(
            WorldGraphErrorCode.InvalidWorldData,
            exception.ErrorCode
        );
        Assert.True(exception.ValidationErrors.Count >= 4);
        Assert.Contains(
            exception.ValidationErrors,
            error => error.Contains("WorldData.Id")
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
        var data = new WorldData
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
            SubWorldData =
            {
                new SubWorldData
                {
                    DomainId = firstCharacterId,
                    Relations =
                    {
                        [relationId] = subWorldRelation
                    }
                },
                new SubWorldData
                {
                    DomainId = firstCharacterId
                }
            }
        };

        WorldGraphException exception = Assert.Throws<WorldGraphException>(
            () => WorldGraph.Create(data)
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
    public void SnapshotIsDetachedAndCanRoundTripThroughJson()
    {
        WorldGraph graph = WorldGraph.Create(new WorldData());
        Guid anchorId = graph.AddAnchor("Anchor", "Before", AnchorType.Item);
        WorldData snapshot = graph.CreateSnapshot();
        snapshot.Anchors.Clear();

        Assert.Equal(anchorId, graph.GetAnchor(anchorId).Id);

        var options = new JsonSerializerOptions
        {
            IncludeFields = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        string json = JsonSerializer.Serialize(graph.CreateSnapshot(), options);
        WorldData restored = JsonSerializer.Deserialize<WorldData>(json, options)!;
        WorldGraph restoredGraph = WorldGraph.Create(restored);

        Assert.Equal(anchorId, restoredGraph.GetAnchor(anchorId).Id);
    }
}
