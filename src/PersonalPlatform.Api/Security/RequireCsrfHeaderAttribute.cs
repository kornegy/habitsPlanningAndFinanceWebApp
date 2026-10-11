using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using PersonalPlatform.Contracts;

namespace PersonalPlatform.Api.Security;

/// <summary>
/// CSRF protection for endpoints authenticated by the refresh cookie (refresh, logout).
/// </summary>
/// <remarks>
/// Layers: (1) SameSite=Strict cookie; (2) a mandatory custom header, which a cross-site page cannot send
/// without a CORS preflight that this API never grants; (3) when the browser supplies <c>Origin</c> or
/// <c>Sec-Fetch-Site</c>, they must indicate a same-origin request.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireCsrfHeaderAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;
        if (!IsSameOriginRequest(request))
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Cross-site request rejected.",
                Extensions = { ["code"] = "csrf_rejected" },
            })
            { StatusCode = StatusCodes.Status403Forbidden };
        }
    }

    internal static bool IsSameOriginRequest(HttpRequest request)
    {
        if (request.Headers[ApiHeaders.Csrf] != ApiHeaders.CsrfValue)
        {
            return false;
        }

        var fetchSite = request.Headers["Sec-Fetch-Site"].ToString();
        if (fetchSite.Length > 0 && fetchSite is not ("same-origin" or "none"))
        {
            return false;
        }

        var origin = request.Headers.Origin.ToString();
        if (origin.Length == 0)
        {
            return true;
        }

        var allowed = request.HttpContext.RequestServices.GetRequiredService<IOptions<SecurityOptions>>().Value.AllowedOrigins;
        if (allowed.Length > 0)
        {
            return allowed.Contains(origin, StringComparer.OrdinalIgnoreCase);
        }

        var expected = $"{request.Scheme}://{request.Host.Value}";
        return string.Equals(origin, expected, StringComparison.OrdinalIgnoreCase);
    }
}
