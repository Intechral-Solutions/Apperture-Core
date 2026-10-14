using System.Linq;
using Apperture.Core.Application.Connector;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Stores operator-supplied secret field values in <see cref="ISecretStore"/> and returns GUID refs for <see cref="Connection.SecretRefs"/>.
/// </summary>
public sealed class ConnectionSecretMaterialProvisioner
{
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

            var descriptor = await _secretStore.CreateAsync(NewOwnedSecretRequest(connectionId, kv.Key), cancellationToken)
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
    /// The ownership rule for a secret reference. A reference names a secret this connection may use or rotate only if the store
    /// holds that secret under provider <c>platform-connection</c> with exactly the name
    /// <c>connection:{connectionId}:{fieldKey}</c>. A well-formed GUID is not proof: the reference is checked against the
    /// secret's own stored descriptor, never against what the connection row says. A missing secret, another provider and another
    /// owner or field are all refused with the same message, which does not repeat the reference or say which case it was.
    /// </summary>
    /// <exception cref="InvalidConnectionRequestException">The reference does not name a secret owned by this connection field.</exception>
    public async ValueTask EnsureReferenceOwnedAsync(
        ConnectionId connectionId, string fieldKey, string reference, CancellationToken cancellationToken = default)
    {
        if (Guid.TryParse(reference, out var guid))
        {
            var descriptor = await _secretStore.GetAsync(new SecretId(guid), cancellationToken).ConfigureAwait(false);
            if (descriptor is not null && ConnectionSecretOwnership.For(connectionId, fieldKey).Matches(descriptor.Provider, descriptor.Name))
                return;
        }

        // A request the caller can fix (so a 400 on update as well as on create), not a missing connection.
        throw new InvalidConnectionRequestException(
            $"Secret reference for '{fieldKey}' does not name a secret owned by this connection field. "
            + "Supply a new value with secretValues, or remove the stored reference with clearSecretFields.");
    }

    /// <summary>
    /// Whether the field's stored reference names a secret this connection field owns. Only the secret's descriptor is read, never
    /// its versions or material, and the answer is the same for a missing secret, another provider, another owner and another
    /// field: no. This is the same rule <see cref="EnsureReferenceOwnedAsync"/> enforces and every resolution applies.
    /// </summary>
    public async ValueTask<bool> IsReferenceOwnedAsync(
        ConnectionId connectionId, string fieldKey, string? reference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference) || !Guid.TryParse(reference, out var guid))
            return false;
        var descriptor = await _secretStore.GetAsync(new SecretId(guid), cancellationToken).ConfigureAwait(false);
        return descriptor is not null && ConnectionSecretOwnership.For(connectionId, fieldKey).Matches(descriptor.Provider, descriptor.Name);
    }

    /// <summary>Applies <see cref="EnsureReferenceOwnedAsync"/> to each reference, in field order.</summary>
    public async ValueTask EnsureReferencesOwnedAsync(
        ConnectionId connectionId, IEnumerable<KeyValuePair<string, string>> references, CancellationToken cancellationToken = default)
    {
        foreach (var kv in references.OrderBy(k => k.Key, StringComparer.Ordinal))
            await EnsureReferenceOwnedAsync(connectionId, kv.Key, kv.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stores each non-blank value for a declared secret field. When the field already names a secret this connection field
    /// owns, the value becomes a new active version of it (a rotation). In every other case the field names nothing usable and the
    /// value is stored as a new secret the connection owns, and <paramref name="workingSecretRefs"/> is repointed to it. "Every
    /// other case" is a missing reference, another connection's secret, another provider's, another field's, a secret that does
    /// not exist, or text that is not a reference at all.
    /// <para>The secret the field named before is never read, adopted, versioned, disabled or otherwise changed: only its
    /// descriptor is looked at, to learn that it is not the field's own. Without a reference to a foreign secret on the row, the
    /// operator needs to know no reference to repair the field.</para>
    /// Mutates <paramref name="workingSecretRefs"/>. Returns, per field that received material, whether it replaced the secret the
    /// field already owned. Meant to run inside the caller's audited unit: the store joins its transaction, so the new secret and
    /// the repointed reference are live together or not at all.
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
        foreach (var kv in secretValues.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(kv.Value))
                continue;

            var material = new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: kv.Value.Trim());

            workingSecretRefs.TryGetValue(kv.Key, out var existingRef);
            if (await IsReferenceOwnedAsync(connectionId, kv.Key, existingRef, cancellationToken).ConfigureAwait(false))
            {
                var secretId = new SecretId(Guid.Parse(existingRef!));
                var version = await _secretStore.AddVersionAsync(secretId, material, cancellationToken).ConfigureAwait(false);
                await _secretStore.ActivateVersionAsync(secretId, version, cancellationToken).ConfigureAwait(false);
                workingSecretRefs[kv.Key] = existingRef!;
                writes.Add(new ConnectionSecretWrite(kv.Key, Replaced: true));
            }
            else
            {
                var descriptor = await _secretStore.CreateAsync(NewOwnedSecretRequest(connectionId, kv.Key), cancellationToken)
                    .ConfigureAwait(false);

                var version = await _secretStore.AddVersionAsync(descriptor.SecretId, material, cancellationToken).ConfigureAwait(false);
                await _secretStore.ActivateVersionAsync(descriptor.SecretId, version, cancellationToken).ConfigureAwait(false);
                workingSecretRefs[kv.Key] = descriptor.SecretId.Value.ToString("D");
                writes.Add(new ConnectionSecretWrite(kv.Key, Replaced: false));
            }
        }

        return writes;
    }

    /// <summary>A new secret under the provider and name <see cref="ConnectionSecretOwnership"/> defines for the field, the rule every resolution checks.</summary>
    private static SecretCreateRequest NewOwnedSecretRequest(ConnectionId connectionId, string fieldKey) =>
        new(ConnectionSecretOwnership.NameFor(connectionId, fieldKey), ConnectionSecretOwnership.Provider, SecretMaterialType.OpaqueString);

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
        ValidateConfigKeys(definition, fieldsByKey, config);

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

    /// <summary>
    /// The check for an update. Config is checked as on creation. Secret references are not: an update leaves a reference it does
    /// not touch exactly as stored (a stored reference of any shape, or a key the connector no longer declares, is a legacy row
    /// that resolution refuses and that clearing or a new value repairs), so only whether each required field still has one is
    /// asked. A request that clears a required field is therefore refused with a message that says so.
    /// </summary>
    /// <exception cref="InvalidConnectionRequestException">A required field would be left without a value.</exception>
    public static void ValidateUpdatedConnectionMaterial(
        ConnectorDefinition definition,
        IReadOnlyDictionary<string, string> config,
        IReadOnlyDictionary<string, string> secretRefs)
    {
        var fieldsByKey = definition.ConnectionFields.ToDictionary(f => f.Key, StringComparer.Ordinal);
        ValidateConfigKeys(definition, fieldsByKey, config);

        foreach (var req in definition.RequiredInputSchema.Keys)
        {
            if (!fieldsByKey.TryGetValue(req, out var field))
                continue;
            var hasValue = field.IsSecret
                ? secretRefs.TryGetValue(req, out var stored) && !string.IsNullOrWhiteSpace(stored)
                : config.TryGetValue(req, out var c) && !string.IsNullOrWhiteSpace(c);
            if (!hasValue)
                throw new InvalidConnectionRequestException($"Required connection field '{req}' is missing for connector {definition.Id.Value}.");
        }
    }

    private static void ValidateConfigKeys(
        ConnectorDefinition definition, IReadOnlyDictionary<string, ConnectionFieldDescriptor> fieldsByKey, IReadOnlyDictionary<string, string> config)
    {
        foreach (var kv in config)
        {
            if (!fieldsByKey.TryGetValue(kv.Key, out var field))
                throw new InvalidOperationException($"Unknown connection config key '{kv.Key}' for connector {definition.Id.Value}.");
            if (field.IsSecret)
                throw new InvalidOperationException($"Key '{kv.Key}' is a secret field and must be supplied via secret provisioning, not config.");
        }
    }
}

/// <summary>One secret field of a connection that received material: <paramref name="Replaced"/> when it became a new active version of the secret the field already named, otherwise it was stored as a new secret.</summary>
public sealed record ConnectionSecretWrite(string FieldKey, bool Replaced);
