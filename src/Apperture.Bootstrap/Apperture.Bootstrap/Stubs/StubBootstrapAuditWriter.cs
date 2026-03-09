using Apperture.Bootstrap.Models;

namespace Apperture.Bootstrap.Stubs;

/// <summary>
/// No-op audit writer. To be replaced by persistent audit implementation.
/// </summary>
public sealed class StubBootstrapAuditWriter : IBootstrapAuditWriter
{
    public Task WriteAsync(BootstrapChangeAuditRecord record, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
