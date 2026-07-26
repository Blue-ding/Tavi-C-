using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Tavi.Host;
using Xunit;

namespace Tavi.Host.Tests;

public sealed class PerformanceEndpointContractTests
{
    [Fact]
    public async Task HostMapsCompletePerformanceAndWorkspaceEventSurface()
    {
        await using WebApplication app = TaviHost.Build(
            [],
            new TaviHostOptions(
                ContentRootPath: Directory.GetCurrentDirectory()));
        string[] routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText!)
            .ToArray();

        string[] expected =
        [
            "/api/v1/performance/",
            "/api/v1/performance/start",
            "/api/v1/performance/archives",
            "/api/v1/performance/archives/{performanceId:guid}",
            "/api/v1/performance/beats",
            "/api/v1/performance/beats/{beatId:guid}/bindings/{slotId}",
            "/api/v1/performance/beats/{beatId:guid}/processing",
            "/api/v1/performance/beats/{beatId:guid}/resolve",
            "/api/v1/performance/beats/{beatId:guid}/publish",
            "/api/v1/performance/complete",
            "/api/v1/performance/abandon",
            "/api/v1/performance/undo",
            "/api/v1/performance/redo",
            "/api/v1/performance/save",
            "/api/v1/performance/archive",
            "/api/v1/performance/events",
            "/api/v1/scenario/events",
            "/api/v1/writing/events",
            "/api/v1/world/events"
        ];

        foreach (string route in expected)
            Assert.Contains(route, routes);
    }

    [Fact]
    public async Task EmptyPerformanceWorkspaceAndArchiveLibraryAreReadableOverHttp()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"tavi-host-performance-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string[] args =
            [
                $"--Tavi:SaveDirectory={directory}",
                $"--Tavi:ScenarioDirectory={directory}",
                $"--Tavi:PerformanceDirectory={directory}",
                $"--Tavi:ManuscriptDirectory={directory}",
                $"--Tavi:SettingsDirectory={directory}",
                $"--Tavi:Logging:Directory={directory}"
            ];
            await using WebApplication app = TaviHost.Build(
                args,
                new TaviHostOptions(
                    Url: "http://127.0.0.1:0",
                    ContentRootPath: Directory.GetCurrentDirectory()));
            await app.StartAsync();
            string address = app.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()!
                .Addresses
                .Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };

            using JsonDocument workspace = JsonDocument.Parse(
                await client.GetStringAsync("/api/v1/performance/"));
            using JsonDocument archives = JsonDocument.Parse(
                await client.GetStringAsync("/api/v1/performance/archives"));

            Assert.False(
                workspace.RootElement.GetProperty("hasSession").GetBoolean());
            Assert.Equal(
                JsonValueKind.Null,
                workspace.RootElement.GetProperty("performance").ValueKind);
            Assert.Equal(0, archives.RootElement.GetArrayLength());
            await app.StopAsync();
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
