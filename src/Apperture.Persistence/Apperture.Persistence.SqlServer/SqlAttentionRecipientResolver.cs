using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlAttentionRecipientResolver : IAttentionRecipientResolver
{
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

        var primaryEx = await ExpandGroupAsync(inst.PrimaryContactGroupId, cancellationToken).ConfigureAwait(false);
        var escEx = await ExpandGroupAsync(inst.EscalationContactGroupId, cancellationToken).ConfigureAwait(false);

        return Build(
            inst,
            integrationOverride: null,
            primaryEx,
            escEx,
            primarySource: string.IsNullOrWhiteSpace(inst.PrimaryContactGroupId)
                ? AttentionRecipientSources.None
                : AttentionRecipientSources.GlobalDefault,
            escSource: string.IsNullOrWhiteSpace(inst.EscalationContactGroupId)
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

        string? primaryGroupId;
        string primarySource;
        if (integ is not null && !string.IsNullOrWhiteSpace(integ.PrimaryContactGroupId))
        {
            primaryGroupId = integ.PrimaryContactGroupId;
            primarySource = AttentionRecipientSources.IntegrationOverride;
        }
        else
        {
            primaryGroupId = inst.PrimaryContactGroupId;
            primarySource = string.IsNullOrWhiteSpace(primaryGroupId)
                ? AttentionRecipientSources.None
                : AttentionRecipientSources.GlobalDefault;
        }

        string? escGroupId;
        string escSource;
        if (integ is not null && !string.IsNullOrWhiteSpace(integ.EscalationContactGroupId))
        {
            escGroupId = integ.EscalationContactGroupId;
            escSource = AttentionRecipientSources.IntegrationOverride;
        }
        else
        {
            escGroupId = inst.EscalationContactGroupId;
            escSource = string.IsNullOrWhiteSpace(escGroupId)
                ? AttentionRecipientSources.None
                : AttentionRecipientSources.GlobalDefault;
        }

        var primaryEx = await ExpandGroupAsync(primaryGroupId, cancellationToken).ConfigureAwait(false);
        var escEx = await ExpandGroupAsync(escGroupId, cancellationToken).ConfigureAwait(false);

        return Build(inst, integ, primaryEx, escEx, primarySource, escSource);
    }

    private AttentionRecipientResolution Build(
        InstanceAttentionRoutingEntity inst,
        IntegrationAttentionRoutingEntity? integrationOverride,
        GroupExpansion primaryEx,
        GroupExpansion escEx,
        string primarySource,
        string escSource)
    {
        bool policyEnabled;
        bool sendResolved;
        bool suppressRem;
        bool suppressEsc;
        long? escTicks;
        long? remTicks;

        if (integrationOverride is not null)
        {
            policyEnabled = integrationOverride.IsEnabled;
            sendResolved = integrationOverride.SendResolvedNotification;
            suppressRem = integrationOverride.SuppressRemindersWhenAcknowledged;
            suppressEsc = integrationOverride.SuppressEscalationWhenAcknowledged;
            escTicks = integrationOverride.EscalationAfterTicks;
            remTicks = integrationOverride.ReminderIntervalTicks;
        }
        else
        {
            policyEnabled = inst.IsEnabled;
            sendResolved = inst.SendResolvedNotification;
            suppressRem = inst.SuppressRemindersWhenAcknowledged;
            suppressEsc = inst.SuppressEscalationWhenAcknowledged;
            escTicks = inst.EscalationAfterTicks;
            remTicks = inst.ReminderIntervalTicks;
        }

        var hasPrimary = primaryEx.Emails.Length > 0;
        var hasEsc = escEx.Emails.Length > 0;
        var hasRoute = hasPrimary || hasEsc;

        return new AttentionRecipientResolution(
            hasRoute,
            policyEnabled,
            primaryEx.Emails,
            primarySource,
            escEx.Emails,
            escSource,
            primaryEx.GroupId,
            primaryEx.GroupName,
            escEx.GroupId,
            escEx.GroupName,
            hasPrimary,
            hasEsc,
            primaryEx.NoRouteReason,
            escEx.NoRouteReason,
            escTicks is { } et ? TimeSpan.FromTicks(et) : null,
            remTicks is { } rt ? TimeSpan.FromTicks(rt) : null,
            sendResolved,
            suppressRem,
            suppressEsc,
            integrationOverride?.PrimaryContactGroupId,
            integrationOverride?.EscalationContactGroupId);
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
            AttentionNoRouteReasons.NoGroupConfigured,
            AttentionNoRouteReasons.NoGroupConfigured,
            null,
            null,
            false,
            false,
            false,
            null,
            null);

    private async ValueTask<GroupExpansion> ExpandGroupAsync(string? groupId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(groupId))
            return new GroupExpansion(Array.Empty<string>(), null, null, AttentionNoRouteReasons.NoGroupConfigured);

        var g = await _db.AttentionContactGroups.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == groupId, cancellationToken)
            .ConfigureAwait(false);
        if (g is null)
            return new GroupExpansion(Array.Empty<string>(), groupId, null, AttentionNoRouteReasons.NoGroupConfigured);
        if (!g.IsEnabled)
            return new GroupExpansion(Array.Empty<string>(), g.Id, g.Name, AttentionNoRouteReasons.GroupDisabled);

        var raw = await _db.AttentionContactGroupMembers.AsNoTracking()
            .Where(m => m.AttentionContactGroupId == g.Id && m.IsEnabled)
            .Select(m => m.EmailAddress)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var emails = raw
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (emails.Length == 0)
            return new GroupExpansion(Array.Empty<string>(), g.Id, g.Name, AttentionNoRouteReasons.GroupEmpty);

        return new GroupExpansion(emails, g.Id, g.Name, null);
    }

    private readonly record struct GroupExpansion(
        string[] Emails,
        string? GroupId,
        string? GroupName,
        string? NoRouteReason);
}
