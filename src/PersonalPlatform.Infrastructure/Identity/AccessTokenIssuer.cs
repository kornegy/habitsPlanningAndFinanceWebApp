using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace PersonalPlatform.Infrastructure.Identity;

public static class AppClaims
{
    public const string Subject = "sub";
    public const string SessionId = "sid";
    public const string Role = "role";
}

/// <summary>Creates short-lived access JWTs carrying only the minimum claims (subject, session, roles).</summary>
public sealed class AccessTokenIssuer(JwtKeyRing keys, IOptions<JwtOptions> options, TimeProvider time)
{
    public (string Token, DateTimeOffset ExpiresAt) Issue(Guid userId, Guid sessionId, IEnumerable<string> roles)
    {
        var jwt = options.Value;
        var now = time.GetUtcNow();
        var expires = now.AddMinutes(jwt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(AppClaims.Subject, userId.ToString()),
            new(AppClaims.SessionId, sessionId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        claims.AddRange(roles.Select(role => new Claim(AppClaims.Role, role)));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(keys.SigningKey, JwtKeyRing.Algorithm),
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}
