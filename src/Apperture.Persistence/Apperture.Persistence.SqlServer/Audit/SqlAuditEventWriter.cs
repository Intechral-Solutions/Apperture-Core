using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Apperture.Persistence.SqlServer.Audit;

/// <summary>
/// Single-row recorder writes (AUD-1 design gate §13.1, §13.4, §13.5) on a context of their own, created from the
/// request's options, so they never flush a caller's pending changes. Each row commits in its own short transaction and
/// keeps its event id across retries: a duplicate on that id after a lost acknowledgement means the row exists. These
/// writes take no operation lock, because no business change depends on them.
/// </summary>
public sealed class SqlAuditEventWriter : IAuditEventWriter
{
    private readonly DbContextOptions<AppertureDbContext> _contextOptions;
    private readonly IPrincipalContext? _principals;
    private readonly AuditActionCatalog _catalog;
    private readonly TimeProvider _time;
    private readonly ILogger<SqlAuditEventWriter> _logger;
    private readonly IAuditEventIdSource _eventIds;

    public SqlAuditEventWriter(
        DbContextOptions<AppertureDbContext> contextOptions,
        TimeProvider? time = null,
        ILogger<SqlAuditEventWriter>? logger = null,
        IPrincipalContext? principals = null)
        : this(contextOptions, principals, AuditActionCatalog.Default, time, logger, null)
    {
    }

    internal SqlAuditEventWriter(
        DbContextOptions<AppertureDbContext> contextOptions,
        IPrincipalContext? principals,
        AuditActionCatalog catalog,
        TimeProvider? time,
        ILogger<SqlAuditEventWriter>? logger,
        IAuditEventIdSource? eventIds)
    {
        _contextOptions = contextOptions;
        _principals = principals;
        _catalog = catalog;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger<SqlAuditEventWriter>.Instance;
        _eventIds = eventIds ?? GuidAuditEventIdSource.Instance;
    }

    /// <inheritdoc />
    public Task WriteAsync(AuditEvent auditEvent, AuditActor actor, CancellationToken cancellationToken = default)
    {
        Require(auditEvent, AuditWriteClass.RequiredSecurityEvent, AuditWriteClass.Attempt);
        return WriteCoreAsync(auditEvent, actor, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryWriteAsync(AuditEvent auditEvent, AuditActor actor, CancellationToken cancellationToken = default)
    {
        Require(auditEvent, AuditWriteClass.Telemetry);
        try
        {
            await WriteCoreAsync(auditEvent, actor, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write the {AuditAction} audit row.", auditEvent.Action);
            return false;
        }
    }

    private void Require(AuditEvent auditEvent, params AuditWriteClass[] classes)
    {
        var definition = _catalog.Find(auditEvent.Action)
            ?? throw new InvalidOperationException($"Audit action '{auditEvent.Action}' is not declared in the audit catalog.");
        if (!classes.Contains(definition.WriteClass) || definition.Details is null)
            throw new InvalidOperationException($"Audit action '{auditEvent.Action}' is {definition.WriteClass}; this write path does not carry its guarantee.");
    }

    private async Task WriteCoreAsync(AuditEvent auditEvent, AuditActor actor, CancellationToken cancellationToken)
    {
        var resolved = await AuditRecorderRows.ResolveAsync(actor, _principals, cancellationToken).ConfigureAwait(false);
        var eventId = _eventIds.NewEventId();

        await using var db = new AppertureDbContext(_contextOptions);
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(
            async ct =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                var username = await AuditRecorderRows.ReadUsernameAsync(db, resolved, ct).ConfigureAwait(false);
                db.AuditLogs.Add(AuditRecorderRows.Build(auditEvent, resolved, username, eventId, eventId, _time.GetUtcNow()));
                try
                {
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (AuditRecorderRows.IsEventIdViolation(ex))
                {
                    // A retry after a lost acknowledgement: the row with this event id already exists.
                    db.ChangeTracker.Clear();
                }
            },
            cancellationToken).ConfigureAwait(false);
    }
}
