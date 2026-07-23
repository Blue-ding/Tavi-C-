using System.Text.Json.Serialization;
using Tavi.Host.Endpoints;
using Tavi.Host.Errors;
using Tavi.Host.Logging;
using Tavi.Host.Runtime;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
    builder.WebHost.UseUrls("http://127.0.0.1:5178");
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

WebApplication app = builder.Build();
app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapWorldEndpoints();
app.MapGuidanceEndpoints();
app.MapFallbackToFile("index.html");
app.Run();

/// <summary>提供测试宿主和外部启动器可以发现的 ASP.NET Core 程序入口。</summary>
public partial class Program;
