using Tavi.Application.World;
using Tavi.Domain.World;
using Tavi.Extensibility;

namespace Tavi.Application.Extensions.World;

/// <summary>协调活动 Module 的 World Authoring 查询、提案验证与原子暂存。</summary>
public sealed class WorldAuthoringCoordinator
{
    private readonly IWorldWorkspace _world;
    private readonly FrozenModuleRuntime _runtime;

    /// <summary>创建只通过 World Application 接口工作的协调器。</summary>
    public WorldAuthoringCoordinator(IWorldWorkspace world, FrozenModuleRuntime runtime)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    /// <summary>获取所有活动 Module 对当前 World 提供的操作。</summary>
    public async ValueTask<IReadOnlyList<WorldAuthoringAction>> GetActionsAsync(CancellationToken cancellationToken = default)
    {
        IWorldView view = WorldExtensibilityAdapter.ToView(_world.Queries.CreateSnapshot());
        var actions = new List<WorldAuthoringAction>();
        foreach ((ModuleId module, IWorldAuthoringExtension extension) in _runtime.WorldAuthoringExtensions)
        {
            IReadOnlyList<WorldAuthoringAction> contributed = await Invoke(module, () => extension.GetActionsAsync(view, _runtime.GetParameters(module), cancellationToken));
            foreach (WorldAuthoringAction action in contributed)
            {
                if (action.Id.Namespace != module)
                    throw new ModuleConfigurationException("TAVI.MODULE.WORLD.ACTION_NAMESPACE", $"World Authoring Action {action.Id} 不属于 Module {module}。");
                actions.Add(action);
            }
        }
        SemanticKey? duplicate = actions.GroupBy(action => action.Id).Where(group => group.Count() > 1).Select(group => (SemanticKey?)group.Key).FirstOrDefault();
        if (duplicate.HasValue)
            throw new ModuleConfigurationException("TAVI.MODULE.WORLD.ACTION_DUPLICATE", $"World Authoring Action {duplicate.Value} 重复。");
        return actions.OrderBy(action => action.Id.Value, StringComparer.Ordinal).ToArray();
    }

    /// <summary>调用指定操作，验证完整候选状态，并把全部 Intent 作为一个原子暂存项写入 WorldSession。</summary>
    public async ValueTask<Guid> ProposeAndStageAsync(SemanticKey actionId, string arguments, WorldStagedChangeSource source = WorldStagedChangeSource.Module, CancellationToken cancellationToken = default)
    {
        (ModuleId module, IWorldAuthoringExtension extension) = _runtime.WorldAuthoringExtensions.SingleOrDefault(value => value.Module == actionId.Namespace);
        if (extension is null)
            throw new ModuleConfigurationException("TAVI.MODULE.WORLD.CAPABILITY_MISSING", $"Module {actionId.Namespace} 没有活动的 World Authoring 能力。");
        WorldSnapshot snapshot = _world.Queries.CreateSnapshot();
        var request = new WorldAuthoringRequest { World = WorldExtensibilityAdapter.ToView(snapshot), ActionId = actionId, Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments)), Parameters = _runtime.GetParameters(module) };
        WorldAuthoringProposal proposal = await Invoke(module, () => extension.ProposeAsync(request, cancellationToken));
        WorldChangeSet changeSet = WorldExtensibilityAdapter.ToChangeSet(proposal, snapshot);
        return _world.Stage(changeSet, source);
    }

    private static async ValueTask<T> Invoke<T>(ModuleId module, Func<ValueTask<T>> action)
    {
        try
        {
            return await action();
        }
        catch (ModuleException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ModuleExecutionException("TAVI.MODULE.EXECUTION_FAILED", $"Module {module} 执行失败。", exception);
        }
    }
}
