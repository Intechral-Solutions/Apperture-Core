namespace Apperture.Core.Application.Notifications.Diagnostics;

/// <summary>
/// Aggregated parity assessment for manual cutover decisions. Does not change configuration.
/// </summary>
public sealed record ExpectedRunMissingParityAcceptanceSummary(
    string IntegrationId,
    int IncidentsEvaluated,
    int KindComparisonsTotal,
    int KindsAligned,
    int KindsMissingInPlatform,
    int KindsExtraInPlatform,
    int KindsStatusMismatch,
    int KindsDeliveryFailedInPlatform,
    int KindsDeliverySucceededInPlatform,
    int KindsSuppressedEpic69Only,
    int KindsSuppressedPlatformOnly,
    int RecipientComparisonsTotal,
    int RecipientsAligned,
    int RecipientsMissingInPlatform,
    int RecipientsExtraInPlatform,
    int RecipientsDeliveryFailedInPlatform,
    int RecipientsPendingInPlatform,
    IReadOnlyList<string> BlockingFindings,
    IReadOnlyList<string> NonBlockingFindings,
    bool LooksAcceptableForCutover,
    DateTimeOffset GeneratedAtUtc);

public static class ExpectedRunMissingParityAcceptanceEvaluator
{
    public static ExpectedRunMissingParityAcceptanceSummary Summarize(
        string integrationId,
        IReadOnlyList<ExpectedRunMissingParityDiagnostics> parityResults,
        DateTimeOffset generatedAtUtc)
    {
        var kindsAligned = 0;
        var kindsMissing = 0;
        var kindsExtra = 0;
        var kindsMismatch = 0;
        var kindsFailed = 0;
        var kindsSucceeded = 0;
        var kindsSuppressedEpic = 0;
        var kindsSuppressedPlatform = 0;
        var recipientTotal = 0;
        var recipientsAligned = 0;
        var recipientsMissing = 0;
        var recipientsExtra = 0;
        var recipientsFailed = 0;
        var recipientsPending = 0;
        var blocking = new List<string>();
        var nonBlocking = new List<string>();

        foreach (var parity in parityResults)
        {
            foreach (var kind in parity.ByKind)
            {
                switch (kind.DifferenceKind)
                {
                    case ParityDifferenceKinds.Aligned:
                        kindsAligned++;
                        break;
                    case ParityDifferenceKinds.MissingInPlatform:
                        kindsMissing++;
                        blocking.Add(
                            $"Incident {parity.AttentionIncidentId} kind {kind.NotificationKind}: missing_in_platform — {kind.Summary}");
                        break;
                    case ParityDifferenceKinds.ExtraInPlatform:
                        kindsExtra++;
                        nonBlocking.Add(
                            $"Incident {parity.AttentionIncidentId} kind {kind.NotificationKind}: extra_in_platform — {kind.Summary}");
                        break;
                    case ParityDifferenceKinds.StatusMismatch:
                        kindsMismatch++;
                        blocking.Add(
                            $"Incident {parity.AttentionIncidentId} kind {kind.NotificationKind}: status_mismatch — {kind.Summary}");
                        break;
                    case ParityDifferenceKinds.DeliveryFailedInPlatform:
                        kindsFailed++;
                        blocking.Add(
                            $"Incident {parity.AttentionIncidentId} kind {kind.NotificationKind}: delivery_failed_in_platform — {kind.Summary}");
                        break;
                    case ParityDifferenceKinds.DeliverySucceededInPlatform:
                        kindsSucceeded++;
                        break;
                    case ParityDifferenceKinds.SuppressedInEpic69Only:
                        kindsSuppressedEpic++;
                        nonBlocking.Add(
                            $"Incident {parity.AttentionIncidentId} kind {kind.NotificationKind}: suppressed_in_epic69_only.");
                        break;
                    case ParityDifferenceKinds.SuppressedInPlatformOnly:
                        kindsSuppressedPlatform++;
                        nonBlocking.Add(
                            $"Incident {parity.AttentionIncidentId} kind {kind.NotificationKind}: suppressed_in_platform_only.");
                        break;
                    case ParityDifferenceKinds.InstancePendingInPlatform:
                        nonBlocking.Add(
                            $"Incident {parity.AttentionIncidentId} kind {kind.NotificationKind}: pending platform delivery.");
                        break;
                    default:
                        kindsMismatch++;
                        blocking.Add(
                            $"Incident {parity.AttentionIncidentId} kind {kind.NotificationKind}: {kind.DifferenceKind} — {kind.Summary}");
                        break;
                }

                foreach (var rc in kind.RecipientComparisons)
                {
                    recipientTotal++;
                    switch (rc.DifferenceKind)
                    {
                        case ParityDifferenceKinds.Aligned:
                            recipientsAligned++;
                            break;
                        case ParityDifferenceKinds.MissingInPlatform:
                            recipientsMissing++;
                            break;
                        case ParityDifferenceKinds.ExtraInPlatform:
                            recipientsExtra++;
                            break;
                        case ParityDifferenceKinds.DeliveryFailedInPlatform:
                            recipientsFailed++;
                            break;
                        case ParityDifferenceKinds.InstancePendingInPlatform:
                            recipientsPending++;
                            break;
                    }
                }
            }
        }

        var kindTotal = parityResults.Sum(p => p.ByKind.Count);
        var acceptable = kindsMissing == 0
                         && kindsFailed == 0
                         && kindsMismatch == 0
                         && recipientsMissing == 0
                         && recipientsFailed == 0
                         && blocking.Count == 0;

        return new ExpectedRunMissingParityAcceptanceSummary(
            integrationId,
            parityResults.Count,
            kindTotal,
            kindsAligned,
            kindsMissing,
            kindsExtra,
            kindsMismatch,
            kindsFailed,
            kindsSucceeded,
            kindsSuppressedEpic,
            kindsSuppressedPlatform,
            recipientTotal,
            recipientsAligned,
            recipientsMissing,
            recipientsExtra,
            recipientsFailed,
            recipientsPending,
            blocking,
            nonBlocking,
            acceptable,
            generatedAtUtc);
    }
}
