using System.Reflection;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Tavi.Host.Logging;

/// <summary>定义 Host 生命周期日志使用的稳定事件标识；1000 至 1099 保留给进程生命周期事件。</summary>
internal static class HostLogEvents
{
    internal static readonly EventId ApplicationStarting = new(1000, nameof(ApplicationStarting));
    internal static readonly EventId ApplicationStartupFailed = new(1001, nameof(ApplicationStartupFailed));
    internal static readonly EventId ApplicationStarted = new(1002, nameof(ApplicationStarted));
    internal static readonly EventId ApplicationStopping = new(1003, nameof(ApplicationStopping));
    internal static readonly EventId ApplicationStopped = new(1004, nameof(ApplicationStopped));
}

/// <summary>集中配置 Host 的结构化控制台与持久化文件日志。</summary>
internal static class HostLogging
{
    private const long FileSizeLimitBytes = 20L * 1024L * 1024L;
    private const int RetainedFileCountLimit = 100;
    private static readonly string ApplicationInstanceId = Guid.NewGuid().ToString("N");

    /// <summary>使用当前 Host 配置初始化日志管道；文件输出失败时自动退化为控制台输出。</summary>
    internal static void Configure(LoggerConfiguration loggerConfiguration, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(loggerConfiguration);
        ArgumentNullException.ThrowIfNull(configuration);
        loggerConfiguration.MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogEventLevel.Fatal)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "Tavi.Host")
            .Enrich.WithProperty("ApplicationVersion", Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown")
            .Enrich.WithProperty("ApplicationInstanceId", ApplicationInstanceId)
            .Enrich.WithProperty("ProcessId", Environment.ProcessId)
            .WriteTo.Console(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}");

        try
        {
            string directory = LogDirectoryResolver.Resolve(configuration);
            LogDirectoryResolver.Prepare(directory);
            string path = Path.Combine(directory, "tavi-.jsonl");
            loggerConfiguration.WriteTo.File(new RenderedCompactJsonFormatter(), path, rollingInterval: RollingInterval.Day, fileSizeLimitBytes: FileSizeLimitBytes, rollOnFileSizeLimit: true, retainedFileCountLimit: RetainedFileCountLimit, shared: true, buffered: false);
            EmergencyLog.Write($"持久化日志目录：{directory}");
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            EmergencyLog.Write("无法初始化持久化日志，Host 将继续使用控制台日志。", exception);
        }
    }

    /// <summary>根据请求结果选择日志等级，并抑制成功静态资源请求产生的噪声。</summary>
    internal static LogEventLevel SelectRequestLevel(HttpContext context, double elapsedMilliseconds, Exception? exception)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (exception is not null || context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
            return LogEventLevel.Error;
        if (context.Response.StatusCode >= StatusCodes.Status400BadRequest)
            return LogEventLevel.Warning;
        return context.Request.Path.StartsWithSegments("/api") ? LogEventLevel.Information : LogEventLevel.Verbose;
    }
}
