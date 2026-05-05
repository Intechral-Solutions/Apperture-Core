using Apperture.Core.Application.Services;
using Apperture.Management.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/operational-teams")]
[Authorize]
public sealed class OperationalTeamsController : ControllerBase
{
    private readonly IOperationalTeamManagementService _teams;

    public OperationalTeamsController(IOperationalTeamManagementService teams) => _teams = teams;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OperationalTeamSummaryDto>>> List(
        [FromQuery] bool includeDisabled = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = await _teams.ListAsync(includeDisabled, cancellationToken).ConfigureAwait(false);
            return Ok(rows.Select(static x => new OperationalTeamSummaryDto(
                x.Id, x.Name, x.Description, x.IsEnabled, x.CreatedAtUtc, x.UpdatedAtUtc)).ToList());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<OperationalTeamDetailDto>> Get(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var team = await _teams.GetAsync(id, cancellationToken).ConfigureAwait(false);
            return team is null ? NotFound() : Ok(ToDto(team));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost]
    public async Task<ActionResult<OperationalTeamDetailDto>> Create(
        [FromBody] CreateOperationalTeamRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var created = await _teams.CreateAsync(
                    new OperationalTeamCreateCommand(body.Name, body.Description, body.IsEnabled),
                    cancellationToken)
                .ConfigureAwait(false);
            return Ok(ToDto(created));
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
    public async Task<ActionResult<OperationalTeamDetailDto>> Update(
        string id,
        [FromBody] UpdateOperationalTeamRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var updated = await _teams.UpdateAsync(id, new OperationalTeamUpdateCommand(body.Name, body.Description, body.IsEnabled), cancellationToken)
                .ConfigureAwait(false);
            return updated is null ? NotFound() : Ok(ToDto(updated));
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
    public async Task<ActionResult<OperationalTeamDetailDto>> ReplaceMembers(
        string id,
        [FromBody] ReplaceOperationalTeamMembersRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var items = body.Members
                .Select(m => new OperationalTeamMemberUpsertItem(
                    m.Id,
                    m.MemberKind,
                    m.PrincipalUserId,
                    m.EmailAddress,
                    m.DisplayName,
                    m.IsEnabled,
                    m.ManagedContactId))
                .ToList();
            var updated = await _teams.ReplaceMembersAsync(id, items, cancellationToken).ConfigureAwait(false);
            return updated is null ? NotFound() : Ok(ToDto(updated));
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

    private static OperationalTeamDetailDto ToDto(OperationalTeamDetail x) =>
        new(
            x.Id,
            x.Name,
            x.Description,
            x.IsEnabled,
            x.CreatedAtUtc,
            x.UpdatedAtUtc,
            x.Members.Select(m => new OperationalTeamMemberDto(
                m.Id,
                m.MemberKind,
                m.PrincipalUserId,
                m.EmailAddress,
                m.ManagedContactId,
                m.DisplayName,
                m.IsEnabled,
                m.CreatedAtUtc,
                m.UpdatedAtUtc)).ToList());
}
