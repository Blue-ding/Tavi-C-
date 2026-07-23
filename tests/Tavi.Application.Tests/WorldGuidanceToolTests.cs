using System.Text.Json;
using Tavi.Application.LanguageModel;
using Tavi.Application.World;
using Tavi.Domain.World;
using Xunit;
using RuntimeWorld = Tavi.Domain.World.World;

namespace Tavi.Application.Tests;

public sealed class WorldGuidanceToolTests
{
    [Fact]
    public async Task CreatesAllToolsWithStrictArgumentSchemas()
    {
        await using WorldSession session = await CreateSession();
        IReadOnlyCollection<ITool> tools = WorldGuidanceTool.CreateTools(session);

        Assert.Equal(22, tools.Count);
        Assert.Equal(tools.Count, tools.Select(tool => tool.name).Distinct().Count());
        ITool getAnchor = GetTool(tools, "get_anchor");
        using JsonDocument schema = JsonDocument.Parse(getAnchor.parameterData);
        JsonElement root = schema.RootElement;
        Assert.True(root.GetProperty("properties").TryGetProperty("Name", out _));
        Assert.Equal("Name", root.GetProperty("required")[0].GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public async Task ExactAndFuzzyAnchorQueriesUseStringMatchingAndHideIds()
    {
        await using WorldSession session = await CreateSession();
        IReadOnlyCollection<ITool> tools = WorldGuidanceTool.CreateTools(session);

        string exact = await Execute(GetTool(tools, "get_anchor"), """{"Name":"alice"}""");
        string fuzzy = await Execute(GetTool(tools, "query_anchor"), """{"Clues":["hero","character"]}""");

        AssertResultCount(exact, 1);
        AssertResultCount(fuzzy, 1);
        Assert.Contains("\"name\": \"Alice\"", exact);
        Guid aliceId = session.Read(world => world.GetCharacters().Single(anchor => anchor.Name == "Alice").Id);
        Assert.DoesNotContain(aliceId.ToString(), exact);
    }

    [Fact]
    public async Task RelationQueriesIncludeScopeAndAnchorNamesWithoutIds()
    {
        await using WorldSession session = await CreateSession();
        IReadOnlyCollection<ITool> tools = WorldGuidanceTool.CreateTools(session);

        string output = await Execute(GetTool(tools, "query_sub_world_relation"), """{"Clues":["secret","Bob"],"CharacterName":"Alice"}""");

        AssertResultCount(output, 1);
        Assert.Contains("\"type\": \"SubWorld\"", output);
        Assert.Contains("\"name\": \"Alice\"", output);
        Assert.Contains("\"name\": \"Bob\"", output);
        session.Read(world =>
        {
            foreach (Anchor anchor in world.GetAnchors())
                Assert.DoesNotContain(anchor.Id.ToString(), output);
            foreach (Relation relation in world.GetRelations(world.GetCharacters().Single(anchor => anchor.Name == "Alice").Id))
                Assert.DoesNotContain(relation.Id.ToString(), output);
            return true;
        });
    }

    [Fact]
    public async Task StructuralQueriesRespectDirectionScopeAndBothEndpoints()
    {
        await using WorldSession session = await CreateSession();
        IReadOnlyCollection<ITool> tools = WorldGuidanceTool.CreateTools(session);

        string outgoing = await Execute(GetTool(tools, "get_anchor_relations"),
            """{"AnchorName":"Alice","Direction":"Outgoing","Scope":"World","CharacterName":""}""");
        string between = await Execute(GetTool(tools, "get_relations_between_anchors"),
            """{"FirstAnchorName":"Alice","SecondAnchorName":"Bob","Scope":"SubWorld","CharacterName":"Alice"}""");

        AssertResultCount(outgoing, 1);
        Assert.Contains("\"name\": \"owns\"", outgoing);
        AssertResultCount(between, 1);
        Assert.Contains("\"name\": \"fears\"", between);
    }

    [Fact]
    public async Task ComparisonKeepsWorldAndSubWorldResultsSeparate()
    {
        await using WorldSession session = await CreateSession();
        ITool tool = GetTool(WorldGuidanceTool.CreateTools(session), "compare_world_with_sub_world");

        string output = await Execute(tool, """{"CharacterName":"Alice","Clues":["ownership"]}""");

        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(1, document.RootElement.GetProperty("world").GetProperty("total").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("subWorld").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task QueryRejectsEmptyClues()
    {
        await using WorldSession session = await CreateSession();
        ITool tool = GetTool(WorldGuidanceTool.CreateTools(session), "query_anchor");

        await Assert.ThrowsAsync<ToolArgumentException>(() => Execute(tool, """{"Clues":[""," "]}"""));
    }

    [Fact]
    public async Task ResultsAreLimitedAndReportTruncation()
    {
        RuntimeWorld world = RuntimeWorld.Create(new WorldSnapshot());
        for (int index = 0; index < 25; index++)
            world.AddAnchor($"Item {index:D2}", "shared clue", AnchorType.Item);
        await using WorldSession session = await CreateSession(world);
        ITool tool = GetTool(WorldGuidanceTool.CreateTools(session), "query_anchor");

        string output = await Execute(tool, """{"Clues":["shared"]}""");
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;

        Assert.Equal(25, root.GetProperty("total").GetInt32());
        Assert.Equal(20, root.GetProperty("returned").GetInt32());
        Assert.True(root.GetProperty("truncated").GetBoolean());
        Assert.Equal(20, root.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task AddAnchorToolUpdatesSessionAndReturnsMutationResult()
    {
        await using WorldSession session = await CreateSession(RuntimeWorld.Create(new WorldSnapshot()));
        ITool tool = GetTool(WorldGuidanceTool.CreateTools(session), "add_anchor");

        string output = await Execute(tool, """{"Name":"Lantern","Description":"A light","Type":"Item"}""");

        Assert.Single(session.Read(world => world.GetAnchors()));
        Assert.True(session.IsDirty);
        Assert.Contains("\"operation\": \"add_anchor\"", output);
        Assert.Contains("\"name\": \"Lantern\"", output);
    }

    [Fact]
    public async Task RelationWriteToolsUseNamesAndSessionUpdate()
    {
        await using WorldSession session = await CreateSession();
        IReadOnlyCollection<ITool> tools = WorldGuidanceTool.CreateTools(session);
        long beforeRevision = session.Read(world => world.Revision);

        string added = await Execute(GetTool(tools, "add_relation"),
            """{"Name":"trusts","Description":"new fact","SourceAnchorName":"Bob","TargetAnchorName":"Alice","Scope":"World","CharacterName":""}""");
        string updated = await Execute(GetTool(tools, "update_relation_description"),
            """{"Name":"trusts","SourceAnchorName":"Bob","TargetAnchorName":"Alice","Scope":"World","CharacterName":"","Description":"updated fact"}""");

        Assert.Contains("\"name\": \"trusts\"", added);
        Assert.Contains("\"description\": \"updated fact\"", updated);
        Assert.Equal(beforeRevision + 2, session.Read(world => world.Revision));
    }

    [Fact]
    public async Task SubWorldRelationUpdateReturnsFreshData()
    {
        await using WorldSession session = await CreateSession();
        ITool tool = GetTool(WorldGuidanceTool.CreateTools(session), "update_relation_description");

        string output = await Execute(tool,
            """{"Name":"fears","SourceAnchorName":"Alice","TargetAnchorName":"Bob","Scope":"SubWorld","CharacterName":"Alice","Description":"changed belief"}""");

        Assert.Contains("\"description\": \"changed belief\"", output);
        Assert.Contains("\"type\": \"SubWorld\"", output);
    }

    private static RuntimeWorld CreateWorld()
    {
        RuntimeWorld world = RuntimeWorld.Create(new WorldSnapshot());
        Guid aliceId = world.AddAnchor("Alice", "hero", AnchorType.Character);
        Guid bobId = world.AddAnchor("Bob", "friend", AnchorType.Character);
        Guid swordId = world.AddAnchor("Sword", "weapon", AnchorType.Item);
        world.AddRelation("owns", "ownership fact", aliceId, swordId);
        world.AddRelation("fears", "secret ownership concern", aliceId, bobId, aliceId);
        return world;
    }

    private static async Task<WorldSession> CreateSession(RuntimeWorld? world = null)
    {
        var session = new WorldSession(new SnapshotWorldStore((world ?? CreateWorld()).CreateSnapshot()), autoSaveDelay: TimeSpan.FromMinutes(1));
        await session.InitializeAsync();
        return session;
    }

    private static ITool GetTool(IEnumerable<ITool> tools, string name)
    {
        return tools.Single(tool => tool.name == name);
    }

    private static Task<string> Execute(ITool tool, string json)
    {
        return tool.Execute(BinaryData.FromString(json), CancellationToken.None);
    }

    private static void AssertResultCount(string json, int expected)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(expected, document.RootElement.GetProperty("total").GetInt32());
    }

    private sealed class SnapshotWorldStore(WorldSnapshot snapshot) : IWorldStore
    {
        public Task<WorldSnapshot?> LoadAsync(string slot, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<WorldSnapshot?>(snapshot);
        }

        public Task SaveAsync(string slot, WorldSnapshot savedSnapshot, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
