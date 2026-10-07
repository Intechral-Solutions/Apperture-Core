using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// AUD-1.2 (record D2, D22 to D24; clarification 2): the only places a person or an external identity is named explicitly as
/// the actor, instead of <c>AuditActor.CurrentPrincipal</c>. Each call site is a flow that has itself authenticated the person
/// (credentials, a one-time link, the sign-in handler's protected state) or validated the external identity. A new call site
/// fails this test until it is reviewed and added here with its reason.
/// </summary>
public sealed partial class AuditActorCallSiteTests
{
    private static readonly Dictionary<string, (int Count, string Reason)> AuthenticatedUserSites = new(StringComparer.Ordinal)
    {
        ["Apperture.ControlPlane/Apperture.Management.Api/Controllers/AuthController.cs"] =
            (1, "local, Recovery Access and break-glass sign-in, after this request verified the credentials (and MFA)"),
        ["Apperture.ControlPlane/Apperture.Management.Host/EntraOpenIdConnectOptionsConfigurer.cs"] =
            (2, "Microsoft sign-in success for the user the callback resolved; step-up refusals for the initiating user in the protected state"),
        ["Apperture.ControlPlane/Apperture.ControlPlane.Security/AccountInvitationService.cs"] =
            (1, "setup completion by the holder of an active one-time link, checked by this flow"),
        ["Apperture.ControlPlane/Apperture.ControlPlane.Security/AccountContactService.cs"] =
            (1, "contact verification by the holder of an active one-time link, checked by this flow"),
        ["Apperture.ControlPlane/Apperture.ControlPlane.Security/ForgottenPasswordService.cs"] =
            (1, "password reset by the holder of an active one-time link, checked by this flow"),
        ["Apperture.ControlPlane/Apperture.ControlPlane.Security/EntraStepUpService.cs"] =
            (1, "step-up for the initiating user in the sign-in handler's protected state"),
        ["Apperture.ControlPlane/Apperture.ControlPlane.Security/EntraIdentityAssociationService.cs"] =
            (2, "proof by the user whose sign-in resolved the link; self-link by the initiating user in the protected state"),
        ["Apperture.ControlPlane/Apperture.ControlPlane.Security/NativeMfaService.cs"] =
            (1, "a recovery code consumed by the user this sign-in is authenticating"),
    };

    private static readonly Dictionary<string, (int Count, string Reason)> ValidatedExternalSites = new(StringComparer.Ordinal)
    {
        ["Apperture.ControlPlane/Apperture.ControlPlane.Security/EntraIdentityAssociationService.cs"] =
            (1, "a validated token's tid and oid refused at sign-in (tenant mismatch, no link)"),
        ["Apperture.ControlPlane/Apperture.ControlPlane.Security/EntraAccessRequestService.cs"] =
            (1, "the access-request session, issued only after token validation, the tenant check and the no-link result"),
    };

    [Fact]
    public void An_explicit_user_actor_is_named_only_by_flows_that_authenticated_that_user()
    {
        Sites(AuthenticatedUserCall()).Should().BeEquivalentTo(
            AuthenticatedUserSites.ToDictionary(s => s.Key, s => s.Value.Count),
            "every AuditActor.AuthenticatedUser call site is reviewed: {0}",
            string.Join("; ", AuthenticatedUserSites.Select(s => $"{s.Key}: {s.Value.Reason}")));
    }

    [Fact]
    public void An_external_actor_is_named_only_from_a_validated_identity()
    {
        Sites(ValidatedExternalCall()).Should().BeEquivalentTo(
            ValidatedExternalSites.ToDictionary(s => s.Key, s => s.Value.Count),
            "every AuditActor.ValidatedExternalIdentity call site is reviewed: {0}",
            string.Join("; ", ValidatedExternalSites.Select(s => $"{s.Key}: {s.Value.Reason}")));
    }

    private static Dictionary<string, int> Sites(Regex call)
    {
        var src = Path.Combine(RepoRoot(), "src");
        var separator = Path.DirectorySeparatorChar;
        var sites = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (new[] { "obj", "bin" }.Any(skip => file.Contains($"{separator}{skip}{separator}", StringComparison.Ordinal)))
                continue;
            var count = call.Matches(File.ReadAllText(file)).Count;
            if (count > 0)
                sites[Path.GetRelativePath(src, file).Replace('\\', '/')] = count;
        }

        return sites;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [GeneratedRegex(@"AuditActor\.AuthenticatedUser\(", RegexOptions.CultureInvariant)]
    private static partial Regex AuthenticatedUserCall();

    [GeneratedRegex(@"AuditActor\.ValidatedExternalIdentity\(", RegexOptions.CultureInvariant)]
    private static partial Regex ValidatedExternalCall();
}
