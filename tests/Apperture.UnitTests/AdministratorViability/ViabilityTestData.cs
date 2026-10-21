using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;

namespace Apperture.UnitTests.AdministratorViability;

/// <summary>Builders for the viability model, so each test states only what it is about.</summary>
internal static class ViabilityTestData
{
    public const string Tenant = "11111111-1111-1111-1111-111111111111";
    public const string OtherTenant = "22222222-2222-2222-2222-222222222222";

    public static AdministratorFacts Admin(
        string id,
        bool local = false,
        bool entraTenant = false,
        string? tenant = Tenant,
        bool recoveryAccess = false) =>
        new(
            id,
            recoveryAccess,
            HasLocalLink: local,
            LocalCredentialEnabled: local,
            ProvenEntraLinkTenantIds: entraTenant ? [tenant] : []);

    public static SignInAvailability Availability(
        ViabilityView view = ViabilityView.Now,
        bool local = true,
        bool entra = true,
        string? tenant = Tenant) =>
        new(view, local, entra, tenant, IsEstimate: false, Notes: []);

    public static AdministratorViabilitySnapshot Snapshot(
        ViabilityView view,
        IEnumerable<string> voa,
        IEnumerable<string>? ei = null) =>
        new(view, voa.ToHashSet(StringComparer.Ordinal), (ei ?? []).ToHashSet(StringComparer.Ordinal));

    /// <summary>A state whose two views are given explicitly.</summary>
    public static AdministratorViabilityState State(
        AdministratorViabilitySnapshot now,
        AdministratorViabilitySnapshot restart,
        bool recoveryHealthy = false,
        ConfigurationTrust? trust = null) =>
        new(now, restart, recoveryHealthy, trust ?? ConfigurationTrust.Full, Configuration(trust));

    /// <summary>A state whose two views are the same sets.</summary>
    public static AdministratorViabilityState Same(
        IEnumerable<string> voa,
        IEnumerable<string>? ei = null,
        bool recoveryHealthy = false,
        ConfigurationTrust? trust = null)
    {
        var voaList = voa.ToList();
        var eiList = (ei ?? []).ToList();
        return State(
            Snapshot(ViabilityView.Now, voaList, eiList),
            Snapshot(ViabilityView.Restart, voaList, eiList),
            recoveryHealthy,
            trust);
    }

    /// <summary>The Recovery Access posture inputs of a healthy account, or of nobody designated.</summary>
    public static RecoveryAccessFacts Recovery(bool healthy = false, string user = "recovery") =>
        healthy
            ? new RecoveryAccessFacts(user, true, true, true, true, 3)
            : new RecoveryAccessFacts(null, false, false, false, false, 0);

    /// <summary>
    /// A state derived from stored facts the way the service derives it: both snapshots are the definitions applied to the facts under
    /// the given availabilities, and the facts travel with the state.
    /// </summary>
    public static AdministratorViabilityState FromFacts(
        IReadOnlyList<AdministratorFacts> administrators,
        SignInAvailability? now = null,
        SignInAvailability? restart = null,
        RecoveryAccessFacts? recovery = null,
        ConfigurationTrust? trust = null)
    {
        now ??= Availability(ViabilityView.Now);
        restart ??= Availability(ViabilityView.Restart);
        recovery ??= Recovery();
        trust ??= ConfigurationTrust.Full;
        return new AdministratorViabilityState(
            AdministratorViabilityEvaluator.Evaluate(administrators, now),
            AdministratorViabilityEvaluator.Evaluate(administrators, restart),
            recovery.IsHealthy,
            trust,
            new EffectiveSignInConfiguration(now, restart, trust),
            administrators,
            recovery);
    }

    private static EffectiveSignInConfiguration Configuration(ConfigurationTrust? trust) =>
        new(
            Availability(ViabilityView.Now),
            Availability(ViabilityView.Restart),
            trust ?? ConfigurationTrust.Full);
}
