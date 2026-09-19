using Apperture.Management.Client;

namespace Apperture.Management.Ui.Client.Molecules;

/// <summary>
/// HSK-A: shared "connector-required fields are filled" predicate for the two Create-connection
/// surfaces (<c>ConnectionNew.razor</c> standalone create and <c>IntegrationConnections.razor</c>
/// in-flow create). Both surfaces render fields via <c>ConnectorConnectionFieldInputs</c>, which already
/// updates <c>ConfigValues</c>/<c>SecretValues</c> live on <c>oninput</c>; this predicate is purely local
/// dictionary inspection (no network calls) so it can be evaluated both live, for Create-button
/// eligibility, and again at submit time.
/// </summary>
public static class ConnectorConnectionFieldValidation
{
    /// <summary>
    /// Returns the connector-declared required fields (per <see cref="ConnectionFieldCatalogDto.IsRequired"/>)
    /// whose current value (via <paramref name="getValue"/>) is null/empty/whitespace. A null
    /// <paramref name="definition"/> or a definition with no fields yields an empty list.
    /// </summary>
    public static IReadOnlyList<ConnectionFieldCatalogDto> GetMissingRequiredFields(
        ConnectorDefinitionCatalogDto? definition,
        Func<ConnectionFieldCatalogDto, string?> getValue)
    {
        if (definition is null)
            return Array.Empty<ConnectionFieldCatalogDto>();

        return definition.Fields
            .Where(f => f.IsRequired && string.IsNullOrWhiteSpace(getValue(f)))
            .ToArray();
    }

    /// <summary>True when every connector-required field currently has a non-empty value.</summary>
    public static bool AllRequiredFieldsFilled(
        ConnectorDefinitionCatalogDto? definition,
        Func<ConnectionFieldCatalogDto, string?> getValue) =>
        GetMissingRequiredFields(definition, getValue).Count == 0;
}
