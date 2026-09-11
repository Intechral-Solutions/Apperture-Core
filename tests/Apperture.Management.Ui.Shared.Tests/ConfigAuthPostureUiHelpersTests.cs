using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// NAV-1 stabilization (independent-review F-01 / 7A): the retired Dashboard auth-warnings
/// card's proactive "no viable sign-in" signal, restored on Config overview.
/// </summary>
public sealed class ConfigAuthPostureUiHelpersTests
{
    private static ConfigAuthOverviewDto Overview(
        string effectiveExperience = "DualVisible",
        IReadOnlyList<string>? warnings = null) =>
        new(
            LocalAuthEnabled: true,
            EntraEnabledInDeploymentConfiguration: true,
            EntraConfigurationValid: true,
            EntraMisconfiguredReason: null,
            ActiveEntraRegistrationId: null,
            ActiveEntraRegistrationDisplayName: null,
            EntraSignInUsesActiveRegistration: true,
            RuntimeMayRequireRestartToApplyChanges: false,
            RuntimeApplicationStatus: "applied",
            ShowLocalSignInInPrimaryChooser: true,
            PreferEntraInChooser: false,
            AutoRedirectToEntra: false,
            LocalShownOnPrimaryLogin: true,
            LocalFallbackOnly: false,
            EntraSignInEnabled: true,
            EffectiveExperience: effectiveExperience,
            EffectiveExperienceDescription: "desc",
            Warnings: warnings ?? []);

    [Fact]
    public void ResolveWarning_NullOverview_ReturnsNull()
    {
        // Operator lacks the read/manage capability -- GetConfigAuthOverviewAsync returns null
        // on 401/403 -- so nothing should be shown, not an error.
        ConfigAuthPostureUiHelpers.ResolveWarning(null).Should().BeNull();
    }

    [Fact]
    public void ResolveWarning_NoViableSignIn_ReturnsTheNoViablePathMessage()
    {
        var overview = Overview(effectiveExperience: "NoViableSignIn");
        ConfigAuthPostureUiHelpers.ResolveWarning(overview)
            .Should().Be(ConfigAuthPostureUiHelpers.NoViableSignInMessage);
    }

    [Fact]
    public void ResolveWarning_NoWarningsAndViableExperience_ReturnsNull()
    {
        var overview = Overview(effectiveExperience: "DualVisible", warnings: []);
        ConfigAuthPostureUiHelpers.ResolveWarning(overview).Should().BeNull();
    }

    [Fact]
    public void ResolveWarning_SingleWarning_ReturnsItVerbatim()
    {
        var overview = Overview(warnings: ["Entra client secret expires in 3 days."]);
        ConfigAuthPostureUiHelpers.ResolveWarning(overview)
            .Should().Be("Entra client secret expires in 3 days.");
    }

    [Fact]
    public void ResolveWarning_MultipleWarnings_ReturnsCount()
    {
        var overview = Overview(warnings: ["one", "two", "three"]);
        ConfigAuthPostureUiHelpers.ResolveWarning(overview)
            .Should().Be("3 authentication warning(s).");
    }

    [Fact]
    public void ResolveWarning_NoViableSignIn_TakesPrecedenceOverWarningsList()
    {
        var overview = Overview(effectiveExperience: "NoViableSignIn", warnings: ["ignored"]);
        ConfigAuthPostureUiHelpers.ResolveWarning(overview)
            .Should().Be(ConfigAuthPostureUiHelpers.NoViableSignInMessage);
    }
}
