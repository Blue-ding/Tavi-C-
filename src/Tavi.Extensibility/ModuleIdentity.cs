namespace Tavi.Extensibility;

/// <summary>表示可跨存档、声明文件和 Plugin 边界稳定识别的 Module 标识。</summary>
public readonly record struct ModuleId
{
    /// <summary>使用小写 ASCII 标识创建 Module 标识。</summary>
    /// <param name="value">由小写字母、数字、连字符或下划线组成且以字母开头的稳定文本。</param>
    public ModuleId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!IsAsciiLetter(value[0]) || value.Any(character => !IsAsciiLowerLetter(character) && !char.IsDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException("Module 标识必须以小写 ASCII 字母开头，并且只能包含小写字母、数字、连字符或下划线。", nameof(value));
        Value = value;
    }

    /// <summary>获取稳定 Module 标识文本。</summary>
    public string Value { get; }

    /// <summary>获取该值是否为默认未初始化值。</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>返回稳定 Module 标识文本。</summary>
    public override string ToString() => Value ?? string.Empty;

    private static bool IsAsciiLetter(char value) => IsAsciiLowerLetter(value);

    private static bool IsAsciiLowerLetter(char value) => value is >= 'a' and <= 'z';
}

/// <summary>表示 Module 的持久化兼容版本；首版使用严格的 major.minor.patch 格式。</summary>
public readonly record struct ModuleVersion
{
    /// <summary>解析并创建 Module 版本。</summary>
    /// <param name="value">不带预发布或构建后缀的 major.minor.patch 文本。</param>
    public ModuleVersion(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string[] segments = value.Split('.');
        if (segments.Length != 3 || segments.Any(segment => !int.TryParse(segment, out int number) || number < 0 || (segment.Length > 1 && segment[0] == '0')))
            throw new ArgumentException("Module 版本必须符合不含前导零的 major.minor.patch 格式。", nameof(value));
        Value = value;
    }

    /// <summary>获取稳定版本文本。</summary>
    public string Value { get; }

    /// <summary>获取该值是否为默认未初始化值。</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>返回稳定版本文本。</summary>
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>表示由 Module 拥有的大小写敏感语义键。</summary>
public readonly record struct SemanticKey
{
    /// <summary>使用 namespace:name 格式创建稳定语义键。</summary>
    /// <param name="value">包含 Module 命名空间和本地名称的稳定文本。</param>
    public SemanticKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("语义键不能包含首尾空白。", nameof(value));
        int separatorIndex = value.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
            throw new ArgumentException("语义键必须符合 namespace:name 格式。", nameof(value));
        Namespace = new ModuleId(value[..separatorIndex]);
        Value = value;
    }

    /// <summary>获取完整稳定语义键。</summary>
    public string Value { get; }

    /// <summary>获取拥有该语义键的 Module 命名空间。</summary>
    public ModuleId Namespace { get; }

    /// <summary>获取该值是否为默认未初始化值。</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>返回完整稳定语义键。</summary>
    public override string ToString() => Value ?? string.Empty;
}
