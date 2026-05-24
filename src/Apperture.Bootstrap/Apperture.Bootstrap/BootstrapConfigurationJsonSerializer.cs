using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apperture.Bootstrap;

/// <summary>
/// Shared JSON options for bootstrap configuration persistence (operator-inspectable PascalCase).
/// </summary>
public static class BootstrapConfigurationJsonSerializer
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
