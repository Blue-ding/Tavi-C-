namespace Tavi.Domain.Scenario;

/// <summary>表示由 Module 提供并可实例化为 Scene 的稳定定义键。</summary>
public readonly record struct SceneDefinitionType
{
    /// <summary>使用符合 namespace:name 约定的稳定文本创建 SceneDefinition 键。</summary>
    public SceneDefinitionType(string value) => Value = ScenarioTypeKeyValidation.Validate(value, nameof(value));

    /// <summary>获取大小写敏感的稳定定义键文本。</summary>
    public string Value { get; }

    /// <summary>获取该值是否为默认未初始化值。</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>返回稳定定义键文本。</summary>
    public override string ToString() => Value ?? string.Empty;
}

internal static class ScenarioTypeKeyValidation
{
    internal static string Validate(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Scenario 类型键不能包含首尾空白。", parameterName);
        int separatorIndex = value.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
            throw new ArgumentException("Scenario 类型键必须符合 namespace:name 约定。", parameterName);
        return value;
    }
}
