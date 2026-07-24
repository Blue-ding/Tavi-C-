namespace Tavi.Domain.Narrative;

/// <summary>表示由 Narrative 内核或模块注册的稳定类型键。使用“命名空间:名称”而非封闭枚举，是为了允许故事模块扩展语义而不修改核心程序集。</summary>
public readonly record struct NarrativeTypeKey
{
    /// <summary>使用符合“命名空间:名称”约定的值创建类型键。</summary>
    /// <param name="value">大小写敏感的稳定类型键。</param>
    public NarrativeTypeKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.IndexOf(':') <= 0 || value.EndsWith(':'))
            throw new ArgumentException("Narrative 类型键必须符合“命名空间:名称”约定。", nameof(value));
        Value = value;
    }

    /// <summary>获取大小写敏感的稳定类型键。</summary>
    public string Value { get; }

    /// <summary>获取该值是否为默认未初始化值。</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>返回稳定类型键文本。</summary>
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>提供 Narrative 内核定义的最小边类型；故事模块可以通过自己的命名空间注册其他类型。</summary>
public static class CoreNarrativeLinkTypes
{
    /// <summary>Beat 到 World 引用的参与关系；边特征可以进一步描述参与者角色。</summary>
    public static NarrativeTypeKey Participant { get; } = new("core:participant");

    /// <summary>源 Beat 的可用性依赖目标 Beat。</summary>
    public static NarrativeTypeKey Requires { get; } = new("core:requires");

    /// <summary>源 Beat 完成后提升目标 Beat 的可用性。</summary>
    public static NarrativeTypeKey Enables { get; } = new("core:enables");

    /// <summary>目标 Beat 在叙事意义上延续源 Beat。</summary>
    public static NarrativeTypeKey Continues { get; } = new("core:continues");

    /// <summary>两个 Beat 不能在同一次选择中同时成立。</summary>
    public static NarrativeTypeKey Conflicts { get; } = new("core:conflicts");

    /// <summary>两个 Beat 竞争相同的有限叙事注意力。</summary>
    public static NarrativeTypeKey Competes { get; } = new("core:competes");

    /// <summary>源 Beat 会降低目标 Beat 的可用性或价值。</summary>
    public static NarrativeTypeKey Suppresses { get; } = new("core:suppresses");
}
