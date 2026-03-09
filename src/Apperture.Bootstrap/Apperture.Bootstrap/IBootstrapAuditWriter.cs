using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap;

/// <summary>
/// Writes bootstrap change audit records. No raw secrets.
/// </summary>
public interface IBootstrapAuditWriter
{
    /// <summary>Writes an audit record.</summary>
    Task WriteAsync(BootstrapChangeAuditRecord record, CancellationToken cancellationToken = default);
}
