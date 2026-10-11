namespace PersonalPlatform.Api.Infrastructure;

/// <summary>
/// Container health check without shipping curl/wget in the image: <c>dotnet PersonalPlatform.Api.dll --healthcheck</c>
/// queries the local liveness endpoint and returns a process exit code.
/// </summary>
public static class HealthProbe
{
    public static async Task<int> CheckAsync()
    {
        var port = (Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080").Split([';', ','])[0];
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var response = await client.GetAsync(new Uri($"http://127.0.0.1:{port}/health/live"));
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
    }
}
