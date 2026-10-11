using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PersonalPlatform.Infrastructure.Identity;

namespace PersonalPlatform.Api.Infrastructure;

/// <summary>Application-level rate limiting policies.</summary>
/// <remarks>
/// Partition keys come only from server-verified data: the authenticated user id (from the validated JWT) or
/// the connection's remote IP. The remote IP is taken from X-Forwarded-For only when the request arrived from
/// a configured trusted proxy (ForwardedHeaders middleware), so clients cannot choose their own bucket.
/// IPv6 clients are grouped by /64 so a single host cannot rotate through its own address block.
/// Limits are per instance (in-memory); see docs/SECURITY.md for multi-instance deployments.
/// </remarks>
public static class RateLimiting
{
    public const string Auth = "auth";
    public const string Refresh = "refresh";
    public const string Export = "export";
    public const string Reports = "reports";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var limits = configuration.GetSection("RateLimits").Get<RateLimitSettings>() ?? new RateLimitSettings();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectionAsync;

            // Every API request: per user when authenticated, per IP otherwise. Static client files are not limited here.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    return RateLimitPartition.GetNoLimiter("static");
                }

                var userId = context.User.FindFirst(AppClaims.Subject)?.Value;
                return userId is not null
                    ? RateLimitPartition.GetTokenBucketLimiter($"u:{userId}", _ => Bucket(limits.UserRequestsPerMinute))
                    : RateLimitPartition.GetTokenBucketLimiter($"ip:{ClientKey(context)}", _ => Bucket(limits.AnonymousRequestsPerMinute));
            });

            // Credential and email endpoints: strict per-IP fixed window (brute force, credential stuffing, mail bombing).
            options.AddPolicy(Auth, context => RateLimitPartition.GetFixedWindowLimiter(
                $"auth:{ClientKey(context)}",
                _ => Window(limits.AuthRequestsPerMinute)));

            options.AddPolicy(Refresh, context => RateLimitPartition.GetFixedWindowLimiter(
                $"refresh:{ClientKey(context)}",
                _ => Window(limits.RefreshRequestsPerMinute)));

            options.AddPolicy(Export, context => RateLimitPartition.GetFixedWindowLimiter(
                $"export:{UserOrIp(context)}",
                _ => Window(limits.ExportsPerMinute)));

            options.AddPolicy(Reports, context => RateLimitPartition.GetTokenBucketLimiter(
                $"reports:{UserOrIp(context)}",
                _ => Bucket(limits.ReportRequestsPerMinute)));
        });

        return services;
    }

    internal static string ClientKey(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        if (ip is null)
        {
            return "unknown";
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = ip.GetAddressBytes();
            Array.Clear(bytes, 8, 8); // keep the /64 network prefix
            return new IPAddress(bytes) + "/64";
        }

        return ip.ToString();
    }

    private static string UserOrIp(HttpContext context) =>
        context.User.FindFirst(AppClaims.Subject)?.Value is { } userId ? $"u:{userId}" : $"ip:{ClientKey(context)}";

    private static TokenBucketRateLimiterOptions Bucket(int perMinute) => new()
    {
        TokenLimit = perMinute,
        TokensPerPeriod = perMinute,
        ReplenishmentPeriod = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    private static FixedWindowRateLimiterOptions Window(int perMinute) => new()
    {
        PermitLimit = perMinute,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay)
            ? (int)Math.Ceiling(delay.TotalSeconds)
            : 60;
        response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many requests. Try again later.",
            Extensions = { ["code"] = "rate_limited", ["retryAfterSeconds"] = retryAfter },
        };
        var writer = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await writer.WriteAsync(new ProblemDetailsContext { HttpContext = context.HttpContext, ProblemDetails = problem });
        context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("PersonalPlatform.Security")
            .LogWarning("security.rate_limited path={Path}", context.HttpContext.Request.Path.Value);
    }
}

public sealed class RateLimitSettings
{
    public int UserRequestsPerMinute { get; set; } = 300;

    public int AnonymousRequestsPerMinute { get; set; } = 60;

    public int AuthRequestsPerMinute { get; set; } = 10;

    public int RefreshRequestsPerMinute { get; set; } = 30;

    public int ExportsPerMinute { get; set; } = 5;

    public int ReportRequestsPerMinute { get; set; } = 60;
}
