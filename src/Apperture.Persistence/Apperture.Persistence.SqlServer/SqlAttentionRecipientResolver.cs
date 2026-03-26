using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlAttentionRecipientResolver : IAttentionRecipientResolver
{
    private readonly AppertureDbContext _db;

    public SqlAttentionRecipientResolver(AppertureDbContext db) => _db = db;

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
        {
            return new AttentionRecipientResolution(
                false,
                false,
                Array.Empty<string>(),
                AttentionRecipientSources.None,
                Array.Empty<string>(),
                AttentionRecipientSources.None,
                null,
                null,
                false,
                false,
                false);
        }

        var instPrimary = await LoadAddressesAsync(inst.Id, null, AttentionRoutingRecipientRoles.Primary, cancellationToken)
            .ConfigureAwait(false);
        var instEsc = await LoadAddressesAsync(inst.Id, null, AttentionRoutingRecipientRoles.Escalation, cancellationToken)
            .ConfigureAwait(false);

        var integ = await _db.IntegrationAttentionRoutings.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.IntegrationId == integrationId.Value && x.ConditionKey == conditionKey,
                cancellationToken)
            .ConfigureAwait(false);

        string[] integPrimary = Array.Empty<string>();
        string[] integEsc = Array.Empty<string>();
        bool policyEnabled;
        bool sendResolved;
        bool suppressRem;
        bool suppressEsc;
        long? escTicks;
        long? remTicks;

        if (integ is not null)
        {
            integPrimary = await LoadAddressesAsync(null, integ.Id, AttentionRoutingRecipientRoles.Primary, cancellationToken)
                .ConfigureAwait(false);
            integEsc = await LoadAddressesAsync(null, integ.Id, AttentionRoutingRecipientRoles.Escalation, cancellationToken)
                .ConfigureAwait(false);
            policyEnabled = integ.IsEnabled;
            sendResolved = integ.SendResolvedNotification;
            suppressRem = integ.SuppressRemindersWhenAcknowledged;
            suppressEsc = integ.SuppressEscalationWhenAcknowledged;
            escTicks = integ.EscalationAfterTicks;
            remTicks = integ.ReminderIntervalTicks;
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

        var primary = integPrimary.Length > 0 ? integPrimary : instPrimary;
        var primarySource = integPrimary.Length > 0
            ? AttentionRecipientSources.IntegrationOverride
            : instPrimary.Length > 0
                ? AttentionRecipientSources.GlobalDefault
                : AttentionRecipientSources.None;

        var esc = integEsc.Length > 0 ? integEsc : instEsc;
        var escSource = integEsc.Length > 0
            ? AttentionRecipientSources.IntegrationOverride
            : instEsc.Length > 0
                ? AttentionRecipientSources.GlobalDefault
                : AttentionRecipientSources.None;

        var hasRoute = primary.Length > 0 || esc.Length > 0;

        return new AttentionRecipientResolution(
            hasRoute,
            policyEnabled,
            primary,
            primarySource,
            esc,
            escSource,
            escTicks is { } et ? TimeSpan.FromTicks(et) : null,
            remTicks is { } rt ? TimeSpan.FromTicks(rt) : null,
            sendResolved,
            suppressRem,
            suppressEsc);
    }

    private async Task<string[]> LoadAddressesAsync(
        string? instanceRoutingId,
        string? integrationRoutingId,
        string role,
        CancellationToken cancellationToken)
    {
        var q = _db.AttentionRoutingRecipients.AsNoTracking().Where(r => r.Role == role);
        if (instanceRoutingId is not null)
            q = q.Where(r => r.InstanceAttentionRoutingId == instanceRoutingId);
        else
            q = q.Where(r => r.IntegrationAttentionRoutingId == integrationRoutingId);
        var list = await q.Select(r => r.Address).ToListAsync(cancellationToken).ConfigureAwait(false);
        return list
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
