using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Notifications.Inbox;
using Apperture.Core.Application.Services;
using Apperture.Management.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>Personal notification inbox for the authenticated platform user (Epic 102).</summary>
[ApiController]
[Route("api/me/notifications")]
[Authorize]
public sealed class MeNotificationsController : ControllerBase
{
    private readonly IPrincipalContext _principalContext;
    private readonly IPrincipalToUserResolver _principalToUserResolver;
    private readonly INotificationUserInboxService _inbox;
    private readonly INotificationUserInboxStore _inboxStore;
    private readonly NotificationDefinitionAuthorization _definitionAuth;

    public MeNotificationsController(
        IPrincipalContext principalContext,
        IPrincipalToUserResolver principalToUserResolver,
        INotificationUserInboxService inbox,
        INotificationUserInboxStore inboxStore,
        NotificationDefinitionAuthorization definitionAuth)
    {
        _principalContext = principalContext;
        _principalToUserResolver = principalToUserResolver;
        _inbox = inbox;
        _inboxStore = inboxStore;
        _definitionAuth = definitionAuth;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PersonalNotificationInboxItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<PersonalNotificationInboxItemDto>>> List(
        [FromQuery] string? readFilter = "all",
        [FromQuery] int take = 50,
        [FromQuery] string? integrationId = null,
        [FromQuery] string? kind = null,
        CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserIdAsync(cancellationToken).ConfigureAwait(false);
        if (userId is null)
            return Forbid();

        var filter = ParseReadFilter(readFilter);
        var items = await _inbox
            .ListForUserAsync(userId, new PersonalNotificationListQuery(filter, take, integrationId, kind), cancellationToken)
            .ConfigureAwait(false);

        var canAdmin = await _definitionAuth.CanViewDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        return Ok(items.Select(x => ToDto(x, canAdmin)).ToList());
    }

    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(PersonalNotificationUnreadCountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PersonalNotificationUnreadCountDto>> UnreadCount(CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserIdAsync(cancellationToken).ConfigureAwait(false);
        if (userId is null)
            return Forbid();

        var count = await _inbox.GetUnreadCountAsync(userId, cancellationToken).ConfigureAwait(false);
        return Ok(new PersonalNotificationUnreadCountDto { UnreadCount = count });
    }

    [HttpPost("{id}/mark-read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> MarkRead(string id, CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserIdAsync(cancellationToken).ConfigureAwait(false);
        if (userId is null)
            return Forbid();

        var ok = await _inbox.MarkReadAsync(userId, id, cancellationToken).ConfigureAwait(false);
        return ok ? NoContent() : NotFound();
    }

    [HttpPost("{id}/mark-unread")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> MarkUnread(string id, CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserIdAsync(cancellationToken).ConfigureAwait(false);
        if (userId is null)
            return Forbid();

        var ok = await _inbox.MarkUnreadAsync(userId, id, cancellationToken).ConfigureAwait(false);
        return ok ? NoContent() : NotFound();
    }

    [HttpPost("mark-all-read")]
    [ProducesResponseType(typeof(PersonalNotificationMarkAllReadResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PersonalNotificationMarkAllReadResultDto>> MarkAllRead(CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserIdAsync(cancellationToken).ConfigureAwait(false);
        if (userId is null)
            return Forbid();

        var count = await _inbox.MarkAllReadAsync(userId, cancellationToken).ConfigureAwait(false);
        return Ok(new PersonalNotificationMarkAllReadResultDto { MarkedCount = count });
    }

    /// <summary>Records menu view timestamps for recent inbox items (does not mark read).</summary>
    [HttpPost("record-menu-viewed")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RecordMenuViewed(
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserIdAsync(cancellationToken).ConfigureAwait(false);
        if (userId is null)
            return Forbid();

        await _inbox.RecordMenuViewedAsync(userId, take, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>Records page view timestamps for listed items (does not mark read).</summary>
    [HttpPost("record-page-viewed")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RecordPageViewed(
        [FromBody] IReadOnlyList<string> inboxItemIds,
        CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserIdAsync(cancellationToken).ConfigureAwait(false);
        if (userId is null)
            return Forbid();

        await _inbox.RecordPageViewedAsync(userId, inboxItemIds ?? [], cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>
    /// Incremental Toast delta feed for the signed-in operator's inbox: returns only newly scanned,
    /// Toast-eligible items since the given composite cursor (or the tip of the inbox when no cursor
    /// is given), along with the cursor to resume from on the next poll.
    /// </summary>
    [HttpGet("toast-delta")]
    [ProducesResponseType(typeof(NotificationToastDeltaResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<NotificationToastDeltaResponseDto>> ToastDelta(
        [FromQuery] DateTimeOffset? afterCreatedAtUtc = null,
        [FromQuery] string? afterId = null,
        CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserIdAsync(cancellationToken).ConfigureAwait(false);
        if (userId is null)
            return Forbid();

        var rows = await _inboxStore
            .ListToastDeltaAsync(userId, afterCreatedAtUtc, afterId, take: 50, cancellationToken)
            .ConfigureAwait(false);

        var unreadCount = await _inboxStore.CountUnreadAsync(userId, cancellationToken).ConfigureAwait(false);

        var last = rows.Count > 0 ? rows[^1] : null;

        return Ok(new NotificationToastDeltaResponseDto
        {
            Items = rows
                .Where(r => r.IsToastEligible)
                .Select(r => new NotificationToastDeltaItemDto
                {
                    Id = r.Id,
                    Subject = r.RenderedSubject,
                    Summary = r.RenderedSummary,
                    Severity = r.DefaultSeverity,
                    NotificationKind = r.NotificationKind,
                })
                .ToList(),
            // No rows scanned this poll: fall back to the incoming cursor so the client doesn't lose
            // its position (a null cursor here would look indistinguishable from "start over").
            NextAfterCreatedAtUtc = last?.CreatedAtUtc ?? afterCreatedAtUtc,
            NextAfterId = last?.Id ?? afterId,
            UnreadCount = unreadCount,
        });
    }

    private async Task<string?> ResolveUserIdAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            return null;

        var userId = await _principalToUserResolver.ResolveUserIdAsync(actor, cancellationToken).ConfigureAwait(false);
        return userId?.Value;
    }

    private static PersonalNotificationReadFilter ParseReadFilter(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "read" => PersonalNotificationReadFilter.Read,
            "unread" => PersonalNotificationReadFilter.Unread,
            _ => PersonalNotificationReadFilter.All,
        };

    private static PersonalNotificationInboxItemDto ToDto(NotificationUserInboxItem item, bool canOpenAdminInstanceDetail) =>
        new()
        {
            Id = item.Id,
            NotificationInstanceId = item.NotificationInstanceId,
            IsRead = item.IsRead,
            ReadAtUtc = item.ReadAtUtc,
            FirstViewedAtUtc = item.FirstViewedAtUtc,
            LastViewedAtUtc = item.LastViewedAtUtc,
            CreatedAtUtc = item.CreatedAtUtc,
            Subject = item.RenderedSubject,
            Summary = item.RenderedSummary,
            NotificationKind = item.NotificationKind,
            InstanceStatus = item.InstanceStatus,
            IntegrationId = item.IntegrationId,
            InstanceCreatedAtUtc = item.InstanceCreatedAtUtc,
            CanOpenAdminInstanceDetail = canOpenAdminInstanceDetail,
            RenderedActionUrl = item.RenderedActionUrl,
            RenderedActionText = item.RenderedActionText,
        };
}
