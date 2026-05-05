using Apperture.Core.Application.Services;
using Apperture.Management.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>Managed contacts (canonical person layer) and Entra import. Epic 76.</summary>
[ApiController]
[Route("api/managed-contacts")]
[Authorize]
public sealed class ManagedContactsController : ControllerBase
{
    private readonly IManagedContactManagementService _managedContacts;

    public ManagedContactsController(IManagedContactManagementService managedContacts) =>
        _managedContacts = managedContacts;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ManagedContactSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ManagedContactSummaryDto>>> List(
        [FromQuery] string? query,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = await _managedContacts.ListAsync(query, Math.Clamp(take, 1, 200), cancellationToken)
                .ConfigureAwait(false);
            return Ok(rows.Select(ToSummaryDto).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ManagedContactDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedContactDetailDto>> Get(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var row = await _managedContacts.GetDetailAsync(id, cancellationToken).ConfigureAwait(false);
            return row is null ? NotFound() : Ok(ToDetailDto(row));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost]
    [ProducesResponseType(typeof(ManagedContactDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ManagedContactDetailDto>> Create(
        [FromBody] CreateManagedContactRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var created = await _managedContacts.CreateCustomAsync(
                    new ManagedContactCreateCommand(body.DisplayName, body.EmailAddress, body.IsEnabled),
                    cancellationToken)
                .ConfigureAwait(false);
            return Ok(ToDetailDto(created));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ManagedContactDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedContactDetailDto>> Update(
        string id,
        [FromBody] UpdateManagedContactRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await _managedContacts.UpdateCustomAsync(
                    id,
                    new ManagedContactUpdateCommand(body.DisplayName, body.EmailAddress, body.IsEnabled),
                    cancellationToken)
                .ConfigureAwait(false);
            return updated is null ? NotFound() : Ok(ToDetailDto(updated));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id}/enabled")]
    [ProducesResponseType(typeof(ManagedContactDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedContactDetailDto>> SetEnabled(
        string id,
        [FromBody] SetManagedContactEnabledRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await _managedContacts.SetEnabledAsync(id, body.IsEnabled, cancellationToken).ConfigureAwait(false);
            return updated is null ? NotFound() : Ok(ToDetailDto(updated));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("import/entra-users")]
    [ProducesResponseType(typeof(ImportEntraUsersResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ImportEntraUsersResponseDto>> ImportEntraUsers(
        [FromBody] ImportEntraUsersRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _managedContacts.ImportEntraUsersAsync(
                    body.EntraRegistrationId,
                    body.EntraUserObjectIds,
                    cancellationToken)
                .ConfigureAwait(false);
            var dto = new ImportEntraUsersResponseDto(
                result.Items.Select(i => new ImportEntraUserItemResultDto(
                    i.EntraObjectId,
                    i.Success,
                    i.ManagedContactId,
                    i.OutcomeCode,
                    i.ErrorMessage)).ToList());
            return Ok(dto);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("import/entra-groups")]
    [ProducesResponseType(typeof(IReadOnlyList<ManagedContactGroupSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ManagedContactGroupSummaryDto>>> ImportEntraGroups(
        [FromBody] ImportEntraGroupsRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = await _managedContacts.ImportEntraGroupsAsync(
                    body.EntraRegistrationId,
                    body.EntraGroupObjectIds,
                    cancellationToken)
                .ConfigureAwait(false);
            return Ok(rows.Select(g => new ManagedContactGroupSummaryDto(g.Id, g.DisplayName, g.EntraGroupObjectId, g.UpdatedAtUtc)).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id}/link-user")]
    [ProducesResponseType(typeof(ManagedContactDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedContactDetailDto>> LinkUser(
        string id,
        [FromBody] LinkManagedContactUserRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await _managedContacts.LinkToUserAsync(id, body.UserId, cancellationToken).ConfigureAwait(false);
            return updated is null ? NotFound() : Ok(ToDetailDto(updated));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id}/unlink-user")]
    [ProducesResponseType(typeof(ManagedContactDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedContactDetailDto>> UnlinkUser(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await _managedContacts.UnlinkUserAsync(id, cancellationToken).ConfigureAwait(false);
            return updated is null ? NotFound() : Ok(ToDetailDto(updated));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    private static ManagedContactSummaryDto ToSummaryDto(ManagedContactSummary x) =>
        new(x.Id, x.DisplayName, x.EmailAddress, x.Source, x.IsEnabled, x.UserLinkStatus, x.NotificationUsabilityStatus, x.UpdatedAtUtc);

    private static ManagedContactDetailDto ToDetailDto(ManagedContactDetail x) =>
        new(
            x.Id,
            x.DisplayName,
            x.EmailAddress,
            x.Source,
            x.IsEnabled,
            x.LinkedUserId,
            x.UserLinkStatus,
            x.NotificationUsabilityStatus,
            x.IdentityLinks.Select(l => new ManagedContactIdentityLinkDto(
                l.Id,
                l.Provider,
                l.ExternalObjectId,
                l.ExternalPrincipalName,
                l.LastSyncedAtUtc,
                l.LinkStatus,
                l.UpdatedAtUtc)).ToList(),
            x.CreatedAtUtc,
            x.UpdatedAtUtc);
}
