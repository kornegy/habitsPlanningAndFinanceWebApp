using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace PersonalPlatform.Infrastructure.Identity;

/// <summary>
/// Opaque high-entropy tokens (refresh tokens). Generated from a CSPRNG; only a SHA-256 digest is persisted.
/// A fast hash is appropriate here because the input has 256 bits of entropy (unlike passwords).
/// </summary>
public static class OpaqueToken
{
    private const int TokenBytes = 32;
    private const int MaxEncodedLength = 64;

    public static string Generate() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    /// <summary>Cheap syntactic check that rejects garbage before touching the database.</summary>
    public static bool IsWellFormed(string? token) =>
        token is { Length: > 0 and <= MaxEncodedLength }
        && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
