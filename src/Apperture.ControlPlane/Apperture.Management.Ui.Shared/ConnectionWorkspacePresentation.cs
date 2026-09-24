using Apperture.Management.Client;

namespace Apperture.Management.Ui.Shared;

/// <summary>One line of the workspace's Configuration section.</summary>
/// <param name="Key">The stored field key (a technical fact, shown only when the connector declares no better label).</param>
/// <param name="Label">The connector-declared display name, or the key when the connector declares none.</param>
/// <param name="Value">A non-secret value as stored. Always null for a secret field.</param>
/// <param name="IsSet">Whether a value (or, for a secret, a stored secret) is present.</param>
public sealed record ConnectionConfigurationRow(string Key, string Label, string? Description, bool IsSecret, string? Value, bool IsSet);

/// <summary>A point-in-time health statement about a Connection. It is a fact, never a gate on anything else.</summary>
public sealed record ConnectionHealth(string Label, ManagementStatusSemantic Semantic, string Detail);

/// <summary>
/// Pure presentation rules for the Connection workspace. Everything here is derived from the Connection read contract and
/// the connector-declared fields, so it introduces no state of its own. In particular a secret field is reduced to
/// present/absent: neither the secret value nor its stored reference ever leaves the read contract into a row.
/// </summary>
public static class ConnectionWorkspacePresentation
{
    public const string SecretSet = "Set";
    public const string SecretNotSet = "Not set";

    public const string UnregisteredConnectorNotice =
        "The connector for this connection is not registered in this host. Its stored settings are shown as saved, "
        + "and testing and editing are unavailable until the connector is registered.";

    public const string RetiredNotice =
        "This connection is retired. It is read-only here and no longer eligible for new bindings; existing bindings keep "
        + "working until they are changed.";

    public static string SecretPresence(bool isSet) => isSet ? SecretSet : SecretNotSet;

    public static bool IsRetired(ConnectionDto connection) =>
        string.Equals(connection.State, "Retired", StringComparison.OrdinalIgnoreCase);

    /// <summary>The server supplies a display name only when the connector is registered in the host that served the read.</summary>
    public static bool IsConnectorRegistered(ConnectionDto connection) =>
        !string.IsNullOrWhiteSpace(connection.ConnectorDisplayName);

    /// <summary>
    /// Declared fields first, in the connector's own order; then any stored key the connector does not declare (kept visible
    /// by its key rather than silently dropped). With no definition every stored key is listed by key.
    /// </summary>
    public static IReadOnlyList<ConnectionConfigurationRow> ConfigurationRows(
        ConnectionDto connection,
        ConnectorDefinitionCatalogDto? definition)
    {
        var rows = new List<ConnectionConfigurationRow>();
        var handled = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in definition?.Fields ?? [])
        {
            handled.Add(field.Key);
            rows.Add(Row(connection, field.Key, field.DisplayName, field.Description, field.IsSecret));
        }

        foreach (var key in connection.Config.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (handled.Add(key))
                rows.Add(Row(connection, key, key, null, isSecret: false));
        }

        foreach (var key in connection.SecretRefs.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (handled.Add(key))
                rows.Add(Row(connection, key, key, null, isSecret: true));
        }

        return rows;
    }

    private static ConnectionConfigurationRow Row(ConnectionDto connection, string key, string label, string? description, bool isSecret)
    {
        if (isSecret)
        {
            // Presence is the whole disclosure: the reference itself is never copied into the row.
            var present = connection.SecretRefs.TryGetValue(key, out var reference) && !string.IsNullOrWhiteSpace(reference);
            return new ConnectionConfigurationRow(key, label, description, IsSecret: true, Value: null, IsSet: present);
        }

        var value = connection.Config.TryGetValue(key, out var stored) && !string.IsNullOrWhiteSpace(stored) ? stored : null;
        return new ConnectionConfigurationRow(key, label, description, IsSecret: false, value, IsSet: value is not null);
    }

    public static ConnectionHealth Health(ConnectionDto connection)
    {
        if (!IsConnectorRegistered(connection))
        {
            // Not "unsupported": the host simply has no definition for this connector, so nothing can be tested.
            return new ConnectionHealth(
                "Not testable", ManagementStatusSemantic.Neutral, "Testing is unavailable because this connector is not registered in this host.");
        }

        if (!connection.SupportsVerification)
        {
            return new ConnectionHealth(
                "Not testable", ManagementStatusSemantic.Neutral, "This connector does not support test connection.");
        }

        return connection.LastVerificationStatus switch
        {
            // A retired connection has no test action, so its health never points the operator at one.
            "NeverTested" when IsRetired(connection) => new ConnectionHealth(
                "Never tested", ManagementStatusSemantic.Neutral, "This connection was never tested."),
            "NeverTested" => new ConnectionHealth(
                "Not tested yet", ManagementStatusSemantic.Neutral, "Run Test connection to check this connection."),
            "Success" => new ConnectionHealth(
                "Verified", ManagementStatusSemantic.Success, "The last test connected successfully."),
            "Failed" => new ConnectionHealth(
                "Verification failed", ManagementStatusSemantic.Error,
                "The last test did not succeed. This is the result of that one test; it does not block anything else."),
            "Unavailable" => new ConnectionHealth(
                "Verification unavailable", ManagementStatusSemantic.Warning,
                "The last test could not be carried out. This is the result of that one test; it does not block anything else."),
            var other => new ConnectionHealth(other, ManagementStatusSemantic.Neutral, "")
        };
    }

    public static ManagementStatusSemantic StateSemantic(string state) =>
        string.Equals(state, "Active", StringComparison.OrdinalIgnoreCase)
            ? ManagementStatusSemantic.Success
            : ManagementStatusSemantic.Neutral;
}
