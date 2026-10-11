using System.Net;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using PersonalPlatform.Api.Infrastructure;
using PersonalPlatform.Api.Security;
using PersonalPlatform.Application.Auth;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Domain.Common;
using PersonalPlatform.Infrastructure.Identity;

namespace PersonalPlatform.UnitTests.Security;

public sealed class CsrfTests
{
    [Fact]
    public void Missing_or_wrong_csrf_header_is_rejected()
    {
        Assert.False(RequireCsrfHeaderAttribute.IsSameOriginRequest(Request().Request));
        Assert.False(RequireCsrfHeaderAttribute.IsSameOriginRequest(Request(("X-CSRF", "0")).Request));
    }

    [Fact]
    public void Same_origin_requests_with_header_are_accepted()
    {
        Assert.True(RequireCsrfHeaderAttribute.IsSameOriginRequest(Request(("X-CSRF", "1")).Request));
        Assert.True(RequireCsrfHeaderAttribute.IsSameOriginRequest(Request(("X-CSRF", "1"), ("Sec-Fetch-Site", "same-origin"), ("Origin", "https://app.example.test")).Request));
    }

    [Theory]
    [InlineData("cross-site", null)]
    [InlineData("same-site", null)]
    [InlineData(null, "https://evil.example.test")]
    [InlineData(null, "http://app.example.test")] // scheme downgrade
    [InlineData(null, "null")]
    public void Cross_origin_signals_are_rejected(string? fetchSite, string? origin)
    {
        var headers = new List<(string, string)> { ("X-CSRF", "1") };
        if (fetchSite is not null)
        {
            headers.Add(("Sec-Fetch-Site", fetchSite));
        }

        if (origin is not null)
        {
            headers.Add(("Origin", origin));
        }

        Assert.False(RequireCsrfHeaderAttribute.IsSameOriginRequest(Request([.. headers]).Request));
    }

    [Fact]
    public void Configured_allowed_origins_replace_the_host_check()
    {
        var context = Request(["https://app.example.test"], ("X-CSRF", "1"), ("Origin", "https://www.example.test"));
        Assert.False(RequireCsrfHeaderAttribute.IsSameOriginRequest(context.Request));

        context = Request(["https://www.example.test"], ("X-CSRF", "1"), ("Origin", "https://www.example.test"));
        Assert.True(RequireCsrfHeaderAttribute.IsSameOriginRequest(context.Request));
    }

    private static DefaultHttpContext Request(params (string Name, string Value)[] headers) => Request([], headers);

    private static DefaultHttpContext Request(string[] allowedOrigins, params (string Name, string Value)[] headers)
    {
        var services = new ServiceCollection()
            .AddSingleton(Options.Create(new SecurityOptions { AllowedOrigins = allowedOrigins }))
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("app.example.test");
        foreach (var (name, value) in headers)
        {
            context.Request.Headers[name] = value;
        }

        return context;
    }
}

public sealed class RateLimitKeyTests
{
    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2::1", "2001:db8:1:2::/64")]
    public void Client_keys_group_ipv6_by_64_prefix(string address, string expected)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);

        Assert.Equal(expected, RateLimiting.ClientKey(context));
    }

    [Fact]
    public void Forwarded_for_header_alone_does_not_change_the_key()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.1");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.99";

        Assert.Equal("198.51.100.1", RateLimiting.ClientKey(context));
    }
}

public sealed class TokenAndKeyTests
{
    [Fact]
    public void Opaque_tokens_are_unique_url_safe_and_256_bit()
    {
        var tokens = Enumerable.Range(0, 1000).Select(_ => OpaqueToken.Generate()).ToList();

        Assert.Equal(tokens.Count, tokens.Distinct(StringComparer.Ordinal).Count());
        Assert.All(tokens, t =>
        {
            Assert.Equal(43, t.Length); // 32 bytes, base64url without padding
            Assert.True(OpaqueToken.IsWellFormed(t));
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("has spaces")]
    [InlineData("a.b.c")]
    [InlineData("' OR 1=1 --")]
    public void Malformed_tokens_are_rejected_without_database_access(string? token) =>
        Assert.False(OpaqueToken.IsWellFormed(token));

    [Fact]
    public void Overlong_tokens_are_rejected() => Assert.False(OpaqueToken.IsWellFormed(new string('a', 65)));

    [Fact]
    public void Only_the_hash_of_a_token_is_derived_and_it_is_deterministic()
    {
        var token = OpaqueToken.Generate();

        Assert.Equal(OpaqueToken.Hash(token), OpaqueToken.Hash(token));
        Assert.Equal(32, OpaqueToken.Hash(token).Length);
        Assert.NotEqual(OpaqueToken.Hash(token), OpaqueToken.Hash(token + "x"));
    }

    [Fact]
    public void Key_ring_rejects_short_invalid_or_missing_keys()
    {
        Assert.Throws<InvalidOperationException>(() => Ring([]));
        Assert.Throws<InvalidOperationException>(() => Ring([("k1", Convert.ToBase64String(new byte[16]))]));
        Assert.Throws<InvalidOperationException>(() => Ring([("k1", "not base64!")]));
        Assert.Throws<InvalidOperationException>(() => Ring([("k1", Key())], active: "missing"));
    }

    [Fact]
    public void Key_ring_signs_with_the_active_key_and_validates_with_all()
    {
        var ring = Ring([("old", Key()), ("new", Key())], active: "new");
        var parameters = ring.CreateValidationParameters(new JwtOptions());

        Assert.Equal("new", ring.SigningKey.KeyId);
        Assert.Equal(2, ring.ValidationKeys.Count);
        Assert.Equal([JwtKeyRing.Algorithm], parameters.ValidAlgorithms);
        Assert.True(parameters.RequireSignedTokens);
        Assert.True(parameters.ValidateLifetime);
        Assert.True(parameters.ValidateAudience);
        Assert.True(parameters.ValidateIssuer);
    }

    private static string Key() => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    private static JwtKeyRing Ring((string Id, string Key)[] keys, string active = "") =>
        new(Options.Create(new JwtOptions
        {
            ActiveKeyId = active,
            SigningKeys = [.. keys.Select(k => new JwtSigningKeyOptions { KeyId = k.Id, Key = k.Key })],
        }));
}

public sealed class ErrorMappingTests
{
    [Fact]
    public void Unexpected_exceptions_never_expose_their_message()
    {
        var problem = ApiExceptionHandler.Map(new InvalidOperationException("Npgsql: password authentication failed for user pp_app at db:5432"));

        Assert.Equal(500, problem.Status);
        Assert.Equal("internal_error", problem.Extensions["code"]);
        Assert.DoesNotContain("pp_app", problem.Title + problem.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("invalid_credentials", 401)]
    [InlineData("invalid_refresh_token", 401)]
    [InlineData("email_not_confirmed", 403)]
    [InlineData("invalid_token", 400)]
    public void Authentication_failures_map_to_neutral_statuses(string code, int status)
    {
        var problem = ApiExceptionHandler.Map(new AuthFailedException(code));

        Assert.Equal(status, problem.Status);
        Assert.Equal(code, problem.Extensions["code"]);
    }

    [Fact]
    public void Known_exceptions_map_to_stable_codes()
    {
        Assert.Equal(422, ApiExceptionHandler.Map(new DomainRuleException("invalid_interval", "x")).Status);
        Assert.Equal(404, ApiExceptionHandler.Map(new NotFoundException()).Status);
        Assert.Equal("duplicate", ApiExceptionHandler.Map(UniqueConstraintException.ForConstraint("ix_tags_owner_id_normalized_name", new InvalidOperationException("23505"))).Extensions["code"]);

        var validation = ApiExceptionHandler.Map(new ValidationException([new ValidationFailure("Amount", "bad") { ErrorCode = "GreaterThanValidator" }]));
        var details = Assert.IsType<ValidationProblemDetails>(validation);
        Assert.Equal(400, details.Status);
        Assert.Equal(["bad"], details.Errors["Amount"]);
    }
}

public sealed class ContentSecurityPolicyTests
{
    [Fact]
    public void Only_inline_scripts_are_hashed_and_external_ones_ignored()
    {
        const string html = """
            <html><head>
            <script type="importmap">{"imports":{}}</script>
            <script src="_framework/blazor.webassembly.js"></script>
            </head></html>
            """;

        var hashes = ContentSecurityPolicy.InlineScriptHashes(new MemoryFile(html));

        var hash = Assert.Single(hashes);
        Assert.Equal(Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData("{\"imports\":{}}"u8)), hash);
    }

    [Fact]
    public void Missing_host_page_yields_no_hashes() =>
        Assert.Empty(ContentSecurityPolicy.InlineScriptHashes(new NotFoundFileInfo("index.html")));

    [Fact]
    public void Api_policy_denies_everything() =>
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", ContentSecurityPolicy.Api);

    private sealed class MemoryFile(string content) : IFileInfo
    {
        private readonly byte[] _bytes = System.Text.Encoding.UTF8.GetBytes(content);

        public bool Exists => true;

        public long Length => _bytes.Length;

        public string? PhysicalPath => null;

        public string Name => "index.html";

        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public bool IsDirectory => false;

        public Stream CreateReadStream() => new MemoryStream(_bytes);
    }
}
