using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.UnitTests.AdministratorViability;
using Apperture.UnitTests.Audit;
using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// The Entra-primary policy (AUD-1.2, IAM-2) decided by the shared administrator-viability guard under the global gate
/// (IAM-10.2b WP4A): the real coordinator, the real viability service and the real guard over facts derived from the same fakes the
/// policy writes to. Only the sign-in configuration and the gate are scripted. The transactional guarantees (rollback of a deferred
/// change, the rerun taking the gate again, the races) are proven against SQL Server (EntraPrimaryPolicyViabilitySqlServerTests).
/// </summary>
public sealed class EntraPrimaryAuthenticationPolicyTests
{
    private const string Tenant = "33333333-3333-3333-3333-333333333333";
    private const string OtherTenant = "44444444-4444-4444-4444-444444444444";

    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly RoleId AdminRole = new(BootstrapConstants.PlatformAdminRoleId);

    private static IdentityLink Local(string name) => new(IdentitySource.Local, name, Now);

    private static IdentityLink ProvenEntra(string tenant = Tenant) =>
        new(IdentitySource.Entra, "oid-" + tenant[..2], LastAuthenticatedAt: Now, TenantId: tenant);

    private static IdentityLink PendingEntra(string tenant = Tenant) =>
        new(IdentitySource.Entra, "oid-" + tenant[..2], LastAuthenticatedAt: null, TenantId: tenant);

    // ----- the cases the policy has always had -----

    [Fact]
    public async Task Ordinary_user_local_credential_is_disabled_and_hash_is_retained()
    {
        var harness = Harness.OrdinaryUser();

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("usr-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
        harness.Credentials.Disabled["usr-1"].Should().BeTrue();
        harness.Credentials.Hashes["usr-1"].Should().Be("hash-original");
        harness.Audit.Actions.Should().Contain(StrongAuthenticationAuditActionNames.LocalDisabledEntraPrimary);
    }

    [Fact]
    public async Task Already_disabled_is_idempotent_without_another_audit_and_without_taking_the_gate()
    {
        var harness = Harness.OrdinaryUser(alreadyDisabled: true);

        var first = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("usr-1"));
        harness.Audit.Actions.Clear();
        var second = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("usr-1"));

        first.Should().Be(EntraPrimaryLocalDisableOutcome.AlreadyDisabled);
        second.Should().Be(EntraPrimaryLocalDisableOutcome.AlreadyDisabled);
        harness.Audit.Actions.Should().BeEmpty();
        harness.Credentials.Hashes["usr-1"].Should().Be("hash-original");
        harness.Gate.Acquired.Should().Be(0, "the steady state of every later sign-in must not queue on the global gate");
        harness.Audit.Unit.Operations.Should().BeEmpty();
    }

    [Fact]
    public async Task A_user_without_a_local_credential_is_a_no_op_that_does_not_take_the_gate()
    {
        var harness = Harness.OrdinaryUser(withCredential: false);

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("usr-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.NoLocalCredential);
        harness.Gate.Acquired.Should().Be(0);
        harness.Audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task The_last_entra_independent_platform_administrator_is_deferred_by_the_fallback_invariant()
    {
        var harness = Harness.SolePlatformAdministrator();

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator);
        harness.Credentials.Disabled["admin-1"].Should().BeFalse("the disable was rolled back with the unit");
        harness.Credentials.Hashes["admin-1"].Should().Be("hash-original");
        harness.Audit.Actions.Should().Contain(StrongAuthenticationAuditActionNames.LocalDisableDeferredLastAdmin);
        harness.Audit.Actions.Should().NotContain(StrongAuthenticationAuditActionNames.LocalDisabledEntraPrimary);
    }

    [Fact]
    public async Task Designated_emergency_account_is_never_auto_disabled()
    {
        var harness = Harness.SolePlatformAdministrator();
        harness.Users.Seed(new User(
            new UserId("admin-1"),
            "Emergency Administrator",
            [Local("admin"), ProvenEntra()],
            [AdminRole],
            IsBreakGlassEligible: true)
        { Username = "admin" });

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.ProtectedEmergencyAccount);
        harness.Credentials.Disabled["admin-1"].Should().BeFalse();
        harness.Audit.Actions.Should().Contain(EmergencyAccessAuditActionNames.LocalProtected);
    }

    [Fact]
    public async Task Healthy_emergency_access_allows_an_ordinary_administrator_with_a_usable_entra_link_to_lose_local_sign_in()
    {
        var harness = Harness.SolePlatformAdministrator();
        harness.SeedRecoveryAccount(healthy: true);

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
        harness.Credentials.Disabled["admin-1"].Should().BeTrue();
        harness.Credentials.Disabled["emerg-1"].Should().BeFalse();
        harness.Audit.Actions.Should().Contain(StrongAuthenticationAuditActionNames.LocalDisabledEntraPrimary);
        harness.Audit.Actions.Should().NotContain(StrongAuthenticationAuditActionNames.LocalDisableDeferredLastAdmin);
        harness.Audit.Single(StrongAuthenticationAuditActionNames.LocalDisabledEntraPrimary).Fields["recoveryAccessHealthy"]
            .Should().Be(true, "the row records the posture the decision was taken under");
    }

    [Fact]
    public async Task An_incomplete_emergency_account_does_not_lift_the_fallback_safeguard()
    {
        var harness = Harness.SolePlatformAdministrator();
        harness.SeedRecoveryAccount(healthy: false);

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator);
        harness.Credentials.Disabled["admin-1"].Should().BeFalse();
    }

    [Fact]
    public async Task A_second_local_platform_administrator_allows_the_policy_to_proceed()
    {
        var harness = Harness.TwoLocalPlatformAdministrators();

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
        harness.Credentials.Disabled["admin-1"].Should().BeTrue();
        harness.Credentials.Disabled["admin-2"].Should().BeFalse();
    }

    [Fact]
    public async Task Ordinary_user_is_not_preserved_because_emergency_access_is_absent()
    {
        var harness = Harness.OrdinaryUser();

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("usr-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
    }

    [Fact]
    public async Task A_previously_deferred_last_admin_is_disabled_after_another_independent_admin_exists()
    {
        var harness = Harness.SolePlatformAdministrator();

        var deferred = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));
        deferred.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator);
        harness.Credentials.Disabled["admin-1"].Should().BeFalse();

        harness.SeedLocalAdministrator("admin-2");
        harness.Audit.Actions.Clear();

        var reevaluated = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        reevaluated.Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
        harness.Credentials.Disabled["admin-1"].Should().BeTrue();
        harness.Credentials.Disabled["admin-2"].Should().BeFalse();
        harness.Credentials.Hashes["admin-1"].Should().Be("hash-original");
        harness.Audit.Actions.Should().Contain(StrongAuthenticationAuditActionNames.LocalDisabledEntraPrimary);
        harness.Audit.Actions.Should().NotContain(StrongAuthenticationAuditActionNames.LocalDisableDeferredLastAdmin);
    }

    [Fact]
    public async Task A_subsequent_apply_while_still_the_last_independent_admin_remains_deferred()
    {
        var harness = Harness.SolePlatformAdministrator();

        var first = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));
        var second = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        first.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator);
        second.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator);
        harness.Credentials.Disabled["admin-1"].Should().BeFalse();
    }

    [Fact]
    public async Task The_policy_acts_as_a_named_subsystem_against_the_user_and_leaving_sign_in_enabled_is_an_attempt_row()
    {
        var disabled = Harness.OrdinaryUser();
        await disabled.Policy.ApplyAfterEntraProvenAsync(new UserId("usr-1"));
        var deferred = Harness.SolePlatformAdministrator();
        await deferred.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        var (change, changeActor) = disabled.Audit.Events.Should().ContainSingle().Subject;
        changeActor.Should().Be(AuditActor.System("entra-primary-policy"), "the policy acts on its own; the user is the target");
        change.Target.Should().Be(new AuditTarget(AuditTargetTypes.User, "usr-1"));
        change.Definition.WriteClass.Should().Be(AuditWriteClass.RequiredMutation);
        var (kept, keptActor) = deferred.Audit.Events.Should().ContainSingle().Subject;
        keptActor.Should().Be(AuditActor.System("entra-primary-policy"));
        kept.Definition.WriteClass.Should().Be(AuditWriteClass.Attempt, "nothing changed (D6)");
        kept.Definition.Basis.Should().Be(AuditBases.Attempt);
        deferred.Audit.Unit.Operations.Should().ContainSingle().Which.NoOp.Should().BeTrue();
    }

    // ----- WP4A: the shared guard decides -----

    [Fact]
    public async Task An_administrator_whose_entra_link_is_still_pending_is_deferred_by_the_viable_administrator_invariant_even_with_healthy_recovery_access()
    {
        // The pending link is not a way in, so disabling Local would leave nobody able to sign in. Healthy Recovery Access
        // satisfies I-FALLBACK, but it is not an ordinary administrator and cannot satisfy I-VIABLE.
        var harness = Harness.SolePlatformAdministrator(entra: PendingEntra());
        harness.SeedRecoveryAccount(healthy: true);

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastViablePlatformAdministrator);
        harness.Credentials.Disabled["admin-1"].Should().BeFalse();
        harness.Audit.Actions.Should().Contain(StrongAuthenticationAuditActionNames.LocalDisableDeferredLastAdmin);
        harness.Audit.Actions.Should().NotContain(StrongAuthenticationAuditActionNames.LocalDisabledEntraPrimary);
    }

    [Fact]
    public async Task An_entra_link_for_another_directory_is_not_a_way_in()
    {
        var harness = Harness.SolePlatformAdministrator(entra: ProvenEntra(OtherTenant));

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastViablePlatformAdministrator);
        harness.Credentials.Disabled["admin-1"].Should().BeFalse();
    }

    [Fact]
    public async Task When_entra_sign_in_is_not_usable_the_last_local_administrator_keeps_local_sign_in()
    {
        var harness = Harness.SolePlatformAdministrator();
        harness.Configuration.EntraUsable = false;

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastViablePlatformAdministrator);
        harness.Credentials.Disabled["admin-1"].Should().BeFalse();
    }

    [Fact]
    public async Task Recovery_access_is_not_counted_as_the_administrator_who_remains()
    {
        // The designated account holds the role and has an enabled credential: it is excluded from VOA and EI, so the one ordinary
        // administrator is still the last, whatever the posture says.
        var harness = Harness.SolePlatformAdministrator(entra: PendingEntra());
        harness.SeedRecoveryAccount(healthy: false);

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastViablePlatformAdministrator);
    }

    [Fact]
    public async Task Role_holders_who_cannot_sign_in_do_not_count_as_the_administrator_who_remains()
    {
        var harness = Harness.SolePlatformAdministrator();
        harness.SeedLocalAdministrator("stranded", credentialDisabled: true);
        harness.SeedLocalAdministrator("no-link", withLocalLink: false);

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator, "two other role holders exist, and neither can sign in");
        harness.Credentials.Disabled["admin-1"].Should().BeFalse();
    }

    [Fact]
    public async Task An_unknown_entra_reading_defers_the_last_local_administrator_without_claiming_a_violation()
    {
        var harness = Harness.SolePlatformAdministrator();
        harness.Configuration.Trust = new ConfigurationTrust(ConfigurationTrustLevel.Trusted, ConfigurationTrustLevel.Unknown, ["The client secret cannot be read."]);

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredAdministratorViabilityUnavailable);
        harness.Credentials.Disabled["admin-1"].Should().BeFalse();
        harness.Credentials.Hashes["admin-1"].Should().Be("hash-original");
        harness.Audit.Events.Should().BeEmpty("nothing is proven about the administrators, so no last-administrator row is written");
        harness.Audit.Unit.Operations.Should().ContainSingle().Which.NoOp.Should().BeTrue();
    }

    [Fact]
    public async Task An_unknown_local_setting_defers_without_claiming_a_violation()
    {
        var harness = Harness.TwoLocalPlatformAdministrators();
        harness.Configuration.Trust = new ConfigurationTrust(ConfigurationTrustLevel.Unknown, ConfigurationTrustLevel.Trusted, ["Set LocalAuth:Enabled."]);

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredAdministratorViabilityUnavailable);
        harness.Credentials.Disabled["admin-1"].Should().BeFalse();
        harness.Audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_entra_reading_does_not_stop_a_change_that_leaves_a_proven_local_administrator()
    {
        var harness = Harness.TwoLocalPlatformAdministrators();
        harness.Configuration.Trust = new ConfigurationTrust(ConfigurationTrustLevel.Trusted, ConfigurationTrustLevel.Unknown, ["The client secret cannot be read."]);

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.Disabled, "the safe post-state proof needs no Microsoft reading");
        harness.Credentials.Disabled["admin-1"].Should().BeTrue();
    }

    [Fact]
    public async Task An_ordinary_non_administrator_is_disabled_whatever_the_configuration_says()
    {
        var harness = Harness.OrdinaryUser();
        harness.Configuration.Trust = new ConfigurationTrust(ConfigurationTrustLevel.Unknown, ConfigurationTrustLevel.Unknown, ["nothing is known"]);

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("usr-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.Disabled, "no administrator-viability input is touched");
        harness.Credentials.Disabled["usr-1"].Should().BeTrue();
    }

    [Fact]
    public async Task Recovery_code_exhaustion_changes_the_fallback_posture_between_applications()
    {
        var harness = Harness.SolePlatformAdministrator();
        harness.SeedRecoveryAccount(healthy: true);
        harness.RecoveryCodes = 0;

        var exhausted = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));
        harness.RecoveryCodes = 3;
        var restored = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        exhausted.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator);
        restored.Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
    }

    // ----- a concurrent change to administrator access between the two reads (IAM-10.2b WP4A review, L-1) -----

    [Fact]
    public async Task A_recovery_posture_change_between_the_reads_defers_a_user_who_is_not_an_administrator_without_the_last_administrator_row()
    {
        // Recovery Access is healthy, nobody is Entra-independent, and the target holds no role. The last recovery code is consumed at
        // its sign-in (ruling C-14: exempt from the gate) after the state before was read. The result reads as a fallback violation,
        // but the user never contributed to it.
        var control = Harness.OrdinaryUser();
        control.SeedRecoveryAccount(healthy: true);
        (await control.Policy.ApplyAfterEntraProvenAsync(new UserId("usr-1"))).Should().Be(EntraPrimaryLocalDisableOutcome.Disabled, "without the change between the reads the same arrangement is allowed");

        var harness = Harness.OrdinaryUser();
        harness.SeedRecoveryAccount(healthy: true);
        harness.Credentials.BeforeDisabledWrite = () => harness.RecoveryCodes = 0;

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("usr-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredConcurrentAdministratorAccessChange);
        harness.RecoveryCodes.Should().Be(0, "the exempt change did happen");
        harness.Credentials.Disabled["usr-1"].Should().BeFalse("the proposed disable is rolled back");
        harness.Credentials.Hashes["usr-1"].Should().Be("hash-original");
        harness.Audit.Actions.Should().BeEmpty("neither the success row nor the last-administrator attempt row is true of this user");
        harness.Audit.Unit.Operations.Should().ContainSingle();
        harness.Gate.Acquired.Should().Be(1);
    }

    [Fact]
    public async Task The_removal_of_the_only_viable_administrator_between_the_reads_is_not_attributed_to_a_user_who_is_not_an_administrator()
    {
        // The I-VIABLE branch of the same attribution: a writer that is not yet under the gate removes the one administrator who can
        // sign in. The target is an ordinary user, so the viable-administrator row would not be true of them either.
        var harness = Harness.OrdinaryUser();
        harness.SeedLocalAdministrator("admin-2");
        harness.Credentials.BeforeDisabledWrite = () => harness.Roles.UserIds.Remove(new UserId("admin-2"));

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("usr-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredConcurrentAdministratorAccessChange);
        harness.Credentials.Disabled["usr-1"].Should().BeFalse();
        harness.Audit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_genuine_fallback_deferral_keeps_its_attempt_row_even_when_the_posture_also_changed_between_the_reads()
    {
        // The user is the only Entra-independent administrator in the state read under the gate, so the row is true of them whatever
        // else happened: disabling their credential is what takes the last such administrator away.
        var harness = Harness.SolePlatformAdministrator();
        harness.SeedRecoveryAccount(healthy: true);
        harness.Credentials.BeforeDisabledWrite = () => harness.RecoveryCodes = 0;

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator);
        harness.Credentials.Disabled["admin-1"].Should().BeFalse();
        harness.Audit.Actions.Should().Equal(StrongAuthenticationAuditActionNames.LocalDisableDeferredLastAdmin);
    }

    [Fact]
    public async Task A_genuine_viable_administrator_deferral_keeps_its_attempt_row_and_its_outcome()
    {
        var harness = Harness.SolePlatformAdministrator(entra: PendingEntra());
        harness.SeedRecoveryAccount(healthy: true);

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastViablePlatformAdministrator);
        harness.Audit.Actions.Should().Equal(StrongAuthenticationAuditActionNames.LocalDisableDeferredLastAdmin);
    }

    [Fact]
    public async Task The_viability_decision_is_the_same_with_or_without_the_attribution()
    {
        // Same arrangement, same state: only what is said about the refusal differs. The credential and the audit trail of the
        // success path are those of any refusal.
        foreach (var attributable in new[] { true, false })
        {
            var harness = attributable ? Harness.SolePlatformAdministrator() : Harness.OrdinaryUser();
            harness.SeedRecoveryAccount(healthy: true);
            harness.Credentials.BeforeDisabledWrite = () => harness.RecoveryCodes = 0;
            var id = attributable ? "admin-1" : "usr-1";

            var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId(id));

            outcome.Should().NotBe(EntraPrimaryLocalDisableOutcome.Disabled, $"attributable to the user: {attributable}");
            harness.Credentials.Disabled[id].Should().BeFalse();
            harness.Audit.Actions.Should().NotContain(StrongAuthenticationAuditActionNames.LocalDisabledEntraPrimary);
        }
    }

    public static IEnumerable<object?[]> RefusalsThatNeedTheUserInTheDecidingView()
    {
        var user = new UserId("usr-1");
        var other = ViabilityTestData.Snapshot(ViabilityView.Now, ["other"], ["other"]);
        var restartOther = ViabilityTestData.Snapshot(ViabilityView.Restart, ["other"], ["other"]);
        var nowMine = ViabilityTestData.Snapshot(ViabilityView.Now, ["usr-1"], ["usr-1"]);
        var restartMine = ViabilityTestData.Snapshot(ViabilityView.Restart, ["usr-1"], ["usr-1"]);

        // The user is Entra-independent in the deciding view.
        yield return [ViabilityVerdictKind.EntraIndependentFallbackRequired, ViabilityView.Now, ViabilityTestData.State(nowMine, restartOther), EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator];
        yield return [ViabilityVerdictKind.EntraIndependentFallbackRequired, ViabilityView.Restart, ViabilityTestData.State(other, restartMine), EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator];
        yield return [ViabilityVerdictKind.LastViableAdministrator, ViabilityView.Now, ViabilityTestData.State(nowMine, restartOther), EntraPrimaryLocalDisableOutcome.DeferredLastViablePlatformAdministrator];
        yield return [ViabilityVerdictKind.LastViableAdministrator, ViabilityView.Restart, ViabilityTestData.State(other, restartMine), EntraPrimaryLocalDisableOutcome.DeferredLastViablePlatformAdministrator];

        // The user is Entra-independent only in the other view, or in neither: the deciding view does not establish it.
        yield return [ViabilityVerdictKind.EntraIndependentFallbackRequired, ViabilityView.Now, ViabilityTestData.State(other, restartMine), EntraPrimaryLocalDisableOutcome.DeferredConcurrentAdministratorAccessChange];
        yield return [ViabilityVerdictKind.LastViableAdministrator, ViabilityView.Restart, ViabilityTestData.State(nowMine, restartOther), EntraPrimaryLocalDisableOutcome.DeferredConcurrentAdministratorAccessChange];
        yield return [ViabilityVerdictKind.EntraIndependentFallbackRequired, ViabilityView.Now, ViabilityTestData.State(other, restartOther), EntraPrimaryLocalDisableOutcome.DeferredConcurrentAdministratorAccessChange];

        // A user who is viable but not Entra-independent in the view (signs in only through Microsoft) is not the one who was lost.
        yield return [ViabilityVerdictKind.LastViableAdministrator, ViabilityView.Now, ViabilityTestData.State(ViabilityTestData.Snapshot(ViabilityView.Now, ["usr-1"], []), restartOther), EntraPrimaryLocalDisableOutcome.DeferredConcurrentAdministratorAccessChange];

        // Nothing established: no deciding view, or no captured state.
        yield return [ViabilityVerdictKind.EntraIndependentFallbackRequired, null, ViabilityTestData.State(nowMine, restartMine), EntraPrimaryLocalDisableOutcome.DeferredConcurrentAdministratorAccessChange];
        yield return [ViabilityVerdictKind.LastViableAdministrator, null, ViabilityTestData.State(nowMine, restartMine), EntraPrimaryLocalDisableOutcome.DeferredConcurrentAdministratorAccessChange];
        yield return [ViabilityVerdictKind.LastViableAdministrator, ViabilityView.Now, null, EntraPrimaryLocalDisableOutcome.DeferredConcurrentAdministratorAccessChange];

        // An unavailable configuration is its own outcome whatever the captured state says.
        yield return [ViabilityVerdictKind.ConfigurationUnavailable, null, ViabilityTestData.State(nowMine, restartMine), EntraPrimaryLocalDisableOutcome.DeferredAdministratorViabilityUnavailable];
        yield return [ViabilityVerdictKind.ConfigurationUnavailable, null, null, EntraPrimaryLocalDisableOutcome.DeferredAdministratorViabilityUnavailable];
    }

    [Theory]
    [MemberData(nameof(RefusalsThatNeedTheUserInTheDecidingView))]
    public void A_refusal_is_attributed_to_the_user_only_when_the_captured_deciding_view_shows_them_entra_independent(
        ViabilityVerdictKind kind, ViabilityView? view, AdministratorViabilityState? captured, EntraPrimaryLocalDisableOutcome expected)
    {
        var verdict = new ViabilityVerdict(kind, view, "detail");

        EntraPrimaryAuthenticationPolicy.Deferred(verdict, captured, new UserId("usr-1")).Should().Be(expected);
    }

    // ----- the gate -----

    [Fact]
    public async Task The_gate_is_taken_after_the_unit_opens_and_before_anything_is_read_or_written_and_the_state_is_read_around_the_write()
    {
        var harness = Harness.TwoLocalPlatformAdministrators();
        harness.Log.Clear();

        await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        harness.Log.Should().Equal(
            "credential read (outside the unit)",
            "gate",
            "user read",
            "credential read",
            "state read",
            "credential write",
            "credential read",
            "state read");
    }

    [Fact]
    public async Task A_busy_gate_is_not_applied_not_disabled_not_recorded_and_not_a_deferral()
    {
        var harness = Harness.TwoLocalPlatformAdministrators();
        harness.Gate.Fail = new PlatformAdministratorSetBusyException(PlatformAdministratorSetLockOutcome.TimedOut);

        var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.NotAppliedBusy);
        harness.Credentials.Disabled["admin-1"].Should().BeFalse();
        harness.Audit.Events.Should().BeEmpty("no success, no attempt row and no false last-administrator deferral");
        harness.Log.Should().NotContain("credential write").And.NotContain("state read");
    }

    [Fact]
    public async Task A_busy_gate_is_followed_by_a_successful_later_application()
    {
        var harness = Harness.TwoLocalPlatformAdministrators();
        harness.Gate.Fail = new PlatformAdministratorSetBusyException(PlatformAdministratorSetLockOutcome.DeadlockVictim);
        var busy = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));
        harness.Gate.Fail = null;

        var later = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

        busy.Should().Be(EntraPrimaryLocalDisableOutcome.NotAppliedBusy);
        later.Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
        harness.Credentials.Disabled["admin-1"].Should().BeTrue();
        harness.Audit.Actions.Should().ContainSingle().Which.Should().Be(StrongAuthenticationAuditActionNames.LocalDisabledEntraPrimary);
    }

    public static IEnumerable<object[]> InfrastructureFailures() =>
    [
        [new PlatformAdministratorSetGateException(-999)],
        [new OperationCanceledException()],
        [new AuditOutcomeUnknownException("op-unknown")],
        [new InvalidOperationException("the database failed")],
    ];

    [Theory]
    [MemberData(nameof(InfrastructureFailures))]
    public async Task Infrastructure_failures_are_never_turned_into_a_busy_outcome(Exception failure)
    {
        var harness = Harness.TwoLocalPlatformAdministrators();
        harness.Gate.Fail = failure;

        var act = () => harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1")).AsTask();

        (await act.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(failure);
        harness.Credentials.Disabled["admin-1"].Should().BeFalse();
        harness.Audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failure_while_the_disable_is_being_recorded_commits_no_unaudited_disable()
    {
        var harness = Harness.TwoLocalPlatformAdministrators();
        var policy = harness.PolicyOver(new FailAfterAttempt(harness.Audit));

        var act = () => policy.ApplyAfterEntraProvenAsync(new UserId("admin-1")).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        harness.Credentials.Disabled["admin-1"].Should().BeFalse("the unit rolled the disable back with the failed row");
    }

    // ----- the presentation uses the same definitions -----

    [Fact]
    public async Task Describe_marks_designated_emergency_local_policy()
    {
        var harness = Harness.SolePlatformAdministrator();
        var user = new User(
            new UserId("emerg-1"),
            "Emergency Administrator",
            [Local("breakglass")],
            [AdminRole],
            IsBreakGlassEligible: true)
        { Username = "breakglass" };
        harness.Users.Seed(user);
        harness.Credentials.Seed("emerg-1", "hash-e", disabled: false);
        var cred = await harness.Credentials.GetByUserIdAsync(new UserId("emerg-1"));

        var presentation = await harness.Policy.DescribeAsync(user, cred);

        presentation.LocalSignInPolicy.Should().Be(AuthenticationMethodStatuses.LocalEmergencyAccess);
        presentation.EmergencyAccessAccount.Should().BeTrue();
        presentation.RecoverySafeguardActive.Should().BeFalse();
    }

    [Fact]
    public async Task Describe_marks_the_recovery_safeguard_for_the_last_local_admin()
    {
        var harness = Harness.SolePlatformAdministrator();
        var user = await harness.Users.GetByIdAsync(new UserId("admin-1"));
        var cred = await harness.Credentials.GetByUserIdAsync(new UserId("admin-1"));

        var presentation = await harness.Policy.DescribeAsync(user!, cred);

        presentation.LocalSignInPolicy.Should().Be(AuthenticationMethodStatuses.LocalRecoverySafeguard);
        presentation.RecoverySafeguardActive.Should().BeTrue();
        presentation.EntraAssociationStatus.Should().Be(AuthenticationMethodStatuses.EntraProven);
    }

    [Fact]
    public async Task Describe_shows_the_safeguard_when_the_entra_link_is_not_usable_even_with_healthy_recovery_access()
    {
        // The old presentation said "enabled" here (healthy Recovery Access lifted the count-based rule), while the policy would
        // keep the credential: disabling it would leave nobody able to sign in.
        var harness = Harness.SolePlatformAdministrator(entra: ProvenEntra(OtherTenant));
        harness.SeedRecoveryAccount(healthy: true);
        var user = await harness.Users.GetByIdAsync(new UserId("admin-1"));
        var cred = await harness.Credentials.GetByUserIdAsync(new UserId("admin-1"));

        var presentation = await harness.Policy.DescribeAsync(user!, cred);

        presentation.RecoverySafeguardActive.Should().BeTrue();
        presentation.LocalSignInPolicy.Should().Be(AuthenticationMethodStatuses.LocalRecoverySafeguard);
    }

    [Fact]
    public async Task Describe_agrees_with_what_the_policy_then_does()
    {
        foreach (var other in new[] { false, true })
        {
            var harness = other ? Harness.TwoLocalPlatformAdministrators() : Harness.SolePlatformAdministrator();
            var user = await harness.Users.GetByIdAsync(new UserId("admin-1"));
            var cred = await harness.Credentials.GetByUserIdAsync(new UserId("admin-1"));

            var presentation = await harness.Policy.DescribeAsync(user!, cred);
            var outcome = await harness.Policy.ApplyAfterEntraProvenAsync(new UserId("admin-1"));

            presentation.RecoverySafeguardActive.Should().Be(outcome != EntraPrimaryLocalDisableOutcome.Disabled, $"another local administrator: {other}");
        }
    }

    [Fact]
    public async Task Describe_does_not_take_the_gate()
    {
        var harness = Harness.SolePlatformAdministrator();
        var user = await harness.Users.GetByIdAsync(new UserId("admin-1"));
        var cred = await harness.Credentials.GetByUserIdAsync(new UserId("admin-1"));

        await harness.Policy.DescribeAsync(user!, cred);

        harness.Gate.Acquired.Should().Be(0, "a display only describes the state");
    }

    // ----- the harness -----

    private sealed class Harness
    {
        public List<string> Log { get; } = [];
        public RecordingUsers Users { get; }
        public RecordingCredentials Credentials { get; }
        public RecordingRoles Roles { get; } = new();
        public AuditRecording Audit { get; } = new();
        public HarnessGate Gate { get; }
        public HarnessConfiguration Configuration { get; } = new();
        public int RecoveryCodes { get; set; } = 3;
        public bool RecoveryMfaEnrolled { get; set; }
        public EntraPrimaryAuthenticationPolicy Policy { get; }

        private Harness()
        {
            Users = new RecordingUsers(Log);
            Credentials = new RecordingCredentials(Log);
            Gate = new HarnessGate(Log);
            Audit.Unit.CaptureState = () =>
            {
                var snapshot = Credentials.Snapshot();
                return () => Credentials.Restore(snapshot);
            };
            Policy = PolicyOver(Audit);
        }

        public EntraPrimaryAuthenticationPolicy PolicyOver(IAuditedUnitOfWork unit)
        {
            var reads = new AdministratorViabilityService(new HarnessFacts(this), Configuration);
            return new EntraPrimaryAuthenticationPolicy(
                Users,
                Credentials,
                NullLogger<EntraPrimaryAuthenticationPolicy>.Instance,
                new AdministratorViabilityCoordinator(Gate, new LoggingReads(reads, Log)),
                reads,
                unit,
                Audit);
        }

        public void SeedRecoveryAccount(bool healthy)
        {
            RecoveryMfaEnrolled = healthy;
            Users.Seed(new User(
                new UserId("emerg-1"),
                "Emergency Administrator",
                [Local("breakglass")],
                [AdminRole],
                IsBreakGlassEligible: true)
            { Username = "breakglass" });
            Credentials.Seed("emerg-1", "hash-e", disabled: false);
            Roles.UserIds.Add(new UserId("emerg-1"));
        }

        public void SeedLocalAdministrator(string id, bool credentialDisabled = false, bool withLocalLink = true)
        {
            Users.Seed(new User(
                new UserId(id),
                id,
                withLocalLink ? [Local(id)] : [],
                [AdminRole],
                false)
            { Username = id });
            Credentials.Seed(id, "hash-" + id, credentialDisabled);
            Roles.UserIds.Add(new UserId(id));
        }

        public static Harness OrdinaryUser(bool alreadyDisabled = false, bool withCredential = true)
        {
            var harness = new Harness();
            harness.Users.Seed(new User(
                new UserId("usr-1"),
                "Operator",
                [Local("operator"), ProvenEntra()],
                [],
                false)
            { Username = "operator" });
            if (withCredential)
                harness.Credentials.Seed("usr-1", "hash-original", alreadyDisabled);
            return harness;
        }

        public static Harness SolePlatformAdministrator(IdentityLink? entra = null)
        {
            var harness = new Harness();
            harness.Users.Seed(new User(
                new UserId("admin-1"),
                "Platform Administrator",
                [Local("admin"), entra ?? ProvenEntra()],
                [AdminRole],
                false)
            { Username = "admin" });
            harness.Credentials.Seed("admin-1", "hash-original", disabled: false);
            harness.Roles.UserIds.Add(new UserId("admin-1"));
            return harness;
        }

        public static Harness TwoLocalPlatformAdministrators()
        {
            var harness = SolePlatformAdministrator();
            harness.SeedLocalAdministrator("admin-2");
            return harness;
        }
    }

    /// <summary>The facts the SQL reader would read, derived from the policy's own fakes, so a write is seen by the next read.</summary>
    private sealed class HarnessFacts(Harness harness) : IAdministratorFactsReader
    {
        public async ValueTask<IReadOnlyList<AdministratorFacts>> ReadAdministratorsAsync(CancellationToken cancellationToken = default)
        {
            var facts = new List<AdministratorFacts>();
            foreach (var id in harness.Roles.UserIds)
            {
                var user = harness.Users.Peek(id.Value)!;
                var credential = harness.Credentials.Peek(id.Value);
                facts.Add(new AdministratorFacts(
                    id.Value,
                    user.IsBreakGlassEligible,
                    user.IdentityLinks.Any(l => l.Source == IdentitySource.Local),
                    credential is { IsDisabled: false },
                    user.IdentityLinks.Where(l => l.Source == IdentitySource.Entra && l.IsProven).Select(l => l.TenantId).ToList()));
            }

            await Task.CompletedTask;
            return facts;
        }

        public ValueTask<RecoveryAccessFacts> ReadRecoveryAccessAsync(CancellationToken cancellationToken = default)
        {
            var designated = harness.Roles.UserIds.Select(id => harness.Users.Peek(id.Value)!).FirstOrDefault(u => u.IsBreakGlassEligible);
            if (designated is null)
                return ValueTask.FromResult(new RecoveryAccessFacts(null, false, false, false, false, 0));
            var credential = harness.Credentials.Peek(designated.Id.Value);
            return ValueTask.FromResult(new RecoveryAccessFacts(
                designated.Id.Value,
                HoldsAdministratorRole: true,
                designated.IdentityLinks.Any(l => l.Source == IdentitySource.Local),
                credential is { IsDisabled: false },
                harness.RecoveryMfaEnrolled,
                harness.RecoveryMfaEnrolled ? harness.RecoveryCodes : 0));
        }
    }

    /// <summary>Local sign-in on; Entra usable for the registered tenant unless the test says otherwise.</summary>
    private sealed class HarnessConfiguration : IEffectiveSignInConfigurationProvider
    {
        public bool EntraUsable { get; set; } = true;
        public ConfigurationTrust Trust { get; set; } = ConfigurationTrust.Full;

        public ValueTask<EffectiveSignInConfiguration> ReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new EffectiveSignInConfiguration(
                ViabilityTestData.Availability(ViabilityView.Now, local: true, entra: EntraUsable, tenant: EntraUsable ? Tenant : null),
                ViabilityTestData.Availability(ViabilityView.Restart, local: true, entra: EntraUsable, tenant: EntraUsable ? Tenant : null),
                Trust));
    }

    private sealed class HarnessGate(List<string> log) : IPlatformAdministratorSetGate
    {
        public int Acquired { get; private set; }
        public Exception? Fail { get; set; }

        public ValueTask AcquireAsync(CancellationToken cancellationToken = default)
        {
            if (Fail is not null)
                throw Fail;
            log.Add("gate");
            Acquired++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class LoggingReads(IAdministratorViabilityService inner, List<string> log) : IAdministratorViabilityService
    {
        public ValueTask<AdministratorViabilityState> ReadAsync(CancellationToken cancellationToken = default)
        {
            log.Add("state read");
            return inner.ReadAsync(cancellationToken);
        }
    }

    /// <summary>A unit that runs the attempt and then fails before the commit, as an audit insert failing at the database would.</summary>
    private sealed class FailAfterAttempt(AuditRecording inner) : IAuditedUnitOfWork
    {
        public Task<T> ExecuteAsync<T>(
            AuditedOperation operation,
            Func<IAuditedTransaction, CancellationToken, Task<T>> attempt,
            Func<AuditCommittedOperation, CancellationToken, Task<T>> reconstruct,
            CancellationToken cancellationToken = default) =>
            inner.Unit.ExecuteAsync(
                operation,
                async (transaction, ct) =>
                {
                    await attempt(transaction, ct);
                    throw new InvalidOperationException("Injected: the audit insert failed.");
                },
                reconstruct,
                cancellationToken);
    }

    private sealed class RecordingUsers(List<string> log) : IUserRepository
    {
        private readonly Dictionary<string, User> _users = new(StringComparer.Ordinal);
        public void Seed(User user) => _users[user.Id.Value] = user;
        public User? Peek(string id) => _users.GetValueOrDefault(id);
        public ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_users.Count);
        public ValueTask<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default)
        {
            log.Add("user read");
            return ValueTask.FromResult(_users.GetValueOrDefault(id.Value));
        }

        public ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.Values.FirstOrDefault(u => u.IdentityLinks.Any(l => l.Source == source && l.ExternalId == externalId)));
        public ValueTask AddAsync(User user, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdateAsync(User user, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdateLastAuthenticatedAtAsync(IdentitySource source, string externalId, DateTimeOffset at, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default) => ValueTask.FromResult<(IReadOnlyList<User>, int)>(([], 0));
        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask CompleteEntraLinkProofAsync(string tenantId, string objectId, DateTimeOffset provenAt, string? registrationIdWhenMissing, string? providerSubject, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<User?> FindDesignatedEmergencyUserAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.Values.FirstOrDefault(u => u.IsBreakGlassEligible));
    }

    private sealed class RecordingCredentials(List<string> log) : ILocalCredentialRepository
    {
        public Dictionary<string, string> Hashes { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, bool> Disabled { get; } = new(StringComparer.Ordinal);

        public void Seed(string userId, string hash, bool disabled)
        {
            Hashes[userId] = hash;
            Disabled[userId] = disabled;
        }

        public LocalCredentialData? Peek(string userId) =>
            Hashes.TryGetValue(userId, out var hash) ? new LocalCredentialData(new UserId(userId), hash, "salt", "stub", Disabled[userId]) : null;

        public (Dictionary<string, string> Hashes, Dictionary<string, bool> Disabled) Snapshot() =>
            (new Dictionary<string, string>(Hashes, StringComparer.Ordinal), new Dictionary<string, bool>(Disabled, StringComparer.Ordinal));

        public void Restore((Dictionary<string, string> Hashes, Dictionary<string, bool> Disabled) snapshot)
        {
            Hashes.Clear();
            Disabled.Clear();
            foreach (var (key, value) in snapshot.Hashes)
                Hashes[key] = value;
            foreach (var (key, value) in snapshot.Disabled)
                Disabled[key] = value;
        }

        public ValueTask<LocalCredentialData?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default)
        {
            // A read before the gate is taken is the policy's own lock-free exit for "nothing to disable", outside the unit.
            log.Add(log.Contains("gate") ? "credential read" : "credential read (outside the unit)");
            return ValueTask.FromResult(Peek(userId.Value));
        }

        public ValueTask SetPasswordAsync(UserId userId, string passwordHash, string salt, string algorithm, CancellationToken cancellationToken = default)
        {
            Hashes[userId.Value] = passwordHash;
            return ValueTask.CompletedTask;
        }

        /// <summary>Runs when the disable is written: after the state before was read and before the state after is, where a concurrent writer's change lands.</summary>
        public Action? BeforeDisabledWrite { get; set; }

        public ValueTask SetDisabledAsync(UserId userId, bool disabled, CancellationToken cancellationToken = default)
        {
            log.Add("credential write");
            BeforeDisabledWrite?.Invoke();
            Disabled[userId.Value] = disabled;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingRoles : IRoleRepository
    {
        public List<UserId> UserIds { get; } = [];
        public ValueTask<Role?> GetAsync(RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.FromResult<Role?>(null);
        public ValueTask<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<Role>>([]);
        public ValueTask<IReadOnlyList<Role>> GetForUserAsync(UserId userId, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<Role>>([]);
        public ValueTask<int> CountUserAssignmentsAsync(RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.FromResult(UserIds.Count);
        public ValueTask<IReadOnlyList<UserId>> ListUserIdsWithRoleAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<UserId>>(UserIds);
        public ValueTask AssignToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RemoveFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdatePermissionsAsync(RoleId roleId, IReadOnlyList<PermissionDescriptor> permissions, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
