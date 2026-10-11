namespace PersonalPlatform.Application.Common;

/// <summary>
/// The requested object does not exist <em>or belongs to another user</em>. Both cases produce the same
/// 404 response so that object ids of other users cannot be probed.
/// </summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException()
        : base("The requested resource was not found.")
    {
    }

    public NotFoundException(string message)
        : base(message)
    {
    }

    public NotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The request conflicts with the current state (duplicate, stale version, dependent data).</summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public ConflictException()
        : this("conflict", "The request conflicts with the current state.")
    {
    }

    public ConflictException(string message)
        : this("conflict", message)
    {
    }

    public ConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "conflict";
    }

    public string Code { get; }

    public static ConflictException StaleVersion() =>
        new("concurrency_conflict", "The item was changed by another request. Reload it and try again.");
}

/// <summary>Raised by the persistence layer when a unique constraint is violated.</summary>
public sealed class UniqueConstraintException : Exception
{
    public static UniqueConstraintException ForConstraint(string? constraintName, Exception innerException) =>
        new("A unique constraint was violated.", innerException) { ConstraintName = constraintName };

    public UniqueConstraintException()
        : base("A unique constraint was violated.")
    {
    }

    public UniqueConstraintException(string message)
        : base(message)
    {
    }

    public UniqueConstraintException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string? ConstraintName { get; private init; }
}
