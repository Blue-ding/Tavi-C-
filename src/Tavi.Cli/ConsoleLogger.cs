using Tavi.Application.Logging;

namespace Tavi.Cli;

internal sealed class ConsoleLogger : ILogger
{
    private readonly object _gate = new();
    private readonly LogLevel _minimumLevel;

    internal ConsoleLogger(LogLevel minimumLevel = LogLevel.Information)
    {
        _minimumLevel = minimumLevel;
    }

    public void Log(
        LogLevel level,
        string category,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        if (level < _minimumLevel)
            return;
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentNullException.ThrowIfNull(message);

        string propertyText = properties is null || properties.Count == 0
            ? string.Empty
            : " " + string.Join(
                " ",
                properties.Select(pair => $"{pair.Key}={Sanitize(pair.Value)}"));
        string line =
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] [{category}] {message}{propertyText}";
        lock (_gate)
        {
            TextWriter writer = level == LogLevel.Error ? Console.Error : Console.Out;
            writer.WriteLine(line);
            if (exception is not null)
                writer.WriteLine(exception);
        }
    }

    private static string Sanitize(object? value)
    {
        string text = value?.ToString() ?? "<null>";
        return text.Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
