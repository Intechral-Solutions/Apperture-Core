using Apperture.Core.Application.Audit;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Authorize-then-delegate wrapper around <see cref="IResourceContextService"/> (CTX-3A §9.1), following the same pattern as
/// <see cref="IntegrationManagementService.CreateIntegrationAsync"/> (authorize via <see cref="IAuthorizationEvaluationService"/>,
/// then delegate). Every mutation is one audited unit (AUD-1.4 slice 5): create, update, archive and unarchive each commit with
/// their one audit row or not at all.
/// </summary>
public sealed class ResourceContextManagementService : IResourceContextManagementService
{
    private readonly IResourceContextService _resourceContexts;
    private readonly IResourceContextRepository _resourceContextRepository;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly IResourceOwnershipMutationGate _ownershipGate;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;

    public ResourceContextManagementService(
        IResourceContextService resourceContexts,
        IResourceContextRepository resourceContextRepository,
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IResourceOwnershipMutationGate ownershipGate,
        IAuditedUnitOfWork? auditedUnitOfWork = null)
    {
        _resourceContexts = resourceContexts;
        _resourceContextRepository = resourceContextRepository;
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _ownershipGate = ownershipGate;
        _auditedUnitOfWork = auditedUnitOfWork;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new AuditInvariantException("Application changes require the audited unit of work.");

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
    /// <remarks>
    /// <para><b>One audited unit</b> (AUD-1.4 slice 5): the context and <c>resource_context.created</c> commit together or not
    /// at all. The service chooses the identity, so a verified commit after a lost acknowledgement is reconstructed from the
    /// primary row's target, and a confirmed rollback reruns with a fresh identity (nothing of the first was committed). The slug
    /// uniqueness read-then-insert can still lose a race on <c>UX_ResourceContexts_Slug</c>; that is now the same refusal as a
    /// slug already in use (it used to be an unmapped 500).</para>
    /// </remarks>
    public async ValueTask<ResourceContext> CreateAsync(string name, string? slug, string? description, CancellationToken cancellationToken = default)
    {
        await RequireManageAsync(cancellationToken).ConfigureAwait(false);
        var unit = Unit;

        return await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.ResourceContextCreated, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                ResourceContext created;
                try
                {
                    created = await _resourceContexts.CreateApplicationContextAsync(name, slug, description, ct).ConfigureAwait(false);
                }
                catch (DbUpdateException ex) when (SqlUniqueKeyViolation.IsOnIndex(ex, "UX_ResourceContexts_Slug"))
                {
                    // A concurrent request committed the same slug between the availability check and the insert.
                    throw new InvalidOperationException("ResourceContext slug is already in use.", ex);
                }

                await transaction.StageAsync(AuditEvents.ResourceContextCreated(created.Id.Value, created.Name, created.Slug), ct).ConfigureAwait(false);
                return created;
            },
            async (committed, ct) => await _resourceContexts.GetAsync(new ResourceContextId(committed.TargetId!), ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The created ResourceContext could not be read back."),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>One audited unit</b> (AUD-1.4 slice 5). The context row is locked to the end of the transaction (no ownership
    /// gate: a rename, a re-slug or a new description neither makes anything live nor moves anything), so two updates of one
    /// context serialize and the second merges over the first. The three changes the request may carry are applied inside the
    /// unit, so they now commit together or not at all: an update whose slug change is refused (reserved, taken, unusable) no
    /// longer leaves its rename committed. This is the one deliberate behaviour change of the conversion (AUD-1.4 record §16.4).</para>
    /// <para>The delegates normalize and trim their input before comparing it to the stored value (a re-slug of "My Slug" to the
    /// stored "my-slug" saves nothing), so each field counts as changed only when it differs before and after the delegate ran.
    /// A request that changes nothing declares a no-op and records nothing.</para>
    /// </remarks>
    public async ValueTask<ResourceContext> UpdateAsync(ResourceContextId id, string? name, string? slug, string? description, CancellationToken cancellationToken = default)
    {
        await RequireManageAsync(cancellationToken).ConfigureAwait(false);
        var unit = Unit;

        return await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.ResourceContextUpdated, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                var current = await _resourceContextRepository.GetByIdForUpdateAsync(id, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"ResourceContext '{id.Value}' not found.");

                var changedFields = new List<string>();
                try
                {
                    if (name is not null && !string.Equals(name, current.Name, StringComparison.Ordinal))
                    {
                        var beforeName = current.Name;
                        current = await _resourceContexts.RenameAsync(id, name, ct).ConfigureAwait(false);
                        if (!string.Equals(current.Name, beforeName, StringComparison.Ordinal))
                            changedFields.Add("name");
                    }

                    if (slug is not null && !string.Equals(slug, current.Slug, StringComparison.Ordinal))
                    {
                        var beforeSlug = current.Slug;
                        current = await _resourceContexts.ChangeSlugAsync(id, slug, ct).ConfigureAwait(false);
                        if (!string.Equals(current.Slug, beforeSlug, StringComparison.Ordinal))
                            changedFields.Add("slug");
                    }

                    if (description is not null && !string.Equals(description, current.Description, StringComparison.Ordinal))
                    {
                        var beforeDescription = current.Description;
                        current = await _resourceContexts.UpdateDescriptionAsync(id, description, ct).ConfigureAwait(false);
                        if (!string.Equals(current.Description, beforeDescription, StringComparison.Ordinal))
                            changedFields.Add("description");
                    }
                }
                catch (DbUpdateException ex) when (SqlUniqueKeyViolation.IsOnIndex(ex, "UX_ResourceContexts_Slug"))
                {
                    throw new InvalidOperationException("ResourceContext slug is already in use.", ex);
                }

                if (changedFields.Count == 0)
                {
                    transaction.DeclareNoOp();
                    return current;
                }

                await transaction.StageAsync(AuditEvents.ResourceContextUpdated(id.Value, current.Name, changedFields), ct).ConfigureAwait(false);
                return current;
            },
            async (committed, ct) => await _resourceContexts.GetAsync(id, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"ResourceContext '{id.Value}' not found."),
            cancellationToken).ConfigureAwait(false);
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
    /// <para><b>One audited unit</b> (AUD-1.4 slice 5). Inside the attempt, in this order: the ownership gate, the context row
    /// (locked to the end of the transaction), then the "nothing live" evaluation and the state change. The archive rule is a
    /// statement about ownership, so evaluating it is an ownership-sensitive read and archiving an ownership-sensitive mutation:
    /// without the gate a concurrent move into this context, or a new integration, connection or enabled schedule, could add live
    /// ownership between the summary read and the state change, producing an archived Application that owns a live resource.
    /// Archiving an archived context changes nothing and records nothing.</para>
    /// </remarks>
    public async ValueTask<ResourceContext> ArchiveAsync(ResourceContextId id, CancellationToken cancellationToken = default)
    {
        await RequireManageAsync(cancellationToken).ConfigureAwait(false);
        var unit = Unit;

        return await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.ResourceContextArchived, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                await _ownershipGate.AcquireAsync(ct).ConfigureAwait(false);

                var before = await _resourceContextRepository.GetByIdForUpdateAsync(id, ct).ConfigureAwait(false);
                var archived = await _resourceContexts.ArchiveAsync(id, ct).ConfigureAwait(false);

                await FireHookAsync("archive:validated", ct).ConfigureAwait(false);

                if (before is null || !before.IsActive)
                {
                    transaction.DeclareNoOp();
                    return archived;
                }

                await transaction.StageAsync(AuditEvents.ResourceContextArchived(id.Value, archived.Name), ct).ConfigureAwait(false);
                return archived;
            },
            async (committed, ct) => await _resourceContexts.GetAsync(id, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"ResourceContext '{id.Value}' not found."),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>One audited unit</b> (AUD-1.4 slice 5). The context row is locked to the end of the transaction; no ownership
    /// gate (it was never taken, and an unarchive creates no ownership: a creation into the context still takes the gate and
    /// reads the state it finds). Unarchiving an active context, including the reserved Shared context which is never archived,
    /// changes nothing and records nothing.</para>
    /// </remarks>
    public async ValueTask<ResourceContext> UnarchiveAsync(ResourceContextId id, CancellationToken cancellationToken = default)
    {
        await RequireManageAsync(cancellationToken).ConfigureAwait(false);
        var unit = Unit;

        return await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.ResourceContextUnarchived, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                var before = await _resourceContextRepository.GetByIdForUpdateAsync(id, ct).ConfigureAwait(false);
                var unarchived = await _resourceContexts.UnarchiveAsync(id, ct).ConfigureAwait(false);

                if (before is null || before.IsActive)
                {
                    transaction.DeclareNoOp();
                    return unarchived;
                }

                await transaction.StageAsync(AuditEvents.ResourceContextUnarchived(id.Value, unarchived.Name), ct).ConfigureAwait(false);
                return unarchived;
            },
            async (committed, ct) => await _resourceContexts.GetAsync(id, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"ResourceContext '{id.Value}' not found."),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask RequireManageAsync(CancellationToken cancellationToken)
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
    }
}
