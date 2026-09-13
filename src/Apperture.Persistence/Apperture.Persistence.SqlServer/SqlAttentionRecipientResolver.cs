using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlAttentionRecipientResolver : IAttentionRecipientResolver
{
    private const string MemberKindPrincipal = "Principal";
    private const string MemberKindExternalEmail = "ExternalEmail";
    private const string MemberKindManagedContact = "ManagedContact";

    private readonly AppertureDbContext _db;
    private readonly OperationalTeamRecipientExpander _teamExpander;

    public SqlAttentionRecipientResolver(AppertureDbContext db, OperationalTeamRecipientExpander teamExpander)
    {
        _db = db;
        _teamExpander = teamExpander;
    }

    public async ValueTask<AttentionRecipientResolution> ResolveInstanceDefaultsAsync(
        string conditionKey,
        CancellationToken cancellationToken = default)
    {
        var inst = await _db.InstanceAttentionRoutings.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == InstanceAttentionRoutingDefaults.RowId && x.ConditionKey == conditionKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (inst is null)
            return UnconfiguredInstance();

        var primaryEx = await ExpandTeamAsync(inst.PrimaryOperationalTeamId, cancellationToken).ConfigureAwait(false);
        var escEx = await ExpandTeamAsync(inst.EscalationOperationalTeamId, cancellationToken).ConfigureAwait(false);

        return Build(
            inst,
            integrationOverride: null,
            primaryEx,
            escEx,
            primarySource: string.IsNullOrWhiteSpace(inst.PrimaryOperationalTeamId)
                ? AttentionRecipientSources.None
                : AttentionRecipientSources.GlobalDefault,
            escSource: string.IsNullOrWhiteSpace(inst.EscalationOperationalTeamId)
                ? AttentionRecipientSources.None
                : AttentionRecipientSources.GlobalDefault);
    }

    public async ValueTask<AttentionRecipientResolution> ResolveAsync(
        IntegrationId integrationId,
        string conditionKey,
        CancellationToken cancellationToken = default)
    {
        var inst = await _db.InstanceAttentionRoutings.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == InstanceAttentionRoutingDefaults.RowId && x.ConditionKey == conditionKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (inst is null)
            return UnconfiguredInstance();

        var integ = await _db.IntegrationAttentionRoutings.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.IntegrationId == integrationId.Value && x.ConditionKey == conditionKey,
                cancellationToken)
            .ConfigureAwait(false);

        string? primaryTeamId;
        string primarySource;
        if (integ is not null && !string.IsNullOrWhiteSpace(integ.PrimaryOperationalTeamId))
        {
            primaryTeamId = integ.PrimaryOperationalTeamId;
            primarySource = AttentionRecipientSources.IntegrationOverride;
        }
        else
        {
            primaryTeamId = inst.PrimaryOperationalTeamId;
            primarySource = string.IsNullOrWhiteSpace(primaryTeamId)
                ? AttentionRecipientSources.None
                : AttentionRecipientSources.GlobalDefault;
        }

        string? escTeamId;
        string escSource;
        if (integ is not null && !string.IsNullOrWhiteSpace(integ.EscalationOperationalTeamId))
        {
            escTeamId = integ.EscalationOperationalTeamId;
            escSource = AttentionRecipientSources.IntegrationOverride;
        }
        else
        {
            escTeamId = inst.EscalationOperationalTeamId;
            escSource = string.IsNullOrWhiteSpace(escTeamId)
                ? AttentionRecipientSources.None
                : AttentionRecipientSources.GlobalDefault;
        }

        var primaryEx = await ExpandTeamAsync(primaryTeamId, cancellationToken).ConfigureAwait(false);
        var escEx = await ExpandTeamAsync(escTeamId, cancellationToken).ConfigureAwait(false);

        return Build(inst, integ, primaryEx, escEx, primarySource, escSource);
    }

    private AttentionRecipientResolution Build(
        InstanceAttentionRoutingEntity inst,
        IntegrationAttentionRoutingEntity? integrationOverride,
        TeamExpansion primaryEx,
        TeamExpansion escEx,
        string primarySource,
        string escSource)
    {
        var policy = integrationOverride is not null
            ? (integrationOverride.IsEnabled,
                integrationOverride.SendResolvedNotification,
                integrationOverride.SuppressRemindersWhenAcknowledged,
                integrationOverride.SuppressEscalationWhenAcknowledged,
                integrationOverride.EscalationAfterTicks,
                integrationOverride.ReminderIntervalTicks)
            : (inst.IsEnabled,
                inst.SendResolvedNotification,
                inst.SuppressRemindersWhenAcknowledged,
                inst.SuppressEscalationWhenAcknowledged,
                inst.EscalationAfterTicks,
                inst.ReminderIntervalTicks);

        var hasPrimary = primaryEx.AllRecipients.Length > 0;
        var hasEsc = escEx.AllRecipients.Length > 0;
        var hasRoute = hasPrimary || hasEsc;

        return new AttentionRecipientResolution(
            hasRoute,
            policy.Item1,
            primaryEx.AllRecipients,
            primarySource,
            escEx.AllRecipients,
            escSource,
            primaryEx.TeamId,
            primaryEx.TeamName,
            escEx.TeamId,
            escEx.TeamName,
            hasPrimary,
            hasEsc,
            primaryEx.NoRouteReason,
            escEx.NoRouteReason,
            primaryEx.PrincipalUserIds,
            escEx.PrincipalUserIds,
            primaryEx.ExternalRecipients,
            escEx.ExternalRecipients,
            policy.Item5 is { } et ? TimeSpan.FromTicks(et) : null,
            policy.Item6 is { } rt ? TimeSpan.FromTicks(rt) : null,
            policy.Item2,
            policy.Item3,
            policy.Item4,
            integrationOverride?.PrimaryOperationalTeamId,
            integrationOverride?.EscalationOperationalTeamId);
    }

    private static AttentionRecipientResolution UnconfiguredInstance() =>
        new(
            false,
            false,
            Array.Empty<string>(),
            AttentionRecipientSources.None,
            Array.Empty<string>(),
            AttentionRecipientSources.None,
            null,
            null,
            null,
            null,
            false,
            false,
            AttentionNoRouteReasons.NoTeamConfigured,
            AttentionNoRouteReasons.NoTeamConfigured,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            null,
            null,
            false,
            false,
            false,
            null,
            null);

    private async ValueTask<TeamExpansion> ExpandTeamAsync(string? teamId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return TeamExpansion.NoRoute(AttentionNoRouteReasons.NoTeamConfigured);

        var expansion = await _teamExpander.ExpandAsync(teamId, cancellationToken).ConfigureAwait(false);
        if (expansion.NoRouteReason == "NoTeamConfigured")
            return TeamExpansion.NoRoute(AttentionNoRouteReasons.NoTeamConfigured, teamId, null);
        if (expansion.NoRouteReason == "TeamDisabled")
            return TeamExpansion.NoRoute(AttentionNoRouteReasons.TeamDisabled, expansion.TeamId, expansion.TeamName);
        if (expansion.NoRouteReason == "TeamEmpty")
            return TeamExpansion.NoRoute(AttentionNoRouteReasons.TeamEmpty, expansion.TeamId, expansion.TeamName);

        var members = await _db.OperationalTeamMembers.AsNoTracking()
            .Where(m => m.OperationalTeamId == expansion.TeamId && m.IsEnabled)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var principalIds = expansion.Members
            .Where(m => string.Equals(m.MemberKind, MemberKindPrincipal, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.UserId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var externalRecipients = expansion.Members
            .Where(m => string.Equals(m.MemberKind, MemberKindExternalEmail, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Email)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var managedContactIds = expansion.Members
            .Where(m => string.Equals(m.MemberKind, MemberKindManagedContact, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.ManagedContactId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var principalEmails = expansion.Members
            .Where(m => string.Equals(m.MemberKind, MemberKindPrincipal, StringComparison.OrdinalIgnoreCase)
                        && m.Usability == NotificationUsabilityStatusValues.Deliverable)
            .Select(m => m.Email!)
            .ToArray();
        var managedContactEmails = expansion.Members
            .Where(m => string.Equals(m.MemberKind, MemberKindManagedContact, StringComparison.OrdinalIgnoreCase)
                        && m.Usability == NotificationUsabilityStatusValues.Deliverable)
            .Select(m => m.Email!)
            .ToArray();

        var recipients = await _teamExpander.ExpandToEmailAddressesAsync(teamId, cancellationToken).ConfigureAwait(false);

        var noRouteReason = ResolveNoRouteWhenRecipientsEmpty(
            recipients,
            members,
            principalIds,
            principalEmails,
            managedContactIds,
            managedContactEmails,
            externalRecipients);

        return new TeamExpansion(
            recipients,
            principalIds,
            externalRecipients,
            expansion.TeamId,
            expansion.TeamName,
            noRouteReason);
    }

    private readonly record struct TeamExpansion(
        string[] AllRecipients,
        string[] PrincipalUserIds,
        string[] ExternalRecipients,
        string? TeamId,
        string? TeamName,
        string? NoRouteReason)
    {
        public static TeamExpansion NoRoute(string reason, string? teamId = null, string? teamName = null) =>
            new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), teamId, teamName, reason);
    }

    /// <summary>
    /// When the union of deliverable addresses is empty but the team has enabled members,
    /// classify why routing failed without mis-attributing managed-contact gaps to principals.
    /// </summary>
    private static string? ResolveNoRouteWhenRecipientsEmpty(
        string[] recipients,
        List<OperationalTeamMemberEntity> members,
        string[] principalIds,
        string[] principalEmails,
        string[] managedContactIds,
        string[] managedContactEmails,
        string[] externalRecipients)
    {
        if (recipients.Length > 0)
            return null;

        static bool HasDeliverable(IEnumerable<string> emails) =>
            emails.Any(e => !string.IsNullOrWhiteSpace(e) && e.Trim().Length > 0);

        var principalsDeliverable = HasDeliverable(principalEmails);
        var managedDeliverable = HasDeliverable(managedContactEmails);
        var externalDeliverable = externalRecipients.Length > 0;
        if (principalsDeliverable || managedDeliverable || externalDeliverable)
            return null;

        var hasPrincipalMember = principalIds.Length > 0;
        var hasManagedMember = managedContactIds.Length > 0;
        var hasExternalMember = members.Exists(m =>
            m.IsEnabled &&
            string.Equals(m.MemberKind, MemberKindExternalEmail, StringComparison.OrdinalIgnoreCase));

        if (hasPrincipalMember && !hasManagedMember && !hasExternalMember)
            return AttentionNoRouteReasons.PrincipalMembersNoEmail;

        if (hasManagedMember && !hasPrincipalMember && !hasExternalMember)
            return AttentionNoRouteReasons.ManagedContactMembersNoDeliverableEmail;

        return AttentionNoRouteReasons.MemberRecipientsNoDeliverableEmail;
    }
}
