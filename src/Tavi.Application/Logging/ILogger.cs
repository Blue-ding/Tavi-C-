namespace Tavi.Application.Logging;

/// <summary>定义 Application 使用的日志等级。</summary>
public enum LogLevel
{
    Trace,
    Information,
    Warning,
    Error
}

/// <summary>
/// Application 使用的最小日志端口。
/// </summary>
public interface ILogger
{
    /// <summary>记录一条结构化日志；实现必须避免泄露凭证等敏感信息。</summary>
    void Log(
        LogLevel level,
        string category,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? properties = null);

    /// <summary>记录普通信息。</summary>
    void Log(string message) =>
        Log(LogLevel.Information, "Application", message);

    /// <summary>记录警告信息。</summary>
    void LogWarning(string message) =>
        Log(LogLevel.Warning, "Application", message);

    /// <summary>记录错误信息。</summary>
    void LogError(string message, Exception? exception = null) =>
        Log(LogLevel.Error, "Application", message, exception);
}

/// <summary>丢弃全部日志的空实现。</summary>
public sealed class NullLogger : ILogger
{
    /// <summary>获取共享空日志实例。</summary>
    public static NullLogger Instance { get; } = new();

    private NullLogger()
    {
    }

    /// <inheritdoc />
    public void Log(
        LogLevel level,
        string category,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
    }
}
