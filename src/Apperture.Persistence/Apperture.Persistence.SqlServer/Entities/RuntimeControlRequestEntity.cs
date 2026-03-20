using Apperture.RuntimePlane.Abstractions;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Durable external-control request for a runtime.
/// Control-plane persists intent; runtime plane consumes it.
/// </summary>
public sealed class RuntimeControlRequestEntity
{
    public long Id { get; set; }
    public string RuntimeId { get; set; } = "";
    public string ControlKind { get; set; } = RuntimeControlKindNames.Shutdown;
    public string Status { get; set; } = RuntimeControlStatusNames.Queued;

    public DateTimeOffset RequestedAt { get; set; }
    public string? RequestedBy { get; set; }

    public DateTimeOffset? AppliedAt { get; set; }
    public string? AppliedBy { get; set; }
    public string? LastError { get; set; }
}

