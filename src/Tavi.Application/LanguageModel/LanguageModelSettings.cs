namespace Tavi.Application.LanguageModel;

/// <summary>定义功能禁用、允许降级或必须支持的业务策略。</summary>
public enum FeaturePolicy
{
    Disabled,
    Preferred,
    Required
}

/// <summary>定义一次运行是否允许或强制模型在第一轮调用工具。</summary>
public enum ToolCallMode
{
    None,
    Auto,
    Required
}

/// <summary>
/// 由 Application 持有并可持久化的模型运行策略。
/// </summary>
public sealed record LanguageModelSettings
{
    /// <summary>获取当前持久化格式版本。</summary>
    public const int CurrentVersion = 1;

    /// <summary>获取设置文件版本。</summary>
    public int Version { get; init; } = CurrentVersion;
    /// <summary>获取一次运行允许的最大工具轮次。</summary>
    public int MaxToolRounds { get; init; } = 8;
    /// <summary>获取输出未通过校验时允许的修复次数。</summary>
    public int MaxOutputRepairAttempts { get; init; } = 2;
    /// <summary>获取覆盖模型调用和工具执行的总超时。</summary>
    public TimeSpan OverallTimeout { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>获取工具调用能力策略。</summary>
    public FeaturePolicy ToolCalls { get; init; } = FeaturePolicy.Preferred;
    /// <summary>获取原生 JSON 输出能力策略。</summary>
    public FeaturePolicy NativeJsonOutput { get; init; } = FeaturePolicy.Preferred;
    /// <summary>获取流式输出能力策略。</summary>
    public FeaturePolicy Streaming { get; init; } = FeaturePolicy.Disabled;

    /// <summary>验证设置版本、数值范围和枚举值。</summary>
    public void Validate()
    {
        if (Version != CurrentVersion)
            throw LanguageModelConfigurationException.Invalid(
                $"不支持语言模型设置版本 {Version}，当前版本为 {CurrentVersion}。");
        if (MaxToolRounds < 0)
            throw LanguageModelConfigurationException.Invalid("最大工具轮次不能小于零。");
        if (MaxOutputRepairAttempts < 0)
            throw LanguageModelConfigurationException.Invalid("最大输出修复次数不能小于零。");
        if (OverallTimeout <= TimeSpan.Zero)
            throw LanguageModelConfigurationException.Invalid("总超时时间必须大于零。");
        if (!Enum.IsDefined(ToolCalls) || !Enum.IsDefined(NativeJsonOutput) || !Enum.IsDefined(Streaming))
            throw LanguageModelConfigurationException.Invalid("语言模型功能策略包含无效枚举值。");
    }
}

/// <summary>
/// Application 设置持久化端口。实现不得在此存储供应商密钥。
/// </summary>
public interface ILanguageModelSettingsStore
{
    /// <summary>加载 Application 模型设置；设置不存在时返回 null。</summary>
    Task<LanguageModelSettings?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>持久化 Application 模型设置；实现不得混入供应商密钥。</summary>
    Task SaveAsync(LanguageModelSettings settings, CancellationToken cancellationToken = default);
}
