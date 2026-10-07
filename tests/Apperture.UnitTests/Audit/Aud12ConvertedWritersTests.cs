using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// AUD-1.2 (gate §22, §13.4): the identity and governance writers it converted write only through the recorder. None of
/// them reaches the append bridge again; the one remaining bridge use in the family is the sign-in failure rows that
/// IAM-11.4 owns.
/// </summary>
public sealed partial class Aud12ConvertedWritersTests
{
    private static readonly string[] ConvertedFiles =
    [
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/UserManagementService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/EmergencyAccessService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/NativeMfaService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/AccountInvitationService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/AccountContactService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/ForgottenPasswordService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/AccountProfileService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/UserProvisioningService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/EntraAccessRequestService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/EntraIdentityAssociationService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/EntraPrimaryAuthenticationPolicy.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/EntraStepUpService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/SqlRuntimeEnrollmentService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/FirstAdminBootstrapService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/RealBootstrapRecoveryService.cs",
        "Apperture.ControlPlane/Apperture.Management.Api/Controllers/EnrollController.cs",
        "Apperture.ControlPlane/Apperture.Management.Api/Controllers/BootstrapController.cs",
        "Apperture.ControlPlane/Apperture.Management.Api/Controllers/ConfigEntraTenantsController.cs",
        "Apperture.ControlPlane/Apperture.Management.Api/Controllers/ConfigAuthUiPolicyController.cs",
        "Apperture.ControlPlane/Apperture.Management.Api/Controllers/MeNotificationSubscriptionsController.cs",
        "Apperture.ControlPlane/Apperture.Management.Host/EntraOpenIdConnectOptionsConfigurer.cs",
        "Apperture.ControlPlane/Apperture.Management.Host/ManagementAuthEndpoints.cs",
        "Apperture.ControlPlane/Apperture.Management.Host/EntraRemoteFailure.cs",
        "Apperture.ControlPlane/Apperture.Management.Host/EntraSignOut.cs",
    ];

    /// <summary>The sign-in failure rows IAM-11.4 owns, which AuthController still writes through the bridge.</summary>
    private static readonly string[] AuthControllerBridgeActions =
    [
        "AuthActionLocalLoginFailed",
        "AuthActionUnlinkedPrincipal",
        "AuthActionBreakGlassLoginFailed",
        "AuthActionBreakGlassRejectedDisabled",
        "EmergencyAccessAuditActionNames.LoginFailed",
    ];

    [Fact]
    public void Converted_writers_never_use_the_append_bridge()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var offenders = ConvertedFiles
            .Select(f => (File: f, Text: File.ReadAllText(Path.Combine(src, f))))
            .Where(f => f.Text.Contains("IAuditService", StringComparison.Ordinal)
                || f.Text.Contains("new AuditRecord(", StringComparison.Ordinal))
            .Select(f => f.File);

        offenders.Should().BeEmpty("a converted writer records through the audited unit of work or the event writer only");
    }

    [Fact]
    public void The_authentication_controller_uses_the_bridge_only_for_the_sign_in_failures_iam_11_4_owns()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Apperture.ControlPlane/Apperture.Management.Api/Controllers/AuthController.cs"));

        var bridged = BridgeCall().Matches(text)
            .Select(m => m.Groups["action"].Value.Trim())
            .Where(a => !a.StartsWith("string ", StringComparison.Ordinal)) // the helper's own declaration
            .ToList();

        bridged.Should().NotBeEmpty();
        bridged.SelectMany(a => a.Split(':', '?').Select(p => p.Trim()).Where(p => p.Length > 0 && !p.StartsWith("user.", StringComparison.Ordinal)))
            .Should().OnlyContain(a => AuthControllerBridgeActions.Contains(a), "sign-in successes and password changes record through the recorder");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>The first argument of each <c>AppendAuthAuditAsync(</c> call.</summary>
    [GeneratedRegex(@"AppendAuthAuditAsync\(\s*(?<action>[^,]+),", RegexOptions.CultureInvariant)]
    private static partial Regex BridgeCall();
}
