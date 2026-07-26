using ApplicationLogLevel = Tavi.Application.Logging.LogLevel;

namespace Tavi.Runtime.Logging;

/// <summary>将 Application 日志端口转发到 ASP.NET Core 日志系统。</summary>
public sealed class ApplicationLoggerAdapter : Tavi.Application.Logging.ILogger
{
    private readonly ILogger<ApplicationLoggerAdapter> _logger;

    /// <summary>创建使用指定 ASP.NET Core Logger 的适配器。</summary>
    /// <param name="logger">目标日志记录器。</param>
    public ApplicationLoggerAdapter(ILogger<ApplicationLoggerAdapter> logger) => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public void Log(ApplicationLogLevel level, string category, string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null)
    {
        Microsoft.Extensions.Logging.LogLevel targetLevel = level switch
        {
            ApplicationLogLevel.Trace => Microsoft.Extensions.Logging.LogLevel.Trace,
            ApplicationLogLevel.Information => Microsoft.Extensions.Logging.LogLevel.Information,
            ApplicationLogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
            ApplicationLogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
            _ => Microsoft.Extensions.Logging.LogLevel.Information
        };
        using IDisposable? scope = properties is null ? null : _logger.BeginScope(properties);
        _logger.Log(targetLevel, exception, "[{Category}] {Message}", category, message);
    }
}
