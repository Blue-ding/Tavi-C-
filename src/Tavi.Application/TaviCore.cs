using Tavi.Application.LanguageModel;
using Tavi.Application.Logging;

namespace Tavi.Application;

/// <summary>
/// Application 服务入口。所有依赖由启动层显式提供。
/// </summary>
public sealed class TaviCore
{
    public TaviCore(ILanguageModelService languageModels, ILogger logger)
    {
        LanguageModels = languageModels ?? throw new ArgumentNullException(nameof(languageModels));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ILanguageModelService LanguageModels { get; }
    public ILogger Logger { get; }
}
