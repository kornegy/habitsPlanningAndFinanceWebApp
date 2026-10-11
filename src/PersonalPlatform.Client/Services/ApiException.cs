using System.Net;
using System.Text.Json;

namespace PersonalPlatform.Client.Services;

/// <summary>An API call failed. Carries the server's stable error code and field errors, never raw exception text.</summary>
public sealed class ApiException : Exception
{
    public ApiException(HttpStatusCode status, string? code, string message, IReadOnlyDictionary<string, string[]>? errors = null, JsonElement? details = null)
        : base(message)
    {
        Status = status;
        Code = code;
        Errors = errors ?? new Dictionary<string, string[]>();
        Details = details;
    }

    public ApiException()
        : this(HttpStatusCode.InternalServerError, null, "Request failed.")
    {
    }

    public ApiException(string message)
        : this(HttpStatusCode.InternalServerError, null, message)
    {
    }

    public ApiException(string message, Exception innerException)
        : base(message, innerException)
    {
        Status = HttpStatusCode.ServiceUnavailable;
        Errors = new Dictionary<string, string[]>();
    }

    public HttpStatusCode Status { get; }

    public string? Code { get; }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public JsonElement? Details { get; }

    public bool IsConflict => Status == HttpStatusCode.Conflict;

    public bool IsNotFound => Status == HttpStatusCode.NotFound;

    public bool IsNetworkError => Status == HttpStatusCode.ServiceUnavailable && Code is null;

    internal static async Task<ApiException> FromResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string? code = null;
        var message = response.ReasonPhrase ?? "Request failed.";
        Dictionary<string, string[]>? errors = null;
        JsonElement? details = null;
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (text.Length > 0)
            {
                using var json = JsonDocument.Parse(text);
                var root = json.RootElement;
                code = root.TryGetProperty("code", out var c) ? c.GetString() : null;
                message = root.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()!
                    : root.TryGetProperty("title", out var t) ? t.GetString() ?? message : message;
                if (root.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Object)
                {
                    errors = e.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(v => v.GetString() ?? string.Empty).ToArray());
                }

                if (root.TryGetProperty("details", out var det))
                {
                    details = det.Clone();
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body (e.g. from a proxy); keep the generic message.
        }

        return new ApiException(response.StatusCode, code, message, errors, details);
    }
}
