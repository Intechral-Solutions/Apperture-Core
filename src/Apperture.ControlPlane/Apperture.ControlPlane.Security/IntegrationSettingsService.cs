using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

public sealed class IntegrationSettingsService : IIntegrationSettingsService
{
    private const string AuditOutcome = "integration-settings";

    private readonly IIntegrationSettingDefinitionRegistry _registry;
    private readonly IPlatformSettingValueRepository _repository;
    private readonly IIntegrationSettingsResolver _resolver;
    private readonly PlatformSettingOptionProviderCoordinator _optionProviderCoordinator;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuditService _audit;

    public IntegrationSettingsService(
        IIntegrationSettingDefinitionRegistry registry,
        IPlatformSettingValueRepository repository,
        IIntegrationSettingsResolver resolver,
        PlatformSettingOptionProviderCoordinator optionProviderCoordinator,
        IPrincipalContext principalContext,
        IAuditService audit)
    {
        _registry = registry;
        _repository = repository;
        _resolver = resolver;
        _optionProviderCoordinator = optionProviderCoordinator;
        _principalContext = principalContext;
        _audit = audit;
    }

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
        CancellationToken cancellationToken = default)
    {
        var def = _registry.TryGet(key)
            ?? throw new ArgumentException($"Unknown integration setting key '{key}'.", nameof(key));

        EnsureIntegrationValueScope(def, key);

        if (!def.Editable)
            throw new InvalidOperationException($"Setting '{key}' is not editable.");

        var parsed = PlatformSettingsUpdateParser.Parse(def, payload);

        if (!string.IsNullOrWhiteSpace(def.OptionProviderKey))
        {
            IReadOnlyList<PlatformSettingAllowedValue> options;
            try
            {
                options = await _optionProviderCoordinator
                    .GetOptionsForIntegrationSettingsValidationAsync(def, integrationId, cancellationToken)
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
            .GetScopedAsync(key, PlatformSettingScopeConstants.IntegrationKind, integrationId, cancellationToken)
            .ConfigureAwait(false);
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
            ScopeKind = PlatformSettingScopeConstants.IntegrationKind,
            ScopeId = integrationId,
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
            var providerSuffix = string.IsNullOrWhiteSpace(def.OptionProviderKey)
                ? string.Empty
                : $" optionProviderKey={def.OptionProviderKey}";
            var details =
                $"key={key} scope={PlatformSettingScopeConstants.IntegrationKind}:{integrationId} " +
                $"oldValue={oldDisplay ?? "(none)"} newValue={PlatformSettingsValueCodec.ToDisplayString(parsed)}{providerSuffix}";
            await AppendAuditAsync(
                IntegrationSettingsAuditActionNames.Set,
                integrationId,
                actor.Id,
                details,
                cancellationToken).ConfigureAwait(false);
        }

        return (await _resolver.GetResolvedAsync(integrationId, key, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<PlatformSettingResolvedValue?> ResetAsync(
        string integrationId,
        string key,
        CancellationToken cancellationToken = default)
    {
        var def = _registry.TryGet(key);
        if (def is null)
            return null;

        EnsureIntegrationValueScope(def, key);

        var existing = await _repository
            .GetScopedAsync(key, PlatformSettingScopeConstants.IntegrationKind, integrationId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
            return await _resolver.GetResolvedAsync(integrationId, key, cancellationToken).ConfigureAwait(false);

        var deleted = await _repository
            .DeleteScopedAsync(key, PlatformSettingScopeConstants.IntegrationKind, integrationId, cancellationToken)
            .ConfigureAwait(false);
        if (!deleted)
            return await _resolver.GetResolvedAsync(integrationId, key, cancellationToken).ConfigureAwait(false);

        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is not null)
        {
            var previous = PlatformSettingsValueCodec.ToDisplayString(
                PlatformSettingsValueCodec.ParseFromStoredJson(def, existing.ValueJson));
            await AppendAuditAsync(
                IntegrationSettingsAuditActionNames.Reset,
                integrationId,
                actor.Id,
                $"key={key} scope={PlatformSettingScopeConstants.IntegrationKind}:{integrationId} previousValue={previous}",
                cancellationToken).ConfigureAwait(false);
        }

        return await _resolver.GetResolvedAsync(integrationId, key, cancellationToken).ConfigureAwait(false);
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

    private async ValueTask AppendAuditAsync(
        string action,
        string integrationId,
        ActorId actorId,
        string details,
        CancellationToken cancellationToken)
    {
        var record = new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            actorId,
            action,
            new ResourceDescriptor(ResourceTypeConstants.Integration, integrationId),
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
