using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PersonalPlatform.Infrastructure.Email;

namespace PersonalPlatform.TestInfrastructure;

/// <summary>
/// In-memory API host wired to the test PostgreSQL database (as the restricted app role), with emails
/// captured and a known JWT signing key so tests can forge invalid tokens.
/// </summary>
public class ApiFactory(PostgresFixture database, IReadOnlyDictionary<string, string?>? overrides = null)
    : WebApplicationFactory<Program>
{
    /// <summary>Header used by tests to simulate different client IP addresses (test host only).</summary>
    public const string TestClientIpHeader = "X-Test-Client-IP";

    public const string Issuer = "personal-platform";
    public const string Audience = "personal-platform-api";
    public const string KeyId = "test-key";

    public static readonly byte[] SigningKey = Enumerable.Range(1, 32).Select(i => (byte)(i * 7)).ToArray();

    public CapturingEmailSender Emails { get; } = new();

    public CapturingLoggerProvider Logs { get; } = new();

    public PostgresFixture Database => database;

    /// <summary>An HTTPS client with its own cookie jar (like one browser profile).</summary>
    public HttpClient CreateHttpsClient() => CreateHttpsClient(out _);

    public HttpClient CreateHttpsClient(out CookieJar cookies)
    {
        cookies = new CookieJar();
        return CreateDefaultClient(new Uri("https://localhost"), cookies);
    }

    /// <summary>An HTTPS client without cookie handling.</summary>
    public HttpClient CreateBareClient() => CreateDefaultClient(new Uri("https://localhost"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = database.AppConnectionString,
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = Audience,
            ["Jwt:ActiveKeyId"] = KeyId,
            ["Jwt:SigningKeys:0:KeyId"] = KeyId,
            ["Jwt:SigningKeys:0:Key"] = Convert.ToBase64String(SigningKey),
            ["AccountEmails:MinSecondsBetweenEmails"] = "0",
            ["AccountEmails:PublicBaseUrl"] = "https://localhost",
            ["RateLimits:UserRequestsPerMinute"] = "100000",
            ["RateLimits:AnonymousRequestsPerMinute"] = "100000",
            ["RateLimits:AuthRequestsPerMinute"] = "100000",
            ["RateLimits:RefreshRequestsPerMinute"] = "100000",
            ["RateLimits:ExportsPerMinute"] = "100000",
            ["RateLimits:ReportRequestsPerMinute"] = "100000",
            ["Logging:LogLevel:Default"] = "Debug",
            ["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"] = "Information",
        };
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ILoggerProvider>();
            services.AddSingleton<ILoggerProvider>(Logs);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
            services.AddSingleton<IStartupFilter, TestClientIpStartupFilter>();
        });
    }

    private sealed class TestClientIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress =
                    IPAddress.TryParse(context.Request.Headers[TestClientIpHeader], out var ip) ? ip : IPAddress.Loopback;
                await nextMiddleware(context);
            });
            next(app);
        };
    }
}

public static class TestData
{
    public static string UniqueEmail(string prefix = "user") =>
        string.Create(CultureInfo.InvariantCulture, $"{prefix}-{Guid.NewGuid():N}@example.test");

    public const string Password = "correct horse battery staple";
}
