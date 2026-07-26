using Tavi.Application.Guidance;
using Tavi.Application.LanguageModel;
using Tavi.Application.Logging;
using Tavi.Application.Performance;
using Tavi.Application.Writing;
using Tavi.Application.World;
using Tavi.Application.Extensions;
using Tavi.Extensibility;

namespace Tavi.Application;

/// <summary>
/// Application 服务入口。所有依赖由启动层显式提供。
/// </summary>
public sealed class TaviCore
{
    /// <summary>使用启动层显式提供的服务创建 Application 入口。</summary>
    public TaviCore(ILanguageModelService languageModels, ILogger logger)
    {
        LanguageModels = languageModels ?? throw new ArgumentNullException(nameof(languageModels));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>获取语言模型 Application 服务。</summary>
    public ILanguageModelService LanguageModels { get; }
    /// <summary>获取 Application 日志端口。</summary>
    public ILogger Logger { get; }

    /// <summary>创建绑定到指定 World 工作区的 Guidance 应用服务。</summary>
    public IGuidanceService CreateGuidanceService(IWorldBuildWorkspace worldWorkspace)
    {
        ArgumentNullException.ThrowIfNull(worldWorkspace);
        return CreateGuidanceService(worldWorkspace, worldWorkspace, worldWorkspace);
    }

    /// <summary>使用分离的 World 构筑权限创建 Guidance 应用模块。</summary>
    public IGuidanceService CreateGuidanceService(
        IWorldBuildView view,
        IWorldBuildContributor contributor,
        IWorldBuildController controller)
    {
        return new GuidanceSession(
            new WorldGuidanceCoordinator(view, contributor, controller),
            LanguageModels,
            Logger);
    }

    /// <summary>创建绑定到指定 World 工作区并使用冻结 Module Runtime 的 Guidance 应用服务。</summary>
    public IGuidanceService CreateGuidanceService(IWorldBuildWorkspace worldWorkspace, FrozenModuleRuntime extensions)
    {
        ArgumentNullException.ThrowIfNull(worldWorkspace);
        ArgumentNullException.ThrowIfNull(extensions);
        return CreateGuidanceService(worldWorkspace, worldWorkspace, worldWorkspace, extensions);
    }

    /// <summary>使用分离的 World 构筑权限和冻结 Module Runtime 创建 Guidance 应用模块。</summary>
    public IGuidanceService CreateGuidanceService(
        IWorldBuildView view,
        IWorldBuildContributor contributor,
        IWorldBuildController controller,
        FrozenModuleRuntime extensions)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(contributor);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(extensions);
        return new GuidanceSession(
            new WorldGuidanceCoordinator(view, contributor, controller),
            LanguageModels,
            Logger,
            extensions);
    }

    /// <summary>创建、初始化并持久化从 Processing Scene 冻结上下文展开的 Performance 工作区。</summary>
    public async Task<PerformanceSession> CreatePerformanceSessionAsync(
        IPerformanceStore store,
        SceneContextView scene,
        long randomSeed,
        IBeatPublisher beatPublisher,
        FrozenModuleRuntime extensions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(beatPublisher);
        ArgumentNullException.ThrowIfNull(extensions);
        var session = new PerformanceSession(store, scene, randomSeed, beatPublisher, extensions);
        await session.InitializeAsync(cancellationToken);
        return session;
    }
}
