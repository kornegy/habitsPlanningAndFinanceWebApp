using Microsoft.EntityFrameworkCore;
using Npgsql;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PersonalPlatform.TestInfrastructure;

/// <summary>
/// Disposable PostgreSQL 17 in Docker, prepared exactly like production: the least-privilege roles from
/// <c>deploy/postgres/roles.sql</c> are created, migrations run as the schema owner, and the application
/// connects as the restricted <c>pp_app</c> role.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string DatabaseName = "personal_platform";
    private const string OwnerPassword = "test-owner-password";
    private const string AppPassword = "test-app-password";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase(DatabaseName)
        .WithUsername("postgres")
        .WithPassword("test-superuser-password")
        .Build();

    public string SuperuserConnectionString => _container.GetConnectionString();

    public string OwnerConnectionString => WithCredentials("pp_owner", OwnerPassword);

    public string AppConnectionString => WithCredentials("pp_app", AppPassword);

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await CreateRolesAsync();
        await using var db = CreateOwnerContext();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>A context connected as the migration owner, bypassing owner filters (test assertions only).</summary>
    public AppDbContext CreateOwnerContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(OwnerConnectionString, o => o.MigrationsHistoryTable("__ef_migrations_history", AppDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options, new FixedUser(null));
    }

    public async Task<T> QueryScalarAsync<T>(string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        var result = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(result!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    private string WithCredentials(string user, string password) =>
        new NpgsqlConnectionStringBuilder(SuperuserConnectionString) { Username = user, Password = password }.ConnectionString;

    private async Task CreateRolesAsync()
    {
        // roles.sql is written for psql variables; substitute them with literals for the test database.
        var sql = (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "roles.sql")))
            .Replace(":'owner_password'", $"'{OwnerPassword}'", StringComparison.Ordinal)
            .Replace(":'app_password'", $"'{AppPassword}'", StringComparison.Ordinal)
            .Replace(":\"db_name\"", $"\"{DatabaseName}\"", StringComparison.Ordinal);
        await using var connection = new NpgsqlConnection(SuperuserConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Current-user stub for direct database contexts in tests.</summary>
    public sealed class FixedUser(Guid? userId) : ICurrentUser
    {
        public bool IsAuthenticated => userId is not null;

        public Guid UserId => userId ?? throw new InvalidOperationException("No user.");

        public Guid? SessionId => null;
    }
}
