using Tavi.Application.LanguageModel;
using Tavi.Application.Logging;
using Tavi.Application.World;
using Tavi.Infrastructure.OpenAI;
using Tavi.Infrastructure.Persistence;

namespace Tavi.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var cancellationSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };
        string saveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tavi", "Saves");
        string settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tavi",
            "Settings");
        var logger = new ConsoleLogger();
        using var store = new JsonFileWorldStore(saveDirectory);
        using var languageModelSettingsStore = new JsonFileLanguageModelSettingsStore(
            Path.Combine(settingsDirectory, "language-model.json"));
        await using var session = new WorldSession(store);
        try
        {
            var settingsService = new LanguageModelSettingsService(languageModelSettingsStore);
            LanguageModelSettings languageModelSettings =
                await settingsService.LoadOrDefaultAsync(cancellationSource.Token);
            if (!File.Exists(languageModelSettingsStore.Path))
                await settingsService.SaveAsync(languageModelSettings, cancellationSource.Token);

            ILanguageModelService? languageModels =
                CreateLanguageModelService(languageModelSettings, logger);
            await session.InitializeAsync(cancellationSource.Token);
            Console.WriteLine($"Tavi CLI：已加载世界 {session.Queries.CreateSnapshot().Id}。");
            Console.WriteLine(languageModels is null
                ? "语言模型未配置；添加 src/Tavi.Infrastructure.OpenAI/SelfCongif.md 后启用。"
                : $"语言模型已就绪：{languageModels.Capabilities.Provider}。");
            Console.WriteLine("工程骨架已就绪，业务命令将在后续迁移阶段接入。");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("启动已取消。");
            return 1;
        }
        catch (WorldStoreException exception)
        {
            Console.Error.WriteLine($"无法加载世界存档：{exception.Message}");
            return 1;
        }
        catch (LanguageModelException exception)
        {
            logger.Log(
                LogLevel.Error,
                "Startup",
                $"语言模型初始化失败：{exception.ErrorCode}",
                exception);
            return 1;
        }
        catch (LanguageModelSettingsStoreException exception)
        {
            logger.Log(LogLevel.Error, "Startup", "无法加载语言模型设置。", exception);
            return 1;
        }
    }

    private static ILanguageModelService? CreateLanguageModelService(
        LanguageModelSettings settings,
        ILogger logger)
    {
        OpenAILanguageModelOptions? options = SelfConfigOpenAILanguageModelOptionsLoader.TryLoad();
        if (options is null)
            return null;
        var client = new OpenAILanguageModelClient(options);
        return new LanguageModelRunner(client, settings, logger);
    }
}
