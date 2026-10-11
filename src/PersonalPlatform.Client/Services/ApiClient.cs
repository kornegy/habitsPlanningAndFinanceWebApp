using System.Net.Http.Json;
using PersonalPlatform.Contracts;

namespace PersonalPlatform.Client.Services;

/// <summary>Typed HTTP helpers. Non-success responses become <see cref="ApiException"/> with the server's problem details.</summary>
public sealed class ApiClient(HttpClient http)
{
    public async Task<T> GetAsync<T>(string url, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(() => http.GetAsync(url, cancellationToken), cancellationToken);
        return (await response.Content.ReadFromJsonAsync<T>(ApiJson.Options, cancellationToken))!;
    }

    public async Task<byte[]> GetBytesAsync(string url, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(() => http.GetAsync(url, cancellationToken), cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<TResponse> PostAsync<TResponse>(string url, object? body, CancellationToken cancellationToken = default, string? idempotencyKey = null)
    {
        using var response = await SendAsync(() => http.SendAsync(Request(HttpMethod.Post, url, body, idempotencyKey), cancellationToken), cancellationToken);
        return (await response.Content.ReadFromJsonAsync<TResponse>(ApiJson.Options, cancellationToken))!;
    }

    public async Task PostAsync(string url, object? body, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(() => http.SendAsync(Request(HttpMethod.Post, url, body, null), cancellationToken), cancellationToken);
    }

    public async Task<TResponse> PutAsync<TResponse>(string url, object body, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(() => http.SendAsync(Request(HttpMethod.Put, url, body, null), cancellationToken), cancellationToken);
        return (await response.Content.ReadFromJsonAsync<TResponse>(ApiJson.Options, cancellationToken))!;
    }

    public async Task<TResponse> DeleteAsync<TResponse>(string url, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(() => http.DeleteAsync(url, cancellationToken), cancellationToken);
        return (await response.Content.ReadFromJsonAsync<TResponse>(ApiJson.Options, cancellationToken))!;
    }

    public async Task DeleteAsync(string url, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(() => http.DeleteAsync(url, cancellationToken), cancellationToken);
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, object? body, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body ?? new { }, options: ApiJson.Options) };
        if (idempotencyKey is not null)
        {
            request.Headers.Add(ApiHeaders.IdempotencyKey, idempotencyKey);
        }

        return request;
    }

    private static async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await send();
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException("The server could not be reached. Check your connection and try again.", ex);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            throw await ApiException.FromResponseAsync(response, cancellationToken);
        }
    }
}

public static class Query
{
    /// <summary>ISO date for URLs and route values.</summary>
    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Builds a query string from non-null values (invariant formatting, URL-encoded).</summary>
    public static string Of(params (string Key, object? Value)[] parameters)
    {
        var parts = parameters
            .Where(p => p.Value is not null && !(p.Value is string s && string.IsNullOrWhiteSpace(s)))
            .Select(p => $"{p.Key}={Uri.EscapeDataString(Format(p.Value!))}");
        var query = string.Join('&', parts);
        return query.Length == 0 ? string.Empty : "?" + query;
    }

    private static string Format(object value) => value switch
    {
        DateOnly d => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
