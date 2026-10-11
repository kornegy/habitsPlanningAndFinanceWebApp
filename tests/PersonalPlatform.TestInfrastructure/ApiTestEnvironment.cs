using Xunit;

namespace PersonalPlatform.TestInfrastructure;

/// <summary>
/// One PostgreSQL container and one API host per test assembly. Tests isolate themselves by creating their
/// own users (every user's data is invisible to every other user), so no database reset is needed.
/// </summary>
public sealed class ApiTestEnvironment : IAsyncLifetime
{
    public PostgresFixture Database { get; } = new();

    public ApiFactory Api { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await Database.InitializeAsync();
        Api = new ApiFactory(Database);
        _ = Api.Server; // start the host now so the first test does not pay for it
    }

    public async ValueTask DisposeAsync()
    {
        await Api.DisposeAsync();
        await Database.DisposeAsync();
    }

    public Task<TestUser> CreateUserAsync(string? email = null) => TestUser.CreateAsync(Api, email);
}
