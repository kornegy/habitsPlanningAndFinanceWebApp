using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Playwright;

[assembly: AssemblyFixture(typeof(PersonalPlatform.E2ETests.BrowserEnvironment))]

namespace PersonalPlatform.E2ETests;

/// <summary>
/// One PostgreSQL container, one API host listening on a real loopback port (serving the Blazor client) and
/// one Chromium instance per test run. Every test uses a fresh browser context, i.e. its own cookies and storage.
/// </summary>
/// <remarks>
/// Set <c>PP_E2E_CHROMIUM</c> to use a preinstalled Chromium binary instead of the one installed by
/// <c>playwright.ps1 install chromium</c>; set <c>PP_E2E_HEADED=1</c> to watch the tests.
/// </remarks>
public sealed class BrowserEnvironment : IAsyncLifetime
{
    public PostgresFixture Database { get; } = new();

    public E2EHost Host { get; private set; } = null!;

    public string BaseUrl { get; private set; } = string.Empty;

    public IBrowser Browser { get; private set; } = null!;

    private IPlaywright _playwright = null!;

    public async ValueTask InitializeAsync()
    {
        await Database.InitializeAsync();
        var port = FreePort();
        BaseUrl = $"http://localhost:{port}";
        Host = new E2EHost(Database, BaseUrl, port);
        Host.StartServer();

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            ExecutablePath = Environment.GetEnvironmentVariable("PP_E2E_CHROMIUM") is { Length: > 0 } path ? path : null,
            Headless = Environment.GetEnvironmentVariable("PP_E2E_HEADED") != "1",
        });
    }

    public async ValueTask DisposeAsync()
    {
        await Browser.DisposeAsync();
        _playwright.Dispose();
        await Host.DisposeAsync();
        await Database.DisposeAsync();
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

/// <summary>The API test host bound to a real Kestrel port so a browser can load the client.</summary>
public sealed class E2EHost : ApiFactory
{
    public E2EHost(PostgresFixture database, string baseUrl, int port)
        : base(database, new Dictionary<string, string?> { ["AccountEmails:PublicBaseUrl"] = baseUrl })
    {
        UseKestrel(port);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseStaticWebAssets();
    }
}
