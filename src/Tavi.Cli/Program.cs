using System.Text;
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
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var cancellationSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };
        string saveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tavi", "Saves");
        string defaultSettingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tavi", "Settings");
        string settingsDirectory = Environment.GetEnvironmentVariable("TAVI_SETTINGS_DIRECTORY") ?? defaultSettingsDirectory;
        string openAIConfigurationPath = Environment.GetEnvironmentVariable("TAVI_OPENAI_CONFIGURATION_PATH") ?? Path.Combine(settingsDirectory, "openai.json");
        var logger = new ConsoleLogger();
        using var store = new JsonFileWorldStore(saveDirectory);
        using var languageModelSettingsStore = new JsonFileLanguageModelSettingsStore(
            Path.Combine(settingsDirectory, "language-model.json"));
        using var openAIConfigurationStore = new JsonFileOpenAIConfigurationStore(openAIConfigurationPath);
        await using var session = new WorldSession(store);
        try
        {
            var settingsService = new LanguageModelSettingsService(languageModelSettingsStore);
            LanguageModelSettings languageModelSettings =
                await settingsService.LoadOrDefaultAsync(cancellationSource.Token);
            if (!File.Exists(languageModelSettingsStore.Path))
                await settingsService.SaveAsync(languageModelSettings, cancellationSource.Token);

            ILanguageModelService? languageModels = await CreateLanguageModelServiceAsync(
                openAIConfigurationStore,
                languageModelSettings,
                logger,
                cancellationSource.Token);
            await session.InitializeAsync(cancellationSource.Token);
            Console.WriteLine($"Tavi CLI：已加载 World 状态 {session.StateId}。");
            Console.WriteLine(languageModels is null
                ? $"语言模型未配置；添加 {openAIConfigurationStore.Path} 后启用。"
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
        catch (OpenAIConfigurationStoreException exception)
        {
            logger.Log(LogLevel.Error, "Startup", "无法加载 OpenAI 配置。", exception);
            return 1;
        }
    }

    private static async Task<ILanguageModelService?> CreateLanguageModelServiceAsync(
        IOpenAIConfigurationStore configurationStore,
        LanguageModelSettings settings,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var configurationService = new OpenAIConfigurationService(configurationStore);
        OpenAILanguageModelOptions? options = await configurationService.LoadOptionsAsync(cancellationToken);
        if (options is null)
            return null;
        var client = new OpenAILanguageModelClient(options);
        return new LanguageModelRunner(client, settings, logger);
    }
}
