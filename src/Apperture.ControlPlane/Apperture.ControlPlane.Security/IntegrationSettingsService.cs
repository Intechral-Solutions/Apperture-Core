using Apperture.Core.Application.Audit;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Integration-scoped settings. Every change is one audited unit (AUD-1.4), as for platform settings: the setting row, the
/// secret a sensitive setting owns, the store's own evidence and the audit rows commit together or not at all, and the decision
/// reads (the override row, its row version, the provider's allowed values) are taken inside the unit's transaction.
/// </summary>
/// <remarks>
/// A setting's rows target the integration setting (the integration and the key) and carry the integration's owning resource
/// context as a related reference, as the previous audit records did. Integration delete does not go through this service: it
/// removes every override by row and disposes of their secrets under owner ruling H1 inside its own unit (slice 3).
/// <para><b>Integration row lock (slice 3).</b> Nothing has a foreign key from a setting to its integration, so a change that
/// commits after an integration delete has passed its checks would leave an orphaned override, and for a sensitive setting an
/// orphaned live secret. Every public change therefore takes the integration row lock as the first act of its unit's attempt,
/// after the unit's operation lock and before any setting row is read, and keeps it to the commit. An integration that is gone by
/// then is a <see cref="KeyNotFoundException"/>: nothing is changed, staged or provisioned. The lock order is the unit's
/// operation lock, the integration row, the setting row, then the secret. The integration delete takes the same row lock in its
/// own unit, so a change and a delete of one integration are serialized. The in-transaction reset takes no lock of its own.</para>
/// </remarks>
public sealed class IntegrationSettingsService : IIntegrationSettingsService, IIntegrationSettingResetInTransaction
{
    private readonly IIntegrationSettingDefinitionRegistry _registry;
    private readonly IPlatformSettingValueRepository _repository;
    private readonly IIntegrationSettingsResolver _resolver;
    private readonly PlatformSettingOptionProviderCoordinator _optionProviderCoordinator;
    private readonly PlatformSettingSecretMaterialProvisioner _secretProvisioner;
    private readonly IIntegrationRepository _integrations;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;
    private readonly IntegrationSettingSecretClassifier? _legacyDisposition;

    public IntegrationSettingsService(
        IIntegrationSettingDefinitionRegistry registry,
        IPlatformSettingValueRepository repository,
        IIntegrationSettingsResolver resolver,
        PlatformSettingOptionProviderCoordinator optionProviderCoordinator,
        PlatformSettingSecretMaterialProvisioner secretProvisioner,
        IIntegrationRepository integrations,
        IAuditedUnitOfWork? auditedUnitOfWork = null,
        IntegrationSettingSecretClassifier? legacyDisposition = null)
    {
        _registry = registry;
        _repository = repository;
        _resolver = resolver;
        _optionProviderCoordinator = optionProviderCoordinator;
        _secretProvisioner = secretProvisioner;
        _integrations = integrations;
        _auditedUnitOfWork = auditedUnitOfWork;
        _legacyDisposition = legacyDisposition;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new AuditInvariantException("Integration setting changes require the audited unit of work.");

    private static AuditedOperation Operation(string primaryAction) => new(primaryAction, AuditActor.CurrentPrincipal);

    public Task<IReadOnlyList<PlatformSettingResolvedValue>> ListAsync(
        string integrationId,
        CancellationToken cancellationToken = default) =>
        _resolver.ListResolvedAsync(integrationId, cancellationToken);

    public Task<PlatformSettingResolvedValue?> GetAsync(
        string integrationId,
        string key,
        CancellationToken cancellationToken = default) =>
        _resolver.GetResolvedAsync(integrationId, key, cancellationToken);

    public async Task<PlatformSettingResolvedValue> SetAsync(
        string integrationId,
        string key,
        PlatformSettingUpdatePayload payload,
        string? rowVersionBase64,
        CancellationToken cancellationToken = default,
        Func<CancellationToken, ValueTask>? reauthorize = null)
    {
        var def = _registry.TryGet(key)
            ?? throw new ArgumentException($"Unknown integration setting key '{key}'.", nameof(key));

        EnsureIntegrationValueScope(def, key);

        if (!def.Editable)
            throw new InvalidOperationException($"Setting '{key}' is not editable.");

        if (def.IsSensitive)
            return await SetSensitiveAsync(integrationId, def, payload, rowVersionBase64, reauthorize, cancellationToken)
                .ConfigureAwait(false);

        return await SetOrdinaryAsync(integrationId, def, payload, rowVersionBase64, reauthorize, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PlatformSettingResolvedValue?> ResetAsync(
        string integrationId,
        string key,
        CancellationToken cancellationToken = default,
        Func<CancellationToken, ValueTask>? reauthorize = null)
    {
        var def = _registry.TryGet(key);
        if (def is null)
            return null;

        EnsureIntegrationValueScope(def, key);

        return await SettingAuditedChange.ExecuteAsync(
            Unit,
            Operation(AuditActionNames.IntegrationSettingReset),
            async (transaction, ct) =>
            {
                await LockIntegrationAsync(integrationId, reauthorize, ct).ConfigureAwait(false);
                if (await ResetCoreAsync(transaction, integrationId, def, ct).ConfigureAwait(false))
                    return true;

                // No override: nothing to reset and nothing recorded.
                transaction.DeclareNoOp();
                return false;
            },
            ct => _resolver.GetResolvedAsync(integrationId, key, ct),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    async Task<bool> IIntegrationSettingResetInTransaction.ResetInTransactionAsync(
        IAuditedTransaction transaction,
        string integrationId,
        string key,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        // A key with no registered definition is left alone, as ResetAsync leaves it.
        var def = _registry.TryGet(key);
        if (def is null)
            return false;

        EnsureIntegrationValueScope(def, key);
        return await ResetCoreAsync(transaction, integrationId, def, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes the integration's override of the setting in the caller's transaction, disables the secret a sensitive setting
    /// owns, and stages the rows. Returns false, having changed and staged nothing, when there is no override. The audit rows
    /// are staged before anything changes, so a transaction that is not the current one fails here first.
    /// </summary>
    private async Task<bool> ResetCoreAsync(
        IAuditedTransaction transaction,
        string integrationId,
        PlatformSettingDefinition def,
        CancellationToken ct)
    {
        var key = def.Key;
        var existing = await _repository
            .GetScopedAsync(key, PlatformSettingScopeConstants.IntegrationKind, integrationId, ct)
            .ConfigureAwait(false);
        if (existing is null)
            return false;

        var resourceContextId = await ResolveResourceContextIdAsync(integrationId, ct).ConfigureAwait(false);
        var previousRef = def.IsSensitive ? ReadSecretRef(def, existing) : null;
        await transaction.StageAsync(
            def.IsSensitive
                ? AuditEvents.IntegrationSettingReset(
                    integrationId, key, sensitive: true, previousConfigured: !string.IsNullOrWhiteSpace(previousRef), resourceContextId: resourceContextId)
                : AuditEvents.IntegrationSettingReset(
                    integrationId, key, sensitive: false,
                    previousValue: PlatformSettingAuditValues.DisplayStoredOrNull(def, existing.ValueJson),
                    resourceContextId: resourceContextId),
            ct).ConfigureAwait(false);

        if (!await _repository
                .DeleteScopedAsync(key, PlatformSettingScopeConstants.IntegrationKind, integrationId, ct)
                .ConfigureAwait(false))
        {
            // Removed by another request after this one read it: not a reset this request made.
            throw new DbUpdateConcurrencyException("Integration setting was modified by another request.");
        }

        if (def.IsSensitive)
            await RetireReleasedSecretAsync(transaction, integrationId, key, previousRef, "reset", resourceContextId, ct).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Retires the secret a setting row has just released (cleared, reset, or repointed at a new secret), in the caller's transaction,
    /// and stages the Secrets row only when one was retired. Only a secret proven to belong to the right holder is ever disabled:
    /// the secret this very setting owns (exact provider and name), or a <i>legacy</i> secret (the instance shape for this key,
    /// which cannot say which integration made it) on H1's evidence that nothing else, in any scope, still refers to it. Every
    /// other secret, whoever's it is, is left exactly as it was. Without a classifier a legacy secret is simply retained.
    /// </summary>
    private async Task RetireReleasedSecretAsync(
        IAuditedTransaction transaction,
        string integrationId,
        string key,
        string? releasedRef,
        string reason,
        string? resourceContextId,
        CancellationToken ct)
    {
        var retired = await _secretProvisioner
            .TryDisableOwnedSecretAsync(SettingSecretOwnership.ForIntegrationSetting(integrationId, key), releasedRef, ct)
            .ConfigureAwait(false);

        if (!retired
            && _legacyDisposition is not null
            && await _legacyDisposition.FindLegacySecretToRetireAsync(key, releasedRef, ct).ConfigureAwait(false) is { } legacy)
        {
            retired = await _secretProvisioner
                .TryDisableOwnedSecretAsync(SettingSecretOwnership.LegacyIntegrationSetting(key), legacy.ToString("D"), ct)
                .ConfigureAwait(false);
        }

        if (retired)
        {
            await transaction.StageAsync(
                AuditEvents.IntegrationSettingSecretRetired(integrationId, key, reason, resourceContextId),
                ct).ConfigureAwait(false);
        }
    }

    private async Task<PlatformSettingResolvedValue> SetSensitiveAsync(
        string integrationId,
        PlatformSettingDefinition def,
        PlatformSettingUpdatePayload payload,
        string? rowVersionBase64,
        Func<CancellationToken, ValueTask>? reauthorize,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(payload.ScalarValue) || payload.ArrayValues is not null)
        {
            throw new ArgumentException(
                $"Sensitive setting '{def.Key}' does not accept Value/Values. Use SecretOperation replace or clear.",
                nameof(payload));
        }

        var operation = PlatformSettingSecretOperations.Normalize(payload.SecretOperation);
        if (operation is null)
        {
            if (!string.IsNullOrWhiteSpace(payload.SecretOperation))
                throw new ArgumentException(
                    $"Unknown SecretOperation '{payload.SecretOperation}'. Use '{PlatformSettingSecretOperations.Replace}' or '{PlatformSettingSecretOperations.Clear}'.",
                    nameof(payload));

            return (await _resolver.GetResolvedAsync(integrationId, def.Key, cancellationToken).ConfigureAwait(false))!;
        }

        if (operation == PlatformSettingSecretOperations.Clear)
            return await ClearSensitiveAsync(integrationId, def, reauthorize, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(payload.SecretValue))
            throw new ArgumentException("SecretValue is required when SecretOperation is replace.", nameof(payload));

        return (await SettingAuditedChange.ExecuteAsync(
            Unit,
            Operation(AuditActionNames.IntegrationSettingSet),
            async (transaction, ct) =>
            {
                await LockIntegrationAsync(integrationId, reauthorize, ct).ConfigureAwait(false);
                var existing = await _repository
                    .GetScopedAsync(def.Key, PlatformSettingScopeConstants.IntegrationKind, integrationId, ct)
                    .ConfigureAwait(false);
                var existingRef = ReadSecretRef(def, existing);

                // A stale row version is a conflict before anything is stored.
                var expectedRowVersion = ParseExpectedRowVersion(existing, rowVersionBase64);
                RequireCurrentRowVersion(existing, expectedRowVersion);

                var resourceContextId = await ResolveResourceContextIdAsync(integrationId, ct).ConfigureAwait(false);

                // The secret store joins this transaction: the new version is live only once the change commits.
                var (secretRef, createdNew) = await _secretProvisioner
                    .ReplaceAsync(SettingSecretOwnership.ForIntegrationSetting(integrationId, def.Key), payload.SecretValue, existingRef, ct)
                    .ConfigureAwait(false);

                await _repository.UpsertAsync(
                    new PlatformSettingValue
                    {
                        Key = def.Key,
                        ScopeKind = PlatformSettingScopeConstants.IntegrationKind,
                        ScopeId = integrationId,
                        ValueJson = PlatformSettingsValueCodec.ToStoredJson(def, secretRef),
                        UpdatedByPrincipalId = transaction.Actor.RecordedValue
                    },
                    expectedRowVersion,
                    ct).ConfigureAwait(false);

                await transaction.StageAsync(
                    AuditEvents.IntegrationSettingSet(
                        integrationId,
                        def.Key,
                        AuditEvents.SettingOperations.Replace,
                        sensitive: true,
                        previousConfigured: !string.IsNullOrWhiteSpace(existingRef),
                        resourceContextId: resourceContextId),
                    ct).ConfigureAwait(false);
                await transaction.StageAsync(
                    AuditEvents.IntegrationSettingSecretStored(integrationId, def.Key, createdNew ? "created" : "replaced", resourceContextId),
                    ct).ConfigureAwait(false);

                // The row now names a new secret. A reference it named before that was not this setting's own (a legacy secret, say)
                // is released: it is retired only on H1's evidence, and every other secret is left untouched.
                if (createdNew && !string.IsNullOrWhiteSpace(existingRef))
                    await RetireReleasedSecretAsync(transaction, integrationId, def.Key, existingRef, "replaced", resourceContextId, ct).ConfigureAwait(false);
                return true;
            },
            ct => _resolver.GetResolvedAsync(integrationId, def.Key, ct),
            cancellationToken).ConfigureAwait(false))!;
    }

    private async Task<PlatformSettingResolvedValue> ClearSensitiveAsync(
        string integrationId,
        PlatformSettingDefinition def,
        Func<CancellationToken, ValueTask>? reauthorize,
        CancellationToken cancellationToken)
    {
        return (await SettingAuditedChange.ExecuteAsync(
            Unit,
            Operation(AuditActionNames.IntegrationSettingSet),
            async (transaction, ct) =>
            {
                await LockIntegrationAsync(integrationId, reauthorize, ct).ConfigureAwait(false);
                var existing = await _repository
                    .GetScopedAsync(def.Key, PlatformSettingScopeConstants.IntegrationKind, integrationId, ct)
                    .ConfigureAwait(false);
                if (existing is null)
                {
                    transaction.DeclareNoOp();
                    return false;
                }

                var resourceContextId = await ResolveResourceContextIdAsync(integrationId, ct).ConfigureAwait(false);
                var previousRef = ReadSecretRef(def, existing);
                await transaction.StageAsync(
                    AuditEvents.IntegrationSettingSet(
                        integrationId,
                        def.Key,
                        AuditEvents.SettingOperations.Clear,
                        sensitive: true,
                        previousConfigured: !string.IsNullOrWhiteSpace(previousRef),
                        resourceContextId: resourceContextId),
                    ct).ConfigureAwait(false);

                if (!await _repository
                        .DeleteScopedAsync(def.Key, PlatformSettingScopeConstants.IntegrationKind, integrationId, ct)
                        .ConfigureAwait(false))
                {
                    throw new DbUpdateConcurrencyException("Integration setting was modified by another request.");
                }

                await RetireReleasedSecretAsync(transaction, integrationId, def.Key, previousRef, "cleared", resourceContextId, ct).ConfigureAwait(false);
                return true;
            },
            ct => _resolver.GetResolvedAsync(integrationId, def.Key, ct),
            cancellationToken).ConfigureAwait(false))!;
    }

    private async Task<PlatformSettingResolvedValue> SetOrdinaryAsync(
        string integrationId,
        PlatformSettingDefinition def,
        PlatformSettingUpdatePayload payload,
        string? rowVersionBase64,
        Func<CancellationToken, ValueTask>? reauthorize,
        CancellationToken cancellationToken)
    {
        var key = def.Key;
        var parsed = PlatformSettingsUpdateParser.Parse(def, payload);

        return (await SettingAuditedChange.ExecuteAsync(
            Unit,
            Operation(AuditActionNames.IntegrationSettingSet),
            async (transaction, ct) =>
            {
                // Before the provider's options are read: they describe the integration, which is now fixed until the commit.
                await LockIntegrationAsync(integrationId, reauthorize, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(def.OptionProviderKey))
                {
                    IReadOnlyList<PlatformSettingAllowedValue> options;
                    try
                    {
                        options = await _optionProviderCoordinator
                            .GetOptionsForIntegrationSettingsValidationAsync(def, integrationId, ct)
                            .ConfigureAwait(false);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        throw new InvalidOperationException(ex.Message);
                    }
                    catch (KeyNotFoundException ex)
                    {
                        throw new ArgumentException(ex.Message);
                    }

                    ValidateProviderMembership(def, parsed, options, key);
                }

                var existing = await _repository
                    .GetScopedAsync(key, PlatformSettingScopeConstants.IntegrationKind, integrationId, ct)
                    .ConfigureAwait(false);
                var expectedRowVersion = ParseExpectedRowVersion(existing, rowVersionBase64);
                RequireCurrentRowVersion(existing, expectedRowVersion);

                var storedJson = PlatformSettingsValueCodec.ToStoredJson(def, parsed);
                if (existing is not null && string.Equals(existing.ValueJson, storedJson, StringComparison.Ordinal))
                {
                    // The same value: nothing to write and no row.
                    transaction.DeclareNoOp();
                    return false;
                }

                var resourceContextId = await ResolveResourceContextIdAsync(integrationId, ct).ConfigureAwait(false);
                await _repository.UpsertAsync(
                    new PlatformSettingValue
                    {
                        Key = key,
                        ScopeKind = PlatformSettingScopeConstants.IntegrationKind,
                        ScopeId = integrationId,
                        ValueJson = storedJson,
                        UpdatedByPrincipalId = transaction.Actor.RecordedValue
                    },
                    expectedRowVersion,
                    ct).ConfigureAwait(false);

                await transaction.StageAsync(
                    AuditEvents.IntegrationSettingSet(
                        integrationId,
                        key,
                        AuditEvents.SettingOperations.Set,
                        sensitive: false,
                        previousValue: existing is null ? null : PlatformSettingAuditValues.DisplayStoredOrNull(def, existing.ValueJson),
                        newValue: PlatformSettingAuditValues.DisplayOrNull(def, parsed),
                        optionProviderKey: string.IsNullOrWhiteSpace(def.OptionProviderKey) ? null : def.OptionProviderKey,
                        resourceContextId: resourceContextId),
                    ct).ConfigureAwait(false);
                return true;
            },
            ct => _resolver.GetResolvedAsync(integrationId, key, ct),
            cancellationToken).ConfigureAwait(false))!;
    }

    private static string? ReadSecretRef(PlatformSettingDefinition def, PlatformSettingValue? row)
    {
        if (row is null)
            return null;
        try
        {
            return PlatformSettingsValueCodec.ParseFromStoredJson(def, row.ValueJson) as string;
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? ParseExpectedRowVersion(PlatformSettingValue? existing, string? rowVersionBase64)
    {
        if (existing is null)
            return null;

        if (string.IsNullOrWhiteSpace(rowVersionBase64))
            throw new ArgumentException("rowVersionBase64 is required when updating an existing override.", nameof(rowVersionBase64));

        try
        {
            return Convert.FromBase64String(rowVersionBase64);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("rowVersionBase64 is not valid base64.", nameof(rowVersionBase64), ex);
        }
    }

    /// <summary>The row version read in this transaction must be the one the caller saw; otherwise nothing is stored.</summary>
    private static void RequireCurrentRowVersion(PlatformSettingValue? existing, byte[]? expectedRowVersion)
    {
        if (existing?.RowVersion is { } current && expectedRowVersion is not null && !current.SequenceEqual(expectedRowVersion))
            throw new DbUpdateConcurrencyException("Integration setting was modified by another request.");
    }

    private static void EnsureIntegrationValueScope(PlatformSettingDefinition def, string key)
    {
        if (!def.AllowedValueScopes.Contains(PlatformSettingValueScopeKind.Integration))
        {
            throw new ArgumentException(
                $"Setting '{key}' is not applicable at integration value scope.",
                nameof(key));
        }
    }

    /// <summary>
    /// Takes the integration row lock (<c>UPDLOCK</c>, held to the end of the unit's transaction) and requires the integration to
    /// exist, then re-runs the caller's authorization against it. Must be the first act of an attempt, after the unit's operation
    /// lock; the helper opens no transaction of its own and the lock is released only by the unit's commit or rollback.
    /// </summary>
    private async Task LockIntegrationAsync(
        string integrationId,
        Func<CancellationToken, ValueTask>? reauthorize,
        CancellationToken cancellationToken)
    {
        // Untracked, so a rerun of the attempt after a verified rollback reads the row afresh.
        if (await _integrations.GetByIdForUpdateAsync(new IntegrationId(integrationId), cancellationToken).ConfigureAwait(false) is null)
            throw new KeyNotFoundException($"Integration {integrationId} not found.");

        // The row is now fixed until the commit: an ownership move that committed before the lock is visible to this check, and
        // none can commit after it.
        if (reauthorize is not null)
            await reauthorize(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<string?> ResolveResourceContextIdAsync(string integrationId, CancellationToken cancellationToken)
    {
        var integration = await _integrations.GetByIdAsync(new IntegrationId(integrationId), cancellationToken).ConfigureAwait(false);
        return integration?.ResourceContextId.Value;
    }

    private static void ValidateProviderMembership(
        PlatformSettingDefinition def,
        object parsed,
        IReadOnlyList<PlatformSettingAllowedValue> options,
        string settingKey)
    {
        var allowed = options.Select(o => o.Value).ToHashSet(StringComparer.Ordinal);

        switch (def.ValueType)
        {
            case PlatformSettingValueType.String:
            {
                if (parsed is not string s)
                    throw new InvalidOperationException($"Parsed value type mismatch for setting '{settingKey}'.");

                if (!allowed.Contains(s))
                    throw new ArgumentException($"Value '{s}' is not an allowed option for '{settingKey}'.");

                break;
            }

            case PlatformSettingValueType.StringArray:
            {
                var values = parsed is string[] arr ? arr : PlatformSettingsValueCodec.CoerceStringArray(parsed);
                var invalid = values.Where(v => !allowed.Contains(v)).Distinct(StringComparer.Ordinal).ToArray();
                if (invalid.Length > 0)
                    throw new ArgumentException(
                        $"Values [{string.Join(", ", invalid)}] are not allowed options for '{settingKey}'.");

                break;
            }

            default:
                throw new InvalidOperationException(
                    $"OptionProviderKey is only supported for Select/MultiSelect settings. Setting '{settingKey}' has ValueType '{def.ValueType}'.");
        }
    }
}
