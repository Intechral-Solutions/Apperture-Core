using Apperture.Management.Client;

namespace Apperture.Management.Ui.Shared;

/// <summary>SDK-6D: operator-facing configuration/readiness display helpers (testable).</summary>
public static class IntegrationConfigurationOperatorUiHelpers
{
    public const string CategorySettings = "settings";
    public const string CategorySecrets = "secrets";
    public const string CategoryBindings = "bindings";
    public const string CategoryVerification = "verification";
    public const string CategoryModule = "module";
    public const string CategoryProcedure = "procedure";
    public const string CategorySchedule = "schedule";

    public sealed record ReadinessIssueOperatorView(
        string Code,
        string Title,
        string Explanation,
        string? RemediationHint,
        string Category,
        string SeverityLabel,
        string? AffectedKey,
        string? RemediationHref);

    public sealed record ReadinessOverviewPill(string Label, ManagementStatusSemantic Semantic);

    public sealed record ScheduleExecutionPill(string Label, ManagementStatusSemantic Semantic, string? RemediationHref);

    public static ReadinessOverviewPill BuildOverallReadinessPill(IntegrationReadinessDto? readiness)
    {
        if (readiness is null)
            return new ReadinessOverviewPill("Readiness unknown", ManagementStatusSemantic.Neutral);

        if (readiness.IsRunnable && readiness.AdvisoryIssueCount == 0)
            return new ReadinessOverviewPill("Ready", ManagementStatusSemantic.Success);

        if (readiness.IsRunnable && readiness.AdvisoryIssueCount > 0)
            return new ReadinessOverviewPill($"Ready with {readiness.AdvisoryIssueCount} advisory issue(s)", ManagementStatusSemantic.Warning);

        if (readiness.BlockingIssueCount > 0)
            return new ReadinessOverviewPill($"Not ready — {readiness.BlockingIssueCount} blocking issue(s)", ManagementStatusSemantic.Warning);

        return new ReadinessOverviewPill("Not ready", ManagementStatusSemantic.Warning);
    }

    public static string BuildSettingsCompletionSummary(
        IReadOnlyList<IntegrationSettingDeclarationDto>? declarations,
        IReadOnlyList<PlatformSettingResolvedDto>? resolvedRows)
    {
        if (declarations is null || declarations.Count == 0)
            return "No declared settings";

        var required = declarations.Where(d => d.IsRequired).ToList();
        if (required.Count == 0)
            return $"{declarations.Count} optional setting(s)";

        var rowsByKey = (resolvedRows ?? [])
            .ToDictionary(r => r.Key, StringComparer.Ordinal);

        var configured = required.Count(d =>
        {
            if (!rowsByKey.TryGetValue(d.Key, out var row))
                return false;
            if (PlatformSettingUiHelpers.IsSecret(row.InputKind, row.IsSensitive))
                return row.IsSecretConfigured;
            return !string.IsNullOrWhiteSpace(row.CurrentValue)
                   || row.CurrentValues is { Count: > 0 };
        });

        return $"{configured} / {required.Count} required configured";
    }

    public static string BuildBindingsCompletionSummary(
        IReadOnlyList<IntegrationConnectionRequirementDto>? declarations,
        IReadOnlyList<IntegrationBindingSummaryDto>? bindings)
    {
        if (declarations is null || declarations.Count == 0)
            return "No connection requirements";

        var required = declarations.Where(d => d.IsRequired).ToList();
        if (required.Count == 0)
            return $"{declarations.Count} optional connection(s)";

        var activeBindings = (bindings ?? [])
            .Where(b => string.Equals(b.State, "Active", StringComparison.OrdinalIgnoreCase))
            .Select(b => b.SlotName)
            .ToHashSet(StringComparer.Ordinal);

        var bound = required.Count(d => activeBindings.Contains(d.Key));
        return $"{bound} / {required.Count} required bound";
    }

    public static string BuildVerificationSummary(IReadOnlyList<IntegrationBindingSummaryDto>? bindings)
    {
        if (bindings is null || bindings.Count == 0)
            return "No bindings to verify";

        var withConnection = bindings.Where(b => !string.IsNullOrWhiteSpace(b.ConnectionId)).ToList();
        if (withConnection.Count == 0)
            return "No connections bound";

        var failed = withConnection.Count(b =>
            string.Equals(b.ConnectionVerificationStatus, "Failed", StringComparison.OrdinalIgnoreCase));
        var unverified = withConnection.Count(b =>
            string.Equals(b.ConnectionVerificationStatus, "NeverTested", StringComparison.OrdinalIgnoreCase)
            || string.Equals(b.ConnectionVerificationStatus, "Unverified", StringComparison.OrdinalIgnoreCase));

        if (failed > 0)
            return $"{failed} failed · {withConnection.Count - failed} other(s)";
        if (unverified > 0)
            return $"{unverified} unverified · {withConnection.Count - unverified} verified or N/A";

        return $"{withConnection.Count} connection(s) — verification OK or not required";
    }

    public static string BuildProcedureReadinessSummary(IntegrationProcedureListDto? procedures)
    {
        if (procedures is null || procedures.Procedures.Count == 0)
            return "No procedures declared";

        var ready = procedures.Procedures.Count(p => p.Launchable);
        var blocked = procedures.Procedures.Count - ready;
        return blocked == 0
            ? $"{ready} ready"
            : $"{ready} ready · {blocked} blocked";
    }

    public static string BuildScheduleImpactSummary(
        IReadOnlyList<ScheduleDto>? schedules,
        IntegrationReadinessDto? readiness)
    {
        if (schedules is null || schedules.Count == 0)
            return "No schedules";

        var enabled = schedules.Count(s => s.Enabled);
        if (enabled == 0)
            return $"{schedules.Count} schedule(s) — none enabled";

        if (readiness is not null && !readiness.IsRunnable)
            return $"{enabled} enabled — dispatch blocked by integration readiness";

        return $"{enabled} enabled — ready to fire when due";
    }

    public static bool HasNoConfigurationRequired(IntegrationConfigurationDeclarationsDto? declarations) =>
        declarations is not null
        && declarations.Settings.Count == 0
        && declarations.Connections.All(c => !c.IsRequired);

    public static IReadOnlyList<string> GetProceduresAffectedByConnection(
        string connectionKey,
        IReadOnlyList<IntegrationProcedureRequirementDto>? procedureRequirements) =>
        (procedureRequirements ?? [])
            .Where(p => p.RequiredConnectionKeys.Contains(connectionKey, StringComparer.Ordinal))
            .Select(p => p.ProcedureId)
            .ToList();

    public static IReadOnlyDictionary<string, IReadOnlyList<ReadinessIssueOperatorView>> GroupIssuesByCategory(
        IEnumerable<IntegrationReadinessIssueDto> issues,
        string integrationId)
    {
        var materialized = issues as IReadOnlyCollection<IntegrationReadinessIssueDto> ?? issues.ToList();
        var crossContextBindingIds = GetCrossContextBindingIds(materialized);

        var grouped = new Dictionary<string, List<ReadinessIssueOperatorView>>(StringComparer.Ordinal);
        foreach (var issue in materialized)
        {
            if (IsRedundantBindingNotEligible(issue, crossContextBindingIds))
                continue;

            var view = MapReadinessIssue(issue, integrationId);
            if (!grouped.TryGetValue(view.Category, out var list))
            {
                list = [];
                grouped[view.Category] = list;
            }

            list.Add(view);
        }

        return grouped.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<ReadinessIssueOperatorView>)kv.Value, StringComparer.Ordinal);
    }

    private static HashSet<string> GetCrossContextBindingIds(IEnumerable<IntegrationReadinessIssueDto> issues) =>
        issues
            .Where(i => string.Equals(i.Code, "binding_cross_context", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(i.BindingId))
            .Select(i => i.BindingId!)
            .ToHashSet(StringComparer.Ordinal);

    private static bool IsRedundantBindingNotEligible(
        IntegrationReadinessIssueDto issue,
        HashSet<string> crossContextBindingIds) =>
        string.Equals(issue.Code, "binding_not_eligible", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrEmpty(issue.BindingId)
        && crossContextBindingIds.Contains(issue.BindingId);

    public static ReadinessIssueOperatorView MapReadinessIssue(
        IntegrationReadinessIssueDto issue,
        string integrationId)
    {
        var code = issue.Code.Trim().ToLowerInvariant();
        var category = MapIssueCategory(code);
        var (title, explanation, hint) = MapIssueCopy(code, issue.Message, issue.SlotName);
        var severity = MapSeverityLabel(issue.Severity);
        var affectedKey = issue.SlotName
                          ?? ExtractSettingKeyFromMessage(issue.Message)
                          ?? issue.ConnectionId;
        var href = BuildRemediationHref(integrationId, code, issue.SlotName, affectedKey);

        return new ReadinessIssueOperatorView(
            issue.Code,
            title,
            explanation,
            hint,
            category,
            severity,
            affectedKey,
            href);
    }

    public static string FormatCapabilityLabel(string? capabilityKind, string? contractType)
    {
        if (!string.IsNullOrWhiteSpace(capabilityKind))
        {
            return capabilityKind.Trim().ToLowerInvariant() switch
            {
                "http-api" => "HTTP API",
                "relational-db" => "Relational database",
                "sftp" => "SFTP",
                _ => HumanizeToken(capabilityKind)
            };
        }

        if (string.IsNullOrWhiteSpace(contractType))
            return "Connection";

        var type = contractType.Trim();
        if (type.Contains("Sftp", StringComparison.OrdinalIgnoreCase))
            return "SFTP";
        if (type.Contains("DbConnection", StringComparison.OrdinalIgnoreCase)
            || type.EndsWith(".IDbConnection", StringComparison.OrdinalIgnoreCase)
            || type.Contains("SqlConnection", StringComparison.OrdinalIgnoreCase))
            return "Relational database";
        if (type.Contains("ApiClient", StringComparison.OrdinalIgnoreCase)
            || type.Contains("Http", StringComparison.OrdinalIgnoreCase))
            return "HTTP API";

        var shortName = type.Contains('.') ? type.Split('.').Last() : type;
        return HumanizeToken(shortName.TrimStart('I'));
    }

    public static string FormatVerificationExpectationLabel(string? expectation) =>
        expectation?.Trim().ToLowerInvariant() switch
        {
            "required" => "Verification required",
            "advisory" => "Verification advisory",
            "none" => "Verification not required",
            _ => string.IsNullOrWhiteSpace(expectation) ? "Verification policy unknown" : HumanizeToken(expectation)
        };

    public static string FormatVerificationStatusLabel(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "succeeded" or "success" or "verified" or "passed" => "Verified",
            "failed" => "Verification failed",
            "nevertested" or "unverified" => "Not verified",
            "unavailable" or "unsupported" => "Verification unavailable",
            null or "" => "Unknown",
            _ => HumanizeToken(status)
        };

    public static ManagementStatusSemantic MapVerificationStatusSemantic(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "succeeded" or "success" or "verified" or "passed" => ManagementStatusSemantic.Success,
            "failed" => ManagementStatusSemantic.Error,
            "nevertested" or "unverified" => ManagementStatusSemantic.Warning,
            "unavailable" or "unsupported" => ManagementStatusSemantic.Neutral,
            _ => ManagementStatusSemantic.Neutral
        };

    public static string FormatSecretPresenceLabel(
        bool isSecret,
        bool isConfigured,
        bool invalidReference = false,
        bool unavailable = false)
    {
        if (!isSecret)
            return isConfigured ? "Configured" : "Not configured";

        if (unavailable)
            return "Unavailable";
        if (invalidReference)
            return "Invalid reference";
        return isConfigured ? "Configured" : "Not configured";
    }

    public static ManagementStatusSemantic MapSecretPresenceSemantic(
        bool isConfigured,
        bool invalidReference = false,
        bool unavailable = false)
    {
        if (unavailable || invalidReference)
            return ManagementStatusSemantic.Error;
        return isConfigured ? ManagementStatusSemantic.Success : ManagementStatusSemantic.Neutral;
    }

    public static string BuildSettingsRemediationHref(string integrationId, string settingKey) =>
        $"{IntegrationUiRoutes.Settings(integrationId)}#{Uri.EscapeDataString(settingKey)}";

    public static string BuildConnectionRequirementAnchorHref(string integrationId, string connectionKey) =>
        $"{IntegrationUiRoutes.Detail(integrationId)}#requirement-{Uri.EscapeDataString(connectionKey)}";

    public static string BuildConfigurationOverviewHref(string integrationId) =>
        $"{IntegrationUiRoutes.Detail(integrationId)}#configuration-overview";

    public static string BuildCreateConnectionHref(string connectorDefinitionId, string returnUrl) =>
        $"/connections/new?connector={Uri.EscapeDataString(connectorDefinitionId)}&returnUrl={Uri.EscapeDataString(returnUrl)}";

    public static ScheduleExecutionPill BuildScheduleExecutionPill(
        bool enabled,
        bool? integrationRunnable,
        string? integrationId)
    {
        if (!enabled)
            return new ScheduleExecutionPill("Disabled", ManagementStatusSemantic.Neutral, null);

        if (integrationRunnable == false && !string.IsNullOrWhiteSpace(integrationId))
        {
            return new ScheduleExecutionPill(
                "Blocked by configuration",
                ManagementStatusSemantic.Warning,
                BuildConfigurationOverviewHref(integrationId));
        }

        if (integrationRunnable == true)
            return new ScheduleExecutionPill("Ready", ManagementStatusSemantic.Success, null);

        return new ScheduleExecutionPill("Ready pending check", ManagementStatusSemantic.Neutral, null);
    }

    public static string? FormatBlockedByReadinessSummary(string? lastOutcome, string? lastOutcomeReason)
    {
        if (!string.Equals(lastOutcome, "BlockedByReadiness", StringComparison.OrdinalIgnoreCase))
            return null;

        return string.IsNullOrWhiteSpace(lastOutcomeReason)
            ? "Last dispatch was blocked because the integration was not ready."
            : $"Last dispatch blocked: {lastOutcomeReason}";
    }

    public static string? BuildRemediationHref(
        string integrationId,
        string code,
        string? slotName,
        string? affectedKey)
    {
        var normalized = code.Trim().ToLowerInvariant();
        return normalized switch
        {
            "integration_not_active" => IntegrationUiRoutes.Detail(integrationId),
            "missing_setting" or "invalid_setting" or "missing_secret" or "invalid_secret_reference"
                when !string.IsNullOrWhiteSpace(affectedKey) =>
                BuildSettingsRemediationHref(integrationId, affectedKey),
            "missing_slot_binding" or "inactive_binding" or "binding_contract_mismatch"
                or "binding_not_eligible" or "binding_cross_context" or "binding_missing_connection_or_capability"
                or "duplicate_active_binding_for_slot"
                when !string.IsNullOrWhiteSpace(slotName) =>
                BuildConnectionRequirementAnchorHref(integrationId, slotName),
            "connection_missing" or "connection_inactive" or "connection_unverified"
                or "connection_verification_failed" or "connection_verification_unavailable"
                when !string.IsNullOrWhiteSpace(affectedKey) =>
                $"/connections/{Uri.EscapeDataString(affectedKey)}",
            "schedule_ownership_mismatch" => $"{IntegrationUiRoutes.Detail(integrationId)}#schedules",
            _ when !string.IsNullOrWhiteSpace(slotName) =>
                BuildConnectionRequirementAnchorHref(integrationId, slotName),
            _ => BuildConfigurationOverviewHref(integrationId)
        };
    }

    private static string MapIssueCategory(string code) => code switch
    {
        "missing_setting" or "invalid_setting" => CategorySettings,
        "missing_secret" or "invalid_secret_reference" => CategorySecrets,
        "missing_slot_binding" or "inactive_binding" or "duplicate_active_binding_for_slot"
            or "binding_contract_mismatch" or "binding_missing_connection_or_capability"
            or "binding_not_eligible" or "binding_cross_context" or "connection_missing" or "connection_inactive"
            or "incompatible_connection_type" => CategoryBindings,
        "connection_unverified" or "connection_verification_failed"
            or "connection_verification_unavailable" => CategoryVerification,
        "integration_not_active" or "module_not_installed" or "module_check_failed" => CategoryModule,
        "schedule_ownership_mismatch" => CategorySchedule,
        "optional_dependency_unavailable" => CategoryBindings,
        _ => CategoryModule
    };

    private static (string Title, string Explanation, string? Hint) MapIssueCopy(
        string code,
        string message,
        string? slotName)
    {
        return code switch
        {
            "integration_not_active" => (
                "Integration inactive",
                "Schedules and launches are paused until the integration is activated.",
                "Set the integration state to Active."),
            "missing_slot_binding" => (
                $"Connection not bound{(string.IsNullOrWhiteSpace(slotName) ? "" : $": {slotName}")}",
                message,
                "Bind a compatible connection to this requirement."),
            "inactive_binding" => (
                "Binding inactive",
                message,
                "Replace or re-create the binding with an active connection."),
            "duplicate_active_binding_for_slot" => (
                "Duplicate binding",
                message,
                "Remove duplicate active bindings so one remains per slot."),
            "binding_contract_mismatch" => (
                "Contract mismatch",
                message,
                "Re-bind using a connection that matches the required capability."),
            "binding_not_eligible" => (
                "Connection not eligible",
                message,
                "Choose a connection compatible with this module slot."),
            "binding_cross_context" => (
                "Binding crosses contexts",
                message,
                "Move the integration or the connection so they share a context, or remove the binding."),
            "connection_missing" => (
                "Connection missing",
                message,
                "Update the binding to reference an existing connection."),
            "connection_inactive" => (
                "Connection retired",
                message,
                "Bind a different connection, or remove this binding. A retired connection cannot be brought back."),
            "connection_unverified" or "connection_verification_unverified" => (
                "Connection not verified",
                message,
                "Run test-connect on the bound connection."),
            "connection_verification_failed" => (
                "Verification failed",
                message,
                "Fix credentials or endpoint access, then verify again."),
            "connection_verification_unavailable" => (
                "Verification unavailable",
                message,
                "This connector does not support automated verification."),
            "missing_setting" => (
                "Required setting missing",
                message,
                "Open Integration Settings and configure the required value."),
            "invalid_setting" => (
                "Invalid setting value",
                message,
                "Correct the setting value on Integration Settings."),
            "missing_secret" => (
                "Required secret missing",
                message,
                "Set the secret on Integration Settings."),
            "invalid_secret_reference" => (
                "Invalid secret reference",
                message,
                "Replace or clear the secret reference on Integration Settings."),
            "schedule_ownership_mismatch" => (
                "Schedule ownership mismatch",
                message,
                "Ensure schedules belong to this integration."),
            "optional_dependency_unavailable" => (
                "Optional dependency unavailable",
                message,
                "Review the optional connection or setting when convenient."),
            _ => (
                HumanizeToken(code.Replace('_', ' ')),
                message,
                "Review configuration on this integration.")
        };
    }

    private static string MapSeverityLabel(string? severity) =>
        severity?.Trim().ToLowerInvariant() switch
        {
            "blocking" or "high" or "critical" => "Blocking",
            "advisory" or "medium" or "warning" => "Advisory",
            "low" or "info" => "Info",
            _ => string.IsNullOrWhiteSpace(severity) ? "Blocking" : HumanizeToken(severity)
        };

    private static string? ExtractSettingKeyFromMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        const string prefix = "Setting '";
        var start = message.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
            return null;

        start += prefix.Length;
        var end = message.IndexOf('\'', start);
        return end > start ? message[start..end] : null;
    }

    private static string HumanizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        return string.Join(' ',
            value.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries)
                .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }
}
