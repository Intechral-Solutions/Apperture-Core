using Apperture.Core.Application.PlatformSettings;

namespace Apperture.ModuleSdk.Integration.Configuration;

/// <summary>
/// Validates integration configuration declarations and maps them onto existing
/// <see cref="PlatformSettingDefinition"/> rows for persistence/UI reuse.
/// </summary>
public static class IntegrationConfigurationDeclarations
{
    public static void Validate(IIntegrationConfigurationContributor contributor)
    {
        ArgumentNullException.ThrowIfNull(contributor);

        var settings = contributor.GetSettingDefinitions() ?? Array.Empty<IntegrationSettingDefinition>();
        var connections = contributor.GetConnectionRequirements() ?? Array.Empty<ConnectionRequirementDefinition>();
        var procedures = contributor.GetProcedureRequirements() ?? Array.Empty<ProcedureRequirementSet>();

        var settingKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var setting in settings)
        {
            if (string.IsNullOrWhiteSpace(setting.Key))
                throw new IntegrationConfigurationException("invalid_declaration", "Setting key is required.");

            if (!settingKeys.Add(setting.Key))
                throw IntegrationConfigurationException.DuplicateDeclaration(setting.Key);

            if (setting.IsSensitive && setting.DefaultValue is not null)
                throw IntegrationConfigurationException.ForbiddenSecretDefault(setting.Key);

            if (setting.ValueKind == IntegrationSettingValueKind.Choice && setting.AllowedValues.Count == 0)
            {
                throw new IntegrationConfigurationException(
                    "invalid_declaration",
                    $"Choice setting '{setting.Key}' must declare AllowedValues.",
                    settingKey: setting.Key);
            }
        }

        var connectionKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var connection in connections)
        {
            if (string.IsNullOrWhiteSpace(connection.Key))
                throw new IntegrationConfigurationException("invalid_declaration", "Connection key is required.");

            if (!connectionKeys.Add(connection.Key))
                throw IntegrationConfigurationException.DuplicateDeclaration(connection.Key);

            if (string.IsNullOrWhiteSpace(connection.CapabilityContractType))
            {
                throw new IntegrationConfigurationException(
                    "invalid_declaration",
                    $"Connection '{connection.Key}' must declare CapabilityContractType.",
                    connectionKey: connection.Key);
            }
        }

        var procedureIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var procedure in procedures)
        {
            if (string.IsNullOrWhiteSpace(procedure.ProcedureId))
                throw new IntegrationConfigurationException("invalid_declaration", "ProcedureId is required.");

            if (!procedureIds.Add(procedure.ProcedureId))
                throw IntegrationConfigurationException.DuplicateDeclaration(procedure.ProcedureId);

            foreach (var key in procedure.RequiredSettingKeys)
            {
                if (!settingKeys.Contains(key))
                {
                    throw new IntegrationConfigurationException(
                        "invalid_declaration",
                        $"Procedure '{procedure.ProcedureId}' references unknown setting '{key}'.",
                        settingKey: key);
                }
            }

            foreach (var key in procedure.RequiredConnectionKeys)
            {
                if (!connectionKeys.Contains(key))
                {
                    throw new IntegrationConfigurationException(
                        "invalid_declaration",
                        $"Procedure '{procedure.ProcedureId}' references unknown connection '{key}'.",
                        connectionKey: key);
                }
            }
        }
    }

    public static IReadOnlyList<PlatformSettingDefinition> ToPlatformSettingDefinitions(
        IEnumerable<IntegrationSettingDefinition> definitions,
        string moduleId)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        return definitions.Select(d => ToPlatformSettingDefinition(d, moduleId)).ToList();
    }

    public static PlatformSettingDefinition ToPlatformSettingDefinition(
        IntegrationSettingDefinition definition,
        string moduleId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        if (definition.IsSensitive && definition.DefaultValue is not null)
            throw IntegrationConfigurationException.ForbiddenSecretDefault(definition.Key);

        var (valueType, inputKind) = MapValueKind(definition.ValueKind);
        var allowed = definition.AllowedValues
            .Select(a => new PlatformSettingAllowedValue
            {
                Value = a.Value,
                Label = a.DisplayName,
                Description = a.Description,
            })
            .ToList();

        return new PlatformSettingDefinition
        {
            Key = definition.Key,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            Category = PlatformSettingCategory.Integrations,
            ValueType = valueType,
            InputKind = inputKind,
            AllowedValueScopes = [PlatformSettingValueScopeKind.Integration],
            ConfigurationPath = $"Modules:{moduleId}:Settings:{definition.Key}",
            DefaultValue = definition.IsSensitive
                ? ""
                : definition.DefaultValue ?? DefaultFor(definition.ValueKind),
            AllowedValues = allowed.Count == 0 ? null : allowed,
            MinInteger = definition.Constraints?.MinInteger,
            MaxInteger = definition.Constraints?.MaxInteger,
            MinDuration = ParseDuration(definition.Constraints?.MinDurationIso),
            RequiresRestart = false,
            Editable = true,
            IsSensitive = definition.IsSensitive,
        };
    }

    private static object DefaultFor(IntegrationSettingValueKind kind) =>
        kind switch
        {
            IntegrationSettingValueKind.Boolean => false,
            IntegrationSettingValueKind.Integer => 0,
            IntegrationSettingValueKind.Duration => TimeSpan.Zero,
            _ => "",
        };

    private static TimeSpan? ParseDuration(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
            return null;
        return System.Xml.XmlConvert.ToTimeSpan(iso);
    }

    private static (PlatformSettingValueType ValueType, PlatformSettingInputKind InputKind) MapValueKind(
        IntegrationSettingValueKind kind) =>
        kind switch
        {
            IntegrationSettingValueKind.Boolean => (PlatformSettingValueType.Boolean, PlatformSettingInputKind.Toggle),
            IntegrationSettingValueKind.Integer => (PlatformSettingValueType.Integer, PlatformSettingInputKind.Number),
            IntegrationSettingValueKind.Duration => (PlatformSettingValueType.Duration, PlatformSettingInputKind.Duration),
            IntegrationSettingValueKind.Choice => (PlatformSettingValueType.String, PlatformSettingInputKind.Select),
            IntegrationSettingValueKind.SensitiveText => (PlatformSettingValueType.String, PlatformSettingInputKind.Secret),
            IntegrationSettingValueKind.MultilineString => (PlatformSettingValueType.String, PlatformSettingInputKind.Text),
            IntegrationSettingValueKind.Decimal => (PlatformSettingValueType.String, PlatformSettingInputKind.Text),
            IntegrationSettingValueKind.Uri => (PlatformSettingValueType.String, PlatformSettingInputKind.Text),
            IntegrationSettingValueKind.TimeZone => (PlatformSettingValueType.String, PlatformSettingInputKind.Text),
            IntegrationSettingValueKind.String => (PlatformSettingValueType.String, PlatformSettingInputKind.Text),
            _ => (PlatformSettingValueType.String, PlatformSettingInputKind.Text),
        };
}
