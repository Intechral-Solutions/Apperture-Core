using Apperture.Management.Client;

namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Epic S7: operator-facing procedure/run launch display helpers (testable).
/// </summary>
public static class ProcedureLaunchOperatorUiHelpers
{
    public static string DescribeBlockedReason(string code) =>
        code.Trim().ToLowerInvariant() switch
        {
            "integration_not_ready" => "Resolve readiness issues for this integration.",
            "integration_inactive" => "Activate the integration to allow launches.",
            "integration_hard_blocked" => "Resolve blocking readiness issues before launch.",
            "target_not_executable" => "Workflow or operation target is missing or not registered for this module.",
            "insufficient_procedure_permission" => "Your role does not satisfy this procedure's required permissions.",
            "insufficient_workflow_permission" => "You cannot start this workflow (missing workflow.execute for the module).",
            "insufficient_operation_permission" => "You cannot start this operation (missing operation.execute).",
            _ => "See readiness issues or contact an administrator."
        };

    public static string FormatLaunchError(string? errorCode, string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
            return errorMessage ?? "Launch failed. Try again or contact an administrator.";

        return errorCode.Trim().ToLowerInvariant() switch
        {
            "invalid_parameters" => errorMessage ?? "Check required parameters and try again.",
            "integration_inactive" => "Activate the integration before launching.",
            "integration_hard_blocked" => "Resolve blocking readiness issues before launch.",
            "integration_not_found" => "Integration was not found.",
            "integration_readiness_unavailable" => "Readiness could not be evaluated; try again later.",
            "procedure_not_found" => "Procedure was not found for this integration.",
            "procedure_wrong_module" => "Procedure does not belong to this integration's module.",
            "target_not_executable" => "Target workflow or operation is not executable.",
            "procedure_permission_denied" => "You do not have permission to launch this procedure.",
            "workflow_start_forbidden" => "You do not have permission to start this workflow.",
            "operation_start_forbidden" => "You do not have permission to start this operation.",
            "unsafe_orchestration_parameters" => errorMessage ?? "Parameters contain unsupported orchestration keys.",
            "dispatch_failed" => "Run dispatch failed. Check platform health and try again.",
            _ => string.IsNullOrWhiteSpace(errorMessage) ? $"Launch failed ({errorCode})." : errorMessage
        };
    }

    public static string FormatLaunchSuccessMessage(string runId, string? status) =>
        string.IsNullOrWhiteSpace(status)
            ? $"Run {runId} was requested."
            : $"Run {runId} was requested ({status}).";

    public static string GetRunDetailHref(string runId) => $"/runs/{Uri.EscapeDataString(runId)}";

    public static string BuildExecuteHref(string integrationId, string? kind = null, string? procedureId = null, string? targetId = null)
    {
        var parts = new List<string> { $"integrationId={Uri.EscapeDataString(integrationId)}" };
        if (!string.IsNullOrWhiteSpace(kind))
            parts.Add($"kind={Uri.EscapeDataString(kind)}");
        if (!string.IsNullOrWhiteSpace(procedureId))
            parts.Add($"procedureId={Uri.EscapeDataString(procedureId)}");
        if (!string.IsNullOrWhiteSpace(targetId))
            parts.Add($"targetId={Uri.EscapeDataString(targetId)}");
        return $"/execute?{string.Join("&", parts)}";
    }

    public static IReadOnlyList<string> ValidateProcedureParameters(
        IReadOnlyList<IntegrationProcedureParameterFieldDto> fields,
        IReadOnlyDictionary<string, object?> values)
    {
        var errors = new List<string>();
        foreach (var field in fields.Where(f => f.Required))
        {
            if (!values.TryGetValue(field.Key, out var value) || IsEmptyValue(value))
                errors.Add($"{field.Label ?? field.Key} is required.");
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateOperationFields(
        IReadOnlyList<ExecutionInputFieldLike> fields,
        IReadOnlyDictionary<string, object?> values)
    {
        var errors = new List<string>();
        foreach (var field in fields.Where(f => f.Required))
        {
            if (!values.TryGetValue(field.Key, out var value) || IsEmptyValue(value))
                errors.Add($"{field.Label} is required.");
        }

        return errors;
    }

    public static string? BuildLaunchDisabledReason(
        bool loading,
        bool launchable,
        bool integrationReady,
        IReadOnlyList<string>? blockedReasonCodes,
        IReadOnlyList<string>? validationErrors)
    {
        if (loading)
            return "Launch in progress…";
        if (validationErrors is { Count: > 0 })
            return validationErrors[0];
        if (!integrationReady)
            return "Integration is not runnable.";
        if (!launchable && blockedReasonCodes is { Count: > 0 })
            return DescribeBlockedReason(blockedReasonCodes[0]);
        if (!launchable)
            return "This target is not available to launch.";
        return null;
    }

    public static string FormatLaunchKindLabel(string? launchKind) =>
        launchKind?.Trim().ToLowerInvariant() switch
        {
            "workflow" => "Workflow",
            "operation" => "Operation",
            null or "" => "Execution",
            _ => launchKind!
        };

    public static string BuildLaunchContextSubtitle(string integrationName, string targetName, string? launchKind = null)
    {
        var kind = string.IsNullOrWhiteSpace(launchKind) ? "" : $"{FormatLaunchKindLabel(launchKind)} · ";
        return $"{kind}{targetName} · {integrationName}";
    }

    private static bool IsEmptyValue(object? value) =>
        value is null || (value is string s && string.IsNullOrWhiteSpace(s));
}

/// <summary>Minimal field shape for launch validation outside Execution contracts.</summary>
public sealed record ExecutionInputFieldLike(string Key, string Label, bool Required);
