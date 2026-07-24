using Tavi.Domain.Narrative;
using Xunit;

namespace Tavi.Domain.Tests;

/// <summary>验证 NarrativeGraph 的多元 Beat 子图与防御性快照边界。</summary>
public sealed class NarrativeGraphTests
{
    /// <summary>验证一个 Beat 可以通过多条参与边连接多个 World 引用，且查询结果不泄露输入集合。</summary>
    [Fact]
    public void BeatCanRepresentMultiParticipantEventSubgraph()
    {
        Guid beatId = Guid.NewGuid();
        Guid actorNodeId = Guid.NewGuid();
        Guid targetNodeId = Guid.NewGuid();
        var nodes = new Dictionary<Guid, NarrativeNode>
        {
            [beatId] = new NarrativeBeatNode { Id = beatId, Meaning = "展示线索", State = NarrativeBeatState.Eligible, Salience = .8, Tension = .7, Momentum = .6, Novelty = .9 },
            [actorNodeId] = new NarrativeWorldReferenceNode { Id = actorNodeId, WorldElementId = Guid.NewGuid() },
            [targetNodeId] = new NarrativeWorldReferenceNode { Id = targetNodeId, WorldElementId = Guid.NewGuid() }
        };
        NarrativeLink actor = new() { Id = Guid.NewGuid(), SourceId = beatId, TargetId = actorNodeId, Type = CoreNarrativeLinkTypes.Participant };
        NarrativeLink target = new() { Id = Guid.NewGuid(), SourceId = beatId, TargetId = targetNodeId, Type = CoreNarrativeLinkTypes.Participant };
        var links = new Dictionary<Guid, NarrativeLink> { [actor.Id] = actor, [target.Id] = target };
        NarrativeGraph graph = NarrativeGraph.Create(new NarrativeGraphSnapshot { SourceWorldStateId = Guid.NewGuid(), Profile = new NarrativeTypeKey("test:profile"), ProfileVersion = 1, RandomSeed = 42, Nodes = nodes, Links = links });

        nodes.Clear();
        links.Clear();

        Assert.Equal(2, graph.GetOutgoingLinks(beatId).Count);
        Assert.IsType<NarrativeBeatNode>(graph.GetNode(beatId));
        Assert.Equal(3, graph.CreateSnapshot().Nodes.Count);
    }
}
