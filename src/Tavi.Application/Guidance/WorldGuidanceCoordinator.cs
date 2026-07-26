using Tavi.Application.Extensions;
using Tavi.Application.Extensions.World;
using Tavi.Application.LanguageModel;
using Tavi.Application.World;

namespace Tavi.Application.Guidance;

/// <summary>协调 Guidance 与 WorldBuildSession 之间的基线读取、提案暂存和玩家确认提交。</summary>
public sealed class WorldGuidanceCoordinator
{
    private readonly IWorldBuildView _view;
    private readonly IWorldBuildContributor _contributor;
    private readonly IWorldBuildController _controller;

    public WorldGuidanceCoordinator(
        IWorldBuildView view,
        IWorldBuildContributor contributor,
        IWorldBuildController controller)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _contributor = contributor ?? throw new ArgumentNullException(nameof(contributor));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    /// <summary>捕获一次 Guidance 生成使用的不可变 World 构筑基线。</summary>
    public WorldStagingSnapshot CaptureBasis() => _view.CreateStagingSnapshot();

    /// <summary>获取当前已提交 World 的状态标识。</summary>
    public Guid CurrentWorldStateId => _view.StateId;

    /// <summary>将 Guidance 草稿编译并作为 Guidance 来源提案写入统一构筑日志。</summary>
    public WorldProposal StageProposal(WorldProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (proposal.Changes.Count == 0)
            return proposal;
        if (proposal.BaseWorldStateId != _view.StateId)
            throw new WorldStateConflictException(proposal.BaseWorldStateId, _view.StateId);
        ProposalCompilationResult compilation = WorldProposalCompiler.Compile(
            proposal,
            proposal.Changes.Select(change => change.Id),
            _view);
        IReadOnlyList<Guid> stagedIds = _contributor.Stage(
            compilation.ChangeSet.Operations,
            WorldStagedChangeSource.Guidance,
            proposal.BaseWorldStateId);
        Dictionary<string, Guid> stagedIdsByChangeId = compilation.OperationChangeIds
            .Select((changeId, index) => (changeId, stagedId: stagedIds[index]))
            .ToDictionary(item => item.changeId, item => item.stagedId, StringComparer.Ordinal);
        ProposalChange[] changes = proposal.Changes.Select(change => change switch
        {
            ProposeAddElement element => (ProposalChange)(element with { Id = stagedIdsByChangeId[element.Id].ToString() }),
            ProposeAddScope scope => scope with { Id = stagedIdsByChangeId[scope.Id].ToString() },
            ProposeAddAspect aspect => aspect with { Id = stagedIdsByChangeId[aspect.Id].ToString() },
            ProposeAddRelation relation => relation with { Id = stagedIdsByChangeId[relation.Id].ToString() },
            ProposeAddLocalAspect localAspect => localAspect with { Id = stagedIdsByChangeId[localAspect.Id].ToString() },
            ProposeAddLocalRelation localRelation => localRelation with { Id = stagedIdsByChangeId[localRelation.Id].ToString() },
            _ => throw new InvalidOperationException($"不支持的提案类型 {change.GetType().Name}。")
        }).ToArray();
        return proposal with { Changes = Array.AsReadOnly(changes) };
    }

    /// <summary>代表玩家审批并原子提交选中的 Guidance 暂存项。</summary>
    public WorldStagingCommitResult Commit(IEnumerable<Guid> selectedChangeIds)
        => _controller.CommitStaged(selectedChangeIds, _view.StateId);

    internal IReadOnlyCollection<ITool> CreateQueryTools()
        => WorldGuidanceTool.CreateQueryTools(_view);

    internal WorldAuthoringCoordinator CreateAuthoringCoordinator(FrozenModuleRuntime runtime)
        => new(_view, _contributor, runtime);
}
