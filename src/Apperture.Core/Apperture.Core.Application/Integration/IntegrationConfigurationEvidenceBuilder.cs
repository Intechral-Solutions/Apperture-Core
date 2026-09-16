using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;
using Apperture.ModuleSdk.Integration.Configuration;
using Apperture.ModuleSdk.Integration.Readiness;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.Core.Application.Integration;

/// <summary>Gathers persistence-backed evidence for <see cref="ConfigurationReadinessEvaluator"/>.</summary>
public sealed class IntegrationConfigurationEvidenceBuilder
{
    private readonly IIntegrationSettingsResolver _settingsResolver;
    private readonly IConnectionRepository _connectionRepository;
    private readonly IBindingEligibilityService _eligibilityService;
    private readonly IModuleRegistryService _moduleRegistry;

    public IntegrationConfigurationEvidenceBuilder(
        IIntegrationSettingsResolver settingsResolver,
        IConnectionRepository connectionRepository,
        IBindingEligibilityService eligibilityService,
        IModuleRegistryService moduleRegistry)
    {
        _settingsResolver = settingsResolver;
        _connectionRepository = connectionRepository;
        _eligibilityService = eligibilityService;
        _moduleRegistry = moduleRegistry;
    }

    public async Task<ConfigurationEvidenceSnapshot> BuildAsync(
        DomainIntegration integration,
        ModuleIntegrationConfiguration moduleConfiguration,
        IReadOnlyList<Binding> bindings,
        IReadOnlyDictionary<string, Connection?> boundConnections,
        CancellationToken cancellationToken)
    {
        var settingKeys = moduleConfiguration.Settings
            .Select(s => s.Key)
            .ToHashSet(StringComparer.Ordinal);

        var resolved = await _settingsResolver
            .ListResolvedAsync(integration.Id.Value, cancellationToken)
            .ConfigureAwait(false);

        var settingValues = new Dictionary<string, object?>(StringComparer.Ordinal);
        var secretPresence = new Dictionary<string, SecretPresenceState>(StringComparer.Ordinal);
        var invalidSettingKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in resolved.Where(r => settingKeys.Contains(r.Key)))
        {
            if (row.IsSensitive)
            {
                secretPresence[row.Key] = row.IsSecretConfigured
                    ? SecretPresenceState.Present
                    : HasInvalidSecretReference(row)
                        ? SecretPresenceState.InvalidReference
                        : SecretPresenceState.Missing;
                continue;
            }

            settingValues[row.Key] = row.CurrentValue;
            if (!TryValidateSettingValue(
                    moduleConfiguration.Settings.First(s => s.Key == row.Key),
                    row.CurrentValue,
                    out _))
            {
                invalidSettingKeys.Add(row.Key);
            }
        }

        var connections = new Dictionary<string, ConnectionEvidenceSnapshot>(StringComparer.Ordinal);
        var activeBySlot = bindings
            .Where(b => b.State == BindingState.Active)
            .ToLookup(b => b.SlotName, StringComparer.Ordinal);

        foreach (var requirement in moduleConfiguration.Connections)
        {
            var active = activeBySlot[requirement.Key].ToList();
            if (active.Count == 0)
            {
                connections[requirement.Key] = new ConnectionEvidenceSnapshot(IsBound: false);
                continue;
            }

            if (active.Count > 1)
            {
                connections[requirement.Key] = new ConnectionEvidenceSnapshot(
                    IsBound: true,
                    IsAvailable: false);
                continue;
            }

            var binding = active[0];
            connections[requirement.Key] = await BuildConnectionEvidenceAsync(
                    integration,
                    requirement,
                    binding,
                    boundConnections,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var modules = await _moduleRegistry.ListModulesAsync(cancellationToken).ConfigureAwait(false);
        var moduleInstalled = modules.Any(m =>
            string.Equals(m.Descriptor.ModuleId, integration.ModuleId, StringComparison.Ordinal)
            && m.Installed);

        return new ConfigurationEvidenceSnapshot(
            moduleInstalled,
            integration.State == IntegrationState.Active,
            settingValues,
            secretPresence,
            invalidSettingKeys,
            connections,
            Array.Empty<ModuleCheckFailure>());
    }

    private async Task<ConnectionEvidenceSnapshot> BuildConnectionEvidenceAsync(
        DomainIntegration integration,
        ConnectionRequirementDefinition requirement,
        Binding binding,
        IReadOnlyDictionary<string, Connection?> boundConnections,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(binding.ContractType, requirement.CapabilityContractType, StringComparison.Ordinal))
        {
            return new ConnectionEvidenceSnapshot(
                IsBound: true,
                IncompatibleType: true);
        }

        if (binding.ConnectionId is not { } connId)
        {
            return new ConnectionEvidenceSnapshot(IsBound: false);
        }

        boundConnections.TryGetValue(connId.Value, out var connection);
        if (connection is null)
        {
            return new ConnectionEvidenceSnapshot(IsBound: true, IsAvailable: false);
        }

        if (connection.State != ConnectionState.Active)
        {
            return new ConnectionEvidenceSnapshot(
                IsBound: true,
                IsDisabled: true,
                VerificationStatus: MapVerification(connection.LastVerificationStatus));
        }

        var eligible = await _eligibilityService
            .GetEligibleConnectionsAsync(
                integration.ModuleId,
                requirement.Key,
                requirement.CapabilityContractType,
                integration.ResourceContextId,
                cancellationToken)
            .ConfigureAwait(false);

        if (!eligible.Any(c => c.Id.Value == connId.Value))
        {
            return new ConnectionEvidenceSnapshot(
                IsBound: true,
                IncompatibleType: true,
                VerificationStatus: MapVerification(connection.LastVerificationStatus));
        }

        return new ConnectionEvidenceSnapshot(
            IsBound: true,
            IsAvailable: true,
            VerificationStatus: MapVerification(connection.LastVerificationStatus));
    }

    private static bool HasInvalidSecretReference(PlatformSettingResolvedValue row) =>
        row.CurrentValue is string s && !string.IsNullOrWhiteSpace(s) && !row.IsSecretConfigured;

    private static ConnectionVerificationStatusKind MapVerification(ConnectionVerificationStatus status) =>
        status switch
        {
            ConnectionVerificationStatus.Success => ConnectionVerificationStatusKind.Verified,
            ConnectionVerificationStatus.Failed => ConnectionVerificationStatusKind.InvalidCredentials,
            ConnectionVerificationStatus.Unavailable => ConnectionVerificationStatusKind.UnsupportedVerification,
            _ => ConnectionVerificationStatusKind.ConfiguredUnverified,
        };

    private static bool TryValidateSettingValue(
        IntegrationSettingDefinition definition,
        object? currentValue,
        out string? error)
    {
        error = null;
        if (currentValue is null)
            return true;

        if (definition.ValueKind == IntegrationSettingValueKind.Choice
            && definition.AllowedValues.Count > 0)
        {
            var text = Convert.ToString(currentValue);
            if (definition.AllowedValues.All(a => !string.Equals(a.Value, text, StringComparison.Ordinal)))
            {
                error = "Value is not in AllowedValues.";
                return false;
            }
        }

        if (definition.Constraints?.MinInteger is { } min
            && int.TryParse(Convert.ToString(currentValue), out var intVal)
            && intVal < min)
        {
            error = $"Value must be at least {min}.";
            return false;
        }

        if (definition.Constraints?.MaxInteger is { } max
            && int.TryParse(Convert.ToString(currentValue), out var intValMax)
            && intValMax > max)
        {
            error = $"Value must be at most {max}.";
            return false;
        }

        return true;
    }
}
