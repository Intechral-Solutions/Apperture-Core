using Apperture.Core.Application.Notifications.Cutover;
using Apperture.Core.Application.Notifications.Diagnostics;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Notifications;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

public sealed class ExpectedRunMissingCutoverSignOffService : IExpectedRunMissingCutoverSignOffService
{
    private readonly IExpectedRunMissingCutoverSignOffStore _store;
    private readonly INotificationDeliveryDiagnosticsService _diagnostics;
    private readonly NotificationDefinitionAuthorization _authorization;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuditService _audit;
    private readonly TimeProvider _time;

    public ExpectedRunMissingCutoverSignOffService(
        IExpectedRunMissingCutoverSignOffStore store,
        INotificationDeliveryDiagnosticsService diagnostics,
        NotificationDefinitionAuthorization authorization,
        IPrincipalContext principalContext,
        IAuditService audit,
        TimeProvider time)
    {
        _store = store;
        _diagnostics = diagnostics;
        _authorization = authorization;
        _principalContext = principalContext;
        _audit = audit;
        _time = time;
    }

    public async ValueTask<ExpectedRunMissingCutoverSignOff?> GetLatestAsync(
        string integrationId,
        CancellationToken cancellationToken = default)
    {
        await _authorization.RequireIntegrationReadAsync(integrationId, cancellationToken).ConfigureAwait(false);
        return await _store
            .GetLatestAsync(integrationId, NotificationDefinitionIds.PlatformScheduleExpectedRunMissing, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<ExpectedRunMissingCutoverSignOff> CreateOrReplaceSignOffAsync(
        string integrationId,
        CreateExpectedRunMissingCutoverSignOffRequest request,
        CancellationToken cancellationToken = default)
    {
        await _authorization.RequireIntegrationManageAsync(integrationId, cancellationToken).ConfigureAwait(false);

        var status = request.Status?.Trim() ?? string.Empty;
        if (status is not (ExpectedRunMissingCutoverSignOffStatuses.ReadyForCutover
            or ExpectedRunMissingCutoverSignOffStatuses.NotReady))
            throw new ArgumentException($"Status must be {ExpectedRunMissingCutoverSignOffStatuses.ReadyForCutover} or {ExpectedRunMissingCutoverSignOffStatuses.NotReady}.");

        var actor = await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = await BuildSnapshotAsync(integrationId, cancellationToken).ConfigureAwait(false);

        if (status == ExpectedRunMissingCutoverSignOffStatuses.ReadyForCutover && !snapshot.ParityAcceptable)
            throw new InvalidOperationException(
                "Cannot sign off as ready for cutover while parity acceptance has blocking findings.");

        var prior = await _store
            .GetLatestAsync(integrationId, NotificationDefinitionIds.PlatformScheduleExpectedRunMissing, cancellationToken)
            .ConfigureAwait(false);

        var now = _time.GetUtcNow();
        var signOff = new ExpectedRunMissingCutoverSignOff(
            Guid.NewGuid().ToString("N"),
            integrationId,
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            status,
            now,
            actor.Id.Value,
            actor.DisplayName,
            snapshot.ParityAcceptable,
            snapshot.WarningCodes,
            snapshot.BlockingFindings,
            snapshot.NonBlockingFindings,
            string.IsNullOrWhiteSpace(request.OperatorNote) ? null : request.OperatorNote.Trim(),
            now,
            now);

        await _store.AppendAsync(signOff, cancellationToken).ConfigureAwait(false);

        var action = prior is not null && prior.Status != ExpectedRunMissingCutoverSignOffStatuses.Revoked
            ? NotificationAuditActionNames.ExpectedRunMissingCutoverSignOffReplaced
            : NotificationAuditActionNames.ExpectedRunMissingCutoverSignedOff;

        await AppendAuditAsync(action, signOff, cancellationToken).ConfigureAwait(false);
        return signOff;
    }

    public async ValueTask<ExpectedRunMissingCutoverSignOff> RevokeSignOffAsync(
        string integrationId,
        string? operatorNote,
        CancellationToken cancellationToken = default)
    {
        await _authorization.RequireIntegrationManageAsync(integrationId, cancellationToken).ConfigureAwait(false);

        var prior = await _store
            .GetLatestAsync(integrationId, NotificationDefinitionIds.PlatformScheduleExpectedRunMissing, cancellationToken)
            .ConfigureAwait(false);
        if (prior is null || prior.Status == ExpectedRunMissingCutoverSignOffStatuses.Revoked)
            throw new InvalidOperationException("No active cutover sign-off to revoke.");

        var actor = await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = await BuildSnapshotAsync(integrationId, cancellationToken).ConfigureAwait(false);
        var now = _time.GetUtcNow();

        var signOff = new ExpectedRunMissingCutoverSignOff(
            Guid.NewGuid().ToString("N"),
            integrationId,
            NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            ExpectedRunMissingCutoverSignOffStatuses.Revoked,
            now,
            actor.Id.Value,
            actor.DisplayName,
            snapshot.ParityAcceptable,
            snapshot.WarningCodes,
            snapshot.BlockingFindings,
            snapshot.NonBlockingFindings,
            string.IsNullOrWhiteSpace(operatorNote) ? null : operatorNote.Trim(),
            now,
            now);

        await _store.AppendAsync(signOff, cancellationToken).ConfigureAwait(false);
        await AppendAuditAsync(
                NotificationAuditActionNames.ExpectedRunMissingCutoverSignOffRevoked,
                signOff,
                cancellationToken)
            .ConfigureAwait(false);
        return signOff;
    }

    private async ValueTask<CutoverSnapshot> BuildSnapshotAsync(
        string integrationId,
        CancellationToken cancellationToken)
    {
        var config = await _diagnostics.GetConfigurationDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        var warningCodes = config.Warnings.Select(w => w.Code).OrderBy(c => c, StringComparer.Ordinal).ToList();

        var acceptance = await _diagnostics
            .GetExpectedRunMissingParityAcceptanceAsync(integrationId, null, 10, 50, cancellationToken)
            .ConfigureAwait(false);

        return new CutoverSnapshot(
            acceptance?.LooksAcceptableForCutover == true,
            warningCodes,
            acceptance?.BlockingFindings.ToList() ?? [],
            acceptance?.NonBlockingFindings.ToList() ?? []);
    }

    private async ValueTask<IActor> RequireActorAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");
        return actor;
    }

    private async ValueTask AppendAuditAsync(
        string action,
        ExpectedRunMissingCutoverSignOff signOff,
        CancellationToken cancellationToken)
    {
        var resource = new ResourceDescriptor(
            ResourceTypeConstants.Integration,
            ScopeKeyConstants.Instance);

        var details =
            $"integrationId={signOff.IntegrationId}; definitionId={signOff.NotificationDefinitionId}; " +
            $"signOffStatus={signOff.Status}; parityAcceptable={signOff.ParityAcceptableAtReview}; " +
            $"warningCodes={string.Join(",", signOff.ConfigurationWarningCodes)}; " +
            $"blockingCount={signOff.BlockingFindings.Count}; nonBlockingCount={signOff.NonBlockingFindings.Count}";

        var record = new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            new ActorId(signOff.ReviewedByActorId),
            action,
            resource,
            "notification",
            signOff.ReviewedAtUtc,
            new CorrelationId(null),
            details);

        await _audit.AppendAsync(record, cancellationToken).ConfigureAwait(false);
    }

    private sealed record CutoverSnapshot(
        bool ParityAcceptable,
        IReadOnlyList<string> WarningCodes,
        IReadOnlyList<string> BlockingFindings,
        IReadOnlyList<string> NonBlockingFindings);
}
