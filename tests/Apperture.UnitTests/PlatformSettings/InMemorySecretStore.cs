using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;

namespace Apperture.UnitTests.PlatformSettings;

/// <summary>
/// Minimal in-memory <see cref="ISecretStore"/> for sensitive setting tests.
/// </summary>
/// <remarks>
/// CTX-3B Plan 6a Task 5: promoted verbatim out of <see cref="SensitivePlatformSettingsTests"/>, where it
/// was a private nested class, so the Integration hard-delete secret-disposal tests
/// (<c>Apperture.UnitTests.Integration.IntegrationLifecycleTestHarness</c>) can assert against a real
/// secret store rather than a <c>Mock&lt;ISecretStore&gt;</c> that would silently no-op the one thing
/// those tests exist to prove.
/// </remarks>
internal sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<Guid, Entry> _entries = new();

    public IReadOnlyList<SecretDescriptor> Descriptors =>
        _entries.Values.Select(e => e.ToDescriptor()).ToList();

    public ValueTask<SecretDescriptor> CreateAsync(SecretCreateRequest request, CancellationToken cancellationToken = default)
    {
        var id = SecretId.New();
        _entries[id.Value] = new Entry(id, request.Name, request.Provider, request.MaterialType);
        return new ValueTask<SecretDescriptor>(_entries[id.Value].ToDescriptor());
    }

    public ValueTask<SecretDescriptor?> GetAsync(SecretId secretId, CancellationToken cancellationToken = default) =>
        new(_entries.TryGetValue(secretId.Value, out var e) ? e.ToDescriptor() : null);

    public ValueTask<IReadOnlyList<SecretDescriptor>> ListAsync(CancellationToken cancellationToken = default) =>
        new(Descriptors);

    public ValueTask<string> AddVersionAsync(SecretId secretId, SecretMaterial material, CancellationToken cancellationToken = default)
    {
        var e = _entries[secretId.Value];
        var version = (e.Versions.Count + 1).ToString();
        e.Versions[version] = material.OpaqueString ?? string.Empty;
        e.PendingVersion = version;
        return new ValueTask<string>(version);
    }

    public ValueTask ActivateVersionAsync(SecretId secretId, string version, CancellationToken cancellationToken = default)
    {
        var e = _entries[secretId.Value];
        if (!e.Versions.ContainsKey(version))
            throw new InvalidOperationException("unknown version");
        e.ActiveVersion = version;
        e.State = SecretState.Active;
        return ValueTask.CompletedTask;
    }

    /// <summary>How many times <see cref="DisableAsync"/> was called.</summary>
    public int DisableCalls { get; private set; }

    public ValueTask DisableAsync(SecretId secretId, CancellationToken cancellationToken = default)
    {
        DisableCalls++;
        if (_entries.TryGetValue(secretId.Value, out var e))
            e.State = SecretState.Disabled;
        return ValueTask.CompletedTask;
    }

    public ValueTask RevokeVersionAsync(SecretId secretId, string version, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask<SecretAuditPage> GetAuditAsync(SecretAuditQuery query, CancellationToken cancellationToken = default) =>
        new(new SecretAuditPage([]));

    public ValueTask<ResolvedSecret> ResolveAsync(SecretRef secretRef)
    {
        if (!Guid.TryParse(secretRef.Value, out var id) || !_entries.TryGetValue(id, out var e))
            throw new InvalidOperationException("not found");
        if (e.State == SecretState.Disabled || e.ActiveVersion is null)
            throw new InvalidOperationException("disabled");
        return new ValueTask<ResolvedSecret>(new ResolvedSecret(
            secretRef,
            new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: e.Versions[e.ActiveVersion]),
            e.ActiveVersion));
    }

    public string? ActiveOpaque(string secretRef) =>
        Guid.TryParse(secretRef, out var id) && _entries.TryGetValue(id, out var e) && e.ActiveVersion is not null
            ? e.Versions[e.ActiveVersion]
            : null;

    public int VersionCount(string secretRef) =>
        Guid.TryParse(secretRef, out var id) && _entries.TryGetValue(id, out var e) ? e.Versions.Count : 0;

    public SecretDescriptor? Get(string secretRef) =>
        Guid.TryParse(secretRef, out var id) && _entries.TryGetValue(id, out var e) ? e.ToDescriptor() : null;

    private sealed class Entry(SecretId id, string name, string provider, SecretMaterialType materialType)
    {
        public SecretId Id { get; } = id;
        public string Name { get; } = name;
        public string Provider { get; } = provider;
        public SecretMaterialType MaterialType { get; } = materialType;
        public SecretState State { get; set; } = SecretState.Pending;
        public string? ActiveVersion { get; set; }
        public string? PendingVersion { get; set; }
        public Dictionary<string, string> Versions { get; } = new(StringComparer.Ordinal);

        public SecretDescriptor ToDescriptor() =>
            new(Id, Name, Provider, MaterialType, ActiveVersion, State, DateTimeOffset.UtcNow);
    }
}
