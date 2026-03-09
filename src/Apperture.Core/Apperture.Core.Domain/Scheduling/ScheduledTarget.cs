namespace Apperture.Core.Domain.Scheduling;

/// <summary>
/// Discriminated target payload for schedule dispatch.
/// </summary>
public sealed record ScheduledTarget(
    ScheduledJobType JobType,
    string? JobConfiguration);
