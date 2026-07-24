using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Tavi.Host;

namespace Tavi.App;

/// <summary>管理随桌面窗口共同启动和停止的本地 Tavi Host。</summary>
public sealed class LocalWebHost
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WebApplication? _application;

    public Uri? BaseAddress { get; private set; }

    public async Task<Uri> StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (BaseAddress is not null)
                return BaseAddress;

            string webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            string indexPath = Path.Combine(webRoot, "index.html");
            if (!File.Exists(indexPath))
                throw new FileNotFoundException("未找到已构建的 Web 前端资源。", indexPath);

            WebApplication application = TaviHost.Build(
                [],
                new TaviHostOptions(
                    Url: "http://127.0.0.1:0",
                    ContentRootPath: AppContext.BaseDirectory,
                    WebRootPath: webRoot));

            try
            {
                await application.StartAsync(cancellationToken);
                IServer server = application.Services.GetRequiredService<IServer>();
                IServerAddressesFeature? addresses =
                    server.Features.Get<IServerAddressesFeature>();
                string address = addresses?.Addresses.SingleOrDefault()
                    ?? throw new InvalidOperationException("本地服务未报告监听地址。");

                _application = application;
                BaseAddress = new Uri($"{address.TrimEnd('/')}/", UriKind.Absolute);
                return BaseAddress;
            }
            catch
            {
                await application.DisposeAsync();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_application is null)
                return;

            try
            {
                await _application.StopAsync(cancellationToken);
            }
            finally
            {
                await _application.DisposeAsync();
                _application = null;
                BaseAddress = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
