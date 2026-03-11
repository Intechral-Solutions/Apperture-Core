using Apperture.Core.Application.Connector;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;

namespace Apperture.ConnectorResolution;

/// <summary>
/// Builds ResolvedConnectionMaterial from a connection by resolving secret refs via ISecretResolver.
/// Internal to the resolution pipeline.
/// </summary>
internal static class ResolvedConnectionMaterialBuilder
{
    public static async ValueTask<ResolvedConnectionMaterial> BuildAsync(
        Connection connection,
        ISecretResolver secretResolver,
        CancellationToken cancellationToken = default)
    {
        var merged = new Dictionary<string, string>(connection.Config);

        foreach (var kv in connection.SecretRefs)
        {
            var secretRef = new SecretRef(kv.Value);
            var resolved = await secretResolver.ResolveAsync(secretRef, cancellationToken);
            var value = resolved.Material.OpaqueString ?? resolved.Material.Json ?? "";
            merged[kv.Key] = value;
        }

        return new ResolvedConnectionMaterial(merged);
    }
}
