namespace Apperture.Core.Domain.Scheduling;

/// <summary>
/// Persisted schedule lifecycle anchors for expected-run grace (Epic 66).
/// Null fields mean unknown or not applicable (e.g. pre-migration rows).
/// </summary>
public sealed record ScheduleLifecycleEvidence(
    DateTimeOffset? LastScheduleEnabledAtUtc,
    DateTimeOffset? LastScheduleDisabledAtUtc,
    DateTimeOffset? MaterialScheduleSemanticsChangedAtUtc);
