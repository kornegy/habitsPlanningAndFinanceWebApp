using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PersonalPlatform.Client.Services;

/// <summary>Wire format shared with the API: camelCase properties, enums as strings.</summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            // Source-generated metadata first (survives trimming); reflection only for incidental shapes such as
            // the empty body object, which have no constructor parameters.
            TypeInfoResolver = JsonTypeInfoResolver.Combine(ClientJsonContext.Default, new DefaultJsonTypeInfoResolver()),
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
