using Apperture.Core.Application.AdministratorViability;

namespace Apperture.ControlPlane.Security.AdministratorViability;

/// <summary>
/// What a state would be if one stored fact changed, derived with the one definition of a viable and of an Entra-independent
/// administrator (<see cref="AdministratorViabilityEvaluator"/>), for a display that must say what a writer would decide. A
/// writer does not use it: it applies its change in its transaction and reads the state it produced.
/// </summary>
public static class AdministratorViabilitySimulation
{
    /// <summary>
    /// The state after the Local credential of <paramref name="userId"/> is disabled. A user who holds no administrator role has
    /// no facts, so the state is unchanged. Null when the state carries no stored facts (a state that was built without them
    /// cannot be changed fact by fact).
    /// </summary>
    public static AdministratorViabilityState? AfterLocalCredentialDisabled(AdministratorViabilityState state, string userId)
    {
        if (state.Administrators is null)
            return null;

        var facts = state.Administrators
            .Select(a => string.Equals(a.UserId, userId, StringComparison.Ordinal) ? a with { LocalCredentialEnabled = false } : a)
            .ToList();

        return state with
        {
            Now = AdministratorViabilityEvaluator.Evaluate(facts, state.Configuration.Now),
            Restart = AdministratorViabilityEvaluator.Evaluate(facts, state.Configuration.Restart),
            Administrators = facts,
        };
    }
}
