using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class AuthSignInEffectiveExperienceTests
{
    [Fact]
    public void Resolve_NoViable_WhenBothDisabled()
    {
        var s = new AuthSignInOptionsSnapshot(
            LocalSignInEnabled: false,
            EntraSignInEnabled: false,
            EntraMisconfigured: false,
            ShowLocalSignInInPrimaryChooser: true,
            PreferEntraInChooser: false,
            AutoRedirectToEntra: false);

        AuthSignInEffectiveExperience.Resolve(s).Should().Be(EffectiveAuthExperienceKind.NoViableSignIn);
    }

    [Fact]
    public void Resolve_LocalOnly_WhenEntraOff()
    {
        var s = new AuthSignInOptionsSnapshot(
            LocalSignInEnabled: true,
            EntraSignInEnabled: false,
            EntraMisconfigured: false,
            ShowLocalSignInInPrimaryChooser: false,
            PreferEntraInChooser: false,
            AutoRedirectToEntra: false);

        AuthSignInEffectiveExperience.Resolve(s).Should().Be(EffectiveAuthExperienceKind.LocalOnly);
    }
}
