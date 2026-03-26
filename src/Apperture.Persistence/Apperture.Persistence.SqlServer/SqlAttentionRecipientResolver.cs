using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlAttentionRecipientResolver : IAttentionRecipientResolver
{
    private const string MemberKindPrincipal = "Principal";
    private const string MemberKindExternalEmail = "ExternalEmail";

    private readonly AppertureDbContext _db;

    public SqlAttentionRecipientResolver(AppertureDbContext db) => _db = db;

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

        var team = await _db.OperationalTeams.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == teamId, cancellationToken)
            .ConfigureAwait(false);
        if (team is null)
            return TeamExpansion.NoRoute(AttentionNoRouteReasons.NoTeamConfigured, teamId, null);
        if (!team.IsEnabled)
            return TeamExpansion.NoRoute(AttentionNoRouteReasons.TeamDisabled, team.Id, team.Name);

        var members = await _db.OperationalTeamMembers.AsNoTracking()
            .Where(m => m.OperationalTeamId == team.Id && m.IsEnabled)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (members.Count == 0)
            return TeamExpansion.NoRoute(AttentionNoRouteReasons.TeamEmpty, team.Id, team.Name);

        var principalIds = members
            .Where(m => string.Equals(m.MemberKind, MemberKindPrincipal, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.PrincipalUserId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var externalRecipients = members
            .Where(m => string.Equals(m.MemberKind, MemberKindExternalEmail, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.EmailAddress)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var principalEmails = principalIds.Length == 0
            ? Array.Empty<string>()
            : await _db.Users.AsNoTracking()
                .Where(u => principalIds.Contains(u.Id) && u.EmailAddress != null)
                .Select(u => u.EmailAddress!)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

        var recipients = principalEmails
            .Concat(externalRecipients)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var noRouteReason = recipients.Length == 0
            ? (principalIds.Length > 0
                ? AttentionNoRouteReasons.PrincipalMembersNoEmail
                : AttentionNoRouteReasons.TeamEmpty)
            : null;

        return new TeamExpansion(
            recipients,
            principalIds,
            externalRecipients,
            team.Id,
            team.Name,
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
}
