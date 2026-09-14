using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// ResourceContext (Application) CRUD and lifecycle API. CTX-3A §9.1 (A4).
/// </summary>
/// <remarks>
/// Every mutating action delegates authorization and audit entirely to
/// <see cref="IResourceContextManagementService"/> (CTX-3A Task 5) -- this controller performs zero
/// permission checks of its own beyond the class-level <see cref="AuthorizeAttribute"/>. The one
/// exception is <see cref="Summary"/>, which additionally probes the caller's own read permissions
/// (<c>integration.read</c>, <c>connection.read</c>, <c>schedule.manage</c>) purely to decide which
/// counts to mask in its response (§9.2) -- that probe grants no access and performs no mutation.
/// </remarks>
[ApiController]
[Route("api/resource-contexts")]
[Authorize]
public sealed class ResourceContextsController(
    IResourceContextManagementService management,
    IPrincipalContext principalContext,
    IAuthorizationEvaluationService authorizationEvaluation) : ControllerBase
{
    public sealed record ResourceContextDto(
        string Id, string Name, string Slug, string Kind, string State,
        string? Description, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

    public sealed record CreateResourceContextRequest(string Name, string? Slug, string? Description);

    public sealed record UpdateResourceContextRequest(string? Name, string? Slug, string? Description);

    public sealed record ResourceContextOwnershipSummaryDto(
        int? LiveIntegrations, int? RetiredIntegrations,
        int? ActiveConnections, int? RetiredConnections,
        int? EnabledSchedules, int? DisabledSchedules);

    /// <summary>Lists ResourceContexts. No permission required beyond authentication (§9.2).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ResourceContextDto>>> List([FromQuery] bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var contexts = await management.ListAsync(includeArchived, cancellationToken).ConfigureAwait(false);
        return Ok(contexts.Select(ToDto).ToList());
    }

    /// <summary>Gets a ResourceContext by id. No permission required beyond authentication (§9.2).</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ResourceContextDto>> Get(string id, CancellationToken cancellationToken = default)
    {
        var context = await management.GetAsync(new ResourceContextId(id), cancellationToken).ConfigureAwait(false);
        return context is null ? NotFound() : Ok(ToDto(context));
    }

    /// <summary>Creates a new Application ResourceContext. Requires <c>resource_context.manage</c>.</summary>
    [HttpPost]
    public async Task<ActionResult<ResourceContextDto>> Create([FromBody] CreateResourceContextRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var created = await management.CreateAsync(request.Name, request.Slug, request.Description, cancellationToken).ConfigureAwait(false);
            return CreatedAtAction(nameof(Get), new { id = created.Id.Value }, ToDto(created));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>Updates name, slug and/or description. Requires <c>resource_context.manage</c>.</summary>
    [HttpPatch("{id}")]
    public async Task<ActionResult<ResourceContextDto>> Update(string id, [FromBody] UpdateResourceContextRequest request, CancellationToken cancellationToken = default)
    {
        var resourceContextId = new ResourceContextId(id);
        if (await management.GetAsync(resourceContextId, cancellationToken).ConfigureAwait(false) is null)
            return NotFound();

        try
        {
            var updated = await management.UpdateAsync(resourceContextId, request.Name, request.Slug, request.Description, cancellationToken).ConfigureAwait(false);
            return Ok(ToDto(updated));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("reserved", StringComparison.OrdinalIgnoreCase)) { return Conflict(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>
    /// Archives a ResourceContext. Requires <c>resource_context.manage</c>. Rejected (409) for the
    /// reserved Shared context, and (409, with the ownership summary as the body) while the context
    /// still owns a live resource (§6.5).
    /// </summary>
    [HttpPost("{id}/archive")]
    public async Task<ActionResult<ResourceContextDto>> Archive(string id, CancellationToken cancellationToken = default)
    {
        var resourceContextId = new ResourceContextId(id);
        if (await management.GetAsync(resourceContextId, cancellationToken).ConfigureAwait(false) is null)
            return NotFound();

        try
        {
            var archived = await management.ArchiveAsync(resourceContextId, cancellationToken).ConfigureAwait(false);
            return Ok(ToDto(archived));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("reserved", StringComparison.OrdinalIgnoreCase)) { return Conflict(new { error = ex.Message }); }
        catch (InvalidOperationException)
        {
            var summary = await management.GetOwnershipSummaryAsync(resourceContextId, cancellationToken).ConfigureAwait(false);
            return Conflict(new ResourceContextOwnershipSummaryDto(
                summary.LiveIntegrations, summary.RetiredIntegrations,
                summary.ActiveConnections, summary.RetiredConnections,
                summary.EnabledSchedules, summary.DisabledSchedules));
        }
    }

    /// <summary>Returns an archived ResourceContext to Active. Requires <c>resource_context.manage</c>.</summary>
    [HttpPost("{id}/unarchive")]
    public async Task<ActionResult<ResourceContextDto>> Unarchive(string id, CancellationToken cancellationToken = default)
    {
        var resourceContextId = new ResourceContextId(id);
        if (await management.GetAsync(resourceContextId, cancellationToken).ConfigureAwait(false) is null)
            return NotFound();

        try
        {
            var unarchived = await management.UnarchiveAsync(resourceContextId, cancellationToken).ConfigureAwait(false);
            return Ok(ToDto(unarchived));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>
    /// Per-context ownership counts, masked per caller: a family's counts are null unless the caller
    /// holds that family's own read permission (§9.2). No <c>resource_context.manage</c> requirement --
    /// masking is the only gate.
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<IReadOnlyDictionary<string, ResourceContextOwnershipSummaryDto>>> Summary(CancellationToken cancellationToken = default)
    {
        var summaries = await management.GetOwnershipSummariesAsync(cancellationToken).ConfigureAwait(false);
        var canReadIntegrations = await HasPermissionAsync(ActionConstants.IntegrationRead, ResourceTypeConstants.Integration, cancellationToken).ConfigureAwait(false);
        var canReadConnections = await HasPermissionAsync(ActionConstants.ConnectionRead, ResourceTypeConstants.Connection, cancellationToken).ConfigureAwait(false);
        var canReadSchedules = await HasPermissionAsync(ActionConstants.ScheduleManage, ResourceTypeConstants.Schedule, cancellationToken).ConfigureAwait(false);

        var masked = summaries.ToDictionary(
            kv => kv.Key,
            kv => new ResourceContextOwnershipSummaryDto(
                canReadIntegrations ? kv.Value.LiveIntegrations : null,
                canReadIntegrations ? kv.Value.RetiredIntegrations : null,
                canReadConnections ? kv.Value.ActiveConnections : null,
                canReadConnections ? kv.Value.RetiredConnections : null,
                canReadSchedules ? kv.Value.EnabledSchedules : null,
                canReadSchedules ? kv.Value.DisabledSchedules : null));

        return Ok(masked);
    }

    /// <summary>
    /// Read-only, per-caller permission probe. Grants nothing and performs no mutation -- used solely to
    /// decide which ownership-count families <see cref="Summary"/> may expose to this caller.
    /// </summary>
    private async Task<bool> HasPermissionAsync(string action, string resourceType, CancellationToken cancellationToken)
    {
        var actor = await principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            return false;

        return await authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(action, new ResourceDescriptor(resourceType, ScopeKeyConstants.Instance)),
                null),
            cancellationToken).ConfigureAwait(false);
    }

    private static ResourceContextDto ToDto(ResourceContext context) => new(
        context.Id.Value, context.Name, context.Slug, context.Kind, context.State,
        context.Description, context.CreatedAtUtc, context.UpdatedAtUtc);
}
