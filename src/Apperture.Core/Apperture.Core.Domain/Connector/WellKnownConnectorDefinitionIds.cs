namespace Apperture.Core.Domain.Connector;

/// <summary>
/// Stable connector definition ids for platform-provided connectors (connections UI, bindings, runtime factories).
/// </summary>
public static class WellKnownConnectorDefinitionIds
{
    public static readonly ConnectorDefinitionId PlatformSql = new("Apperture.Platform.Sql");

    /// <summary>Campus Nexus Student / CNS HTTP API (commands + OData).</summary>
    public static readonly ConnectorDefinitionId PlatformCampusNexus = new("Apperture.Platform.CampusNexus");

    /// <summary>SFTP file access (SSH.NET).</summary>
    public static readonly ConnectorDefinitionId PlatformSftp = new("Apperture.Platform.Sftp");
}
