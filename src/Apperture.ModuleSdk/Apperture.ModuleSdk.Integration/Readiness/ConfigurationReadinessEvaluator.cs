using Apperture.ModuleSdk.Integration.Configuration;

namespace Apperture.ModuleSdk.Integration.Readiness;

/// <summary>
/// Pure readiness evaluation over declared requirements and supplied evidence snapshots.
/// Platform adapters gather evidence; this type remains persistence-free for the testing toolkit.
/// </summary>
public static class ConfigurationReadinessEvaluator
{
    public static ConfigurationReadinessResult EvaluateIntegration(
        IReadOnlyList<IntegrationSettingDefinition> settings,
        IReadOnlyList<ConnectionRequirementDefinition> connections,
        ConfigurationEvidenceSnapshot evidence,
        DateTimeOffset evaluatedAtUtc)
    {
        var reasons = new List<ConfigurationReadinessReason>();

        if (!evidence.ModuleInstalled)
        {
            reasons.Add(new ConfigurationReadinessReason(
                ConfigurationReadinessReasonCodes.ModuleNotInstalled,
                "The integration module is not installed on this instance.",
                RemediationHint: "Install and enable the module for this deployment."));
        }

        if (!evidence.IntegrationEnabled)
        {
            reasons.Add(new ConfigurationReadinessReason(
                ConfigurationReadinessReasonCodes.IntegrationDisabled,
                "The integration is disabled or not active.",
                RemediationHint: "Enable the integration before launching procedures."));
        }

        EvaluateSettings(settings, requiredKeys: null, evidence, reasons);
        EvaluateConnections(connections, requiredKeys: null, evidence, reasons);

        foreach (var check in evidence.ModuleCheckFailures)
        {
            reasons.Add(new ConfigurationReadinessReason(
                ConfigurationReadinessReasonCodes.ModuleCheckFailed,
                check.Message,
                SettingKey: check.SettingKey,
                ConnectionKey: check.ConnectionKey,
                RemediationHint: check.RemediationHint));
        }

        return ConfigurationReadinessResult.FromReasons(reasons, evaluatedAtUtc);
    }

    public static ProcedureReadinessResult EvaluateProcedure(
        string procedureId,
        IReadOnlyList<IntegrationSettingDefinition> settings,
        IReadOnlyList<ConnectionRequirementDefinition> connections,
        IReadOnlyList<ProcedureRequirementSet> procedureRequirements,
        ConfigurationEvidenceSnapshot evidence,
        DateTimeOffset evaluatedAtUtc)
    {
        var set = procedureRequirements.FirstOrDefault(p =>
            string.Equals(p.ProcedureId, procedureId, StringComparison.Ordinal));

        IReadOnlyCollection<string>? requiredSettings = set?.RequiredSettingKeys;
        IReadOnlyCollection<string>? requiredConnections = set?.RequiredConnectionKeys;

        // When no procedure set exists, fall back to all module-required declarations.
        if (set is null)
        {
            requiredSettings = null;
            requiredConnections = null;
        }

        var reasons = new List<ConfigurationReadinessReason>();

        if (!evidence.ModuleInstalled)
        {
            reasons.Add(new ConfigurationReadinessReason(
                ConfigurationReadinessReasonCodes.ModuleNotInstalled,
                "The integration module is not installed on this instance.",
                RemediationHint: "Install and enable the module for this deployment."));
        }

        if (!evidence.IntegrationEnabled)
        {
            reasons.Add(new ConfigurationReadinessReason(
                ConfigurationReadinessReasonCodes.IntegrationDisabled,
                "The integration is disabled or not active.",
                RemediationHint: "Enable the integration before launching procedures."));
        }

        EvaluateSettings(settings, requiredSettings, evidence, reasons);
        EvaluateConnections(connections, requiredConnections, evidence, reasons);

        foreach (var check in evidence.ModuleCheckFailures)
        {
            reasons.Add(new ConfigurationReadinessReason(
                ConfigurationReadinessReasonCodes.ModuleCheckFailed,
                check.Message,
                SettingKey: check.SettingKey,
                ConnectionKey: check.ConnectionKey,
                RemediationHint: check.RemediationHint));
        }

        return ProcedureReadinessResult.FromReasons(procedureId, reasons, evaluatedAtUtc);
    }

    private static void EvaluateSettings(
        IReadOnlyList<IntegrationSettingDefinition> settings,
        IReadOnlyCollection<string>? requiredKeys,
        ConfigurationEvidenceSnapshot evidence,
        List<ConfigurationReadinessReason> reasons)
    {
        foreach (var setting in settings)
        {
            var mustHave = requiredKeys is null
                ? setting.IsRequired
                : requiredKeys.Contains(setting.Key);

            if (!mustHave)
                continue;

            if (setting.IsSensitive)
            {
                if (!evidence.SecretPresence.TryGetValue(setting.Key, out var presence) || presence == SecretPresenceState.Missing)
                {
                    reasons.Add(new ConfigurationReadinessReason(
                        ConfigurationReadinessReasonCodes.MissingSecret,
                        $"Required secret '{setting.DisplayName}' is not configured.",
                        SettingKey: setting.Key,
                        RemediationHint: $"Set the secret for '{setting.Key}'."));
                    continue;
                }

                if (presence == SecretPresenceState.InvalidReference)
                {
                    reasons.Add(new ConfigurationReadinessReason(
                        ConfigurationReadinessReasonCodes.InvalidSecretReference,
                        $"Secret for '{setting.DisplayName}' has an invalid reference.",
                        SettingKey: setting.Key,
                        RemediationHint: $"Replace the secret for '{setting.Key}'."));
                }

                continue;
            }

            // Prefer explicit invalid evidence over missing when the key is flagged.
            if (evidence.InvalidSettingKeys.Contains(setting.Key))
            {
                reasons.Add(new ConfigurationReadinessReason(
                    ConfigurationReadinessReasonCodes.InvalidSetting,
                    $"Setting '{setting.DisplayName}' has an invalid value.",
                    SettingKey: setting.Key,
                    RemediationHint: $"Correct the value for '{setting.Key}'."));
                continue;
            }

            if (!evidence.SettingValues.TryGetValue(setting.Key, out var value) || value is null || IsBlank(value))
            {
                // Defaults satisfy optional presence when the declaration supplies a safe default.
                if (setting.DefaultValue is not null && !setting.IsSensitive)
                    continue;

                reasons.Add(new ConfigurationReadinessReason(
                    ConfigurationReadinessReasonCodes.MissingSetting,
                    $"Required setting '{setting.DisplayName}' is not configured.",
                    SettingKey: setting.Key,
                    RemediationHint: $"Configure '{setting.Key}'."));
            }
        }
    }

    private static void EvaluateConnections(
        IReadOnlyList<ConnectionRequirementDefinition> connections,
        IReadOnlyCollection<string>? requiredKeys,
        ConfigurationEvidenceSnapshot evidence,
        List<ConfigurationReadinessReason> reasons)
    {
        foreach (var connection in connections)
        {
            var mustHave = requiredKeys is null
                ? connection.IsRequired
                : requiredKeys.Contains(connection.Key);

            if (!evidence.Connections.TryGetValue(connection.Key, out var snap))
            {
                if (!mustHave)
                {
                    reasons.Add(new ConfigurationReadinessReason(
                        ConfigurationReadinessReasonCodes.OptionalDependencyUnavailable,
                        $"Optional connection '{connection.DisplayName}' is not bound.",
                        ConfigurationReadinessSeverity.Advisory,
                        ConnectionKey: connection.Key,
                        RemediationHint: $"Bind a compatible connection to '{connection.Key}' when needed."));
                    continue;
                }

                reasons.Add(new ConfigurationReadinessReason(
                    ConfigurationReadinessReasonCodes.MissingConnectionBinding,
                    $"Required connection '{connection.DisplayName}' is not bound.",
                    ConnectionKey: connection.Key,
                    RemediationHint: $"Bind a compatible connection to '{connection.Key}'."));
                continue;
            }

            if (!snap.IsBound)
            {
                if (!mustHave)
                {
                    reasons.Add(new ConfigurationReadinessReason(
                        ConfigurationReadinessReasonCodes.OptionalDependencyUnavailable,
                        $"Optional connection '{connection.DisplayName}' is not bound.",
                        ConfigurationReadinessSeverity.Advisory,
                        ConnectionKey: connection.Key));
                    continue;
                }

                reasons.Add(new ConfigurationReadinessReason(
                    ConfigurationReadinessReasonCodes.MissingConnectionBinding,
                    $"Required connection '{connection.DisplayName}' is not bound.",
                    ConnectionKey: connection.Key,
                    RemediationHint: $"Bind a compatible connection to '{connection.Key}'."));
                continue;
            }

            if (snap.IsDisabled)
            {
                var severity = mustHave
                    ? ConfigurationReadinessSeverity.Blocking
                    : ConfigurationReadinessSeverity.Advisory;
                reasons.Add(new ConfigurationReadinessReason(
                    mustHave
                        ? ConfigurationReadinessReasonCodes.ConnectionDisabled
                        : ConfigurationReadinessReasonCodes.OptionalDependencyUnavailable,
                    $"Connection '{connection.DisplayName}' is retired and cannot be used.",
                    severity,
                    ConnectionKey: connection.Key,
                    RemediationHint: "Select another connection. A retired connection cannot be brought back."));
                continue;
            }

            if (!snap.IsAvailable)
            {
                var severity = mustHave
                    ? ConfigurationReadinessSeverity.Blocking
                    : ConfigurationReadinessSeverity.Advisory;
                reasons.Add(new ConfigurationReadinessReason(
                    mustHave
                        ? ConfigurationReadinessReasonCodes.ConnectionUnavailable
                        : ConfigurationReadinessReasonCodes.OptionalDependencyUnavailable,
                    $"Connection '{connection.DisplayName}' is unavailable.",
                    severity,
                    ConnectionKey: connection.Key));
                continue;
            }

            if (snap.IncompatibleType)
            {
                reasons.Add(new ConfigurationReadinessReason(
                    ConfigurationReadinessReasonCodes.IncompatibleConnectionType,
                    $"Bound connection for '{connection.DisplayName}' is not compatible.",
                    mustHave ? ConfigurationReadinessSeverity.Blocking : ConfigurationReadinessSeverity.Advisory,
                    ConnectionKey: connection.Key,
                    RemediationHint: "Select a connection that matches the required capability."));
                continue;
            }

            EvaluateVerification(connection, snap, mustHave, reasons);
        }
    }

    private static void EvaluateVerification(
        ConnectionRequirementDefinition connection,
        ConnectionEvidenceSnapshot snap,
        bool mustHave,
        List<ConfigurationReadinessReason> reasons)
    {
        if (connection.VerificationExpectation == ConnectionVerificationExpectation.UnsupportedOk)
            return;

        var advisoryOnly =
            connection.VerificationExpectation == ConnectionVerificationExpectation.Advisory || !mustHave;

        var severity = advisoryOnly
            ? ConfigurationReadinessSeverity.Advisory
            : ConfigurationReadinessSeverity.Blocking;

        switch (snap.VerificationStatus)
        {
            case ConnectionVerificationStatusKind.Verified:
                return;
            case ConnectionVerificationStatusKind.ConfiguredUnverified:
                reasons.Add(new ConfigurationReadinessReason(
                    ConfigurationReadinessReasonCodes.VerificationUnverified,
                    $"Connection '{connection.DisplayName}' has not been verified.",
                    severity,
                    ConnectionKey: connection.Key,
                    RemediationHint: "Run connection verification."));
                break;
            case ConnectionVerificationStatusKind.StaleAfterConfigurationChange:
                reasons.Add(new ConfigurationReadinessReason(
                    ConfigurationReadinessReasonCodes.VerificationStale,
                    $"Verification for '{connection.DisplayName}' is stale after a configuration change.",
                    severity,
                    ConnectionKey: connection.Key,
                    RemediationHint: "Re-run connection verification."));
                break;
            case ConnectionVerificationStatusKind.UnsupportedVerification:
                if (connection.VerificationExpectation == ConnectionVerificationExpectation.Required)
                {
                    reasons.Add(new ConfigurationReadinessReason(
                        ConfigurationReadinessReasonCodes.VerificationUnsupported,
                        $"Connection '{connection.DisplayName}' does not support verification but verification is required.",
                        severity,
                        ConnectionKey: connection.Key));
                }
                break;
            case ConnectionVerificationStatusKind.InvalidCredentials:
            case ConnectionVerificationStatusKind.EndpointUnreachable:
            case ConnectionVerificationStatusKind.InvalidConfiguration:
                reasons.Add(new ConfigurationReadinessReason(
                    ConfigurationReadinessReasonCodes.VerificationFailed,
                    $"Verification for '{connection.DisplayName}' failed ({snap.VerificationStatus}).",
                    severity,
                    ConnectionKey: connection.Key,
                    RemediationHint: "Fix connection configuration and re-verify."));
                break;
        }
    }

    private static bool IsBlank(object value) =>
        value is string s && string.IsNullOrWhiteSpace(s);
}

public enum SecretPresenceState
{
    Missing = 0,
    Present = 1,
    InvalidReference = 2,
}

public sealed record ModuleCheckFailure(
    string Message,
    string? SettingKey = null,
    string? ConnectionKey = null,
    string? RemediationHint = null);

public sealed record ConnectionEvidenceSnapshot(
    bool IsBound,
    bool IsAvailable = true,
    bool IsDisabled = false,
    bool IncompatibleType = false,
    ConnectionVerificationStatusKind VerificationStatus = ConnectionVerificationStatusKind.ConfiguredUnverified);

/// <summary>Persistence-free evidence bag for readiness evaluation.</summary>
public sealed record ConfigurationEvidenceSnapshot(
    bool ModuleInstalled,
    bool IntegrationEnabled,
    IReadOnlyDictionary<string, object?> SettingValues,
    IReadOnlyDictionary<string, SecretPresenceState> SecretPresence,
    IReadOnlySet<string> InvalidSettingKeys,
    IReadOnlyDictionary<string, ConnectionEvidenceSnapshot> Connections,
    IReadOnlyList<ModuleCheckFailure> ModuleCheckFailures)
{
    public static ConfigurationEvidenceSnapshot Empty(bool moduleInstalled = true, bool integrationEnabled = true) =>
        new(
            moduleInstalled,
            integrationEnabled,
            new Dictionary<string, object?>(),
            new Dictionary<string, SecretPresenceState>(),
            new HashSet<string>(StringComparer.Ordinal),
            new Dictionary<string, ConnectionEvidenceSnapshot>(StringComparer.Ordinal),
            Array.Empty<ModuleCheckFailure>());
}
