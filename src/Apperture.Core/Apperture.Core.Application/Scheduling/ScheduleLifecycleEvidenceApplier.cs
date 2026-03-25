using Apperture.Core.Domain.Scheduling;

namespace Apperture.Core.Application.Scheduling;

/// <summary>
/// Computes persisted lifecycle timestamps on schedule create/update (Epic 66).
/// Idempotent: identical effective scheduling fields and no transitions do not advance anchors.
/// </summary>
public static class ScheduleLifecycleEvidenceApplier
{
    /// <summary>
    /// Returns <paramref name="proposed"/> with <see cref="ScheduleDefinition.Lifecycle"/> set from
    /// <paramref name="previous"/> and transition/material rules. Ignores <paramref name="proposed"/>.Lifecycle.
    /// </summary>
    public static ScheduleDefinition MergeForPersist(
        ScheduleDefinition? previous,
        ScheduleDefinition proposed,
        DateTimeOffset utcNow)
    {
        if (previous is null)
            return proposed with { Lifecycle = LifecycleForCreate(proposed, utcNow) };

        var priorLife = previous.Lifecycle;
        var enabledWas = previous.Enabled;
        var enabledNow = proposed.Enabled;
        var materialChanged = ScheduleMaterialSemanticsComparer.HasMaterialSemanticsChange(previous, proposed);

        DateTimeOffset? lastEnabled = priorLife?.LastScheduleEnabledAtUtc;
        DateTimeOffset? lastDisabled = priorLife?.LastScheduleDisabledAtUtc;
        DateTimeOffset? materialAt = priorLife?.MaterialScheduleSemanticsChangedAtUtc;

        var changed = false;
        if (!enabledWas && enabledNow)
        {
            lastEnabled = utcNow;
            changed = true;
        }

        if (enabledWas && !enabledNow)
        {
            lastDisabled = utcNow;
            changed = true;
        }

        if (materialChanged)
        {
            materialAt = utcNow;
            changed = true;
        }

        if (!changed)
            return proposed with { Lifecycle = priorLife };

        var nextLife = NormalizeEvidence(lastEnabled, lastDisabled, materialAt);
        return proposed with { Lifecycle = nextLife };
    }

    private static ScheduleLifecycleEvidence LifecycleForCreate(ScheduleDefinition proposed, DateTimeOffset utcNow)
    {
        if (proposed.Enabled)
            return new ScheduleLifecycleEvidence(utcNow, null, utcNow);

        return new ScheduleLifecycleEvidence(null, utcNow, null);
    }

    private static ScheduleLifecycleEvidence? NormalizeEvidence(
        DateTimeOffset? lastEnabled,
        DateTimeOffset? lastDisabled,
        DateTimeOffset? materialAt)
    {
        if (lastEnabled is null && lastDisabled is null && materialAt is null)
            return null;

        return new ScheduleLifecycleEvidence(lastEnabled, lastDisabled, materialAt);
    }
}
