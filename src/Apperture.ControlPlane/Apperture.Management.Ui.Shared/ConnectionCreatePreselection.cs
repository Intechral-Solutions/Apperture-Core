namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Resolves the optional <c>?connectorDefinitionId=</c> value on the create-connection page to a connector the
/// form can actually select. Anything not currently registered resolves to "" (the form's normal unselected
/// state); the value is never trusted as-is.
/// </summary>
public static class ConnectionCreatePreselection
{
    public static string ResolveConnectorDefinitionId(string? requested, IEnumerable<string> availableDefinitionIds)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return "";

        return availableDefinitionIds.Contains(requested, StringComparer.Ordinal) ? requested : "";
    }
}
