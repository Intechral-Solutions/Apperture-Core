using Apperture.Core.Application.Notifications.Cutover;
using Apperture.Core.Application.Notifications.Diagnostics;
using Apperture.Core.Domain.Notifications;

namespace Apperture.ControlPlane.Security;

public sealed class ExpectedRunMissingCutoverValidationService : IExpectedRunMissingCutoverValidationService
{
    private readonly IExpectedRunMissingCutoverSignOffStore _signOffs;
    private readonly INotificationDeliveryDiagnosticsService _diagnostics;
    private readonly NotificationDefinitionAuthorization _authorization;
    private readonly TimeProvider _time;

    public ExpectedRunMissingCutoverValidationService(
        IExpectedRunMissingCutoverSignOffStore signOffs,
        INotificationDeliveryDiagnosticsService diagnostics,
        NotificationDefinitionAuthorization authorization,
        TimeProvider time)
    {
        _signOffs = signOffs;
        _diagnostics = diagnostics;
        _authorization = authorization;
        _time = time;
    }

    public async ValueTask<ExpectedRunMissingCutoverValidation> ValidateAsync(
        string integrationId,
        CancellationToken cancellationToken = default)
    {
        await _authorization.RequireIntegrationReadAsync(integrationId, cancellationToken).ConfigureAwait(false);

        var definitionId = NotificationDefinitionIds.PlatformScheduleExpectedRunMissing;
        var config = await _diagnostics.GetConfigurationDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        var parity = await _diagnostics
            .GetExpectedRunMissingParityAcceptanceAsync(integrationId, null, 10, 50, cancellationToken)
            .ConfigureAwait(false);
        var integrationDiag = await _diagnostics
            .GetByIntegrationAsync(integrationId, definitionId, take: 50, cancellationToken)
            .ConfigureAwait(false);
        var signOff = await _signOffs.GetLatestAsync(integrationId, definitionId, cancellationToken).ConfigureAwait(false);

        var failedOutbox = integrationDiag.RecentFailedDeliveries;
        var succeededOutbox = integrationDiag.RecentOutboxRows
            .Count(r => string.Equals(r.Status, "succeeded", StringComparison.OrdinalIgnoreCase));

        var recipientsMissing = parity?.RecipientsMissingInPlatform ?? 0;
        var hasRecipientGap = recipientsMissing > 0
                              || parity?.BlockingFindings.Any(f =>
                                  f.Contains("missing_in_platform", StringComparison.OrdinalIgnoreCase)) == true;

        var signOffRevoked = signOff?.Status == ExpectedRunMissingCutoverSignOffStatuses.Revoked;
        var hasReadySignOff = signOff?.Status == ExpectedRunMissingCutoverSignOffStatuses.ReadyForCutover;
        var signOffStale = signOff is not null
                           && signOff.Status != ExpectedRunMissingCutoverSignOffStatuses.Revoked
                           && ExpectedRunMissingCutoverStaleness.IsSignOffStale(signOff, parity, config);

        var blockers = BuildReadinessBlockers(
            config,
            parity,
            signOff,
            hasReadySignOff,
            signOffStale,
            signOffRevoked,
            failedOutbox.Count,
            hasRecipientGap,
            recipientsMissing);

        return new ExpectedRunMissingCutoverValidation(
            integrationId,
            ExpectedRunMissingCutoverOperatingModes.Classify(config),
            hasReadySignOff,
            signOffRevoked,
            signOffStale,
            signOff?.Status,
            signOff?.ReviewedAtUtc,
            parity?.LooksAcceptableForCutover == true,
            config.Warnings.Count > 0,
            config.Warnings.Select(w => w.Code).ToList(),
            failedOutbox.Count,
            succeededOutbox,
            failedOutbox.Count > 0,
            hasRecipientGap,
            recipientsMissing,
            blockers,
            _time.GetUtcNow());
    }

    private static IReadOnlyList<string> BuildReadinessBlockers(
        NotificationConfigurationDiagnostics config,
        ExpectedRunMissingParityAcceptanceSummary? parity,
        ExpectedRunMissingCutoverSignOff? signOff,
        bool hasReadySignOff,
        bool signOffStale,
        bool signOffRevoked,
        int failedOutboxCount,
        bool hasRecipientGap,
        int recipientsMissing)
    {
        var blockers = new List<string>();

        foreach (var warning in config.Warnings)
            blockers.Add($"configuration:{warning.Code}");

        if (parity is null)
            blockers.Add("parity:no_recent_incidents");
        else if (!parity.LooksAcceptableForCutover)
            blockers.Add("parity:not_acceptable");

        if (signOff is null)
            blockers.Add("sign_off:none");
        else if (signOffRevoked)
            blockers.Add("sign_off:revoked");
        else if (!hasReadySignOff)
            blockers.Add("sign_off:not_ready");
        else if (signOffStale)
            blockers.Add("sign_off:stale");

        if (failedOutboxCount > 0)
            blockers.Add($"delivery:failed_outbox:{failedOutboxCount}");

        if (hasRecipientGap)
            blockers.Add($"recipients:missing_in_platform:{recipientsMissing}");

        if (!config.PlatformEventsEnabled)
            blockers.Add("flags:platform_events_disabled");

        if (!config.EvaluationEnabled)
            blockers.Add("flags:evaluation_disabled");

        if (!config.InstancesEnabled)
            blockers.Add("flags:instances_disabled");

        return blockers;
    }
}
