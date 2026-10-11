namespace PersonalPlatform.Contracts;

/// <summary>A page of results. <see cref="TotalCount"/> counts all matching items.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public static class Paging
{
    public const int DefaultPageSize = 25;

    /// <summary>Hard upper bound enforced by the server; larger requests are rejected with 400.</summary>
    public const int MaxPageSize = 100;

    public const int MaxPage = 10_000;
}

public static class QueryLimits
{
    public const int SearchMaxLength = 100;

    /// <summary>Largest date range accepted by list and report endpoints.</summary>
    public const int MaxRangeDays = 366;

    /// <summary>Largest date range accepted by the planner.</summary>
    public const int MaxPlannerRangeDays = 42;

    /// <summary>Largest number of objects a bulk operation may touch.</summary>
    public const int MaxBulkItems = 100;
}

/// <summary>Well-known HTTP header names used by the API.</summary>
public static class ApiHeaders
{
    public const string IdempotencyKey = "Idempotency-Key";

    /// <summary>
    /// Anti-CSRF header required on cookie-authenticated endpoints. Browsers cannot attach custom headers to
    /// cross-site requests without a CORS preflight, which the API never approves.
    /// </summary>
    public const string Csrf = "X-CSRF";

    public const string CsrfValue = "1";

    public const string CorrelationId = "X-Correlation-Id";
}

public sealed record IdName(Guid Id, string Name);

public sealed record CurrencyAmount(string CurrencyCode, decimal Amount);
