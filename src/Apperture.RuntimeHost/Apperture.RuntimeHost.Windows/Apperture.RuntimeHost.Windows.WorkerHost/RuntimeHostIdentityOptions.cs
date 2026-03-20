namespace Apperture.RuntimeHost.Windows.WorkerHost;

/// <summary>
/// Minimal runtime identity used by this worker host for runtime control polling.
/// </summary>
public sealed class RuntimeHostIdentityOptions
{
    /// <summary>
    /// Runtime id this worker host represents.
    /// If null/empty, shutdown control requests are applied regardless of runtimeId.
    /// </summary>
    public string? RuntimeId { get; set; }
}

