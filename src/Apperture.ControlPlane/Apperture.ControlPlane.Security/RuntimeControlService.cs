using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Runtime;
using Apperture.Core.Security.Trust;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

public sealed class RuntimeControlService : IRuntimeControlService
{
    private readonly AppertureDbContext _db;
    private readonly IRuntimeIdentityService _runtimeIdentity;
    private readonly IPrincipalContext _principalContext;
    private readonly IEnvironmentContext _environment;
    private readonly IAuditService _audit;

    public RuntimeControlService(
        AppertureDbContext db,
        IRuntimeIdentityService runtimeIdentity,
        IPrincipalContext principalContext,
        IEnvironmentContext environment,
        IAuditService audit)
    {
        _db = db;
        _runtimeIdentity = runtimeIdentity;
        _principalContext = principalContext;
        _environment = environment;
        _audit = audit;
    }

    public async ValueTask<RuntimeControlRequestResult?> RequestShutdownAsync(
        string runtimeId,
        CancellationToken cancellationToken = default)
    {
        var runtime = await _runtimeIdentity.GetByIdAsync(runtimeId, cancellationToken).ConfigureAwait(false);
        if (runtime is null)
            return null;

        // Capability prerequisite: runtime must declare the shutdown control capability.
        if (!runtime.DeclaredScopes.Contains(RuntimeCapabilityCatalog.ControlShutdown, StringComparer.Ordinal))
            return null;

        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            throw new InvalidOperationException("Actor context is required for runtime control requests.");

        // Idempotency: if a queued request already exists, return it without inserting again.
        var existing = await _db.RuntimeControlRequests.AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.RuntimeId == runtimeId &&
                x.ControlKind == RuntimeControlKindNames.Shutdown &&
                x.Status == RuntimeControlStatusNames.Queued,
                cancellationToken).ConfigureAwait(false);

        if (existing is not null)
            return Map(existing);

        var requestedAt = DateTimeOffset.UtcNow;
        var requestedBy = actor.Id.Value;

        _db.RuntimeControlRequests.Add(new RuntimeControlRequestEntity
        {
            RuntimeId = runtimeId,
            ControlKind = RuntimeControlKindNames.Shutdown,
            Status = RuntimeControlStatusNames.Queued,
            RequestedAt = requestedAt,
            RequestedBy = requestedBy,
            AppliedAt = null,
            AppliedBy = null,
            LastError = null
        });

        // Persist first; if insert succeeds we also emit audit.
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsUniqueQueuedControlViolation(ex))
        {
            _db.ChangeTracker.Clear();
        }

        // Re-read queued row (either this call inserted it, or another call won).
        var queued = await _db.RuntimeControlRequests.AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.RuntimeId == runtimeId &&
                x.ControlKind == RuntimeControlKindNames.Shutdown &&
                x.Status == RuntimeControlStatusNames.Queued,
                cancellationToken).ConfigureAwait(false);

        if (queued is null)
            return null;

        var runtimeScopeKey = ScopeKeyConstants.RuntimeInstance(_environment.EnvironmentId, runtimeId);
        var runtimeResource = new ResourceDescriptor(ResourceTypeConstants.RuntimeInstance, runtimeScopeKey);

        var record = new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            actor.Id,
            GovernanceAuditActionNames.RuntimeShutdownRequestCreated,
            runtimeResource,
            "auth-admin",
            requestedAt,
            new CorrelationId(null),
            $"runtimeId={runtimeId} controlKind={RuntimeControlKindNames.Shutdown}");

        await _audit.AppendAsync(record, cancellationToken).ConfigureAwait(false);

        return Map(queued);
    }

    public async ValueTask<IReadOnlyList<RuntimeControlRequestResult>> GetShutdownRequestsAsync(
        string runtimeId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.RuntimeControlRequests.AsNoTracking()
            .Where(x =>
                x.RuntimeId == runtimeId &&
                x.ControlKind == RuntimeControlKindNames.Shutdown)
            .OrderByDescending(x => x.RequestedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows.Select(Map).ToList();
    }

    private static RuntimeControlRequestResult Map(RuntimeControlRequestEntity row) =>
        new(
            row.Id,
            row.RuntimeId,
            row.ControlKind,
            row.Status,
            row.RequestedAt,
            row.RequestedBy,
            row.AppliedAt,
            row.AppliedBy,
            row.LastError);

    private static bool IsUniqueQueuedControlViolation(DbUpdateException ex)
    {
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is SqlException sql && sql.Number is 2601 or 2627)
                return true;
        }

        return false;
    }
}

