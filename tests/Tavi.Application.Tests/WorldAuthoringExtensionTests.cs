using Tavi.Application.Extensions;
using Tavi.Application.Extensions.World;
using Tavi.Application.World;
using Tavi.Domain.World;
using Tavi.Extensibility;
using Xunit;

namespace Tavi.Application.Tests;

/// <summary>验证 Module World Authoring 通过公共投影产生原子暂存提案。</summary>
public sealed class WorldAuthoringExtensionTests
{
    /// <summary>验证一个 Action 可以原子创建 Element、Scope、Aspect 和 Relation，并按一个撤销单位提交。</summary>
    [Fact]
    public async Task CompoundActionStagesAndCommitsAsOneAtomicUnit()
    {
        await using WorldSession world = await CreateWorldAsync();
        var plugin = new AuthoringPlugin();
        FrozenModuleRuntime runtime = new ExtensionSession([Definition()], plugins: [plugin]).Freeze();
        var coordinator = new WorldAuthoringCoordinator(world, runtime);

        Guid changeId = await coordinator.ProposeAndStageAsync(new SemanticKey("test:create-character"), "{}");
        WorldStagingSnapshot staged = world.CreateStagingSnapshot();

        WorldStagedChange change = Assert.Single(staged.Changes);
        Assert.Equal(changeId, change.Id);
        Assert.Equal(WorldStagedChangeStatus.Valid, change.Status);
        Assert.Equal(4, change.ChangeSet.Operations.Count);
        world.CommitStaged([changeId], world.StateId);
        Assert.Single(world.Queries.GetElements());
        Assert.Single(world.Queries.GetScopes());
        Assert.Single(world.Queries.GetAspects());
        Assert.Single(world.Queries.GetRelations());
        world.Undo(world.StateId);
        Assert.Empty(world.Queries.GetElements());
    }

    /// <summary>验证过期提案在进入暂存区前失败且不会污染已有暂存状态。</summary>
    [Fact]
    public async Task StaleProposalDoesNotPolluteStaging()
    {
        await using WorldSession world = await CreateWorldAsync();
        var plugin = new AuthoringPlugin { ReturnStaleState = true };
        var coordinator = new WorldAuthoringCoordinator(world, new ExtensionSession([Definition()], plugins: [plugin]).Freeze());

        await Assert.ThrowsAsync<ModuleSemanticException>(async () => await coordinator.ProposeAndStageAsync(new SemanticKey("test:create-character"), "{}"));

        Assert.Empty(world.CreateStagingSnapshot().Changes);
    }

    private static ModulePackageDefinition Definition() => new() { Manifest = new ModuleManifest { Id = new ModuleId("test"), Version = new ModuleVersion("1.0.0"), SchemaVersion = 1, Name = "Test" }, Semantics = new SemanticModuleDefinition() };

    private static async Task<WorldSession> CreateWorldAsync()
    {
        var world = new WorldSession(new MemoryWorldStore());
        await world.InitializeAsync();
        return world;
    }

    private sealed class AuthoringPlugin : ITaviPlugin, IWorldAuthoringExtension
    {
        public ModuleId Module => new("test");
        public ModuleVersion Version => new("1.0.0");
        internal bool ReturnStaleState { get; init; }
        public ValueTask<IReadOnlyList<WorldAuthoringAction>> GetActionsAsync(IWorldView world, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<WorldAuthoringAction>>([new() { Id = new SemanticKey("test:create-character"), Name = "Create character" }]);
        public ValueTask<WorldAuthoringProposal> ProposeAsync(WorldAuthoringRequest request, CancellationToken cancellationToken)
        {
            Guid elementId = Guid.NewGuid();
            Guid scopeId = Guid.NewGuid();
            Guid aspectId = Guid.NewGuid();
            Guid relationId = Guid.NewGuid();
            return ValueTask.FromResult(new WorldAuthoringProposal
            {
                ExpectedWorldStateId = ReturnStaleState ? Guid.NewGuid() : request.World.StateId,
                Intents =
                [
                    new WorldAuthoringIntent.AddElement(elementId, "Alice", "", new SemanticKey("core:none")),
                    new WorldAuthoringIntent.AddScope(scopeId, "Identity", "", 1, new SemanticKey("core:none"), elementId),
                    new WorldAuthoringIntent.AddAspect(aspectId, "Person", "", 1, new SemanticKey("core:none"), elementId, scopeId),
                    new WorldAuthoringIntent.AddRelation(relationId, "Self", "", 1, new SemanticKey("core:none"), elementId, elementId, scopeId)
                ]
            });
        }
    }

    private sealed class MemoryWorldStore : IWorldStore
    {
        public Task<WorldSnapshot?> LoadAsync(string slot, CancellationToken cancellationToken = default) => Task.FromResult<WorldSnapshot?>(new WorldSnapshot());
        public Task SaveAsync(string slot, WorldSnapshot snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
