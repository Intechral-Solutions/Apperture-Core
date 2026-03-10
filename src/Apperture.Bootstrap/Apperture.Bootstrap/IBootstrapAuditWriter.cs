using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap;

/// <summary>
/// Writes bootstrap change and recovery audit records. No raw secrets.
/// </summary>
public interface IBootstrapAuditWriter
{
    /// <summary>Writes a bootstrap change audit record.</summary>
    Task WriteAsync(BootstrapChangeAuditRecord record, CancellationToken cancellationToken = default);

    /// <summary>Writes a recovery/break-glass audit record. No secret values.</summary>
    Task WriteRecoveryAsync(BootstrapRecoveryAuditRecord record, CancellationToken cancellationToken = default);
}
