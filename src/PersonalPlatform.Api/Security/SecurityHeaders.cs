using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace PersonalPlatform.Api.Security;

/// <summary>
/// Content-Security-Policy for the Blazor host page. Blazor WebAssembly needs <c>'wasm-unsafe-eval'</c> to
/// compile WebAssembly; <c>'unsafe-eval'</c> and <c>'unsafe-inline'</c> are never allowed. The only inline
/// script is the build-generated import map, allowed by its SHA-256 hash computed from the served file.
/// </summary>
public sealed partial class ContentSecurityPolicy
{
    public ContentSecurityPolicy(IWebHostEnvironment environment)
    {
        var hashes = InlineScriptHashes(environment.WebRootFileProvider.GetFileInfo("index.html"));
        var scriptSources = string.Join(' ', new[] { "'self'", "'wasm-unsafe-eval'" }.Concat(hashes.Select(h => $"'sha256-{h}'")));
        HostPage =
            "default-src 'self'; " +
            $"script-src {scriptSources}; " +
            "style-src 'self'; " +
            "img-src 'self' data:; " +
            "font-src 'self'; " +
            "connect-src 'self'; " +
            "manifest-src 'self'; " +
            "worker-src 'self'; " +
            "object-src 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self'; " +
            "frame-ancestors 'none'";
    }

    /// <summary>Policy for the HTML host page and static assets.</summary>
    public string HostPage { get; }

    /// <summary>Policy for API responses: they are data, never documents.</summary>
    public static string Api => "default-src 'none'; frame-ancestors 'none'";

    internal static IReadOnlyList<string> InlineScriptHashes(IFileInfo file)
    {
        if (!file.Exists)
        {
            return [];
        }

        using var stream = file.CreateReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var html = reader.ReadToEnd();
        return InlineScript().Matches(html)
            .Select(m => m.Groups["body"].Value)
            .Where(body => body.Length > 0)
            .Select(body => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body))))
            .Distinct()
            .ToList();
    }

    [GeneratedRegex(@"<script(?![^>]*\bsrc=)[^>]*>(?<body>[\s\S]*?)</script>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineScript();
}

public static class SecurityHeadersExtensions
{
    /// <summary>Adds browser security headers to every response and disables caching of API responses.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        var csp = app.ApplicationServices.GetRequiredService<ContentSecurityPolicy>();
        var options = app.ApplicationServices.GetRequiredService<IOptions<SecurityOptions>>().Value;

        return app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                var isApi = context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
                headers.ContentSecurityPolicy = isApi ? ContentSecurityPolicy.Api : csp.HostPage;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY"; // legacy browsers; CSP frame-ancestors is authoritative
                headers["Referrer-Policy"] = "no-referrer";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers["Cross-Origin-Resource-Policy"] = "same-origin";
                // Everything is served from this origin, so requiring CORP for embedded resources costs nothing and
                // isolates the page from cross-origin resources.
                headers["Cross-Origin-Embedder-Policy"] = "require-corp";
                if (isApi)
                {
                    // Personal and financial data must never be stored by browsers or shared caches.
                    headers.CacheControl = "no-store";
                    headers.Pragma = "no-cache";
                }

                if (options.EnableHsts && context.Request.IsHttps)
                {
                    headers.StrictTransportSecurity = "max-age=31536000; includeSubDomains";
                }

                headers.Remove("Server");
                headers.Remove("X-Powered-By");
                return Task.CompletedTask;
            });

            await next();
        });
    }
}
