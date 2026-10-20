using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using FluentAssertions;
using Xunit;
using static Apperture.UnitTests.AdministratorViability.ViabilityTestData;

namespace Apperture.UnitTests.AdministratorViability;

/// <summary>
/// The definitions of a viable ordinary administrator (VOA) and an Entra-independent administrator (EI) (IAM-10 gate §3.2, rulings
/// D-3 and D-4; A10.2b.3, A10.2b.4): viability is the ability to sign in, not a role-assignment count.
/// </summary>
public sealed class AdministratorViabilityEvaluatorTests
{
    private static AdministratorViabilitySnapshot Evaluate(SignInAvailability availability, params AdministratorFacts[] admins) =>
        AdministratorViabilityEvaluator.Evaluate(admins, availability);

    [Fact]
    public void A_user_with_an_enabled_local_credential_while_local_sign_in_is_enabled_is_viable_and_entra_independent()
    {
        var snapshot = Evaluate(Availability(), Admin("a", local: true));

        snapshot.ViableOrdinaryAdministrators.Should().BeEquivalentTo(["a"]);
        snapshot.EntraIndependentAdministrators.Should().BeEquivalentTo(["a"]);
    }

    [Fact]
    public void An_administrator_with_a_disabled_credential_and_no_proven_entra_link_is_assigned_but_not_viable()
    {
        var disabled = new AdministratorFacts("a", false, HasLocalLink: true, LocalCredentialEnabled: false, []);

        var snapshot = Evaluate(Availability(), disabled);

        snapshot.ViableOrdinaryAdministrators.Should().BeEmpty("a disabled credential and no proven link is no way to sign in");
        snapshot.EntraIndependentAdministrators.Should().BeEmpty();
    }

    [Fact]
    public void A_local_credential_without_a_local_link_is_not_a_sign_in_method()
    {
        var noLink = new AdministratorFacts("a", false, HasLocalLink: false, LocalCredentialEnabled: true, []);

        Evaluate(Availability(), noLink).ViableOrdinaryAdministrators.Should().BeEmpty();
    }

    [Fact]
    public void Local_sign_in_being_disabled_removes_every_local_method_and_every_entra_independent_administrator()
    {
        var snapshot = Evaluate(Availability(local: false), Admin("a", local: true), Admin("b", local: true, entraTenant: true));

        snapshot.EntraIndependentAdministrators.Should().BeEmpty("without Local sign-in nobody is independent of Entra");
        snapshot.ViableOrdinaryAdministrators.Should().BeEquivalentTo(["b"], "b still has a proven Entra link");
    }

    [Fact]
    public void A_proven_entra_link_counts_only_when_its_tenant_is_the_tenant_the_authentication_accepts()
    {
        var snapshot = Evaluate(
            Availability(local: false),
            Admin("same", entraTenant: true, tenant: Tenant),
            Admin("other", entraTenant: true, tenant: OtherTenant),
            Admin("none", entraTenant: true, tenant: null));

        snapshot.ViableOrdinaryAdministrators.Should().BeEquivalentTo(["same"]);
    }

    [Fact]
    public void The_tenant_comparison_is_the_sign_ins_own_comparison_trimmed_and_case_insensitive()
    {
        var snapshot = Evaluate(
            Availability(local: false, tenant: Tenant),
            Admin("a", entraTenant: true, tenant: "  " + Tenant.ToUpperInvariant() + " "));

        snapshot.ViableOrdinaryAdministrators.Should().BeEquivalentTo(["a"]);
    }

    [Fact]
    public void When_no_tenant_is_accepted_no_entra_link_counts()
    {
        var snapshot = Evaluate(Availability(local: false, tenant: null), Admin("a", entraTenant: true));

        snapshot.ViableOrdinaryAdministrators.Should().BeEmpty();
    }

    [Fact]
    public void When_entra_sign_in_is_not_usable_no_entra_link_counts()
    {
        var snapshot = Evaluate(Availability(local: false, entra: false), Admin("a", entraTenant: true));

        snapshot.ViableOrdinaryAdministrators.Should().BeEmpty();
    }

    [Fact]
    public void A_viable_entra_only_administrator_is_viable_but_not_entra_independent()
    {
        var snapshot = Evaluate(Availability(), Admin("a", entraTenant: true));

        snapshot.ViableOrdinaryAdministrators.Should().BeEquivalentTo(["a"]);
        snapshot.EntraIndependentAdministrators.Should().BeEmpty();
    }

    [Fact]
    public void Recovery_access_is_never_counted_in_either_set_even_though_it_holds_the_role_and_can_sign_in()
    {
        var recovery = Admin("recovery", local: true, entraTenant: true, recoveryAccess: true);

        var snapshot = Evaluate(Availability(), recovery, Admin("ordinary", entraTenant: true));

        snapshot.ViableOrdinaryAdministrators.Should().BeEquivalentTo(["ordinary"]);
        snapshot.EntraIndependentAdministrators.Should().BeEmpty("Recovery Access is not an ordinary Entra-independent administrator");
    }

    [Fact]
    public void Entra_independent_administrators_are_always_a_subset_of_the_viable_ones()
    {
        var admins = new[]
        {
            Admin("a", local: true),
            Admin("b", entraTenant: true),
            Admin("c", local: true, entraTenant: true),
            Admin("d"),
            Admin("e", local: true, recoveryAccess: true),
        };

        foreach (var local in new[] { true, false })
        foreach (var entra in new[] { true, false })
        foreach (var tenant in new string?[] { Tenant, null })
        {
            var snapshot = Evaluate(Availability(local: local, entra: entra, tenant: tenant), admins);
            snapshot.EntraIndependentAdministrators.Should().BeSubsetOf(snapshot.ViableOrdinaryAdministrators);
        }
    }

    [Fact]
    public void The_snapshot_reports_the_view_it_was_evaluated_under()
    {
        Evaluate(Availability(ViabilityView.Restart), Admin("a", local: true)).View.Should().Be(ViabilityView.Restart);
    }
}
