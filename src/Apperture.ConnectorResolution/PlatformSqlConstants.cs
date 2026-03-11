using Apperture.Core.Domain.Connector;

namespace Apperture.ConnectorResolution;

/// <summary>
/// Well-known connector definition id for the platform-managed SQL connector.
/// Used to register and resolve the platform SQL IConnectorClientFactory&lt;IDbConnection&gt;.
/// </summary>
public static class PlatformSqlConstants
{
    /// <summary>
    /// Connector definition id for the platform SQL connector (e.g. instance database).
    /// </summary>
    public static readonly ConnectorDefinitionId ConnectorDefinitionId = new("Apperture.Platform.Sql");
}
