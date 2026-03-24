using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Client;
using Apperture.Core.Domain.Connector;

namespace Apperture.Connectors.CampusNexus;

/// <summary>
/// Contributes the Campus Nexus API client definition. Epic 51.
/// </summary>
public sealed class CampusNexusClientDefinitionProvider : IClientDefinitionProvider
{
    private const string ClientId = "Apperture.Platform.CampusNexus.Client";

    /// <inheritdoc />
    public async IAsyncEnumerable<ClientDefinition> GetClientDefinitionsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        yield return new ClientDefinition(
            new ClientDefinitionId(ClientId),
            "Campus Nexus API Client",
            "Higher-level client for Campus Nexus HTTP API: commands and OData queries.",
            WellKnownConnectorDefinitionIds.PlatformCampusNexus,
            ModuleId: null,
            UsesGateway: false,
            [
                new ClientOperationMetadata(
                    "ExecuteCommand",
                    "Execute API Command",
                    "Execute a Campus Nexus API command (POST).",
                    IsOperatorFacing: true,
                    MayRequireUserInputs: true,
                    ParameterHint: "{\"command\": \"...\", \"parameters\": {...}}"),
                new ClientOperationMetadata(
                    "Query",
                    "Query OData",
                    "Execute an OData query against Campus Nexus.",
                    IsOperatorFacing: true,
                    MayRequireUserInputs: true,
                    ParameterHint: "{\"entitySet\": \"...\", \"filter\": \"...\"}")
            ]);
    }
}
