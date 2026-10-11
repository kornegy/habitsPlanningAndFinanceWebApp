using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PersonalPlatform.Contracts;
using PersonalPlatform.Contracts.Auth;

namespace PersonalPlatform.TestInfrastructure;

public static class Json
{
    /// <summary>Same wire format as the API (camelCase, enums as strings).</summary>
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

/// <summary>A registered, confirmed and signed-in user with their own HTTP client (and cookie jar).</summary>
public sealed class TestUser
{
    private TestUser(ApiFactory factory, HttpClient client, CookieJar cookies, string email, Guid id, AccessTokenResponse login)
    {
        Factory = factory;
        Client = client;
        Cookies = cookies;
        Email = email;
        Id = id;
        UseAccessToken(login.AccessToken);
        SessionLogin = login;
    }

    public ApiFactory Factory { get; }

    public HttpClient Client { get; }

    public CookieJar Cookies { get; }

    public string Email { get; }

    public Guid Id { get; }

    public string AccessToken { get; private set; } = string.Empty;

    public AccessTokenResponse SessionLogin { get; }

    public static async Task<TestUser> CreateAsync(ApiFactory factory, string? email = null, string password = TestData.Password)
    {
        email ??= TestData.UniqueEmail();
        var client = factory.CreateHttpsClient(out var cookies);
        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, password, "Test User", "Europe/Moscow", "ru-RU"), Json.Options)
            .EnsureStatusAsync(HttpStatusCode.Accepted);

        var mail = await factory.Emails.WaitForAsync(email, "/confirm-email#");
        var (userId, token) = CapturingEmailSender.ParseLink(mail, "confirm-email");
        await client.PostAsJsonAsync("/api/v1/auth/confirm-email", new ConfirmEmailRequest(userId, token), Json.Options)
            .EnsureStatusAsync(HttpStatusCode.NoContent);

        var login = await LoginAsync(client, email, password);
        return new TestUser(factory, client, cookies, email, userId, login);
    }

    public static async Task<AccessTokenResponse> LoginAsync(HttpClient client, string email, string password = TestData.Password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password), Json.Options);
        await response.EnsureStatusAsync(HttpStatusCode.OK);
        return await response.ReadJsonAsync<AccessTokenResponse>();
    }

    public void UseAccessToken(string token)
    {
        AccessToken = token;
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>Calls the refresh endpoint with the cookie held by this user's client.</summary>
    public async Task<HttpResponseMessage> RefreshAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add(ApiHeaders.Csrf, ApiHeaders.CsrfValue);
        return await Client.SendAsync(request);
    }

    public Task<HttpResponseMessage> GetAsync(string url) => Client.GetAsync(url);

    public Task<HttpResponseMessage> PostAsync<T>(string url, T body) => Client.PostAsJsonAsync(url, body, Json.Options);

    public Task<HttpResponseMessage> PutAsync<T>(string url, T body) => Client.PutAsJsonAsync(url, body, Json.Options);

    public Task<HttpResponseMessage> DeleteAsync(string url) => Client.DeleteAsync(url);

    public async Task<T> GetJsonAsync<T>(string url)
    {
        var response = await Client.GetAsync(url).EnsureStatusAsync(HttpStatusCode.OK);
        return await response.ReadJsonAsync<T>();
    }

    public async Task<TResponse> PostJsonAsync<TRequest, TResponse>(string url, TRequest body, HttpStatusCode expected = HttpStatusCode.Created)
    {
        var response = await PostAsync(url, body).EnsureStatusAsync(expected);
        return await response.ReadJsonAsync<TResponse>();
    }

    public async Task<TResponse> PutJsonAsync<TRequest, TResponse>(string url, TRequest body)
    {
        var response = await PutAsync(url, body).EnsureStatusAsync(HttpStatusCode.OK);
        return await response.ReadJsonAsync<TResponse>();
    }
}

public static class HttpAssertions
{
    public static async Task<HttpResponseMessage> EnsureStatusAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Expected {(int)expected} {expected} but got {(int)response.StatusCode} {response.StatusCode}: {body}");
        }

        return response;
    }

    public static async Task<HttpResponseMessage> EnsureStatusAsync(this Task<HttpResponseMessage> response, HttpStatusCode expected) =>
        await (await response).EnsureStatusAsync(expected);

    public static async Task<T> ReadJsonAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json.Options))!;

    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
