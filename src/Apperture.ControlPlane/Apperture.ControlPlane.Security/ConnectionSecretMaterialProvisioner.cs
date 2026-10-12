using System.Linq;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Stores operator-supplied secret field values in <see cref="ISecretStore"/> and returns GUID refs for <see cref="Connection.SecretRefs"/>.
/// </summary>
public sealed class ConnectionSecretMaterialProvisioner
{
    private const string SecretProvider = "platform-connection";

    private readonly ISecretStore _secretStore;

    public ConnectionSecretMaterialProvisioner(ISecretStore secretStore) =>
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));

    /// <summary>
    /// Creates new logical secrets for each non-empty entry. Used on connection create.
    /// </summary>
    public async ValueTask<IReadOnlyDictionary<string, string>> ProvisionForCreateAsync(
        ConnectionId connectionId,
        ConnectorDefinition definition,
        IReadOnlyDictionary<string, string> secretValues,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidSecretKeys(definition, secretValues);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in secretValues)
        {
            if (string.IsNullOrWhiteSpace(kv.Value))
                continue;

            var descriptor = await _secretStore.CreateAsync(
                    new SecretCreateRequest(
                        BuildSecretName(connectionId, kv.Key),
                        SecretProvider,
                        SecretMaterialType.OpaqueString),
                    cancellationToken)
                .ConfigureAwait(false);

            var version = await _secretStore.AddVersionAsync(
                    descriptor.SecretId,
                    new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: kv.Value.Trim()),
                    cancellationToken)
                .ConfigureAwait(false);

            await _secretStore.ActivateVersionAsync(descriptor.SecretId, version, cancellationToken).ConfigureAwait(false);
            result[kv.Key] = descriptor.SecretId.Value.ToString("D");
        }

        return result;
    }

    /// <summary>
    /// Adds a new active version to an existing secret when the connection already references a GUID; otherwise creates a new secret. Mutates <paramref name="workingSecretRefs"/>.
    /// Returns, per field that received material, whether it replaced the secret the field already named. Meant to run inside the
    /// caller's audited unit: the store joins its transaction, so the new version is live only if the unit commits.
    /// </summary>
    public async ValueTask<IReadOnlyList<ConnectionSecretWrite>> ApplySecretValuesForUpdateAsync(
        ConnectionId connectionId,
        ConnectorDefinition definition,
        IReadOnlyDictionary<string, string> secretValues,
        IDictionary<string, string> workingSecretRefs,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidSecretKeys(definition, secretValues);
        var writes = new List<ConnectionSecretWrite>();
        foreach (var kv in secretValues)
        {
            if (string.IsNullOrWhiteSpace(kv.Value))
                continue;

            var material = new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: kv.Value.Trim());

            if (workingSecretRefs.TryGetValue(kv.Key, out var existingRef)
                && Guid.TryParse(existingRef, out var existingGuid))
            {
                var secretId = new SecretId(existingGuid);
                var version = await _secretStore.AddVersionAsync(secretId, material, cancellationToken).ConfigureAwait(false);
                await _secretStore.ActivateVersionAsync(secretId, version, cancellationToken).ConfigureAwait(false);
                workingSecretRefs[kv.Key] = existingRef;
                writes.Add(new ConnectionSecretWrite(kv.Key, Replaced: true));
            }
            else
            {
                var descriptor = await _secretStore.CreateAsync(
                        new SecretCreateRequest(
                            BuildSecretName(connectionId, kv.Key),
                            SecretProvider,
                            SecretMaterialType.OpaqueString),
                        cancellationToken)
                    .ConfigureAwait(false);

                var version = await _secretStore.AddVersionAsync(descriptor.SecretId, material, cancellationToken).ConfigureAwait(false);
                await _secretStore.ActivateVersionAsync(descriptor.SecretId, version, cancellationToken).ConfigureAwait(false);
                workingSecretRefs[kv.Key] = descriptor.SecretId.Value.ToString("D");
                writes.Add(new ConnectionSecretWrite(kv.Key, Replaced: false));
            }
        }

        return writes;
    }

    private static string BuildSecretName(ConnectionId connectionId, string fieldKey) =>
        $"connection:{connectionId.Value}:{fieldKey}";

    /// <summary>
    /// Maps request dictionary keys to canonical <see cref="ConnectionFieldDescriptor.Key"/> values (case-insensitive).
    /// JSON clients often emit camelCase keys; connector factories expect declared casing (e.g. ApiKey, BaseUrl).
    /// </summary>
    public static Dictionary<string, string> NormalizeToDeclaredFieldKeys(
        IReadOnlyDictionary<string, string> values,
        ConnectorDefinition definition)
    {
        var canonicalByInsensitive = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in definition.ConnectionFields)
            canonicalByInsensitive[f.Key] = f.Key;

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in values)
        {
            if (!canonicalByInsensitive.TryGetValue(kv.Key, out var canonical))
                throw new InvalidOperationException($"Unknown connection field key '{kv.Key}' for connector {definition.Id.Value}.");

            if (result.ContainsKey(canonical))
                throw new InvalidOperationException($"Duplicate connection field '{canonical}' (key casing conflict).");

            result[canonical] = kv.Value;
        }

        return result;
    }

    private static void ThrowIfInvalidSecretKeys(ConnectorDefinition definition, IReadOnlyDictionary<string, string> secretValues)
    {
        var secretFields = definition.ConnectionFields.Where(f => f.IsSecret).Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in secretValues.Keys)
        {
            if (!secretFields.Contains(key))
                throw new InvalidOperationException($"Secret field '{key}' is not declared for connector {definition.Id.Value}.");
        }
    }

    /// <summary>
    /// Validates config keys, secret ref keys, and required inputs for a connector definition.
    /// </summary>
    public static void ValidateConnectionMaterial(
        ConnectorDefinition definition,
        IReadOnlyDictionary<string, string> config,
        IReadOnlyDictionary<string, string> secretRefs)
    {
        var fieldsByKey = definition.ConnectionFields.ToDictionary(f => f.Key, StringComparer.Ordinal);
        foreach (var kv in config)
        {
            if (!fieldsByKey.TryGetValue(kv.Key, out var field))
                throw new InvalidOperationException($"Unknown connection config key '{kv.Key}' for connector {definition.Id.Value}.");
            if (field.IsSecret)
                throw new InvalidOperationException($"Key '{kv.Key}' is a secret field and must be supplied via secret provisioning, not config.");
        }

        foreach (var kv in secretRefs)
        {
            if (!fieldsByKey.TryGetValue(kv.Key, out var field))
                throw new InvalidOperationException($"Unknown connection secret key '{kv.Key}' for connector {definition.Id.Value}.");
            if (!field.IsSecret)
                throw new InvalidOperationException($"Key '{kv.Key}' is not a secret field; store it in config.");
            if (!Guid.TryParse(kv.Value, out _))
                throw new InvalidOperationException($"Secret reference for '{kv.Key}' must be a secret id (GUID).");
        }

        foreach (var req in definition.RequiredInputSchema.Keys)
        {
            if (!fieldsByKey.ContainsKey(req))
                continue;
            var field = fieldsByKey[req];
            var hasValue = field.IsSecret
                ? secretRefs.ContainsKey(req) && !string.IsNullOrWhiteSpace(secretRefs[req])
                : config.TryGetValue(req, out var c) && !string.IsNullOrWhiteSpace(c);
            if (!hasValue)
                throw new InvalidOperationException($"Required connection field '{req}' is missing for connector {definition.Id.Value}.");
        }
    }
}

/// <summary>One secret field of a connection that received material: <paramref name="Replaced"/> when it became a new active version of the secret the field already named, otherwise it was stored as a new secret.</summary>
public sealed record ConnectionSecretWrite(string FieldKey, bool Replaced);
