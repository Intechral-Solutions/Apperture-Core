using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Decides, for an integration about to be deleted, which of its persisted setting overrides refer to a settings-provisioned secret
/// and what the delete must do about each (AUD-1.4 slice 3, work package 4a; owner ruling H1, refined conservative option B).
/// Read-only: it changes and stages nothing, so it can run before any mutation and its refusal leaves nothing partial.
/// </summary>
/// <remarks>
/// <para><b>What counts as evidence.</b> The override row alone proves nothing: a setting that holds a reference does not own the
/// secret it names. The evidence is the secret store's own record of what the secret is. The settings lifecycle provisions a secret
/// with the provider <see cref="PlatformSettingSecretMaterialProvisioner.SecretProvider"/> and the name
/// <c>platform-setting:{key}</c>. The store records no owner id, so that provider and name are the only provenance there is, and
/// they say which <i>setting key</i> a secret was made for, not which integration owns it. Ownership by this integration is
/// therefore established by three facts together: the secret is settings-provisioned; its name matches the key of the one override
/// that refers to it; and no reference outside this integration refers to it.</para>
/// <para><b>Definitions are a hint, never a gate.</b> An override is classified whether or not a definition for its key exists or is
/// integration-scoped. A definition that declares the setting sensitive only raises the bar: an unreadable value on it is ambiguous.
/// Without a definition, or with an ordinary one, any GUID the value holds is still looked up in the store and a settings-provisioned
/// secret found there is treated exactly as for a sensitive setting, so a removed or reclassified definition cannot hide a credential
/// from the classifier, and cannot authorize its retirement either.</para>
/// <para><b>Outcomes</b> (<see cref="IntegrationSecretDispositionKind"/>): retire the secret after the overrides are removed; retain
/// it because a reference outside this integration still holds it; no applicable usable secret; or ambiguous. A credible
/// settings-provisioned secret that is still referenced elsewhere is retained even when its ownership is unclear, since leaving it
/// alone is safe whoever owns it. Otherwise, ambiguity (no key in its name, a name for another key, or several overrides referring
/// to it) refuses the whole delete: it is never reported as a successful no-op. A secret referred to by several overrides gets one
/// disposition, shared by all of them, and is planned for retirement at most once.</para>
/// <para><b>Concurrency.</b> Call it inside the caller's transaction after the caller took its integration row lock. It takes no lock
/// and opens no transaction. Its reads are not stable against writers that commit later; see
/// <see cref="IntegrationSettingSecretInventory"/> for what the scan can and cannot promise.</para>
/// </remarks>
public sealed class IntegrationSettingSecretClassifier
{
    private static readonly string NamePrefix = PlatformSettingSecretMaterialProvisioner.BuildSecretName(string.Empty);

    private readonly IntegrationSettingSecretInventory _inventory;
    private readonly ISecretStore _secretStore;
    private readonly IIntegrationSettingDefinitionRegistry _registry;

    public IntegrationSettingSecretClassifier(
        IntegrationSettingSecretInventory inventory,
        ISecretStore secretStore,
        IIntegrationSettingDefinitionRegistry registry)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>Classifies every persisted override of <paramref name="integrationId"/>. Never throws for ambiguity: the plan carries the refusal.</summary>
    public async Task<IntegrationSettingDeletionPlan> ClassifyAsync(string integrationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(integrationId);

        var overrides = await _inventory.ListOverridesAsync(integrationId, cancellationToken).ConfigureAwait(false);

        // 1. Read each override's value. This is where the override's own shape decides whether it can hold a credential at all.
        var items = new List<Item>();
        foreach (var row in overrides)
        {
            var sensitive = _registry.TryGet(row.Key)?.IsSensitive == true;
            var reading = SecretReferenceReader.ReadJson(row.ValueJson);
            var item = new Item(row.Key);
            items.Add(item);

            switch (reading.Shape)
            {
                case SecretReferenceShape.None:
                    item.Outcomes.Add(Outcome.NoSecret(IntegrationSecretNoSecretReason.NoReference));
                    break;
                case SecretReferenceShape.Guid:
                    item.Candidates.AddRange(reading.Guids);
                    break;
                default:
                    if (sensitive)
                    {
                        // A credential setting whose value is not one readable reference: the secret may exist and cannot be found.
                        item.Outcomes.Add(Outcome.Ambiguous(IntegrationSecretAmbiguityReason.UnreadableReference));
                    }
                    else if (reading.Guids.Count == 0)
                    {
                        item.Outcomes.Add(Outcome.NoSecret(IntegrationSecretNoSecretReason.NoReference));
                    }
                    else
                    {
                        item.Candidates.AddRange(reading.Guids);
                    }

                    break;
            }
        }

        // 2. Look every distinct candidate up in the store, once.
        var descriptors = new Dictionary<Guid, SecretDescriptor?>();
        foreach (var guid in items.SelectMany(i => i.Candidates).Distinct())
            descriptors[guid] = await _secretStore.GetAsync(new SecretId(guid), cancellationToken).ConfigureAwait(false);

        // 3. A candidate is credible evidence only when the store says it is a usable settings-provisioned secret.
        var credible = new Dictionary<Guid, List<Item>>();
        foreach (var item in items)
        {
            foreach (var guid in item.Candidates.Distinct())
            {
                var descriptor = descriptors[guid];
                if (descriptor is null)
                    item.Outcomes.Add(Outcome.NoSecret(IntegrationSecretNoSecretReason.ReferenceDoesNotResolve));
                else if (!string.Equals(descriptor.Provider, PlatformSettingSecretMaterialProvisioner.SecretProvider, StringComparison.Ordinal))
                    item.Outcomes.Add(Outcome.NoSecret(IntegrationSecretNoSecretReason.NotSettingsProvisioned));
                else if (descriptor.State is SecretState.Disabled or SecretState.Revoked)
                    item.Outcomes.Add(Outcome.NoSecret(IntegrationSecretNoSecretReason.AlreadyUnusable));
                else
                {
                    if (!credible.TryGetValue(guid, out var holders))
                        credible[guid] = holders = [];
                    holders.Add(item);
                }
            }
        }

        // 4. What survives the delete. Only a credible secret needs the scan.
        var surviving = credible.Count == 0
            ? new Dictionary<Guid, IReadOnlyList<SurvivingSecretReference>>()
            : new Dictionary<Guid, IReadOnlyList<SurvivingSecretReference>>(
                await _inventory.FindSurvivingReferencesAsync(integrationId, credible.Keys.ToList(), cancellationToken).ConfigureAwait(false));

        // 5. One disposition per credible secret, shared by every override that refers to it.
        var retirements = new List<PlannedSecretRetirement>();
        foreach (var (guid, holders) in credible)
        {
            Outcome outcome;
            if (surviving.TryGetValue(guid, out var references) && references.Count > 0)
            {
                outcome = Outcome.Retain(references);
            }
            else
            {
                var name = descriptors[guid]!.Name;
                if (holders.Count == 1 && string.Equals(name, PlatformSettingSecretMaterialProvisioner.BuildSecretName(holders[0].Key), StringComparison.Ordinal))
                {
                    outcome = Outcome.Retire();
                    retirements.Add(new PlannedSecretRetirement(holders[0].Key, guid));
                }
                else
                {
                    outcome = Outcome.Ambiguous(
                        name.StartsWith(NamePrefix, StringComparison.Ordinal)
                            ? IntegrationSecretAmbiguityReason.SettingKeyMismatch
                            : IntegrationSecretAmbiguityReason.OwnershipNotEstablished);
                }
            }

            foreach (var holder in holders)
                holder.Outcomes.Add(outcome);
        }

        // 6. One disposition per override: the most consequential outcome of the secrets it refers to.
        var dispositions = items.Select(Combine).OrderBy(d => d.Key, StringComparer.Ordinal).ToList();
        var ambiguous = dispositions.Where(d => d.Kind == IntegrationSecretDispositionKind.Ambiguous).ToList();
        var refusal = ambiguous.Count == 0
            ? null
            : new IntegrationSettingDispositionRefusal(
                ambiguous.Select(d => d.Key).ToList(),
                ambiguous.Select(d => d.AmbiguityReason!.Value).Distinct().OrderBy(r => r).ToList());

        return new IntegrationSettingDeletionPlan(
            integrationId,
            overrides,
            dispositions,
            retirements.OrderBy(r => r.SettingKey, StringComparer.Ordinal).ToList(),
            refusal);
    }

    private static IntegrationOverrideDisposition Combine(Item item)
    {
        if (item.Outcomes.FirstOrDefault(o => o.Kind == IntegrationSecretDispositionKind.Ambiguous) is { } ambiguous)
            return new IntegrationOverrideDisposition(item.Key, ambiguous.Kind, ambiguityReason: ambiguous.AmbiguityReason);
        if (item.Outcomes.Any(o => o.Kind == IntegrationSecretDispositionKind.RetireAfterOverrideRemoval))
            return new IntegrationOverrideDisposition(item.Key, IntegrationSecretDispositionKind.RetireAfterOverrideRemoval);
        if (item.Outcomes.Where(o => o.Kind == IntegrationSecretDispositionKind.RetainSharedReference).ToList() is { Count: > 0 } retained)
        {
            return new IntegrationOverrideDisposition(
                item.Key,
                IntegrationSecretDispositionKind.RetainSharedReference,
                survivingReferences: retained.SelectMany(o => o.Surviving).ToList());
        }

        return new IntegrationOverrideDisposition(
            item.Key,
            IntegrationSecretDispositionKind.NoApplicableSecret,
            noSecretReason: item.Outcomes.FirstOrDefault()?.NoSecretReason ?? IntegrationSecretNoSecretReason.NoReference);
    }

    private sealed class Item(string key)
    {
        public string Key { get; } = key;
        public List<Guid> Candidates { get; } = [];
        public List<Outcome> Outcomes { get; } = [];
    }

    private sealed class Outcome
    {
        private Outcome(IntegrationSecretDispositionKind kind) => Kind = kind;

        public IntegrationSecretDispositionKind Kind { get; }
        public IntegrationSecretNoSecretReason? NoSecretReason { get; private init; }
        public IntegrationSecretAmbiguityReason? AmbiguityReason { get; private init; }
        public IReadOnlyList<SurvivingSecretReference> Surviving { get; private init; } = [];

        public static Outcome NoSecret(IntegrationSecretNoSecretReason reason) =>
            new(IntegrationSecretDispositionKind.NoApplicableSecret) { NoSecretReason = reason };

        public static Outcome Retire() => new(IntegrationSecretDispositionKind.RetireAfterOverrideRemoval);

        public static Outcome Retain(IReadOnlyList<SurvivingSecretReference> references) =>
            new(IntegrationSecretDispositionKind.RetainSharedReference) { Surviving = references };

        public static Outcome Ambiguous(IntegrationSecretAmbiguityReason reason) =>
            new(IntegrationSecretDispositionKind.Ambiguous) { AmbiguityReason = reason };
    }
}
