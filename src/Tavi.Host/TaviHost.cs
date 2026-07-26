using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Serilog;
using Serilog.Context;
using Serilog.Debugging;
using Tavi.Extensibility;
using Tavi.Host.Endpoints;
using Tavi.Host.Errors;
using Tavi.Host.Logging;
using Tavi.Modules.Magic;
using Tavi.Runtime;
using Tavi.Runtime.Logging;

namespace Tavi.Host;

/// <summary>配置并运行 Tavi 的本地 HTTP/SSE 宿主。</summary>
public static class TaviHost
{
    public static WebApplication Build(
        string[] args,
        TaviHostOptions? options = null)
    {
        ConfigureConsoleEncoding();
        options ??= new TaviHostOptions();
        EmergencyLog.Write("Tavi Host 正在启动。");
        SelfLog.Enable(EmergencyLog.Write);

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = options.ContentRootPath,
            WebRootPath = options.WebRootPath,
        });

        if (!string.IsNullOrWhiteSpace(options.Url))
            builder.WebHost.UseUrls(options.Url);
        else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
            builder.WebHost.UseUrls("http://127.0.0.1:5178");

        builder.Logging.ClearProviders();
        builder.Logging.Configure(logging =>
            logging.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);
        builder.Services.AddSerilog(
            (_, configuration) => HostLogging.Configure(configuration, builder.Configuration),
            preserveStaticLogger: true);
        builder.Services.ConfigureHttpJsonOptions(httpJsonOptions =>
            httpJsonOptions.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<TaviExceptionHandler>();
        builder.Services.AddSingleton<ApplicationLoggerAdapter>();
        builder.Services.AddSingleton<ITaviPlugin, MagicPlugin>();
        builder.Services.AddSingleton<ExtensionRuntime>();
        builder.Services.AddHostedService(services => services.GetRequiredService<ExtensionRuntime>());
        builder.Services.AddSingleton<WorldEventBroker>();
        builder.Services.AddSingleton<WorldRuntime>();
        builder.Services.AddHostedService(services => services.GetRequiredService<WorldRuntime>());
        builder.Services.AddSingleton<ScenarioRuntime>();
        builder.Services.AddHostedService(services => services.GetRequiredService<ScenarioRuntime>());
        builder.Services.AddSingleton<GuidanceEventBroker>();
        builder.Services.AddSingleton<GuidanceRuntime>();
        builder.Services.AddHostedService(services => services.GetRequiredService<GuidanceRuntime>());
        builder.Services.AddSingleton<SettingsRuntime>();
        builder.Services.AddSingleton<WritingRuntime>();
        builder.Services.AddHostedService(services => services.GetRequiredService<WritingRuntime>());

        WebApplication app = builder.Build();
        app.UseSerilogRequestLogging(requestLogging =>
        {
            requestLogging.GetLevel = HostLogging.SelectRequestLevel;
            requestLogging.EnrichDiagnosticContext = (diagnosticContext, context) =>
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
        app.MapScenarioEndpoints();
        app.MapGuidanceEndpoints();
        app.MapSettingsEndpoints();
        app.MapWritingEndpoints();
        app.MapFallbackToFile("index.html");
        app.Lifetime.ApplicationStarted.Register(() =>
            app.Logger.LogInformation(HostLogEvents.ApplicationStarted, "Tavi Host 已完成启动。"));
        app.Lifetime.ApplicationStopping.Register(() =>
            app.Logger.LogInformation(HostLogEvents.ApplicationStopping, "Tavi Host 正在停止。"));
        app.Lifetime.ApplicationStopped.Register(() =>
            app.Logger.LogInformation(HostLogEvents.ApplicationStopped, "Tavi Host 已停止。"));
        app.Logger.LogInformation(HostLogEvents.ApplicationStarting, "Tavi Host 开始接受启动流程。");

        return app;
    }

    internal static void ConfigureConsoleEncoding(Action<Encoding>? setOutputEncoding = null)
    {
        try
        {
            (setOutputEncoding ?? (encoding => Console.OutputEncoding = encoding))(
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (IOException)
        {
            // Windows GUI 进程可能没有控制台句柄；此时没有可配置的控制台输出。
        }
    }

    public static async Task RunAsync(string[] args)
    {
        WebApplication? app = null;
        try
        {
            app = Build(args);
            await app.RunAsync();
        }
        catch (Exception exception)
        {
            if (app is not null)
                app.Logger.LogCritical(
                    HostLogEvents.ApplicationStartupFailed,
                    exception,
                    "Tavi Host 因未处理异常退出。");
            EmergencyLog.Write("Tavi Host 因未处理异常退出。", exception);
            throw;
        }
        finally
        {
            if (app is not null)
                await app.DisposeAsync();
            EmergencyLog.Write("Tavi Host 已完成日志刷新并退出。");
        }
    }
}

/// <param name="Url">宿主监听地址；桌面外壳使用端口 0 请求动态回环端口。</param>
/// <param name="ContentRootPath">宿主内容根目录。</param>
/// <param name="WebRootPath">前端静态资源目录。</param>
public sealed record TaviHostOptions(
    string? Url = null,
    string? ContentRootPath = null,
    string? WebRootPath = null);
