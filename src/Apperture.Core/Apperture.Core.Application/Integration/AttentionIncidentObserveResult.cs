namespace Apperture.Core.Application.Integration;

/// <summary>Epic 69: outcome of persisting schedule-expectation observation for incident notifications.</summary>
public enum AttentionIncidentObserveTransition
{
    NoChange,
    Opened,
    ObservedStillActive,
    Resolved,
}

/// <param name="IncidentId">Populated when transition is Opened, ObservedStillActive, or Resolved.</param>
public sealed record AttentionIncidentObserveResult(
    AttentionIncidentObserveTransition Transition,
    string? IncidentId);
