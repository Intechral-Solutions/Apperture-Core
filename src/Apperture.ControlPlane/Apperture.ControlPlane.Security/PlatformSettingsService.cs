using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Notifications.Diagnostics;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Platform settings (instance value scope). Every change is one audited unit (AUD-1.4): the setting row, the secret a
/// sensitive setting owns, the store's own evidence and the audit rows commit together or not at all.
/// </summary>
/// <remarks>
/// <para>Only validation that needs no stored state (the definition, the payload's shape) runs before the unit. Everything the
/// decision depends on (the override row, its row version, the notification safety check, the provider's allowed values) is
/// read inside the unit's transaction. A sensitive replace stores its version, activates it and updates the row in that
/// transaction, so the previous secret stays live until the change commits and there is nothing to compensate if it does not.</para>
/// <para>A change that leaves the stored state as it was (an identical value, a clear or reset of an absent override) is declared a
/// no-op and records nothing.</para>
/// </remarks>
public sealed class PlatformSettingsService : IPlatformSettingsService
{
    private readonly IPlatformSettingDefinitionRegistry _registry;
    private readonly IPlatformSettingValueRepository _repository;
    private readonly IPlatformSettingsResolver _resolver;
    private readonly PlatformSettingOptionProviderCoordinator _optionProviderCoordinator;
    private readonly PlatformSettingSecretMaterialProvisioner _secretProvisioner;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;

    public PlatformSettingsService(
        IPlatformSettingDefinitionRegistry registry,
        IPlatformSettingValueRepository repository,
        IPlatformSettingsResolver resolver,
        PlatformSettingOptionProviderCoordinator optionProviderCoordinator,
        PlatformSettingSecretMaterialProvisioner secretProvisioner,
        IAuditedUnitOfWork? auditedUnitOfWork = null)
    {
        _registry = registry;
        _repository = repository;
        _resolver = resolver;
        _optionProviderCoordinator = optionProviderCoordinator;
        _secretProvisioner = secretProvisioner;
        _auditedUnitOfWork = auditedUnitOfWork;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new AuditInvariantException("Platform setting changes require the audited unit of work.");

    private static AuditedOperation Operation(string primaryAction) => new(primaryAction, AuditActor.CurrentPrincipal);

    public Task<IReadOnlyList<PlatformSettingResolvedValue>> ListAsync(CancellationToken cancellationToken = default) =>
        _resolver.ListResolvedAsync(cancellationToken);

    public Task<PlatformSettingResolvedValue?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        _resolver.GetResolvedAsync(key, cancellationToken);

    public async Task<PlatformSettingResolvedValue> SetAsync(
        string key,
        PlatformSettingUpdatePayload payload,
        string? rowVersionBase64,
        CancellationToken cancellationToken = default)
    {
        var def = _registry.TryGet(key)
            ?? throw new ArgumentException($"Unknown platform setting key '{key}'.", nameof(key));

        if (!def.AllowedValueScopes.Contains(PlatformSettingValueScopeKind.Instance))
        {
            throw new ArgumentException(
                $"Setting '{key}' is not applicable at instance value scope.",
                nameof(key));
        }

        if (!def.Editable)
            throw new InvalidOperationException($"Setting '{key}' is not editable.");

        if (def.IsSensitive)
            return await SetSensitiveAsync(def, payload, rowVersionBase64, cancellationToken).ConfigureAwait(false);

        return await SetOrdinaryAsync(def, payload, rowVersionBase64, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PlatformSettingResolvedValue?> ResetAsync(string key, CancellationToken cancellationToken = default)
    {
        var def = _registry.TryGet(key);
        if (def is null)
            return null;

        if (!def.AllowedValueScopes.Contains(PlatformSettingValueScopeKind.Instance))
            return null;

        return await SettingAuditedChange.ExecuteAsync(
            Unit,
            Operation(AuditActionNames.PlatformSettingReset),
            async (transaction, ct) =>
            {
                var existing = await _repository.GetByKeyAsync(key, ct).ConfigureAwait(false);
                if (existing is null)
                {
                    // No override: nothing to reset and nothing recorded.
                    transaction.DeclareNoOp();
                    return false;
                }

                // A malformed override is still reset: a value that cannot be displayed is left out of the audit details.
                var previousRef = def.IsSensitive ? ReadSecretRef(def, existing) : null;
                await transaction.StageAsync(
                    def.IsSensitive
                        ? AuditEvents.PlatformSettingReset(key, sensitive: true, previousConfigured: !string.IsNullOrWhiteSpace(previousRef))
                        : AuditEvents.PlatformSettingReset(
                            key, sensitive: false, previousValue: PlatformSettingAuditValues.DisplayStoredOrNull(def, existing.ValueJson)),
                    ct).ConfigureAwait(false);

                if (!await _repository.DeleteAsync(key, ct).ConfigureAwait(false))
                {
                    // The override was removed by another request after this one read it: not a reset this request made.
                    throw new DbUpdateConcurrencyException("Platform setting was modified by another request.");
                }

                if (def.IsSensitive
                    && await _secretProvisioner.TryDisableOwnedSecretAsync(SettingSecretOwnership.ForPlatformSetting(key), previousRef, ct).ConfigureAwait(false))
                {
                    await transaction.StageAsync(AuditEvents.PlatformSettingSecretRetired(key, "reset"), ct).ConfigureAwait(false);
                }

                return true;
            },
            ct => _resolver.GetResolvedAsync(key, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<PlatformSettingResolvedValue> SetSensitiveAsync(
        PlatformSettingDefinition def,
        PlatformSettingUpdatePayload payload,
        string? rowVersionBase64,
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

            // Unchanged: blank / omitted operation leaves the current secret as-is.
            return (await _resolver.GetResolvedAsync(def.Key, cancellationToken).ConfigureAwait(false))!;
        }

        if (operation == PlatformSettingSecretOperations.Clear)
            return await ClearSensitiveAsync(def, cancellationToken).ConfigureAwait(false);

        // Replace
        if (string.IsNullOrWhiteSpace(payload.SecretValue))
            throw new ArgumentException("SecretValue is required when SecretOperation is replace.", nameof(payload));

        return (await SettingAuditedChange.ExecuteAsync(
            Unit,
            Operation(AuditActionNames.PlatformSettingSet),
            async (transaction, ct) =>
            {
                var existing = await _repository.GetByKeyAsync(def.Key, ct).ConfigureAwait(false);
                var existingRef = ReadSecretRef(def, existing);

                // A stale row version is a conflict before anything is stored.
                var expectedRowVersion = ParseExpectedRowVersion(existing, rowVersionBase64);
                RequireCurrentRowVersion(existing, expectedRowVersion);

                // The secret store joins this transaction: the new version is live only once the change commits.
                var (secretRef, createdNew) = await _secretProvisioner
                    .ReplaceAsync(SettingSecretOwnership.ForPlatformSetting(def.Key), payload.SecretValue, existingRef, ct)
                    .ConfigureAwait(false);

                await _repository.UpsertAsync(
                    new PlatformSettingValue
                    {
                        Key = def.Key,
                        ScopeKind = PlatformSettingScopeConstants.InstanceKind,
                        ScopeId = PlatformSettingScopeConstants.InstanceId,
                        ValueJson = PlatformSettingsValueCodec.ToStoredJson(def, secretRef),
                        UpdatedByPrincipalId = transaction.Actor.RecordedValue
                    },
                    expectedRowVersion,
                    ct).ConfigureAwait(false);

                await transaction.StageAsync(
                    AuditEvents.PlatformSettingSet(
                        def.Key,
                        AuditEvents.SettingOperations.Replace,
                        sensitive: true,
                        previousConfigured: !string.IsNullOrWhiteSpace(existingRef)),
                    ct).ConfigureAwait(false);
                await transaction.StageAsync(
                    AuditEvents.PlatformSettingSecretStored(def.Key, createdNew ? "created" : "replaced"),
                    ct).ConfigureAwait(false);
                return true;
            },
            ct => _resolver.GetResolvedAsync(def.Key, ct),
            cancellationToken).ConfigureAwait(false))!;
    }

    private async Task<PlatformSettingResolvedValue> ClearSensitiveAsync(
        PlatformSettingDefinition def,
        CancellationToken cancellationToken)
    {
        return (await SettingAuditedChange.ExecuteAsync(
            Unit,
            Operation(AuditActionNames.PlatformSettingSet),
            async (transaction, ct) =>
            {
                var existing = await _repository.GetByKeyAsync(def.Key, ct).ConfigureAwait(false);
                if (existing is null)
                {
                    transaction.DeclareNoOp();
                    return false;
                }

                var previousRef = ReadSecretRef(def, existing);
                await transaction.StageAsync(
                    AuditEvents.PlatformSettingSet(
                        def.Key,
                        AuditEvents.SettingOperations.Clear,
                        sensitive: true,
                        previousConfigured: !string.IsNullOrWhiteSpace(previousRef)),
                    ct).ConfigureAwait(false);

                if (!await _repository.DeleteAsync(def.Key, ct).ConfigureAwait(false))
                    throw new DbUpdateConcurrencyException("Platform setting was modified by another request.");

                if (await _secretProvisioner.TryDisableOwnedSecretAsync(SettingSecretOwnership.ForPlatformSetting(def.Key), previousRef, ct).ConfigureAwait(false))
                    await transaction.StageAsync(AuditEvents.PlatformSettingSecretRetired(def.Key, "cleared"), ct).ConfigureAwait(false);
                return true;
            },
            ct => _resolver.GetResolvedAsync(def.Key, ct),
            cancellationToken).ConfigureAwait(false))!;
    }

    private async Task<PlatformSettingResolvedValue> SetOrdinaryAsync(
        PlatformSettingDefinition def,
        PlatformSettingUpdatePayload payload,
        string? rowVersionBase64,
        CancellationToken cancellationToken)
    {
        var key = def.Key;
        var parsed = PlatformSettingsUpdateParser.Parse(def, payload);

        return (await SettingAuditedChange.ExecuteAsync(
            Unit,
            Operation(AuditActionNames.PlatformSettingSet),
            async (transaction, ct) =>
            {
                if (!string.IsNullOrWhiteSpace(def.OptionProviderKey))
                {
                    IReadOnlyList<PlatformSettingAllowedValue> options;
                    try
                    {
                        options = await _optionProviderCoordinator
                            .GetOptionsForValidationAsync(def, payload.IntegrationId, ct)
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

                await ValidateNotificationSafetyAsync(key, parsed, ct).ConfigureAwait(false);

                var existing = await _repository.GetByKeyAsync(key, ct).ConfigureAwait(false);
                var expectedRowVersion = ParseExpectedRowVersion(existing, rowVersionBase64);
                RequireCurrentRowVersion(existing, expectedRowVersion);

                var storedJson = PlatformSettingsValueCodec.ToStoredJson(def, parsed);
                if (existing is not null && string.Equals(existing.ValueJson, storedJson, StringComparison.Ordinal))
                {
                    // The same value: nothing to write and no row.
                    transaction.DeclareNoOp();
                    return false;
                }

                await _repository.UpsertAsync(
                    new PlatformSettingValue
                    {
                        Key = key,
                        ScopeKind = PlatformSettingScopeConstants.InstanceKind,
                        ScopeId = PlatformSettingScopeConstants.InstanceId,
                        ValueJson = storedJson,
                        UpdatedByPrincipalId = transaction.Actor.RecordedValue
                    },
                    expectedRowVersion,
                    ct).ConfigureAwait(false);

                await transaction.StageAsync(
                    AuditEvents.PlatformSettingSet(
                        key,
                        AuditEvents.SettingOperations.Set,
                        sensitive: false,
                        previousValue: existing is null ? null : PlatformSettingAuditValues.DisplayStoredOrNull(def, existing.ValueJson),
                        newValue: PlatformSettingAuditValues.DisplayOrNull(def, parsed)),
                    ct).ConfigureAwait(false);
                return true;
            },
            ct => _resolver.GetResolvedAsync(key, ct),
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
            throw new DbUpdateConcurrencyException("Platform setting was modified by another request.");
    }

    private async Task ValidateNotificationSafetyAsync(string key, object parsedValue, CancellationToken cancellationToken)
    {
        var resolved = await _resolver.ListResolvedAsync(cancellationToken).ConfigureAwait(false);
        var effective = resolved.ToDictionary(x => x.Key, x => x.CurrentValue, StringComparer.Ordinal);
        effective[key] = parsedValue;

        var snapshot = BuildNotificationSnapshot(effective);
        var warnings = NotificationConfigurationSafetyValidator.Validate(snapshot);
        if (warnings.Count > 0)
        {
            var message = string.Join(" ", warnings.Select(w => w.Message));
            throw new InvalidOperationException(message);
        }
    }

    internal static NotificationConfigurationSnapshot BuildNotificationSnapshot(IReadOnlyDictionary<string, object> effective) =>
        new(
            GetBool(effective, PlatformSettingKeys.PlatformEventsEnabled),
            GetBool(effective, PlatformSettingKeys.NotificationsEvaluationEnabled),
            GetBool(effective, PlatformSettingKeys.NotificationsInstancesEnabled),
            GetBool(effective, PlatformSettingKeys.NotificationsDeliveryEnabled));

    private static bool GetBool(IReadOnlyDictionary<string, object> effective, string key) =>
        effective.TryGetValue(key, out var value) && value is bool b && b;

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
