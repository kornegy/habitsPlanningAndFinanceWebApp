namespace PersonalPlatform.Contracts.Admin;

/// <summary>Aggregate, non-personal operational counters available to administrators only.</summary>
public sealed record AdminStatsDto(int Users, int ConfirmedUsers, int ActiveSessions, DateTimeOffset GeneratedAt);

public static class Roles
{
    public const string Admin = "admin";
}
