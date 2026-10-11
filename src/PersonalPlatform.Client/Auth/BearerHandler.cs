using System.Net;
using System.Net.Http.Headers;

namespace PersonalPlatform.Client.Auth;

/// <summary>
/// Attaches the in-memory access token to API calls. On 401 it refreshes once and replays the request;
/// if that fails the session is cleared and the UI returns to the sign-in page.
/// </summary>
public sealed class BearerHandler(AuthSession session) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsolutePath.StartsWith("/api/v1/auth/", StringComparison.Ordinal) == true)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var token = await session.GetAccessTokenAsync();
        byte[]? body = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        var response = await base.SendAsync(WithToken(request, token, body), cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized || token is null)
        {
            return response;
        }

        response.Dispose();
        if (!await session.RefreshAsync(token))
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized) { RequestMessage = request };
        }

        return await base.SendAsync(WithToken(Clone(request, body), await session.GetAccessTokenAsync(), body), cancellationToken);
    }

    private static HttpRequestMessage WithToken(HttpRequestMessage request, string? token, byte[]? body)
    {
        if (body is not null && request.Content is not null)
        {
            var headers = request.Content.Headers.ToList();
            request.Content = new ByteArrayContent(body);
            foreach (var header in headers)
            {
                request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        request.Headers.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static HttpRequestMessage Clone(HttpRequestMessage original, byte[]? body)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);
        foreach (var header in original.Headers.Where(h => h.Key != "Authorization"))
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            foreach (var header in original.Content!.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
