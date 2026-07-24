using Tavi.Application.Guidance;
using Tavi.Application.LanguageModel;
using Tavi.Application.Logging;
using Tavi.Application.World;

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

    /// <summary>创建绑定到指定 World 服务的 Guidance 应用服务。</summary>
    public IGuidanceService CreateGuidanceService(IWorldService worldService)
    {
        ArgumentNullException.ThrowIfNull(worldService);
        return new GuidanceSession(worldService, LanguageModels, Logger);
    }
}
