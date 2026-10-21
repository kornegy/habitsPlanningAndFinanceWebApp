using System.Reflection;
using System.Text.Json;
using PersonalPlatform.Client.Services;
using PersonalPlatform.Contracts;
using PersonalPlatform.Contracts.Auth;
using PersonalPlatform.Contracts.Finance;
using PersonalPlatform.Contracts.Tasks;

namespace PersonalPlatform.UnitTests.Client;

/// <summary>
/// The published client is trimmed, which strips the metadata reflection-based JSON needs for records. These tests
/// make sure the client can serialize every contract from compile-time metadata alone.
/// </summary>
public sealed class ClientJsonContextTests
{
    public static TheoryData<Type> ContractTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var type in typeof(PagedResult<>).Assembly.GetExportedTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false } && !(t.IsAbstract && t.IsSealed)))
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ContractTypes))]
    public void Every_contract_type_has_source_generated_metadata(Type type) =>
        Assert.NotNull(ClientJsonContext.Default.GetTypeInfo(type));

    [Fact]
    public void Records_round_trip_without_reflection_metadata()
    {
        // Only the generated context: if a type were missing or needed reflection, this would throw.
        var options = new JsonSerializerOptions(ClientJsonContext.Default.Options) { TypeInfoResolver = ClientJsonContext.Default };
        var register = new RegisterRequest("user@example.test", "correct horse battery staple", "Анна", "Europe/Moscow", "ru-RU");
        var transaction = new CreateTransactionRequest(TransactionKind.Transfer, new DateOnly(2026, 10, 5), null, Guid.NewGuid(), 100.25m, null, Guid.NewGuid(), null);

        var registerJson = JsonSerializer.Serialize(register, options);
        var transactionJson = JsonSerializer.Serialize(transaction, options);

        Assert.Contains("\"email\":\"user@example.test\"", registerJson, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"Transfer\"", transactionJson, StringComparison.Ordinal);
        Assert.Equal(register, JsonSerializer.Deserialize<RegisterRequest>(registerJson, options));
        Assert.Equal(transaction, JsonSerializer.Deserialize<CreateTransactionRequest>(transactionJson, options));
    }

    [Fact]
    public void Paged_responses_used_by_the_client_deserialize()
    {
        const string json = """{"items":[{"id":"0195f3a0-0000-7000-8000-000000000001","title":"Task","description":null,"status":"Open","priority":"High","categoryId":null,"dueDate":"2026-10-21","dueTime":null,"reminderAt":null,"completedAt":null,"isOverdue":false,"seriesId":null,"occurrenceDate":null,"tags":[],"subtasks":[],"createdAt":"2026-10-21T10:00:00Z","updatedAt":"2026-10-21T10:00:00Z","version":1}],"page":1,"pageSize":50,"totalCount":1}""";

        var page = JsonSerializer.Deserialize(json, ClientJsonContext.Default.PagedResultTaskDto);

        Assert.NotNull(page);
        Assert.Equal(TaskPriority.High, Assert.Single(page.Items).Priority);
    }

    [Fact]
    public void Client_options_prefer_generated_metadata()
    {
        var info = ApiJson.Options.GetTypeInfo(typeof(RegisterRequest));

        Assert.NotNull(info);
        Assert.NotNull(typeof(ClientJsonContext).GetProperty(nameof(ClientJsonContext.RegisterRequest), BindingFlags.Public | BindingFlags.Instance));
    }
}
