using Apperture.Core.Application.Services;
using Apperture.Management.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/attention-contact-groups")]
[Authorize]
public sealed class AttentionContactGroupsController : ControllerBase
{
    private readonly IAttentionContactGroupManagementService _groups;

    public AttentionContactGroupsController(IAttentionContactGroupManagementService groups) => _groups = groups;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AttentionContactGroupSummaryDto>>> List(
        [FromQuery] bool includeDisabled = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = await _groups.ListAsync(includeDisabled, cancellationToken).ConfigureAwait(false);
            return Ok(rows.Select(ToSummaryDto).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AttentionContactGroupDetailDto>> Get(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var g = await _groups.GetAsync(id, cancellationToken).ConfigureAwait(false);
            return g is null ? NotFound() : Ok(ToDetailDto(g));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost]
    public async Task<ActionResult<AttentionContactGroupDetailDto>> Create(
        [FromBody] CreateAttentionContactGroupRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var created = await _groups.CreateAsync(
                    new AttentionContactGroupCreateCommand(body.Name, body.Description, body.IsEnabled),
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
    public async Task<ActionResult<AttentionContactGroupDetailDto>> Update(
        string id,
        [FromBody] UpdateAttentionContactGroupRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await _groups.UpdateAsync(
                    id,
                    new AttentionContactGroupUpdateCommand(body.Name, body.Description, body.IsEnabled),
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

    [HttpPut("{id}/members")]
    public async Task<ActionResult<AttentionContactGroupDetailDto>> ReplaceMembers(
        string id,
        [FromBody] ReplaceAttentionContactGroupMembersRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var items = body.Members
                .Select(m => new AttentionContactGroupMemberUpsertItem(m.Id, m.EmailAddress, m.DisplayName, m.IsEnabled))
                .ToList();
            var updated = await _groups.ReplaceMembersAsync(id, items, cancellationToken).ConfigureAwait(false);
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

    private static AttentionContactGroupSummaryDto ToSummaryDto(AttentionContactGroupListItem x) =>
        new(x.Id, x.Name, x.Description, x.IsEnabled, x.CreatedAtUtc, x.UpdatedAtUtc);

    private static AttentionContactGroupDetailDto ToDetailDto(AttentionContactGroupDetail x) =>
        new(
            x.Id,
            x.Name,
            x.Description,
            x.IsEnabled,
            x.CreatedAtUtc,
            x.UpdatedAtUtc,
            x.Members.Select(m => new AttentionContactGroupMemberDto(
                m.Id,
                m.EmailAddress,
                m.DisplayName,
                m.IsEnabled,
                m.CreatedAtUtc,
                m.UpdatedAtUtc)).ToList());
}
