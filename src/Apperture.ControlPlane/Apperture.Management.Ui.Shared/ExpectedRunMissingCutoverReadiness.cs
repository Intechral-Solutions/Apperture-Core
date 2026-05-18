using Apperture.Management.Client;

namespace Apperture.Management.Ui.Shared;

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
