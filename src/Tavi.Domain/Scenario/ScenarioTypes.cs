namespace Tavi.Domain.Scenario;

/// <summary>表示由 Scenario Kernel 或 Module 定义的 Element 类型开放键。</summary>
public readonly record struct ElementType
{
    /// <summary>使用符合 namespace:name 约定的稳定文本创建 Element 类型。</summary>
    public ElementType(string value) => Value = ScenarioTypeKeyValidation.Validate(value, nameof(value));

    /// <summary>获取不携带 Module 专属语义的内置类型。</summary>
    public static ElementType None { get; } = new("core:none");

    /// <summary>获取大小写敏感的稳定类型键文本。</summary>
    public string Value { get; }

    /// <summary>获取该值是否为默认未初始化值。</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>返回稳定类型键文本。</summary>
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>表示由 Scenario Kernel 或 Module 定义的 Aspect 类型开放键。</summary>
public readonly record struct AspectType
{
    /// <summary>使用符合 namespace:name 约定的稳定文本创建 Aspect 类型。</summary>
    public AspectType(string value) => Value = ScenarioTypeKeyValidation.Validate(value, nameof(value));

    /// <summary>获取不携带 Module 专属语义的内置类型。</summary>
    public static AspectType None { get; } = new("core:none");

    /// <summary>获取大小写敏感的稳定类型键文本。</summary>
    public string Value { get; }

    /// <summary>获取该值是否为默认未初始化值。</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>返回稳定类型键文本。</summary>
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>表示由 Scenario Kernel 或 Module 定义的 Relation 类型开放键。</summary>
public readonly record struct RelationType
{
    /// <summary>使用符合 namespace:name 约定的稳定文本创建 Relation 类型。</summary>
    public RelationType(string value) => Value = ScenarioTypeKeyValidation.Validate(value, nameof(value));

    /// <summary>获取不携带 Module 专属语义的内置类型。</summary>
    public static RelationType None { get; } = new("core:none");

    /// <summary>获取大小写敏感的稳定类型键文本。</summary>
    public string Value { get; }

    /// <summary>获取该值是否为默认未初始化值。</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>返回稳定类型键文本。</summary>
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>表示由 Scenario Kernel 或 Module 定义的 Scope 类型开放键。</summary>
public readonly record struct ScopeType
{
    /// <summary>使用符合 namespace:name 约定的稳定文本创建 Scope 类型。</summary>
    public ScopeType(string value) => Value = ScenarioTypeKeyValidation.Validate(value, nameof(value));

    /// <summary>获取不携带 Module 专属语义的内置类型。</summary>
    public static ScopeType None { get; } = new("core:none");

    /// <summary>获取大小写敏感的稳定类型键文本。</summary>
    public string Value { get; }

    /// <summary>获取该值是否为默认未初始化值。</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>返回稳定类型键文本。</summary>
    public override string ToString() => Value ?? string.Empty;
}

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
