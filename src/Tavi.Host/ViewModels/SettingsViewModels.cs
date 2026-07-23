namespace Tavi.Host.ViewModels;

/// <summary>展示可由本地工作台调整的语言模型运行策略。</summary>
public sealed record LanguageModelSettingsViewModel(
    int MaxToolRounds,
    int MaxOutputRepairAttempts,
    int OverallTimeoutSeconds,
    string ToolCalls,
    string NativeJsonOutput,
    string Streaming);

/// <summary>请求替换持久化的语言模型运行策略。</summary>
public sealed record UpdateLanguageModelSettingsRequest(
    int MaxToolRounds,
    int MaxOutputRepairAttempts,
    int OverallTimeoutSeconds,
    string ToolCalls,
    string NativeJsonOutput,
    string Streaming);

/// <summary>展示不包含明文密钥的 OpenAI 连接配置。</summary>
public sealed record OpenAIConfigurationViewModel(
    string Endpoint,
    string Model,
    string ClientType,
    bool SupportsRequiredToolChoice,
    bool? EnableThinking,
    bool HasApiKey);

/// <summary>请求替换本机 OpenAI 连接配置；空密钥表示保留现值。</summary>
public sealed record UpdateOpenAIConfigurationRequest(
    string Endpoint,
    string Model,
    string? ApiKey,
    string ClientType,
    bool SupportsRequiredToolChoice,
    bool? EnableThinking);

/// <summary>一次性替换前端设置页中的全部语言模型配置。</summary>
public sealed record UpdateSettingsRequest(
    UpdateLanguageModelSettingsRequest LanguageModel,
    UpdateOpenAIConfigurationRequest OpenAI);

/// <summary>报告保存后的配置快照及其运行时应用方式。</summary>
public sealed record SettingsSaveResultViewModel(
    LanguageModelSettingsViewModel LanguageModel,
    OpenAIConfigurationViewModel OpenAI,
    bool SessionRecreationRequired,
    string Message);
