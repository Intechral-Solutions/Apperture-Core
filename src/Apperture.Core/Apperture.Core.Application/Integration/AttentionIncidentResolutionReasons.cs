using Apperture.Core.Application.Scheduling;

namespace Apperture.Core.Application.Integration;

/// <summary>Epic 68: why a condition incident left the active state.</summary>
public static class AttentionIncidentResolutionReasons
{
    /// <summary>Schedule expectation no longer reports <see cref="ScheduleExpectationEvaluationStatus.MissingExpectedRun"/>.</summary>
    public const string ExpectationCleared = "expectation_cleared";
}
