using Apperture.ControlPlane.Security.Audit;
using Apperture.Core.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Audit timeline and discoverability. Epic 32.
/// </summary>
[ApiController]
[Route("api/control/audit")]
[Authorize]
public sealed class AuditController : ControllerBase
{
    private readonly IAuditTimelineQueryService _auditTimeline;

    public AuditController(IAuditTimelineQueryService auditTimeline) => _auditTimeline = auditTimeline;

    /// <summary>
    /// Lists audit timeline entries with optional filters.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AuditTimelinePage), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuditTimelinePage>> List(
        [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null,
        [FromQuery] string? actor = null,
        [FromQuery] string? actionPrefix = null,
        [FromQuery] string? moduleId = null,
        [FromQuery] string? scheduleId = null,
        [FromQuery] string? runId = null,
        [FromQuery] string? outcome = null,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        var criteria = new AuditFilterCriteria(
            FromUtc: fromUtc ?? DateTimeOffset.UtcNow.AddHours(-24),
            ToUtc: toUtc,
            Actor: actor,
            ActionPrefix: actionPrefix,
            ModuleId: moduleId,
            ScheduleId: scheduleId,
            RunId: runId,
            Outcome: outcome,
            PageSize: pageSize,
            ContinuationToken: continuationToken);

        var page = await _auditTimeline.QueryAsync(criteria, cancellationToken).ConfigureAwait(false);
        return Ok(page);
    }

    /// <summary>
    /// Gets a single audit entry by id.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(AuditTimelineEntry), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AuditTimelineEntry>> GetById(
        string id,
        CancellationToken cancellationToken = default)
    {
        var entry = await _auditTimeline.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return NotFound();
        return Ok(entry);
    }
}
