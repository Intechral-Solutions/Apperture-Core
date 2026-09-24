using System.Net.Http;
using Apperture.Connectors.CampusNexus.Models;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;

namespace Apperture.Connectors.CampusNexus;

/// <summary>
/// Verifies Campus Nexus API connectivity by running a minimal OData read against the campus query API:
/// top 1 active campus, <c>$select=Id</c>, ensuring a successful HTTP response and valid OData payload.
/// </summary>
public sealed class CampusNexusConnectionVerifier : IConnectionVerifier
{
    private readonly ISecretResolver _secretResolver;
    private readonly CampusNexusApiConnectorClientFactory _factory;

    public CampusNexusConnectionVerifier(ISecretResolver secretResolver, CampusNexusApiConnectorClientFactory factory)
    {
        _secretResolver = secretResolver;
        _factory = factory;
    }

    public ConnectorDefinitionId ConnectorDefinitionId => WellKnownConnectorDefinitionIds.PlatformCampusNexus;

    public async ValueTask<ConnectionVerificationResult> VerifyAsync(Connection connection, CancellationToken cancellationToken = default)
    {
        var at = DateTimeOffset.UtcNow;
        ResolvedConnectionMaterial material;
        try
        {
            material = await BuildMaterialAsync(connection, _secretResolver, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionVerificationResult(
                ConnectionVerificationStatus.Failed,
                at,
                ConnectionVerificationMessages.SecretResolutionFailed);
        }

        try
        {
            using var api = _factory.Create(connection, material);
            var apiQuery = new ApiQuery(
                CampusNexusConnectionVerification.CampusEntitySet,
                CampusNexusConnectionVerification.ActiveCampusODataQuery);

            IReadOnlyList<CampusIdRow> rows =
                await api.QueryAsync<CampusIdRow>(apiQuery, cancellationToken).ConfigureAwait(false);

            string summary = rows.Count switch
            {
                0 => "Campus query OK: valid OData response; no active campuses matched the filter.",
                1 => $"Campus query OK: active campus Id={rows[0].Id}.",
                _ => $"Campus query OK: received {rows.Count} row(s); expected at most one ($top=1).",
            };

            return new ConnectionVerificationResult(ConnectionVerificationStatus.Success, at, summary);
        }
        catch (CampusNexusApiException ex)
        {
            return new ConnectionVerificationResult(
                ConnectionVerificationStatus.Failed,
                at,
                ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return new ConnectionVerificationResult(
                ConnectionVerificationStatus.Failed,
                at,
                $"HTTP request failed: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new ConnectionVerificationResult(
                ConnectionVerificationStatus.Failed,
                at,
                $"Request timed out: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return new ConnectionVerificationResult(
                ConnectionVerificationStatus.Failed,
                at,
                $"Invalid connection configuration: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionVerificationResult(
                ConnectionVerificationStatus.Failed,
                at,
                $"Verification failed: {ex.Message}");
        }
    }

    private static async ValueTask<ResolvedConnectionMaterial> BuildMaterialAsync(
        Connection connection,
        ISecretResolver secretResolver,
        CancellationToken cancellationToken)
    {
        var merged = new Dictionary<string, string>(connection.Config);
        foreach (var kv in connection.SecretRefs)
        {
            var secretRef = new SecretRef(kv.Value);
            var resolved = await secretResolver.ResolveAsync(secretRef, cancellationToken).ConfigureAwait(false);
            var value = resolved.Material.OpaqueString ?? resolved.Material.Json ?? "";
            merged[kv.Key] = value;
        }

        return new ResolvedConnectionMaterial(merged);
    }
}
