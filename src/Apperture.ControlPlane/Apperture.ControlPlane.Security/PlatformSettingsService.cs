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
    private readonly IPrincipalContext _principalContext;
    private readonly IAuditService _audit;

    public PlatformSettingsService(
        IPlatformSettingDefinitionRegistry registry,
        IPlatformSettingValueRepository repository,
        IPlatformSettingsResolver resolver,
        IPrincipalContext principalContext,
        IAuditService audit)
    {
        _registry = registry;
        _repository = repository;
        _resolver = resolver;
        _principalContext = principalContext;
        _audit = audit;
    }

    public Task<IReadOnlyList<PlatformSettingResolvedValue>> ListAsync(CancellationToken cancellationToken = default) =>
        _resolver.ListResolvedAsync(cancellationToken);

    public Task<PlatformSettingResolvedValue?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        _resolver.GetResolvedAsync(key, cancellationToken);

    public async Task<PlatformSettingResolvedValue> SetAsync(
        string key,
        string value,
        string? rowVersionBase64,
        CancellationToken cancellationToken = default)
    {
        var def = _registry.TryGet(key)
            ?? throw new ArgumentException($"Unknown platform setting key '{key}'.", nameof(key));

        if (!def.Editable)
            throw new InvalidOperationException($"Setting '{key}' is not editable.");

        var parsed = PlatformSettingsValueCodec.ParseFromInput(def, value);
        await ValidateNotificationSafetyAsync(key, parsed, cancellationToken).ConfigureAwait(false);

        var existing = await _repository.GetByKeyAsync(key, cancellationToken).ConfigureAwait(false);
        var oldDisplay = existing is null
            ? null
            : PlatformSettingsValueCodec.ToDisplayString(PlatformSettingsValueCodec.ParseFromStoredJson(def, existing.ValueJson));

        byte[]? expectedRowVersion = null;
        if (existing is not null)
        {
            if (string.IsNullOrWhiteSpace(rowVersionBase64))
                throw new ArgumentException("rowVersionBase64 is required when updating an existing override.", nameof(rowVersionBase64));

            try
            {
                expectedRowVersion = Convert.FromBase64String(rowVersionBase64);
            }
            catch (FormatException ex)
            {
                throw new ArgumentException("rowVersionBase64 is not valid base64.", nameof(rowVersionBase64), ex);
            }
        }

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

        try
        {
            await _repository.UpsertAsync(domain, expectedRowVersion, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw;
        }

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

    public async Task<PlatformSettingResolvedValue?> ResetAsync(string key, CancellationToken cancellationToken = default)
    {
        var def = _registry.TryGet(key);
        if (def is null)
            return null;

        var existing = await _repository.GetByKeyAsync(key, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return await _resolver.GetResolvedAsync(key, cancellationToken).ConfigureAwait(false);

        var deleted = await _repository.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
        if (!deleted)
            return await _resolver.GetResolvedAsync(key, cancellationToken).ConfigureAwait(false);

        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is not null)
        {
            var previous = PlatformSettingsValueCodec.ToDisplayString(
                PlatformSettingsValueCodec.ParseFromStoredJson(def, existing.ValueJson));
            await AppendAuditAsync(
                PlatformSettingsAuditActionNames.Reset,
                actor.Id,
                $"key={key} previousValue={previous}",
                cancellationToken).ConfigureAwait(false);
        }

        return await _resolver.GetResolvedAsync(key, cancellationToken).ConfigureAwait(false);
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
}
