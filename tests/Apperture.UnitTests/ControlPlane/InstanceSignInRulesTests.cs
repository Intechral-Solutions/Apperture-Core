using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// IAM-10.2b WP3: the instance-level sign-in rules (some sign-in path remains; the chooser policy stays satisfiable) stated over
/// the two facts they have always read, whether Local sign-in is enabled and whether Entra sign-in is usable, so that they can be
/// asked of the persisted configuration a change has just applied instead of a hand-built simulation of it.
/// </summary>
public sealed class InstanceSignInRulesTests
{
    private static InstanceAuthUiPolicy Policy(bool showLocal = true, bool autoRedirect = false) =>
        new() { ShowLocalSignInInPrimaryChooser = showLocal, PreferEntraInChooser = false, AutoRedirectToEntra = autoRedirect };

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_path_remains_when_either_method_is_usable(bool local, bool entra) =>
        AuthenticationConfigurationGuard.ValidateNoLockout(local, entra, Policy()).Should().BeNull();

    [Fact]
    public void No_path_remains_when_neither_method_is_usable() =>
        AuthenticationConfigurationGuard.ValidateNoLockout(localSignInEnabled: false, entraSignInUsable: false, Policy())
            .Should().Be("That change would remove every viable sign-in path for this instance.");

    [Fact]
    public void Hiding_local_sign_in_needs_a_usable_entra() =>
        AuthenticationConfigurationGuard.ValidateNoLockout(localSignInEnabled: true, entraSignInUsable: false, Policy(showLocal: false))
            .Should().StartWith("Cannot hide local sign-in");

    [Fact]
    public void Auto_redirect_needs_a_usable_entra() =>
        AuthenticationConfigurationGuard.ValidateNoLockout(localSignInEnabled: true, entraSignInUsable: false, Policy(autoRedirect: true))
            .Should().StartWith("Auto-redirect to Entra requires");

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    public void The_options_form_and_the_availability_form_give_the_same_answer(bool local, bool entra, bool showLocalHidden)
    {
        var policy = Policy(showLocal: !showLocalHidden);
        var options = new EntraAuthOptions
        {
            Enabled = entra,
            Authority = "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0",
            ClientId = "client",
            ClientSecret = "secret",
            AzureTenantId = "11111111-1111-1111-1111-111111111111",
        };

        var viaOptions = AuthenticationConfigurationGuard.ValidateNoLockout(new LocalAuthOptions { Enabled = local }, policy, options);
        var viaFacts = AuthenticationConfigurationGuard.ValidateNoLockout(local, entra, policy);

        viaFacts.Should().Be(viaOptions);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData(null, "violated", true)]
    [InlineData("violated", "violated", false)]
    [InlineData("violated", null, false)]
    public void A_change_newly_violates_a_rule_only_when_it_was_satisfied_before_and_is_not_after(string? before, string? after, bool newlyViolates) =>
        AuthenticationConfigurationGuard.NewlyViolates(before, after).Should().Be(newlyViolates);

    [Fact]
    public void Disabling_the_only_usable_method_of_a_local_off_instance_newly_violates_the_rule()
    {
        var before = AuthenticationConfigurationGuard.ValidateNoLockout(localSignInEnabled: false, entraSignInUsable: true, Policy());
        var after = AuthenticationConfigurationGuard.ValidateNoLockout(localSignInEnabled: false, entraSignInUsable: false, Policy());

        AuthenticationConfigurationGuard.NewlyViolates(before, after).Should().BeTrue();
    }

    [Fact]
    public void An_instance_that_already_has_no_path_does_not_make_an_unrelated_change_a_violation()
    {
        var before = AuthenticationConfigurationGuard.ValidateNoLockout(false, false, Policy());
        var after = AuthenticationConfigurationGuard.ValidateNoLockout(false, false, Policy());

        before.Should().NotBeNull();
        AuthenticationConfigurationGuard.NewlyViolates(before, after).Should().BeFalse();
    }

    [Fact]
    public void A_change_that_repairs_is_never_a_violation()
    {
        var before = AuthenticationConfigurationGuard.ValidateNoLockout(false, false, Policy());
        var after = AuthenticationConfigurationGuard.ValidateNoLockout(false, true, Policy());

        AuthenticationConfigurationGuard.NewlyViolates(before, after).Should().BeFalse();
    }
}
