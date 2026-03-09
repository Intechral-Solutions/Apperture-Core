namespace Apperture.Shared.Kernel;

/// <summary>
/// Sink for appending audit events. Implementation handles persistence; kernel defines contract only.
/// </summary>
public interface IAuditEventSink
{
    ValueTask AppendAsync(AuditEvent evt, CancellationToken cancellationToken = default);
}
