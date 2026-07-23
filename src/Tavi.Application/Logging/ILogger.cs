namespace Tavi.Application.Logging;

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
    void Log(
        LogLevel level,
        string category,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? properties = null);

    void Log(string message) =>
        Log(LogLevel.Information, "Application", message);

    void LogWarning(string message) =>
        Log(LogLevel.Warning, "Application", message);

    void LogError(string message, Exception? exception = null) =>
        Log(LogLevel.Error, "Application", message, exception);
}

public sealed class NullLogger : ILogger
{
    public static NullLogger Instance { get; } = new();

    private NullLogger()
    {
    }

    public void Log(
        LogLevel level,
        string category,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
    }
}
