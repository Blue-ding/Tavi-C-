using Tavi.Application.Guidance;
using Tavi.Application.World;
using Tavi.Domain.World;
using Xunit;

namespace Tavi.Application.Tests;

public sealed class GuidanceSessionTests
{
    [Fact]
    public async Task ProposalRelationCanReferenceTemporaryAnchors()
    {
        await using WorldSession worldSession = await CreateSession();
        var guidance = new GuidanceSession(worldSession.Revision);
        ProposalAnchorId character = guidance.ProposeAnchor("character", "建立视角角色", "Alice", "", AnchorType.Character);
        ProposalAnchorId item = guidance.ProposeAnchor("item", "建立谜团核心", "Bell", "", AnchorType.Item);
        guidance.ProposeRelation(
            "relation",
            "建立角色认知",
            "hears",
            "",
            new ProposalAnchorReference.Proposed(character),
            new ProposalAnchorReference.Proposed(item),
            new ProposedRelationScope.SubWorld(new ProposalAnchorReference.Proposed(character)));

        WorldProposal proposal = guidance.CreateProposal();
        ProposalCompilationResult compilation = WorldProposalCompiler.Compile(proposal, ["character", "item", "relation"], worldSession);
        worldSession.Apply(compilation.ChangeSet, proposal.BaseWorldRevision);

        Guid characterId = compilation.AnchorIds[character];
        Guid itemId = compilation.AnchorIds[item];
        Assert.Equal("Alice", worldSession.Queries.GetAnchor(characterId).Name);
        ScopedRelation relation = Assert.Single(worldSession.Queries.GetSubWorldRelations("Alice"));
        Assert.Equal(characterId, relation.Source.Id);
        Assert.Equal(itemId, relation.Target.Id);
    }

    [Fact]
    public async Task CompilerRejectsRelationWhoseTemporaryAnchorWasNotAccepted()
    {
        await using WorldSession worldSession = await CreateSession();
        var guidance = new GuidanceSession(worldSession.Revision);
        ProposalAnchorId source = guidance.ProposeAnchor("source", "", "Source", "", AnchorType.Item);
        ProposalAnchorId target = guidance.ProposeAnchor("target", "", "Target", "", AnchorType.Item);
        guidance.ProposeRelation("relation", "", "links", "", new ProposalAnchorReference.Proposed(source), new ProposalAnchorReference.Proposed(target), new ProposedRelationScope.World());
        WorldProposal proposal = guidance.CreateProposal();

        Assert.Throws<InvalidOperationException>(() => WorldProposalCompiler.Compile(proposal, ["source", "relation"], worldSession));
    }

    private static async Task<WorldSession> CreateSession()
    {
        var session = new WorldSession(new SnapshotWorldStore(), autoSaveDelay: TimeSpan.FromMinutes(1));
        await session.InitializeAsync();
        return session;
    }

    private sealed class SnapshotWorldStore : IWorldStore
    {
        public Task<WorldSnapshot?> LoadAsync(string slot, CancellationToken cancellationToken = default) => Task.FromResult<WorldSnapshot?>(new WorldSnapshot());
        public Task SaveAsync(string slot, WorldSnapshot snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
