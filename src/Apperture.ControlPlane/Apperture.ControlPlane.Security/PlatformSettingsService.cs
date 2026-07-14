using Apperture.Core.Application.Notifications.Diagnostics;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

public sealed class PlatformSettingsService : IPlatformSettingsService
{
    private const string AuditOutcome = "platform-settings";

    private readonly IPlatformSettingDefinitionRegistry _registry;
    private readonly IPlatformSettingValueRepository _repository;
    private readonly IPlatformSettingsResolver _resolver;
    private readonly PlatformSettingOptionProviderCoordinator _optionProviderCoordinator;
    private readonly PlatformSettingSecretMaterialProvisioner _secretProvisioner;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuditService _audit;

    public PlatformSettingsService(
        IPlatformSettingDefinitionRegistry registry,
        IPlatformSettingValueRepository repository,
        IPlatformSettingsResolver resolver,
        PlatformSettingOptionProviderCoordinator optionProviderCoordinator,
        PlatformSettingSecretMaterialProvisioner secretProvisioner,
        IPrincipalContext principalContext,
        IAuditService audit)
    {
        _registry = registry;
        _repository = repository;
        _resolver = resolver;
        _optionProviderCoordinator = optionProviderCoordinator;
        _secretProvisioner = secretProvisioner;
        _principalContext = principalContext;
        _audit = audit;
    }

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

        var existing = await _repository.GetByKeyAsync(key, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return await _resolver.GetResolvedAsync(key, cancellationToken).ConfigureAwait(false);

        string? previousRef = null;
        if (def.IsSensitive)
        {
            try
            {
                previousRef = PlatformSettingsValueCodec.ParseFromStoredJson(def, existing.ValueJson) as string;
            }
            catch
            {
                previousRef = null;
            }
        }

        var deleted = await _repository.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
        if (!deleted)
            return await _resolver.GetResolvedAsync(key, cancellationToken).ConfigureAwait(false);

        if (def.IsSensitive)
        {
            await _secretProvisioner
                .TryDisableOwnedSecretAsync(key, previousRef, cancellationToken)
                .ConfigureAwait(false);
        }

        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is not null)
        {
            var details = def.IsSensitive
                ? $"key={key} operation=reset sensitive=true previousConfigured={(string.IsNullOrWhiteSpace(previousRef) ? "false" : "true")}"
                : $"key={key} previousValue={PlatformSettingsValueCodec.ToDisplayString(PlatformSettingsValueCodec.ParseFromStoredJson(def, existing.ValueJson))}";
            await AppendAuditAsync(
                PlatformSettingsAuditActionNames.Reset,
                actor.Id,
                details,
                cancellationToken).ConfigureAwait(false);
        }

        return await _resolver.GetResolvedAsync(key, cancellationToken).ConfigureAwait(false);
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

        var existing = await _repository.GetByKeyAsync(def.Key, cancellationToken).ConfigureAwait(false);
        string? existingRef = null;
        if (existing is not null)
        {
            try
            {
                existingRef = PlatformSettingsValueCodec.ParseFromStoredJson(def, existing.ValueJson) as string;
            }
            catch
            {
                existingRef = null;
            }
        }

        // Config-only GUID refs are not rotated in place — create a new owned secret and write a DB override.
        byte[]? expectedRowVersion = ParseExpectedRowVersion(existing, rowVersionBase64);

        var (secretRef, createdNew) = await _secretProvisioner
            .ReplaceAsync(def.Key, payload.SecretValue, existingRef, cancellationToken)
            .ConfigureAwait(false);

        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        var domain = new PlatformSettingValue
        {
            Key = def.Key,
            ScopeKind = PlatformSettingScopeConstants.InstanceKind,
            ScopeId = PlatformSettingScopeConstants.InstanceId,
            ValueJson = PlatformSettingsValueCodec.ToStoredJson(def, secretRef),
            UpdatedByPrincipalId = actor?.Id.Value
        };

        try
        {
            await _repository.UpsertAsync(domain, expectedRowVersion, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (createdNew)
            {
                await _secretProvisioner
                    .TryDisableOwnedSecretAsync(def.Key, secretRef, cancellationToken)
                    .ConfigureAwait(false);
            }

            throw;
        }

        if (actor is not null)
        {
            await AppendAuditAsync(
                PlatformSettingsAuditActionNames.Set,
                actor.Id,
                $"key={def.Key} operation=replace sensitive=true configured=true",
                cancellationToken).ConfigureAwait(false);
        }

        return (await _resolver.GetResolvedAsync(def.Key, cancellationToken).ConfigureAwait(false))!;
    }

    private async Task<PlatformSettingResolvedValue> ClearSensitiveAsync(
        PlatformSettingDefinition def,
        CancellationToken cancellationToken)
    {
        var existing = await _repository.GetByKeyAsync(def.Key, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return (await _resolver.GetResolvedAsync(def.Key, cancellationToken).ConfigureAwait(false))!;

        string? previousRef = null;
        try
        {
            previousRef = PlatformSettingsValueCodec.ParseFromStoredJson(def, existing.ValueJson) as string;
        }
        catch
        {
            previousRef = null;
        }

        await _repository.DeleteAsync(def.Key, cancellationToken).ConfigureAwait(false);
        await _secretProvisioner
            .TryDisableOwnedSecretAsync(def.Key, previousRef, cancellationToken)
            .ConfigureAwait(false);

        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is not null)
        {
            await AppendAuditAsync(
                PlatformSettingsAuditActionNames.Set,
                actor.Id,
                $"key={def.Key} operation=clear sensitive=true previousConfigured={(string.IsNullOrWhiteSpace(previousRef) ? "false" : "true")}",
                cancellationToken).ConfigureAwait(false);
        }

        return (await _resolver.GetResolvedAsync(def.Key, cancellationToken).ConfigureAwait(false))!;
    }

    private async Task<PlatformSettingResolvedValue> SetOrdinaryAsync(
        PlatformSettingDefinition def,
        PlatformSettingUpdatePayload payload,
        string? rowVersionBase64,
        CancellationToken cancellationToken)
    {
        var key = def.Key;
        var parsed = PlatformSettingsUpdateParser.Parse(def, payload);

        if (!string.IsNullOrWhiteSpace(def.OptionProviderKey))
        {
            IReadOnlyList<PlatformSettingAllowedValue> options;
            try
            {
                options = await _optionProviderCoordinator
                    .GetOptionsForValidationAsync(def, payload.IntegrationId, cancellationToken)
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

        await ValidateNotificationSafetyAsync(key, parsed, cancellationToken).ConfigureAwait(false);

        var existing = await _repository.GetByKeyAsync(key, cancellationToken).ConfigureAwait(false);
        var oldDisplay = existing is null
            ? null
            : PlatformSettingsValueCodec.ToDisplayString(PlatformSettingsValueCodec.ParseFromStoredJson(def, existing.ValueJson));

        byte[]? expectedRowVersion = ParseExpectedRowVersion(existing, rowVersionBase64);

        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        var principalId = actor?.Id.Value;

        var domain = new PlatformSettingValue
        {
            Key = key,
            ScopeKind = PlatformSettingScopeConstants.InstanceKind,
            ScopeId = PlatformSettingScopeConstants.InstanceId,
            ValueJson = PlatformSettingsValueCodec.ToStoredJson(def, parsed),
            UpdatedByPrincipalId = principalId
        };

        await _repository.UpsertAsync(domain, expectedRowVersion, cancellationToken).ConfigureAwait(false);

        if (actor is not null)
        {
            var details =
                $"key={key} scope={PlatformSettingScopeConstants.InstanceKind}:{PlatformSettingScopeConstants.InstanceId} " +
                $"oldValue={oldDisplay ?? "(none)"} newValue={PlatformSettingsValueCodec.ToDisplayString(parsed)}";
            await AppendAuditAsync(
                PlatformSettingsAuditActionNames.Set,
                actor.Id,
                details,
                cancellationToken).ConfigureAwait(false);
        }

        return (await _resolver.GetResolvedAsync(key, cancellationToken).ConfigureAwait(false))!;
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
            GetBool(effective, PlatformSettingKeys.NotificationsDeliveryEnabled),
            GetBool(effective, PlatformSettingKeys.NotificationsUsePlatformPipelineEnabled));

    private static bool GetBool(IReadOnlyDictionary<string, object> effective, string key) =>
        effective.TryGetValue(key, out var value) && value is bool b && b;

    private async ValueTask AppendAuditAsync(string action, ActorId actorId, string details, CancellationToken cancellationToken)
    {
        var record = new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            actorId,
            action,
            new ResourceDescriptor(ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance),
            AuditOutcome,
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            details);
        await _audit.AppendAsync(record, cancellationToken).ConfigureAwait(false);
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
