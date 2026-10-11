using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonalPlatform.Client.Services;

/// <summary>Wire format shared with the API: camelCase properties, enums as strings.</summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
