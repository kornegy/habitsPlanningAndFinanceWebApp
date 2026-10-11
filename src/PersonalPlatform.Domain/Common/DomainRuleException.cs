namespace PersonalPlatform.Domain.Common;

/// <summary>
/// Raised when an operation would violate a business invariant. The <see cref="Code"/> is a stable,
/// machine-readable identifier that is safe to return to clients; the message never contains user data.
/// </summary>
public sealed class DomainRuleException : Exception
{
    public DomainRuleException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public DomainRuleException()
        : this("domain_rule", "A business rule was violated.")
    {
    }

    public DomainRuleException(string message)
        : this("domain_rule", message)
    {
    }

    public DomainRuleException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "domain_rule";
    }

    public string Code { get; }
}
