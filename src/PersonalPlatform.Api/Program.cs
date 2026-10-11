using System.Diagnostics;
using System.Net;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using PersonalPlatform.Api.Infrastructure;
using PersonalPlatform.Api.Security;
using PersonalPlatform.Application;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Infrastructure;
using PersonalPlatform.Infrastructure.Identity;
using PersonalPlatform.Infrastructure.Persistence;

if (args is ["--healthcheck"])
{
    return await HealthProbe.CheckAsync();
}

var builder = WebApplication.CreateBuilder(args);

ConfigureLogging(builder);
ConfigureKestrel(builder);
ConfigureServices(builder.Services, builder.Configuration, builder.Environment);

var app = builder.Build();
ConfigurePipeline(app);
await app.RunAsync();
return 0;

static void ConfigureLogging(WebApplicationBuilder builder)
{
    builder.Logging.ClearProviders();
    if (builder.Environment.IsDevelopment())
    {
        builder.Logging.AddSimpleConsole(o => o.IncludeScopes = true);
    }
    else
    {
        // Structured JSON logs for aggregation; scopes carry the correlation id.
        builder.Logging.AddJsonConsole(o =>
        {
            o.IncludeScopes = true;
            o.UseUtcTimestamp = true;
            o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        });
    }
}

static void ConfigureKestrel(WebApplicationBuilder builder) =>
    builder.WebHost.ConfigureKestrel(kestrel =>
    {
        kestrel.AddServerHeader = false;
        kestrel.Limits.MaxRequestBodySize = RequestBodyLimitMiddleware.MaxBodyBytes; // largest legitimate payload: a 100k-char note
        kestrel.Limits.MaxRequestHeadersTotalSize = 32 * 1024;
        kestrel.Limits.MaxRequestLineSize = 8 * 1024;
        kestrel.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
        kestrel.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(60);
        kestrel.Limits.MinRequestBodyDataRate = new(bytesPerSecond: 240, gracePeriod: TimeSpan.FromSeconds(5));
    });

static void ConfigureServices(IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
{
    services.AddOptions<SecurityOptions>().Bind(configuration.GetSection(SecurityOptions.SectionName));
    services.AddApplication();
    services.AddInfrastructure(configuration, environment);
    services.AddHttpContextAccessor();
    services.AddScoped<ICurrentUser, HttpCurrentUser>();
    services.AddSingleton<ContentSecurityPolicy>();

    services
        .AddControllers(o => o.Filters.Add<ValidationFilter>())
        .AddJsonOptions(o =>
        {
            o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
            // Unknown properties (e.g. "ownerId", "balance") are rejected instead of silently ignored:
            // DTOs define exactly which fields a client may set.
            o.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            o.JsonSerializerOptions.MaxDepth = 16;
            // Deserialization errors get a generic message instead of one naming internal .NET types.
            o.AllowInputFormatterExceptionMessages = false;
        });
    services.AddProblemDetails(o => o.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Instance = null;
        context.ProblemDetails.Extensions.Remove("exception");
        context.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
    });
    services.AddExceptionHandler<ApiExceptionHandler>();

    AddAuthentication(services);
    services.AddApiRateLimiting(configuration);
    services.AddRequestTimeouts(o => o.DefaultPolicy = new RequestTimeoutPolicy
    {
        Timeout = TimeSpan.FromSeconds(30),
        TimeoutStatusCode = StatusCodes.Status503ServiceUnavailable,
    });
    services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);
    services.AddOpenApi(o => o.AddDocumentTransformer<BearerSecuritySchemeTransformer>());
    ConfigureForwardedHeaders(services, configuration);
}

static void AddAuthentication(IServiceCollection services)
{
    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
    services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
        .Configure<JwtKeyRing, IOptions<JwtOptions>>((options, keys, jwt) =>
        {
            options.MapInboundClaims = false;
            options.IncludeErrorDetails = false;
            options.TokenValidationParameters = keys.CreateValidationParameters(jwt.Value);
            options.Events = new JwtBearerEvents
            {
                // Signature, issuer, audience, lifetime and algorithm are already validated here.
                // Additionally require the session (token family) to be active, so revocation is immediate.
                OnTokenValidated = async context =>
                {
                    var userId = Guid.TryParse(context.Principal?.FindFirst(AppClaims.Subject)?.Value, out var u) ? u : Guid.Empty;
                    var sessionId = Guid.TryParse(context.Principal?.FindFirst(AppClaims.SessionId)?.Value, out var s) ? s : Guid.Empty;
                    var validator = context.HttpContext.RequestServices.GetRequiredService<SessionValidator>();
                    if (userId == Guid.Empty || sessionId == Guid.Empty
                        || !await validator.IsActiveAsync(userId, sessionId, context.HttpContext.RequestAborted))
                    {
                        context.Fail("Session is not active.");
                    }
                },
            };
        });

    // Deny by default: every endpoint requires an authenticated user unless explicitly marked anonymous.
    services.AddAuthorizationBuilder()
        .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
}

static void ConfigureForwardedHeaders(IServiceCollection services, IConfiguration configuration)
{
    var security = configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();
    services.Configure<ForwardedHeadersOptions>(o =>
    {
        // ForwardedHeadersMiddleware trusts EVERY sender when both lists are empty, so the headers are only
        // processed when at least one trusted proxy is configured (see UseForwardedHeaders below).
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.ForwardLimit = 1;
        o.KnownProxies.Clear();
        o.KnownIPNetworks.Clear();
        foreach (var proxy in security.KnownProxies)
        {
            o.KnownProxies.Add(IPAddress.Parse(proxy));
        }

        foreach (var network in security.KnownNetworks)
        {
            o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }
    });
}

static void ConfigurePipeline(WebApplication app)
{
    var security = app.Services.GetRequiredService<IOptions<SecurityOptions>>().Value;

    if (security.KnownProxies.Length > 0 || security.KnownNetworks.Length > 0)
    {
        app.UseForwardedHeaders();
    }
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<RequestLoggingMiddleware>();
    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseSecurityHeaders();
    app.UseMiddleware<RequestBodyLimitMiddleware>();

    app.UseRouting();
    app.UseRequestTimeouts();
    app.UseAuthentication();
    app.UseRateLimiter();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
    if (app.Environment.IsDevelopment() || security.ExposeOpenApi)
    {
        app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
    }

    // Unknown API routes get a JSON 404 instead of the client's index.html.
    app.MapFallback("/api/{**path}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found.")).AllowAnonymous();

    // Blazor WebAssembly client (same origin as the API).
    app.MapStaticAssets().AllowAnonymous();
    app.MapFallbackToFile("index.html").AllowAnonymous();
}

/// <summary>Entry point marker for WebApplicationFactory in tests.</summary>
public partial class Program;
