using Tavi.Application.LanguageModel;
using Tavi.Host.ViewModels;
using Tavi.Infrastructure.OpenAI;
using Tavi.Runtime;

namespace Tavi.Host.Mapping;

/// <summary>在 HTTP 设置契约与 Runtime 设置契约之间转换。</summary>
internal static class SettingsViewModelMapper
{
    internal static LanguageModelSettings ToSettings(UpdateLanguageModelSettingsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new LanguageModelSettings
        {
            MaxToolRounds = request.MaxToolRounds,
            MaxOutputRepairAttempts = request.MaxOutputRepairAttempts,
            OverallTimeout = TimeSpan.FromSeconds(request.OverallTimeoutSeconds),
            ToolCalls = ParsePolicy(request.ToolCalls, nameof(request.ToolCalls)),
            NativeJsonOutput = ParsePolicy(request.NativeJsonOutput, nameof(request.NativeJsonOutput)),
            Streaming = ParsePolicy(request.Streaming, nameof(request.Streaming))
        };
    }

    internal static LanguageModelSettingsViewModel ToViewModel(LanguageModelSettings settings) => new(
        settings.MaxToolRounds,
        settings.MaxOutputRepairAttempts,
        checked((int)settings.OverallTimeout.TotalSeconds),
        settings.ToolCalls.ToString(),
        settings.NativeJsonOutput.ToString(),
        settings.Streaming.ToString());

    internal static OpenAIConfigurationUpdate ToUpdate(UpdateOpenAIConfigurationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Endpoint))
            throw new ArgumentException("OpenAI Endpoint 不能为空。", nameof(request.Endpoint));
        if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out Uri? endpoint))
            throw new ArgumentException("OpenAI Endpoint 必须是绝对地址。", nameof(request.Endpoint));
        if (string.IsNullOrWhiteSpace(request.Model))
            throw new ArgumentException("OpenAI 模型名称不能为空。", nameof(request.Model));
        if (!Enum.TryParse(request.ClientType, true, out OpenAIClientType clientType) || !Enum.IsDefined(clientType))
            throw new ArgumentException($"不支持的 OpenAI API 类型“{request.ClientType}”。", nameof(request.ClientType));
        return new OpenAIConfigurationUpdate(
            endpoint,
            request.Model,
            request.ApiKey,
            clientType,
            request.SupportsRequiredToolChoice,
            request.EnableThinking);
    }

    internal static OpenAIConfigurationViewModel ToViewModel(OpenAIConfigurationSnapshot configuration) => new(
        configuration.Endpoint.ToString(),
        configuration.Model,
        configuration.ClientType.ToString(),
        configuration.SupportsRequiredToolChoice,
        configuration.EnableThinking,
        configuration.HasApiKey);

    private static FeaturePolicy ParsePolicy(string value, string parameterName)
    {
        if (!Enum.TryParse(value, true, out FeaturePolicy policy) || !Enum.IsDefined(policy))
            throw new ArgumentException($"不支持的功能策略“{value}”。", parameterName);
        return policy;
    }
}
