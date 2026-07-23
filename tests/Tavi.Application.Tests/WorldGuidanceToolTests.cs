using System.Text.Json;
using Tavi.Application.LanguageModel;
using Tavi.Application.World;
using Tavi.Domain.World;
using Xunit;

namespace Tavi.Application.Tests;

public sealed class WorldGuidanceToolTests
{
    [Fact]
    public void CreatesAllToolsWithStrictArgumentSchemas()
    {
        IReadOnlyCollection<ITool> tools = WorldGuidanceTool.CreateTools(CreateGraph());

        Assert.Equal(11, tools.Count);
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
        WorldGraph graph = CreateGraph();
        IReadOnlyCollection<ITool> tools = WorldGuidanceTool.CreateTools(graph);

        string exact = await Execute(GetTool(tools, "get_anchor"), """{"Name":"alice"}""");
        string fuzzy = await Execute(
            GetTool(tools, "query_anchor"),
            """{"Clues":["hero","character"]}"""
        );

        AssertResultCount(exact, 1);
        AssertResultCount(fuzzy, 1);
        Assert.Contains("\"name\": \"Alice\"", exact);
        Assert.DoesNotContain(graph.GetCharacters().Single(anchor => anchor.Name == "Alice").Id.ToString(), exact);
    }

    [Fact]
    public async Task RelationQueriesIncludeScopeAndAnchorNamesWithoutIds()
    {
        WorldGraph graph = CreateGraph();
        IReadOnlyCollection<ITool> tools = WorldGuidanceTool.CreateTools(graph);

        string output = await Execute(
            GetTool(tools, "query_sub_world_relation"),
            """{"Clues":["secret","Bob"],"CharacterName":"Alice"}"""
        );

        AssertResultCount(output, 1);
        Assert.Contains("\"type\": \"SubWorld\"", output);
        Assert.Contains("\"name\": \"Alice\"", output);
        Assert.Contains("\"name\": \"Bob\"", output);
        foreach (Anchor anchor in graph.GetAnchors())
            Assert.DoesNotContain(anchor.Id.ToString(), output);
        Guid aliceId = graph.GetCharacters().Single(anchor => anchor.Name == "Alice").Id;
        foreach (Relation relation in graph.GetRelations(aliceId))
            Assert.DoesNotContain(relation.Id.ToString(), output);
    }

    [Fact]
    public async Task StructuralQueriesRespectDirectionScopeAndBothEndpoints()
    {
        IReadOnlyCollection<ITool> tools = WorldGuidanceTool.CreateTools(CreateGraph());

        string outgoing = await Execute(
            GetTool(tools, "get_anchor_relations"),
            """
            {
              "AnchorName":"Alice",
              "Direction":"Outgoing",
              "Scope":"World",
              "CharacterName":""
            }
            """
        );
        string between = await Execute(
            GetTool(tools, "get_relations_between_anchors"),
            """
            {
              "FirstAnchorName":"Alice",
              "SecondAnchorName":"Bob",
              "Scope":"SubWorld",
              "CharacterName":"Alice"
            }
            """
        );

        AssertResultCount(outgoing, 1);
        Assert.Contains("\"name\": \"owns\"", outgoing);
        AssertResultCount(between, 1);
        Assert.Contains("\"name\": \"fears\"", between);
    }

    [Fact]
    public async Task ComparisonKeepsWorldAndSubWorldResultsSeparate()
    {
        IReadOnlyCollection<ITool> tools = WorldGuidanceTool.CreateTools(CreateGraph());

        string output = await Execute(
            GetTool(tools, "compare_world_with_sub_world"),
            """{"CharacterName":"Alice","Clues":["ownership"]}"""
        );

        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(1, document.RootElement.GetProperty("world").GetProperty("total").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("subWorld").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task QueryRejectsEmptyClues()
    {
        ITool tool = GetTool(WorldGuidanceTool.CreateTools(CreateGraph()), "query_anchor");

        await Assert.ThrowsAsync<ToolArgumentException>(
            () => Execute(tool, """{"Clues":[""," "]}""")
        );
    }

    [Fact]
    public async Task ResultsAreLimitedAndReportTruncation()
    {
        WorldGraph graph = WorldGraph.Create(new WorldData());
        for (int index = 0; index < 25; index++)
            graph.AddAnchor($"Item {index:D2}", "shared clue", AnchorType.Item);
        ITool tool = GetTool(WorldGuidanceTool.CreateTools(graph), "query_anchor");

        string output = await Execute(tool, """{"Clues":["shared"]}""");
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;

        Assert.Equal(25, root.GetProperty("total").GetInt32());
        Assert.Equal(20, root.GetProperty("returned").GetInt32());
        Assert.True(root.GetProperty("truncated").GetBoolean());
        Assert.Equal(20, root.GetProperty("items").GetArrayLength());
    }

    private static WorldGraph CreateGraph()
    {
        WorldGraph graph = WorldGraph.Create(new WorldData());
        Guid aliceId = graph.AddAnchor("Alice", "hero", AnchorType.Character);
        Guid bobId = graph.AddAnchor("Bob", "friend", AnchorType.Character);
        Guid swordId = graph.AddAnchor("Sword", "weapon", AnchorType.Item);
        graph.AddRelation("owns", "ownership fact", aliceId, swordId);
        graph.AddRelation("fears", "secret ownership concern", aliceId, bobId, aliceId);
        return graph;
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
}
