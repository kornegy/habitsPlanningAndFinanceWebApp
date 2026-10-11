using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace PersonalPlatform.Infrastructure.Identity;

/// <summary>
/// Signing keys for access tokens. All configured keys validate; only the active key signs. Rotation:
/// add a new key, make it active, wait at least one access-token lifetime, then remove the old key.
/// </summary>
public sealed class JwtKeyRing
{
    /// <summary>The only accepted algorithm; tokens declaring any other "alg" (including "none") are rejected.</summary>
    public const string Algorithm = SecurityAlgorithms.HmacSha256;

    private const int MinimumKeyBytes = 32;

    public JwtKeyRing(IOptions<JwtOptions> options)
    {
        var jwt = options.Value;
        if (jwt.SigningKeys.Count == 0)
        {
            throw new InvalidOperationException("No JWT signing keys are configured (Jwt:SigningKeys).");
        }

        var keys = new List<SymmetricSecurityKey>();
        foreach (var configured in jwt.SigningKeys)
        {
            byte[] material;
            try
            {
                material = Convert.FromBase64String(configured.Key);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException($"JWT signing key '{configured.KeyId}' is not valid base64.");
            }

            if (material.Length < MinimumKeyBytes)
            {
                throw new InvalidOperationException($"JWT signing key '{configured.KeyId}' must be at least 256 bits.");
            }

            keys.Add(new SymmetricSecurityKey(material) { KeyId = configured.KeyId });
        }

        ValidationKeys = keys;
        var activeId = string.IsNullOrEmpty(jwt.ActiveKeyId) ? keys[0].KeyId : jwt.ActiveKeyId;
        SigningKey = keys.SingleOrDefault(k => k.KeyId == activeId)
            ?? throw new InvalidOperationException("Jwt:ActiveKeyId does not match any configured signing key.");
    }

    public SymmetricSecurityKey SigningKey { get; }

    public IReadOnlyList<SymmetricSecurityKey> ValidationKeys { get; }

    /// <summary>Validation rules shared by the API's bearer handler and the tests.</summary>
    public TokenValidationParameters CreateValidationParameters(JwtOptions options) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = options.Issuer,
        ValidateAudience = true,
        ValidAudience = options.Audience,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKeys = ValidationKeys,
        ValidAlgorithms = [Algorithm],
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = "sub",
        RoleClaimType = "role",
    };
}
