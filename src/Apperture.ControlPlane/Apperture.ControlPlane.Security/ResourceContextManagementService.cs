using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Authorize-then-delegate wrapper around <see cref="IResourceContextService"/> (CTX-3A §9.1),
/// following the same pattern as <see cref="IntegrationManagementService.CreateIntegrationAsync"/>
/// (authorize via <see cref="IAuthorizationEvaluationService"/>, then delegate) combined with
/// <see cref="PlatformSettingsService"/>'s audit-append shape.
/// </summary>
public sealed class ResourceContextManagementService : IResourceContextManagementService
{
    private const string AuditOutcome = "resource-context-management";

    private readonly IResourceContextService _resourceContexts;
    private readonly IResourceContextRepository _resourceContextRepository;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly IAuditService _audit;
    private readonly AppertureDbContext _db;
    private readonly IResourceOwnershipMutationGate _ownershipGate;

    public ResourceContextManagementService(
        IResourceContextService resourceContexts,
        IResourceContextRepository resourceContextRepository,
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IAuditService audit,
        AppertureDbContext db,
        IResourceOwnershipMutationGate ownershipGate)
    {
        _resourceContexts = resourceContexts;
        _resourceContextRepository = resourceContextRepository;
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _audit = audit;
        _db = db;
        _ownershipGate = ownershipGate;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ResourceContext>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default) =>
        _resourceContexts.ListAsync(includeArchived, cancellationToken);

    /// <inheritdoc />
    public ValueTask<ResourceContext?> GetAsync(ResourceContextId id, CancellationToken cancellationToken = default) =>
        _resourceContexts.GetAsync(id, cancellationToken);

    /// <inheritdoc />
    public ValueTask<ResourceContextOwnershipSummary> GetOwnershipSummaryAsync(ResourceContextId id, CancellationToken cancellationToken = default) =>
        _resourceContextRepository.GetOwnershipSummaryAsync(id, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<string, ResourceContextOwnershipSummary>> GetOwnershipSummariesAsync(CancellationToken cancellationToken = default) =>
        _resourceContextRepository.GetOwnershipSummariesAsync(cancellationToken);

    /// <inheritdoc />
    public async ValueTask<ResourceContext> CreateAsync(string name, string? slug, string? description, CancellationToken cancellationToken = default)
    {
        var actor = await RequireManageAsync(cancellationToken).ConfigureAwait(false);
        var created = await _resourceContexts.CreateApplicationContextAsync(name, slug, description, cancellationToken).ConfigureAwait(false);
        await AppendAuditAsync("resource_context.created", actor.Id, created.Id, $"Created Application '{created.Name}' ({created.Slug}).", cancellationToken).ConfigureAwait(false);
        return created;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Each delegate call below (<see cref="IResourceContextService.RenameAsync"/>,
    /// <see cref="IResourceContextService.ChangeSlugAsync"/>,
    /// <see cref="IResourceContextService.UpdateDescriptionAsync"/>) commits independently -- so if, say,
    /// the rename succeeds and the slug change then throws (reserved/taken/unnormalizable), the rename is
    /// already durable by the time the exception propagates. The <c>finally</c> block below still appends
    /// an audit record for whatever actually changed before the exception escapes, so a partially-applied
    /// update is never silently unaudited.
    /// </para>
    /// <para>
    /// The pre-call guards above compare the caller's raw input to the current in-memory field, purely to
    /// decide whether it is worth calling the delegate at all -- they cannot be trusted to prove a change
    /// actually happened, because the delegates normalize/trim their input before comparing against the
    /// stored value (e.g. <see cref="IResourceContextService.ChangeSlugAsync"/> normalizes "My Slug" to
    /// "my-slug" and, finding that equal to the stored slug, returns the existing entity with NO save at
    /// all). So each branch below additionally captures the specific field's value immediately before the
    /// delegate call and compares it to that same field on the delegate's result, adding to
    /// <c>changedFields</c> only when the field actually differs afterward -- the same before/after
    /// pattern <see cref="ArchiveAsync"/> and <see cref="UnarchiveAsync"/> already use.
    /// </para>
    /// </remarks>
    public async ValueTask<ResourceContext> UpdateAsync(ResourceContextId id, string? name, string? slug, string? description, CancellationToken cancellationToken = default)
    {
        var actor = await RequireManageAsync(cancellationToken).ConfigureAwait(false);

        var current = await _resourceContexts.GetAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"ResourceContext '{id.Value}' not found.");

        var changedFields = new List<string>();
        try
        {
            if (name is not null && !string.Equals(name, current.Name, StringComparison.Ordinal))
            {
                var beforeName = current.Name;
                current = await _resourceContexts.RenameAsync(id, name, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(current.Name, beforeName, StringComparison.Ordinal))
                    changedFields.Add("name");
            }

            if (slug is not null && !string.Equals(slug, current.Slug, StringComparison.Ordinal))
            {
                var beforeSlug = current.Slug;
                current = await _resourceContexts.ChangeSlugAsync(id, slug, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(current.Slug, beforeSlug, StringComparison.Ordinal))
                    changedFields.Add("slug");
            }

            if (description is not null && !string.Equals(description, current.Description, StringComparison.Ordinal))
            {
                var beforeDescription = current.Description;
                current = await _resourceContexts.UpdateDescriptionAsync(id, description, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(current.Description, beforeDescription, StringComparison.Ordinal))
                    changedFields.Add("description");
            }

            return current;
        }
        finally
        {
            if (changedFields.Count > 0)
            {
                await AppendAuditAsync("resource_context.updated", actor.Id, id, $"Changed fields: {string.Join(", ", changedFields)}.", cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Test-only synchronization point, fired inside the archive transaction after the gate has been
    /// acquired and the ownership summary evaluated, but before commit. Lets a concurrency test hold this
    /// transaction open at a precisely known moment and observe what a concurrent blocker-creating operation
    /// does. Never set in production.
    /// </summary>
    internal Func<string, CancellationToken, Task>? MidTransactionHook { get; set; }

    private Task FireHookAsync(string checkpoint, CancellationToken cancellationToken) =>
        MidTransactionHook?.Invoke(checkpoint, cancellationToken) ?? Task.CompletedTask;

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="IResourceContextService.ArchiveAsync"/> is a no-op (no state change, no save) when the
    /// context is already archived, so the pre-call state is captured and compared to the delegate's
    /// result: an audit record is appended only when the context actually transitioned out of Active.
    /// </remarks>
    public async ValueTask<ResourceContext> ArchiveAsync(ResourceContextId id, CancellationToken cancellationToken = default)
    {
        var actor = await RequireManageAsync(cancellationToken).ConfigureAwait(false);

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();

            await using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
                : null;

            // The archive rule is a statement about ownership ("nothing live"), so evaluating it is an
            // ownership-sensitive read and archiving is an ownership-sensitive mutation. Without the gate,
            // a concurrent move into this context can add live ownership between the summary read and the
            // state change, producing an archived Application that owns a live Integration.
            await _ownershipGate.AcquireAsync(cancellationToken).ConfigureAwait(false);

            var before = await _resourceContexts.GetAsync(id, cancellationToken).ConfigureAwait(false);
            var archived = await _resourceContexts.ArchiveAsync(id, cancellationToken).ConfigureAwait(false);

            await FireHookAsync("archive:validated", cancellationToken).ConfigureAwait(false);

            if (before is not null && before.IsActive)
            {
                await AppendAuditAsync("resource_context.archived", actor.Id, id, $"Archived '{archived.Name}'.", cancellationToken).ConfigureAwait(false);
            }

            if (tx is not null)
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return archived;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="IResourceContextService.UnarchiveAsync"/> is a no-op (no state change, no save) when the
    /// context is already Active -- including the reserved Shared context, which is never archived -- so
    /// the pre-call state is captured and compared to the delegate's result: an audit record is appended
    /// only when the context actually transitioned out of Archived.
    /// </remarks>
    public async ValueTask<ResourceContext> UnarchiveAsync(ResourceContextId id, CancellationToken cancellationToken = default)
    {
        var actor = await RequireManageAsync(cancellationToken).ConfigureAwait(false);
        var before = await _resourceContexts.GetAsync(id, cancellationToken).ConfigureAwait(false);
        var unarchived = await _resourceContexts.UnarchiveAsync(id, cancellationToken).ConfigureAwait(false);
        if (before is not null && !before.IsActive)
        {
            await AppendAuditAsync("resource_context.unarchived", actor.Id, id, $"Unarchived '{unarchived.Name}'.", cancellationToken).ConfigureAwait(false);
        }
        return unarchived;
    }

    private async ValueTask<IActor> RequireManageAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(
                actor,
                new PermissionDescriptor(ActionConstants.ResourceContextManage, new ResourceDescriptor(ResourceTypeConstants.ResourceContext, ScopeKeyConstants.Instance)),
                null),
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to manage applications.");

        return actor;
    }

    private async ValueTask AppendAuditAsync(string action, ActorId actorId, ResourceContextId resourceContextId, string details, CancellationToken cancellationToken)
    {
        var record = new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            actorId,
            action,
            new ResourceDescriptor(ResourceTypeConstants.ResourceContext, ScopeKeyConstants.Instance),
            AuditOutcome,
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            details,
            ResourceContextId: resourceContextId.Value);
        await _audit.AppendAsync(record, cancellationToken).ConfigureAwait(false);
    }
}
