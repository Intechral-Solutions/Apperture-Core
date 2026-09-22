using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlNotificationRecipientResolver : INotificationRecipientResolver
{
    private readonly AppertureDbContext _db;
    private readonly INotificationSubscriptionStore _subscriptions;
    private readonly INotificationDefinitionStore _definitions;
    private readonly OperationalTeamRecipientExpander _teamExpander;

    public SqlNotificationRecipientResolver(
        AppertureDbContext db,
        INotificationSubscriptionStore subscriptions,
        INotificationDefinitionStore definitions,
        OperationalTeamRecipientExpander teamExpander)
    {
        _db = db;
        _subscriptions = subscriptions;
        _definitions = definitions;
        _teamExpander = teamExpander;
    }

    public async ValueTask<NotificationRecipientResolutionPreview> ResolvePreviewAsync(
        string notificationDefinitionId,
        string? integrationId,
        CancellationToken cancellationToken = default)
    {
        var definition = await _definitions
            .GetAsync(NotificationDefinitionId.From(notificationDefinitionId), cancellationToken)
            .ConfigureAwait(false);
        if (definition is null)
            throw new InvalidOperationException($"Notification definition {notificationDefinitionId} not found.");

        var subs = await _subscriptions
            .ListEnabledForResolutionAsync(notificationDefinitionId, integrationId, cancellationToken)
            .ConfigureAwait(false);
        var overrides = await _subscriptions
            .ListUserOverridesAsync(notificationDefinitionId, cancellationToken)
            .ConfigureAwait(false);

        var deliverable = new List<ResolvedNotificationRecipient>();
        var skipped = new List<SkippedNotificationRecipient>();
        var inApp = new List<InAppEligibleRecipient>();
        var seenEmails = new Dictionary<string, ResolvedNotificationRecipient>(StringComparer.OrdinalIgnoreCase);

        foreach (var sub in subs)
        {
            await ExpandSubscriptionAsync(sub, deliverable, skipped, inApp, seenEmails, overrides, cancellationToken)
                .ConfigureAwait(false);
        }

        if (await ShouldApplyImplicitAttentionRoutingAsync(definition.Id.Value, integrationId, subs, cancellationToken)
                .ConfigureAwait(false))
        {
            await ApplyImplicitAttentionRoutingAsync(
                    integrationId!,
                    deliverable,
                    skipped,
                    inApp,
                    seenEmails,
                    overrides,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return new NotificationRecipientResolutionPreview(deliverable, skipped, inApp);
    }

    private async ValueTask<bool> ShouldApplyImplicitAttentionRoutingAsync(
        string definitionId,
        string? integrationId,
        IReadOnlyList<NotificationSubscription> subs,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                definitionId,
                NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(integrationId))
        {
            return false;
        }

        var routing = await _db.IntegrationAttentionRoutings.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.IntegrationId == integrationId
                     && x.ConditionKey == AttentionConditionKeys.ExpectedRunMissing,
                cancellationToken)
            .ConfigureAwait(false);
        if (routing is null || string.IsNullOrWhiteSpace(routing.PrimaryOperationalTeamId))
            return false;

        return !subs.Any(s =>
            s.RecipientKind == NotificationRecipientKinds.OperationalTeam
            && string.Equals(s.RecipientRef, routing.PrimaryOperationalTeamId, StringComparison.Ordinal));
    }

    private async ValueTask ApplyImplicitAttentionRoutingAsync(
        string integrationId,
        List<ResolvedNotificationRecipient> deliverable,
        List<SkippedNotificationRecipient> skipped,
        List<InAppEligibleRecipient> inApp,
        Dictionary<string, ResolvedNotificationRecipient> seenEmails,
        IReadOnlyList<NotificationSubscriptionUserOverride> overrides,
        CancellationToken cancellationToken)
    {
        var routing = await _db.IntegrationAttentionRoutings.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.IntegrationId == integrationId
                     && x.ConditionKey == AttentionConditionKeys.ExpectedRunMissing,
                cancellationToken)
            .ConfigureAwait(false);
        if (routing is null || string.IsNullOrWhiteSpace(routing.PrimaryOperationalTeamId))
            return;

        var expansion = await _teamExpander
            .ExpandAsync(routing.PrimaryOperationalTeamId, cancellationToken)
            .ConfigureAwait(false);
        if (expansion.NoRouteReason is not null)
            return;

        foreach (var member in expansion.Members)
        {
            ProcessExpandedMember(
                member,
                expansion.TeamId,
                NotificationRecipientSources.AttentionRoutingIntegrationOverride,
                subscriptionId: null,
                recipientKind: NotificationRecipientKinds.OperationalTeam,
                deliverable,
                skipped,
                inApp,
                seenEmails,
                overrides);
        }
    }

    private async ValueTask ExpandSubscriptionAsync(
        NotificationSubscription sub,
        List<ResolvedNotificationRecipient> deliverable,
        List<SkippedNotificationRecipient> skipped,
        List<InAppEligibleRecipient> inApp,
        Dictionary<string, ResolvedNotificationRecipient> seenEmails,
        IReadOnlyList<NotificationSubscriptionUserOverride> overrides,
        CancellationToken cancellationToken)
    {
        if (sub.DeliveryMode == NotificationDeliveryModes.Muted)
        {
            skipped.Add(new SkippedNotificationRecipient(
                null,
                null,
                null,
                null,
                NotificationRecipientSources.DirectSubscription,
                sub.Id.Value,
                sub.RecipientKind,
                NotificationRecipientSkipReasons.DeliveryModeMuted));
            return;
        }

        if (sub.DeliveryMode == NotificationDeliveryModes.DigestOnly)
        {
            skipped.Add(new SkippedNotificationRecipient(
                null,
                null,
                null,
                null,
                NotificationRecipientSources.DirectSubscription,
                sub.Id.Value,
                sub.RecipientKind,
                NotificationRecipientSkipReasons.DeliveryModeDigestOnly));
            return;
        }

        switch (sub.RecipientKind)
        {
            case NotificationRecipientKinds.Principal:
                await ExpandPrincipalAsync(sub, deliverable, skipped, inApp, seenEmails, overrides, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case NotificationRecipientKinds.OperationalTeam:
                await ExpandOperationalTeamAsync(sub, deliverable, skipped, inApp, seenEmails, overrides, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case NotificationRecipientKinds.ManagedContact:
                await ExpandManagedContactAsync(sub, deliverable, skipped, inApp, seenEmails, overrides, cancellationToken)
                    .ConfigureAwait(false);
                break;
        }
    }

    private async ValueTask ExpandPrincipalAsync(
        NotificationSubscription sub,
        List<ResolvedNotificationRecipient> deliverable,
        List<SkippedNotificationRecipient> skipped,
        List<InAppEligibleRecipient> inApp,
        Dictionary<string, ResolvedNotificationRecipient> seenEmails,
        IReadOnlyList<NotificationSubscriptionUserOverride> overrides,
        CancellationToken cancellationToken)
    {
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sub.RecipientRef, cancellationToken)
            .ConfigureAwait(false);
        if (user is null)
            return;

        var member = new OperationalTeamExpandedMember(
            user.Id,
            null,
            user.EmailAddress?.Trim(),
            user.DisplayName,
            "Principal",
            string.IsNullOrWhiteSpace(user.EmailAddress)
                ? NotificationUsabilityStatusValues.MissingEmail
                : NotificationUsabilityStatusValues.Deliverable);

        ProcessExpandedMember(
            member,
            operationalTeamId: null,
            NotificationRecipientSources.DirectSubscription,
            sub.Id.Value,
            NotificationRecipientKinds.Principal,
            deliverable,
            skipped,
            inApp,
            seenEmails,
            overrides);
    }

    private async ValueTask ExpandOperationalTeamAsync(
        NotificationSubscription sub,
        List<ResolvedNotificationRecipient> deliverable,
        List<SkippedNotificationRecipient> skipped,
        List<InAppEligibleRecipient> inApp,
        Dictionary<string, ResolvedNotificationRecipient> seenEmails,
        IReadOnlyList<NotificationSubscriptionUserOverride> overrides,
        CancellationToken cancellationToken)
    {
        var expansion = await _teamExpander.ExpandAsync(sub.RecipientRef, cancellationToken).ConfigureAwait(false);
        if (expansion.NoRouteReason == "TeamDisabled")
        {
            skipped.Add(new SkippedNotificationRecipient(
                null,
                expansion.TeamName,
                null,
                null,
                NotificationRecipientSources.DirectSubscription,
                sub.Id.Value,
                sub.RecipientKind,
                NotificationRecipientSkipReasons.TeamDisabled));
            return;
        }

        if (expansion.NoRouteReason == "TeamEmpty")
        {
            skipped.Add(new SkippedNotificationRecipient(
                null,
                expansion.TeamName,
                null,
                null,
                NotificationRecipientSources.DirectSubscription,
                sub.Id.Value,
                sub.RecipientKind,
                NotificationRecipientSkipReasons.TeamEmpty));
            return;
        }

        foreach (var member in expansion.Members)
        {
            ProcessExpandedMember(
                member,
                expansion.TeamId,
                NotificationRecipientSources.OperationalTeamMembership,
                sub.Id.Value,
                NotificationRecipientKinds.OperationalTeam,
                deliverable,
                skipped,
                inApp,
                seenEmails,
                overrides);
        }
    }

    private async ValueTask ExpandManagedContactAsync(
        NotificationSubscription sub,
        List<ResolvedNotificationRecipient> deliverable,
        List<SkippedNotificationRecipient> skipped,
        List<InAppEligibleRecipient> inApp,
        Dictionary<string, ResolvedNotificationRecipient> seenEmails,
        IReadOnlyList<NotificationSubscriptionUserOverride> overrides,
        CancellationToken cancellationToken)
    {
        var contact = await _db.ManagedContacts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sub.RecipientRef, cancellationToken)
            .ConfigureAwait(false);
        if (contact is null)
            return;

        var usability = !contact.IsEnabled
            ? NotificationUsabilityStatusValues.ContactDisabled
            : string.IsNullOrWhiteSpace(contact.EmailAddress)
                ? NotificationUsabilityStatusValues.MissingEmail
                : NotificationUsabilityStatusValues.Deliverable;

        var member = new OperationalTeamExpandedMember(
            contact.LinkedUserId,
            contact.Id,
            contact.EmailAddress?.Trim(),
            contact.DisplayName,
            "ManagedContact",
            usability);

        ProcessExpandedMember(
            member,
            operationalTeamId: null,
            NotificationRecipientSources.DirectSubscription,
            sub.Id.Value,
            NotificationRecipientKinds.ManagedContact,
            deliverable,
            skipped,
            inApp,
            seenEmails,
            overrides);
    }

    private static void ProcessExpandedMember(
        OperationalTeamExpandedMember member,
        string? operationalTeamId,
        string source,
        string? subscriptionId,
        string recipientKind,
        List<ResolvedNotificationRecipient> deliverable,
        List<SkippedNotificationRecipient> skipped,
        List<InAppEligibleRecipient> inApp,
        Dictionary<string, ResolvedNotificationRecipient> seenEmails,
        IReadOnlyList<NotificationSubscriptionUserOverride> overrides)
    {
        if (IsOptedOut(member.UserId, operationalTeamId, overrides))
        {
            skipped.Add(new SkippedNotificationRecipient(
                member.Email,
                member.DisplayName,
                member.UserId,
                member.ManagedContactId,
                source,
                subscriptionId,
                recipientKind,
                NotificationRecipientSkipReasons.UserOptOut));
            return;
        }

        if (member.Usability == NotificationUsabilityStatusValues.ContactDisabled)
        {
            skipped.Add(new SkippedNotificationRecipient(
                member.Email,
                member.DisplayName,
                member.UserId,
                member.ManagedContactId,
                source,
                subscriptionId,
                recipientKind,
                NotificationRecipientSkipReasons.ContactDisabled));
            return;
        }

        if (!string.IsNullOrWhiteSpace(member.UserId))
        {
            inApp.Add(new InAppEligibleRecipient(
                member.UserId,
                member.DisplayName,
                member.Email,
                source,
                subscriptionId,
                recipientKind));
        }

        if (member.Usability == NotificationUsabilityStatusValues.MissingEmail)
        {
            skipped.Add(new SkippedNotificationRecipient(
                member.Email,
                member.DisplayName,
                member.UserId,
                member.ManagedContactId,
                source,
                subscriptionId,
                recipientKind,
                NotificationRecipientSkipReasons.MissingEmail));
            return;
        }

        var email = member.Email!.Trim();
        if (seenEmails.ContainsKey(email))
        {
            skipped.Add(new SkippedNotificationRecipient(
                email,
                member.DisplayName,
                member.UserId,
                member.ManagedContactId,
                source,
                subscriptionId,
                recipientKind,
                NotificationRecipientSkipReasons.DuplicateEmail));
            return;
        }

        var resolved = new ResolvedNotificationRecipient(
            email,
            member.DisplayName,
            member.UserId,
            member.ManagedContactId,
            member.Usability,
            source,
            subscriptionId,
            recipientKind);
        seenEmails[email] = resolved;
        deliverable.Add(resolved);
    }

    private static bool IsOptedOut(
        string? userId,
        string? operationalTeamId,
        IReadOnlyList<NotificationSubscriptionUserOverride> overrides)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        foreach (var o in overrides)
        {
            if (!string.Equals(o.UserId, userId, StringComparison.Ordinal))
                continue;
            if (o.OperationalTeamId is null)
                return true;
            if (!string.IsNullOrWhiteSpace(operationalTeamId)
                && string.Equals(o.OperationalTeamId, operationalTeamId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
