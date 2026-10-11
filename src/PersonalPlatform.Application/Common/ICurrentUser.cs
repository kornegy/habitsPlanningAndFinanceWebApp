namespace PersonalPlatform.Application.Common;

/// <summary>
/// The authenticated caller, resolved on the server from the validated access token only.
/// Request bodies, query strings and headers are never used as a source of identity.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>The caller's user id. Throws when there is no authenticated user (fail closed).</summary>
    Guid UserId { get; }

    /// <summary>Id of the session (refresh-token family) the access token belongs to.</summary>
    Guid? SessionId { get; }
}
