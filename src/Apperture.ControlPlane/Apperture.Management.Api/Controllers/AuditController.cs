using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Audit timeline and discoverability. Epic 32. Epic 34: requires audit.read.
/// </summary>
[ApiController]
[Route("api/control/audit")]
[Authorize]
public sealed class AuditController : ControllerBase
{
    private readonly IAuditTimelineQueryService _auditTimeline;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly IAuditService? _audit;

    public AuditController(
        IAuditTimelineQueryService auditTimeline,
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IAuditService? audit = null)
    {
        _auditTimeline = auditTimeline;
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _audit = audit;
    }

    /// <summary>
    /// Lists audit timeline entries with optional filters.
    /// Epic 37: All filters are DB-backed (fromUtc, toUtc, actor, actionPrefix, outcome, moduleId, scheduleId, runId).
    /// Defaults: fromUtc = 24h ago if omitted.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AuditTimelinePage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuditTimelinePage>> List(
        [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null,
        [FromQuery] string? actor = null,
        [FromQuery] string? actionPrefix = null,
        [FromQuery] string? moduleId = null,
        [FromQuery] string? scheduleId = null,
        [FromQuery] string? runId = null,
        [FromQuery] string? outcome = null,
        [FromQuery] string? resourceContextId = null,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        if (!await CanReadAuditAsync(cancellationToken).ConfigureAwait(false))
        {
            await TryAuditReadDeniedAsync(cancellationToken).ConfigureAwait(false);
            return Forbid();
        }
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
            ContinuationToken: continuationToken,
            ResourceContextId: resourceContextId);

        var page = await _auditTimeline.QueryAsync(criteria, cancellationToken).ConfigureAwait(false);
        return Ok(page);
    }

    /// <summary>
    /// Gets a single audit entry by id.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(AuditTimelineEntry), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AuditTimelineEntry>> GetById(
        string id,
        CancellationToken cancellationToken = default)
    {
        if (!await CanReadAuditAsync(cancellationToken).ConfigureAwait(false))
        {
            await TryAuditReadDeniedAsync(cancellationToken).ConfigureAwait(false);
            return Forbid();
        }
        var entry = await _auditTimeline.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return NotFound();
        return Ok(entry);
    }

    private async Task<bool> CanReadAuditAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            return false;
        return await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.AuditRead, new ResourceDescriptor(ResourceTypeConstants.AuditPolicy, ScopeKeyConstants.Instance)),
                null),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask TryAuditReadDeniedAsync(CancellationToken ct)
    {
        if (_audit is null) return;
        try
        {
            var actor = await _principalContext.GetCurrentActorAsync(ct).ConfigureAwait(false);
            await _audit.AppendAsync(
                new AuditRecord(
                    new AuditRecordId(Guid.NewGuid().ToString("N")),
                    actor?.Id ?? new ActorId("system"),
                    GovernanceAuditActionNames.AuditReadDenied,
                    Resource: null,
                    Outcome: "denied",
                    Timestamp: DateTimeOffset.UtcNow,
                    new CorrelationId(null),
                    "reason=permission_missing"),
                ct).ConfigureAwait(false);
        }
        catch { /* best-effort */ }
    }
}
