using Tavi.Host.Mapping;
using Tavi.Host.ViewModels;
using Tavi.Runtime;

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
            CancellationToken cancellationToken) => LoadLanguageModelAsync(runtime, cancellationToken));
        settings.MapPut("/language-model", (
            UpdateLanguageModelSettingsRequest request,
            SettingsRuntime runtime,
            GuidanceRuntime guidance,
            CancellationToken cancellationToken) => SaveLanguageModelAsync(request, runtime, guidance, cancellationToken));
        settings.MapGet("/openai", (
            SettingsRuntime runtime,
            CancellationToken cancellationToken) => LoadOpenAIAsync(runtime, cancellationToken));
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
        OpenAIConfigurationViewModel openAI = SettingsViewModelMapper.ToViewModel(
            await settings.SaveOpenAIAsync(SettingsViewModelMapper.ToUpdate(request.OpenAI), cancellationToken));
        LanguageModelSettingsViewModel languageModel = SettingsViewModelMapper.ToViewModel(
            await settings.SaveLanguageModelAsync(SettingsViewModelMapper.ToSettings(request.LanguageModel), cancellationToken));
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
        LanguageModelSettingsViewModel saved = SettingsViewModelMapper.ToViewModel(
            await settings.SaveLanguageModelAsync(SettingsViewModelMapper.ToSettings(request), cancellationToken));
        await guidance.ReloadSettingsAsync(cancellationToken);
        return saved;
    }

    private static async Task<OpenAIConfigurationViewModel> SaveOpenAIAsync(
        UpdateOpenAIConfigurationRequest request,
        SettingsRuntime settings,
        GuidanceRuntime guidance,
        CancellationToken cancellationToken)
    {
        OpenAIConfigurationViewModel saved = SettingsViewModelMapper.ToViewModel(
            await settings.SaveOpenAIAsync(SettingsViewModelMapper.ToUpdate(request), cancellationToken));
        await guidance.ReloadSettingsAsync(cancellationToken);
        return saved;
    }

    private static async Task<LanguageModelSettingsViewModel> LoadLanguageModelAsync(
        SettingsRuntime settings,
        CancellationToken cancellationToken) =>
        SettingsViewModelMapper.ToViewModel(await settings.LoadLanguageModelAsync(cancellationToken));

    private static async Task<OpenAIConfigurationViewModel> LoadOpenAIAsync(
        SettingsRuntime settings,
        CancellationToken cancellationToken) =>
        SettingsViewModelMapper.ToViewModel(await settings.LoadOpenAIAsync(cancellationToken));
}
