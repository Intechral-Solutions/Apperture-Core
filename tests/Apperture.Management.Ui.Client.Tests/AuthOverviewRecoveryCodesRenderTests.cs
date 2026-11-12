using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Pages.Config.Auth;
using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Moq;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// IAM-10.2b WP5, A10.2b.12 (ruling C-14): renders the real authentication overview and checks that, when the designated Recovery Access
/// account's authenticator is enrolled and every recovery code has been used, the Emergency Access section says the codes are exhausted and
/// names the remedy, and that it says nothing of the kind while codes are left or before an authenticator is enrolled.
/// </summary>
public sealed class AuthOverviewRecoveryCodesRenderTests
{
    private const string Exhausted = "Recovery codes are exhausted";
    private const string Remedy = "Run 'emergency regenerate-codes'";

    [Fact]
    public async Task An_enrolled_recovery_access_account_with_no_codes_left_is_shown_as_exhausted_with_the_remedy()
    {
        var html = await RenderAsync(Posture(enrolled: true, unusedCodes: 0));

        html.Should().Contain("Recovery codes: 0 available");
        html.Should().Contain("Not healthy");
        html.Should().Contain(Exhausted).And.Contain("every code has been used").And.Contain(Remedy);
    }

    [Theory]
    [InlineData(true, 3)]
    [InlineData(false, 0)]
    public async Task Codes_left_or_no_authenticator_is_not_shown_as_exhausted(bool enrolled, int unusedCodes)
    {
        var html = await RenderAsync(Posture(enrolled, unusedCodes));

        html.Should().Contain($"Recovery codes: {unusedCodes} available");
        html.Should().NotContain(Exhausted).And.NotContain("regenerate-codes");
    }

    [Fact]
    public async Task No_designated_account_is_not_shown_as_exhausted()
    {
        var html = await RenderAsync(new EmergencyAccessPostureDto(
            IsHealthy: false,
            FallbackSafeguardRequired: true,
            DesignatedUserId: null,
            DesignatedDisplayName: null,
            DesignatedIsPlatformAdministrator: false,
            LocalCredentialEnabled: false,
            NativeMfaEnrolled: false,
            RecoveryCodesPresent: false,
            UnusedRecoveryCodeCount: 0));

        html.Should().Contain("Account: (none)");
        html.Should().NotContain(Exhausted);
    }

    private static EmergencyAccessPostureDto Posture(bool enrolled, int unusedCodes) =>
        new(
            IsHealthy: enrolled && unusedCodes > 0,
            FallbackSafeguardRequired: !(enrolled && unusedCodes > 0),
            DesignatedUserId: "usr-recovery",
            DesignatedDisplayName: "Recovery Access",
            DesignatedIsPlatformAdministrator: true,
            LocalCredentialEnabled: true,
            NativeMfaEnrolled: enrolled,
            RecoveryCodesPresent: unusedCodes > 0,
            UnusedRecoveryCodeCount: unusedCodes,
            DesignatedUsername: "recovery-access");

    private static ConfigAuthOverviewDto Overview() =>
        new(
            LocalAuthEnabled: true,
            EntraEnabledInDeploymentConfiguration: false,
            EntraConfigurationValid: false,
            EntraMisconfiguredReason: null,
            ActiveEntraRegistrationId: null,
            ActiveEntraRegistrationDisplayName: null,
            EntraSignInUsesActiveRegistration: false,
            RuntimeMayRequireRestartToApplyChanges: false,
            RuntimeApplicationStatus: "The running host uses the persisted configuration.",
            ShowLocalSignInInPrimaryChooser: true,
            PreferEntraInChooser: false,
            AutoRedirectToEntra: false,
            LocalShownOnPrimaryLogin: true,
            LocalFallbackOnly: false,
            EntraSignInEnabled: false,
            EffectiveExperience: "local",
            EffectiveExperienceDescription: "People sign in with a local account.",
            Warnings: []);

    private static async Task<string> RenderAsync(EmergencyAccessPostureDto posture)
    {
        var client = new Mock<IManagementClient>(MockBehavior.Loose);
        client.Setup(c => c.GetConfigAuthOverviewAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Overview());
        client.Setup(c => c.GetEmergencyAccessAsync(It.IsAny<CancellationToken>())).ReturnsAsync(posture);
        client.Setup(c => c.GetResourceContextsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ResourceContextDto>)
            [
                new(ResourceContextIds.SharedValue, "Shared", "shared", ResourceContextKinds.Shared, ResourceContextStates.Active, true, null, default, default)
            ]);

        var caps = new Mock<IOperatorCapabilitiesService>();
        caps.Setup(c => c.GetCapabilitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorCapabilitiesDto(false, false, false, false, false) with { CanViewAuthConfiguration = true });

        var nav = new TestNavigationManager("https://localhost/", "https://localhost/config/auth");
        var session = new SessionStateService(client.Object);
        var js = new FakeJsRuntime();
        var viewState = new ResourceContextViewState(js, nav, client.Object, session);
        await viewState.InitializeAsync();

        using var toasts = new ToastService();
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(client.Object)
            .AddSingleton(caps.Object)
            .AddSingleton<NavigationManager>(nav)
            .AddSingleton<IJSRuntime>(js)
            .AddSingleton(session)
            .AddSingleton(new ShellOrientationService())
            .AddSingleton<IToastService>(toasts)
            .AddSingleton(viewState)
            .BuildServiceProvider();

        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<AuthOverview>();
            await root.QuiescenceTask;
            return root.ToHtmlString();
        });

        // The renderer encodes text (an apostrophe becomes a character reference); the assertions read what a person reads.
        return System.Net.WebUtility.HtmlDecode(html);
    }
}
