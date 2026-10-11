using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace PersonalPlatform.TestInfrastructure;

/// <summary>Builds access tokens with deliberately chosen defects for negative authentication tests.</summary>
public static class TokenForge
{
    public static string Create(
        Guid userId,
        Guid sessionId,
        string issuer = ApiFactory.Issuer,
        string audience = ApiFactory.Audience,
        byte[]? key = null,
        string algorithm = SecurityAlgorithms.HmacSha256,
        DateTimeOffset? expires = null,
        DateTimeOffset? notBefore = null,
        IEnumerable<string>? roles = null)
    {
        var now = DateTimeOffset.UtcNow;
        var exp = expires ?? now.AddMinutes(5);
        var nbf = notBefore ?? (exp < now ? exp.AddMinutes(-10) : now);
        var claims = new List<Claim> { new("sub", userId.ToString()), new("sid", sessionId.ToString()) };
        claims.AddRange((roles ?? []).Select(r => new Claim("role", r)));
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = nbf.UtcDateTime,
            NotBefore = nbf.UtcDateTime,
            Expires = exp.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key ?? ApiFactory.SigningKey) { KeyId = ApiFactory.KeyId },
                algorithm),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>An unsigned token ("alg":"none") with otherwise valid claims.</summary>
    public static string Unsigned(Guid userId, Guid sessionId)
    {
        var header = Base64Url("""{"alg":"none","typ":"JWT"}""");
        var exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
        var payload = Base64Url($$"""{"sub":"{{userId}}","sid":"{{sessionId}}","iss":"{{ApiFactory.Issuer}}","aud":"{{ApiFactory.Audience}}","exp":{{exp}}}""");
        return $"{header}.{payload}.";
    }

    /// <summary>Reads the session id from a token issued by the API.</summary>
    public static Guid SessionIdOf(string accessToken) =>
        Guid.Parse(new JsonWebToken(accessToken).GetClaim("sid").Value);

    private static string Base64Url(string json) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(json));
}
