using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Management.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/attention-routing")]
[Authorize]
public sealed class AttentionRoutingDefaultsController : ControllerBase
{
    private readonly IScheduleGovernanceService _scheduleGovernance;
    private readonly IAttentionRecipientResolver _resolver;
    private readonly IAttentionRoutingManagementService _routing;

    public AttentionRoutingDefaultsController(
        IScheduleGovernanceService scheduleGovernance,
        IAttentionRecipientResolver resolver,
        IAttentionRoutingManagementService routing)
    {
        _scheduleGovernance = scheduleGovernance;
        _resolver = resolver;
        _routing = routing;
    }

    [HttpGet("defaults")]
    public async Task<ActionResult<AttentionRoutingEffectiveDto>> GetDefaults(
        [FromQuery] string? conditionKey,
        CancellationToken cancellationToken = default)
    {
        if (!await _scheduleGovernance.CanManageAsync(cancellationToken).ConfigureAwait(false))
            return Forbid();
        var key = string.IsNullOrWhiteSpace(conditionKey)
            ? AttentionConditionKeys.ExpectedRunMissing
            : conditionKey.Trim();
        var r = await _resolver.ResolveInstanceDefaultsAsync(key, cancellationToken).ConfigureAwait(false);
        return Ok(ToDto(r));
    }

    [HttpPut("defaults")]
    public async Task<ActionResult<AttentionRoutingEffectiveDto>> PutDefaults(
        [FromBody] UpsertAttentionRoutingRequest body,
        [FromQuery] string? conditionKey,
        CancellationToken cancellationToken = default)
    {
        if (!await _scheduleGovernance.CanManageAsync(cancellationToken).ConfigureAwait(false))
            return Forbid();
        var key = string.IsNullOrWhiteSpace(conditionKey)
            ? AttentionConditionKeys.ExpectedRunMissing
            : conditionKey.Trim();
        try
        {
            await _routing.UpsertInstanceDefaultRoutingAsync(
                    key,
                    new AttentionRoutingUpsertCommand(
                        body.IsEnabled,
                        body.SendResolvedNotification,
                        body.SuppressRemindersWhenAcknowledged,
                        body.SuppressEscalationWhenAcknowledged,
                        body.EscalationAfterTicks is { } et ? TimeSpan.FromTicks(et) : null,
                        body.ReminderIntervalTicks is { } rt ? TimeSpan.FromTicks(rt) : null,
                        body.PrimaryContactGroupId,
                        body.EscalationContactGroupId),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }

        var r = await _resolver.ResolveInstanceDefaultsAsync(key, cancellationToken).ConfigureAwait(false);
        return Ok(ToDto(r));
    }

    private static AttentionRoutingEffectiveDto ToDto(AttentionRecipientResolution r) =>
        new(
            r.HasRoute,
            r.Enabled,
            r.PrimaryRecipients.ToList(),
            r.PrimarySource,
            r.EscalationRecipients.ToList(),
            r.EscalationSource,
            r.PrimaryContactGroupId,
            r.PrimaryContactGroupName,
            r.EscalationContactGroupId,
            r.EscalationContactGroupName,
            r.HasPrimaryRoute,
            r.HasEscalationRoute,
            r.PrimaryNoRouteReason,
            r.EscalationNoRouteReason,
            r.EscalationAfter?.Ticks,
            r.ReminderInterval?.Ticks,
            r.SendResolvedNotification,
            r.SuppressRemindersWhenAcknowledged,
            r.SuppressEscalationWhenAcknowledged,
            r.IntegrationRowPrimaryContactGroupId,
            r.IntegrationRowEscalationContactGroupId);
}
