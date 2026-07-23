using Tavi.Host.Runtime;
using Tavi.Host.ViewModels;

namespace Tavi.Host.Endpoints;

internal static class SettingsEndpoints
{
    internal static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder settings = endpoints.MapGroup("/api/v1/settings");
        settings.MapPut("/", (
            UpdateSettingsRequest request,
            SettingsRuntime runtime,
            GuidanceRuntime guidance,
            CancellationToken cancellationToken) => SaveAllAsync(request, runtime, guidance, cancellationToken));
        settings.MapGet("/language-model", (
            SettingsRuntime runtime,
            CancellationToken cancellationToken) => runtime.LoadAsync(cancellationToken));
        settings.MapPut("/language-model", (
            UpdateLanguageModelSettingsRequest request,
            SettingsRuntime runtime,
            GuidanceRuntime guidance,
            CancellationToken cancellationToken) => SaveLanguageModelAsync(request, runtime, guidance, cancellationToken));
        settings.MapGet("/openai", (
            SettingsRuntime runtime,
            CancellationToken cancellationToken) => runtime.LoadOpenAIAsync(cancellationToken));
        settings.MapPut("/openai", (
            UpdateOpenAIConfigurationRequest request,
            SettingsRuntime runtime,
            GuidanceRuntime guidance,
            CancellationToken cancellationToken) => SaveOpenAIAsync(request, runtime, guidance, cancellationToken));
        return endpoints;
    }

    private static async Task<SettingsSaveResultViewModel> SaveAllAsync(
        UpdateSettingsRequest request,
        SettingsRuntime settings,
        GuidanceRuntime guidance,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        OpenAIConfigurationViewModel openAI = await settings.SaveOpenAIAsync(request.OpenAI, cancellationToken);
        LanguageModelSettingsViewModel languageModel = await settings.SaveAsync(request.LanguageModel, cancellationToken);
        await guidance.ReloadSettingsAsync(cancellationToken);
        return new SettingsSaveResultViewModel(
            languageModel,
            openAI,
            false,
            "已保存并即时生效。已有 Guidance Session 无需重建。");
    }

    private static async Task<LanguageModelSettingsViewModel> SaveLanguageModelAsync(
        UpdateLanguageModelSettingsRequest request,
        SettingsRuntime settings,
        GuidanceRuntime guidance,
        CancellationToken cancellationToken)
    {
        LanguageModelSettingsViewModel saved = await settings.SaveAsync(request, cancellationToken);
        await guidance.ReloadSettingsAsync(cancellationToken);
        return saved;
    }

    private static async Task<OpenAIConfigurationViewModel> SaveOpenAIAsync(
        UpdateOpenAIConfigurationRequest request,
        SettingsRuntime settings,
        GuidanceRuntime guidance,
        CancellationToken cancellationToken)
    {
        OpenAIConfigurationViewModel saved = await settings.SaveOpenAIAsync(request, cancellationToken);
        await guidance.ReloadSettingsAsync(cancellationToken);
        return saved;
    }
}
