using Apperture.Management.Client;

namespace Apperture.Management.Ui.Shared;

public static class ExpectedRunMissingCutoverSignOffUi
{
    public static string GetStatusLabel(string status) =>
        status switch
        {
            "readyForCutover" => "Ready for cutover (signed off)",
            "notReady" => "Not ready (signed off)",
            "revoked" => "Sign-off revoked",
            _ => status,
        };

    public static bool HasStaleness(
        ExpectedRunMissingCutoverSignOffDto signOff,
        ExpectedRunMissingParityAcceptanceSummaryDto? currentParity,
        NotificationConfigurationDiagnosticsDto? currentConfig)
    {
        if (currentParity is not null
            && signOff.ParityAcceptableAtReview != currentParity.LooksAcceptableForCutover)
            return true;

        if (currentConfig is not null)
        {
            var currentCodes = currentConfig.Warnings.Select(w => w.Code).OrderBy(c => c, StringComparer.Ordinal).ToList();
            if (!signOff.ConfigurationWarningCodes.SequenceEqual(currentCodes, StringComparer.Ordinal))
                return true;
        }

        if (currentParity is not null)
        {
            if (signOff.BlockingFindings.Count != currentParity.BlockingFindings.Count
                || signOff.NonBlockingFindings.Count != currentParity.NonBlockingFindings.Count)
                return true;
        }

        return false;
    }
}

/// <summary>Operator-facing labels for expected-run-missing platform notification cutover readiness.</summary>
public enum ExpectedRunMissingCutoverReadinessState
{
    NotEvaluated,
    NoRecentIncidents,
    NotReadyForCutover,
    LooksAcceptableForCutover,
    NeedsReview,
}

public static class ExpectedRunMissingCutoverReadiness
{
    public const string RunbookDocPath =
        "docs/03-operations/epic77-expected-run-missing-notification-cutover-runbook.md";

    public static ExpectedRunMissingCutoverReadinessState ResolveState(
        bool parityEvaluated,
        ExpectedRunMissingParityAcceptanceSummaryDto? acceptance)
    {
        if (!parityEvaluated)
            return ExpectedRunMissingCutoverReadinessState.NotEvaluated;

        if (acceptance is null)
            return ExpectedRunMissingCutoverReadinessState.NoRecentIncidents;

        if (acceptance.BlockingFindings.Count > 0 || !acceptance.LooksAcceptableForCutover)
            return ExpectedRunMissingCutoverReadinessState.NotReadyForCutover;

        if (acceptance.NonBlockingFindings.Count > 0
            || acceptance.KindsExtraInPlatform > 0
            || acceptance.RecipientsPendingInPlatform > 0
            || acceptance.KindsSuppressedPlatformOnly > 0
            || acceptance.KindsSuppressedEpic69Only > 0)
            return ExpectedRunMissingCutoverReadinessState.NeedsReview;

        return ExpectedRunMissingCutoverReadinessState.LooksAcceptableForCutover;
    }

    public static string GetLabel(ExpectedRunMissingCutoverReadinessState state) =>
        state switch
        {
            ExpectedRunMissingCutoverReadinessState.NotEvaluated => "Not evaluated",
            ExpectedRunMissingCutoverReadinessState.NoRecentIncidents => "No recent incidents available",
            ExpectedRunMissingCutoverReadinessState.NotReadyForCutover => "Not ready for cutover",
            ExpectedRunMissingCutoverReadinessState.LooksAcceptableForCutover => "Looks acceptable for cutover",
            ExpectedRunMissingCutoverReadinessState.NeedsReview => "Needs review",
            _ => "Not evaluated",
        };

    public static string GetIntegrationNotificationStatusSummary(
        bool? builtInDefinitionEnabled,
        int recentPlatformInstanceCount,
        ExpectedRunMissingCutoverReadinessState parityState)
    {
        var definition = builtInDefinitionEnabled switch
        {
            true => "Expected-run-missing definition enabled",
            false => "Expected-run-missing definition disabled",
            null => "Expected-run-missing definition status unknown",
        };
        var instances = recentPlatformInstanceCount > 0
            ? $"{recentPlatformInstanceCount} recent platform instance(s)"
            : "No recent platform instances";
        return $"{definition}; {instances}; parity: {GetLabel(parityState)}.";
    }
}
