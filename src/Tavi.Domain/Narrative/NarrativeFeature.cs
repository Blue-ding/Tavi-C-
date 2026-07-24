namespace Tavi.Domain.Narrative;

/// <summary>表示模块拥有的稳定特征键。特征键必须使用模块命名空间，避免不同故事设施间发生名称碰撞。</summary>
public readonly record struct NarrativeFeatureKey
{
    /// <summary>使用符合“命名空间:名称”约定的值创建特征键。</summary>
    public NarrativeFeatureKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.IndexOf(':') <= 0 || value.EndsWith(':'))
            throw new ArgumentException("Narrative 特征键必须符合“命名空间:名称”约定。", nameof(value));
        Value = value;
    }

    /// <summary>获取大小写敏感的稳定特征键。</summary>
    public string Value { get; }

    /// <summary>获取该值是否为默认未初始化值。</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>返回稳定特征键文本。</summary>
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>表示可持久化的 Narrative 特征值。使用封闭值联合而非 object，可防止模块把不可复制的运行时对象放入领域状态。</summary>
public abstract record NarrativeFeatureValue
{
    private NarrativeFeatureValue() { }

    /// <summary>表示有限双精度数值。</summary>
    public sealed record Number : NarrativeFeatureValue
    {
        /// <summary>创建有限双精度数值。</summary>
        public Number(double value)
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Narrative 数值特征必须是有限值。");
            Value = value;
        }

        /// <summary>获取数值。</summary>
        public double Value { get; }
    }

    /// <summary>表示整数值。</summary>
    public sealed record Integer(long Value) : NarrativeFeatureValue;

    /// <summary>表示布尔值。</summary>
    public sealed record Flag(bool Value) : NarrativeFeatureValue;

    /// <summary>表示非空文本值。</summary>
    public sealed record Text : NarrativeFeatureValue
    {
        /// <summary>创建非空文本值。</summary>
        public Text(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            Value = value;
        }

        /// <summary>获取文本值。</summary>
        public string Value { get; }
    }
}
