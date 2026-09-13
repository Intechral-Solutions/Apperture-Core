using Apperture.ControlPlane.Security;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Read-only platform event query (Epic 77 Phase 1 verification). Requires integration.read when filtering by integration.
/// </summary>
[ApiController]
[Route("api/platform/events")]
[Authorize]
public sealed class PlatformEventsController : ControllerBase
{
    private readonly IPlatformEventStore _store;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;

    public PlatformEventsController(
        IPlatformEventStore store,
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation)
    {
        _store = store;
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PlatformEventQueryPage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PlatformEventQueryPage>> List(
        [FromQuery] string? integrationId,
        [FromQuery] string? eventType,
        [FromQuery] DateTimeOffset? fromOccurredAtUtc,
        [FromQuery] DateTimeOffset? toOccurredAtUtc,
        [FromQuery] string? runId,
        [FromQuery] string? moduleId,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default,
        [FromQuery] string? resourceContextId = null)
    {
        if (!await CanReadAsync(integrationId, cancellationToken).ConfigureAwait(false))
            return Forbid();

        var page = await _store
            .QueryAsync(
                new PlatformEventQuery(
                    integrationId,
                    eventType,
                    Severity: null,
                    fromOccurredAtUtc,
                    toOccurredAtUtc,
                    runId,
                    moduleId,
                    take,
                    resourceContextId),
                cancellationToken)
            .ConfigureAwait(false);
        return Ok(page);
    }

    private async Task<bool> CanReadAsync(string? integrationId, CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            return false;

        return await _authorizationEvaluation
            .EvaluateAsync(
                new AuthorizationEvaluationContext(
                    actor,
                    new PermissionDescriptor(
                        ActionConstants.IntegrationRead,
                        new ResourceDescriptor(ResourceTypeConstants.Integration, ScopeKeyConstants.Instance)),
                    null),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
