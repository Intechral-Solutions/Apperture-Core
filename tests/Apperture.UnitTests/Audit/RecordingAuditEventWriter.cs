using Apperture.Core.Application.Audit;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// An in-memory <see cref="IAuditEventWriter"/> for service tests: records W2 (<see cref="WriteAsync"/>) and W3/W4
/// (<see cref="TryWriteAsync"/>) rows, and can be told to fail, to prove a failed W2 write withholds its result and a failed
/// W3 write leaves the response unchanged.
/// </summary>
internal sealed class RecordingAuditEventWriter : IAuditEventWriter
{
    public List<(AuditEvent Event, AuditActor Actor, bool BestEffort)> Written { get; } = [];

    /// <summary>When set, every write fails as a database failure would.</summary>
    public bool Fail { get; set; }

    public IReadOnlyList<string> Actions => Written.Select(w => w.Event.Action).ToList();

    public AuditEvent Single(string action) => Written.Single(w => w.Event.Action == action).Event;

    public Task WriteAsync(AuditEvent auditEvent, AuditActor actor, CancellationToken cancellationToken = default)
    {
        if (auditEvent.Definition.WriteClass is not (AuditWriteClass.RequiredSecurityEvent or AuditWriteClass.Attempt))
            throw new InvalidOperationException($"'{auditEvent.Action}' is not a W2 or W3 action.");
        if (Fail)
            throw new InvalidOperationException("Injected: the audit write failed.");
        Written.Add((auditEvent, actor, false));
        return Task.CompletedTask;
    }

    public Task<bool> TryWriteAsync(AuditEvent auditEvent, AuditActor actor, CancellationToken cancellationToken = default)
    {
        if (auditEvent.Definition.WriteClass is not (AuditWriteClass.Attempt or AuditWriteClass.Telemetry))
            throw new InvalidOperationException($"'{auditEvent.Action}' is not a W3 or W4 action.");
        if (Fail)
            return Task.FromResult(false);
        Written.Add((auditEvent, actor, true));
        return Task.FromResult(true);
    }
}
