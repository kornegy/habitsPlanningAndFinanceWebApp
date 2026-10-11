using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PersonalPlatform.Application.Common;

namespace PersonalPlatform.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c> to create migrations and bundles. When a database connection is needed, the
/// connection string comes from the file named by PP_MIGRATIONS_CONNECTION_FILE (e.g. a Docker secret) or from
/// the PP_MIGRATIONS_CONNECTION environment variable; creating a migration does not connect.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PP_MIGRATIONS_CONNECTION_FILE") is { Length: > 0 } file
            ? File.ReadAllText(file).TrimEnd('\r', '\n')
            : Environment.GetEnvironmentVariable("PP_MIGRATIONS_CONNECTION")
                ?? "Host=localhost;Database=personal_platform_design;Username=design";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, o => o.MigrationsHistoryTable("__ef_migrations_history", AppDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options, new NoUser());
    }

    private sealed class NoUser : ICurrentUser
    {
        public bool IsAuthenticated => false;

        public Guid UserId => throw new InvalidOperationException("No user at design time.");

        public Guid? SessionId => null;
    }
}
