using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace PersonalPlatform.Api.Infrastructure;

/// <summary>Documents the bearer access-token scheme in the OpenAPI document.</summary>
public sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Short-lived access token from POST /api/v1/auth/login or /refresh. Keep it in memory only.",
        };
        document.Info.Title = "Personal Platform API";
        document.Info.Version = "v1";
        return Task.CompletedTask;
    }
}
