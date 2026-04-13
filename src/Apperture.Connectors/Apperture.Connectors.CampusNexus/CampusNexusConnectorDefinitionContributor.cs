using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;

namespace Apperture.Connectors.CampusNexus;

/// <summary>
/// Enriches the Campus Nexus connector definition with catalog metadata (connection field shape only).
/// </summary>
public sealed class CampusNexusConnectorDefinitionContributor : IConnectorDefinitionContributor
{
    /// <inheritdoc />
    public void Contribute(IConnectorDefinitionRegistry registry)
    {
        registry.Register(new ConnectorDefinition(
            WellKnownConnectorDefinitionIds.PlatformCampusNexus,
            "Campus Nexus API",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["BaseUrl"] = "",
                ["AuthMode"] = "",
            })
        {
            Description =
                "HTTP API for Ellucian Campus Nexus (commands and OData). Modules consume ICampusNexusApiClient from this connector package; the platform stores connection material and bindings only.",
            ConnectionFields =
            [
                new ConnectionFieldDescriptor(
                    "BaseUrl",
                    "API base URL",
                    false,
                    "Root URL for the tenant API (e.g. https://tenant.example.edu/CampusNexus)."),
                new ConnectionFieldDescriptor(
                    "AuthMode",
                    "Authentication mode",
                    false,
                    "ApiKey or Basic."),
                new ConnectionFieldDescriptor(
                    "ApiKey",
                    "API key",
                    true,
                    "Sent as ApiKey header when AuthMode is ApiKey."),
                new ConnectionFieldDescriptor("Username", "Username", false, "Basic auth user when AuthMode is Basic."),
                new ConnectionFieldDescriptor(
                    "Password",
                    "Password",
                    true,
                    "Basic auth password when AuthMode is Basic."),
            ],
        });
    }
}
