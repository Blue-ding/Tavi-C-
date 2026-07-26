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
    public IGuidanceService CreateGuidanceService(IWorldWorkspace worldWorkspace)
    {
        ArgumentNullException.ThrowIfNull(worldWorkspace);
        return new GuidanceSession(worldWorkspace, LanguageModels, Logger);
    }

    /// <summary>创建绑定到指定 World 工作区并使用冻结 Module Runtime 的 Guidance 应用服务。</summary>
    public IGuidanceService CreateGuidanceService(IWorldWorkspace worldWorkspace, FrozenModuleRuntime extensions)
    {
        ArgumentNullException.ThrowIfNull(worldWorkspace);
        ArgumentNullException.ThrowIfNull(extensions);
        return new GuidanceSession(worldWorkspace, LanguageModels, Logger, extensions);
    }

    /// <summary>创建从 Processing Scene 冻结上下文展开并向 Writing 发布 Beat 的 Performance 服务。</summary>
    public IPerformanceService CreatePerformanceService(SceneContextView scene, IWritingService writing, FrozenModuleRuntime extensions)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(writing);
        ArgumentNullException.ThrowIfNull(extensions);
        return new PerformanceSession(scene, writing, extensions);
    }
}
