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
            Console.WriteLine($"Tavi CLI：已加载世界 {session.Current.CreateSnapshot().Id}。");
            Console.WriteLine(languageModels is null
                ? "语言模型未配置；设置 TAVI_OPENAI_API_KEY 与 TAVI_OPENAI_MODEL 后启用。"
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
        string? apiKey = Environment.GetEnvironmentVariable("TAVI_OPENAI_API_KEY");
        string? model = Environment.GetEnvironmentVariable("TAVI_OPENAI_MODEL");
        if (string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(model))
            return null;
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(model))
            throw LanguageModelConfigurationException.Invalid(
                "TAVI_OPENAI_API_KEY 与 TAVI_OPENAI_MODEL 必须同时配置。");

        string? endpointValue = Environment.GetEnvironmentVariable("TAVI_OPENAI_ENDPOINT");
        Uri endpoint;
        if (string.IsNullOrWhiteSpace(endpointValue))
        {
            endpoint = new Uri("https://api.openai.com/v1");
        }
        else if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out Uri? parsedEndpoint))
        {
            throw LanguageModelConfigurationException.Invalid(
                "TAVI_OPENAI_ENDPOINT 必须是绝对 URI。");
        }
        else
        {
            endpoint = parsedEndpoint;
        }
        string? clientTypeValue =
            Environment.GetEnvironmentVariable("TAVI_OPENAI_CLIENT_TYPE");
        OpenAIClientType clientType = clientTypeValue?.ToLowerInvariant() switch
        {
            null or "" or "chat" => OpenAIClientType.Chat,
            "responses" => OpenAIClientType.Responses,
            _ => throw LanguageModelConfigurationException.Invalid(
                "TAVI_OPENAI_CLIENT_TYPE 只允许 chat 或 responses。")
        };
        var client = new OpenAILanguageModelClient(new OpenAILanguageModelOptions
        {
            Endpoint = endpoint,
            ApiKey = apiKey,
            Model = model,
            ClientType = clientType
        });
        return new LanguageModelRunner(client, settings, logger);
    }
}
