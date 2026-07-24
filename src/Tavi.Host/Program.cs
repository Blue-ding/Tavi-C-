using System.Diagnostics;
using System.Text.Json.Serialization;
using Serilog;
using Serilog.Context;
using Serilog.Debugging;
using Tavi.Host.Endpoints;
using Tavi.Host.Errors;
using Tavi.Host.Logging;
using Tavi.Host.Runtime;

EmergencyLog.Write("Tavi Host 正在启动。");
SelfLog.Enable(EmergencyLog.Write);
WebApplication? app = null;
bool runInvoked = false;
try
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
        builder.WebHost.UseUrls("http://127.0.0.1:5178");
    builder.Logging.ClearProviders();
    builder.Logging.Configure(options => options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);
    builder.Services.AddSerilog((_, loggerConfiguration) => HostLogging.Configure(loggerConfiguration, builder.Configuration), preserveStaticLogger: true);
    builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<TaviExceptionHandler>();
    builder.Services.AddSingleton<ApplicationLoggerAdapter>();
    builder.Services.AddSingleton<WorldEventBroker>();
    builder.Services.AddSingleton<WorldRuntime>();
    builder.Services.AddHostedService(services => services.GetRequiredService<WorldRuntime>());
    builder.Services.AddSingleton<GuidanceEventBroker>();
    builder.Services.AddSingleton<GuidanceRuntime>();
    builder.Services.AddHostedService(services => services.GetRequiredService<GuidanceRuntime>());
    builder.Services.AddSingleton<SettingsRuntime>();

    app = builder.Build();
    app.UseSerilogRequestLogging(options =>
    {
        options.GetLevel = HostLogging.SelectRequestLevel;
        options.EnrichDiagnosticContext = (diagnosticContext, context) =>
        {
            diagnosticContext.Set("TraceId", context.TraceIdentifier);
            diagnosticContext.Set("RequestMethod", context.Request.Method);
            diagnosticContext.Set("RequestPath", context.Request.Path.Value);
        };
    });
    app.Use(async (context, next) =>
    {
        using IDisposable traceScope = LogContext.PushProperty("TraceId", context.TraceIdentifier);
        await next();
    });
    app.UseExceptionHandler();
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapWorldEndpoints();
    app.MapGuidanceEndpoints();
    app.MapSettingsEndpoints();
    app.MapFallbackToFile("index.html");
    app.Lifetime.ApplicationStarted.Register(() => app.Logger.LogInformation(HostLogEvents.ApplicationStarted, "Tavi Host 已完成启动。"));
    app.Lifetime.ApplicationStopping.Register(() => app.Logger.LogInformation(HostLogEvents.ApplicationStopping, "Tavi Host 正在停止。"));
    app.Lifetime.ApplicationStopped.Register(() => app.Logger.LogInformation(HostLogEvents.ApplicationStopped, "Tavi Host 已停止。"));
    app.Logger.LogInformation(HostLogEvents.ApplicationStarting, "Tavi Host 开始接受启动流程。");
    runInvoked = true;
    await app.RunAsync();
}
catch (Exception exception)
{
    if (app is not null)
        app.Logger.LogCritical(HostLogEvents.ApplicationStartupFailed, exception, "Tavi Host 因未处理异常退出。");
    EmergencyLog.Write("Tavi Host 因未处理异常退出。", exception);
    throw;
}
finally
{
    if (app is not null && !runInvoked)
        await app.DisposeAsync();
    EmergencyLog.Write("Tavi Host 已完成日志刷新并退出。");
}

/// <summary>提供测试宿主和外部启动器可以发现的 ASP.NET Core 程序入口。</summary>
public partial class Program;
