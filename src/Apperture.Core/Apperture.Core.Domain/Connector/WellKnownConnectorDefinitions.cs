namespace Apperture.Core.Domain.Connector;

/// <summary>
/// Shared platform connector definition instances for control plane and runtime registries.
/// </summary>
public static class WellKnownConnectorDefinitions
{
    private static readonly IReadOnlyDictionary<string, string> EmptySchema =
        new Dictionary<string, string>();

    public static ConnectorDefinition CreatePlatformSql() =>
        new(WellKnownConnectorDefinitionIds.PlatformSql, "Platform SQL", null, EmptySchema)
        {
            Description = "Microsoft SQL Server via System.Data.IDbConnection.",
            ConnectionFields =
            [
                new ConnectionFieldDescriptor("Server", "Server", false),
                new ConnectionFieldDescriptor("Database", "Database", false),
                new ConnectionFieldDescriptor(
                    "UserId",
                    "User ID",
                    false,
                    "Optional; integrated security when omitted."),
                new ConnectionFieldDescriptor("Password", "Password", true),
            ],
        };

    /// <summary>Placeholder until a connector-package contributor enriches metadata (control plane).</summary>
    public static ConnectorDefinition CreatePlatformCampusNexusMinimal() =>
        new(WellKnownConnectorDefinitionIds.PlatformCampusNexus, "Campus Nexus API", null, EmptySchema);

    public static ConnectorDefinition CreatePlatformSftp() =>
        new(WellKnownConnectorDefinitionIds.PlatformSftp, "SFTP", null, EmptySchema)
        {
            Description = "SFTP file access (SSH.NET).",
            ConnectionFields =
            [
                new ConnectionFieldDescriptor("Host", "Host", false),
                new ConnectionFieldDescriptor("Port", "Port", false, "Optional; default 22."),
                new ConnectionFieldDescriptor("Username", "Username", false),
                new ConnectionFieldDescriptor("PrivateKeyPem", "Private key (PEM)", true),
                new ConnectionFieldDescriptor("PrivateKeyPassphrase", "Key passphrase", true),
                new ConnectionFieldDescriptor(
                    "Password",
                    "Password",
                    true,
                    "Password authentication when not using a private key."),
            ],
        };
}
