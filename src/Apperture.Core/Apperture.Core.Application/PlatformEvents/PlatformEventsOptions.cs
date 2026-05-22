namespace Apperture.Core.Application.PlatformEvents;

public sealed class PlatformEventsOptions
{
    public const string SectionName = "PlatformEvents";

    /// <summary>When false, emitters no-op and dual-emit is skipped. Attention incidents are unaffected.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Platform-owned execution lifecycle events (Epic 103D). Default off.</summary>
    public ExecutionLifecycleOptions ExecutionLifecycle { get; set; } = new();
}

public sealed class ExecutionLifecycleOptions
{
    /// <summary>When false, no <c>apperture.execution.run.*</c> events are emitted.</summary>
    public bool Enabled { get; set; }
}
