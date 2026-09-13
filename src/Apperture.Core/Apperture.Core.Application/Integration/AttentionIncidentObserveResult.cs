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
/// <param name="ResourceContextId">
/// CTX-2: the incident's immutable ResourceContext snapshot (set once at Open, from the Integration's
/// ResourceContext at that moment). Populated whenever <paramref name="IncidentId"/> is.
/// </param>
public sealed record AttentionIncidentObserveResult(
    AttentionIncidentObserveTransition Transition,
    string? IncidentId,
    string? ResourceContextId = null);
