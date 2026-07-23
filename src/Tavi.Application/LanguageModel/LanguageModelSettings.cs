namespace Tavi.Application.LanguageModel;

public enum FeaturePolicy
{
    Disabled,
    Preferred,
    Required
}

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
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public int MaxToolRounds { get; init; } = 8;
    public int MaxOutputRepairAttempts { get; init; } = 2;
    public TimeSpan OverallTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public FeaturePolicy ToolCalls { get; init; } = FeaturePolicy.Preferred;
    public FeaturePolicy NativeJsonOutput { get; init; } = FeaturePolicy.Preferred;
    public FeaturePolicy Streaming { get; init; } = FeaturePolicy.Disabled;

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
    Task<LanguageModelSettings?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        LanguageModelSettings settings,
        CancellationToken cancellationToken = default);
}
