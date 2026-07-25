namespace Tavi.Extensibility;

/// <summary>表示 Module 公共失败协议的基类。</summary>
public abstract class ModuleException : Exception
{
    /// <summary>使用稳定错误码、重试语义和可选内部异常创建 Module 异常。</summary>
    protected ModuleException(string code, string message, bool retryable, Exception? innerException = null) : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        Retryable = retryable;
    }

    /// <summary>获取供程序判断的稳定错误码。</summary>
    public string Code { get; }
    /// <summary>获取调用方调整输入后是否可以重试。</summary>
    public bool Retryable { get; }
}

/// <summary>表示玩家或 Guidance 提供的 Module 参数无效。</summary>
public sealed class ModuleArgumentException : ModuleException
{
    /// <summary>创建可重试的参数异常。</summary>
    public ModuleArgumentException(string code, string message, string? parameterName = null, Exception? innerException = null) : base(code, message, true, innerException) => ParameterName = parameterName;
    /// <summary>获取相关参数名。</summary>
    public string? ParameterName { get; }
}

/// <summary>表示候选结果违反 Module 语义。</summary>
public sealed class ModuleSemanticException : ModuleException
{
    /// <summary>创建不可重试的语义异常。</summary>
    public ModuleSemanticException(string code, string message, SemanticKey? definitionId = null, Guid? entityId = null, Exception? innerException = null) : base(code, message, false, innerException)
    {
        DefinitionId = definitionId;
        EntityId = entityId;
    }
    /// <summary>获取相关 Definition 键。</summary>
    public SemanticKey? DefinitionId { get; }
    /// <summary>获取相关实体标识。</summary>
    public Guid? EntityId { get; }
}

/// <summary>表示 Module Definition、Plugin 或 Runtime 配置无效。</summary>
public sealed class ModuleConfigurationException : ModuleException
{
    /// <summary>创建不可重试的配置异常。</summary>
    public ModuleConfigurationException(string code, string message, Exception? innerException = null) : base(code, message, false, innerException) { }
}

/// <summary>表示 Module 内部执行意外失败。</summary>
public sealed class ModuleExecutionException : ModuleException
{
    /// <summary>创建不可重试的执行异常。</summary>
    public ModuleExecutionException(string code, string message, Exception? innerException = null) : base(code, message, false, innerException) { }
}
