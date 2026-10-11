namespace PersonalPlatform.Api.Security;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>
    /// Origins (scheme://host[:port]) from which the browser app is served. When set, cookie-authenticated
    /// endpoints accept only these origins. When empty, the request's own origin is required.
    /// The API never enables CORS: the client is served from the same origin.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>IP addresses of reverse proxies whose X-Forwarded-For/Proto headers are trusted.</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>CIDR networks of trusted reverse proxies (e.g. "10.0.0.0/8").</summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>Send Strict-Transport-Security. Enable only once HTTPS is correctly configured for the host.</summary>
    public bool EnableHsts { get; set; }

    /// <summary>Expose the OpenAPI document (always on in Development).</summary>
    public bool ExposeOpenApi { get; set; }
}
