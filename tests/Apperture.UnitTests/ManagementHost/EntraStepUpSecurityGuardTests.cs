using System.Text.RegularExpressions;
using Apperture.ControlPlane.Security;
using Apperture.Management.Host;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Apperture.UnitTests.ManagementHost;

/// <summary>
/// SEC-1A2b structural pins. Behavior is proven in <see cref="EntraStepUpCallbackTests"/> and in the browser suite; these keep the
/// three design constraints from quietly eroding: the ordinary session cookie is never loosened to make step-up work, the
/// step-up callback never depends on the ambient session, and the Entra identity comparison stays tenant + object id only.
/// </summary>
public sealed class EntraStepUpSecurityGuardTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull("repository root not found from test output directory");
        return dir!.FullName;
    }

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private const string Host = "src/Apperture.ControlPlane/Apperture.Management.Host";

    // ---- the ordinary session cookie is not loosened ---------------------------------------------------------------------------

    [Fact]
    public void The_application_session_cookies_stay_SameSite_Lax()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAppertureAuthentication(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();

        options.Get(CookieAuthenticationDefaults.AuthenticationScheme).Cookie.SameSite.Should().Be(SameSiteMode.Lax);
        options.Get(EntraAccessRequestAuthentication.Scheme).Cookie.SameSite.Should().Be(SameSiteMode.Lax);
    }

    [Fact]
    public void No_application_source_sets_an_application_cookie_SameSite_None()
    {
        var offenders = Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "src", "Apperture.ControlPlane"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("SameSiteMode.None", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(RepoRoot(), f).Replace('\\', '/'))
            .ToArray();

        offenders.Should().BeEmpty("the session cookie is not made cross-site to fix step-up; the framework's own OIDC flow cookies are the only None cookies");
    }

    // ---- the callback does not depend on the ambient session -------------------------------------------------------------------

    [Fact]
    public void The_step_up_callback_never_reads_the_ambient_session_or_the_session_cookie()
    {
        var configurer = Source($"{Host}/EntraOpenIdConnectOptionsConfigurer.cs");
        var start = configurer.IndexOf("private async Task CompleteStepUpAsync", StringComparison.Ordinal);
        var end = configurer.IndexOf("private static async Task AppendStepUpAuditAsync", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        end.Should().BeGreaterThan(start);
        var callback = configurer[start..end];

        // Executable code only: the doc comment above the method names the constraint.
        callback.Should().NotContain("AuthenticateAsync(", "the Lax session cookie is absent on a cross-site POST; the callback cannot authenticate it");
        callback.Should().NotContain("HttpContext.User").And.NotContain("ctx.User");
        callback.Should().NotContain("Request.Cookies").And.NotContain("GetCurrentActorAsync").And.NotContain("IPrincipalContext");
        callback.Should().Contain("EntraLinkFlowConstants.GetInitiatingUserId(ctx.Properties)", "the initiating user comes from the protected OIDC properties");
        callback.Should().Contain("stepUp.CompleteAsync", "the returned identity is proven against the initiating user by the step-up service");
    }

    [Fact]
    public void The_continuation_travels_only_in_protected_oidc_authentication_properties()
    {
        var constants = Source($"{Host}/EntraStepUpFlowConstants.cs");
        constants.Should().Contain("OpenIdConnectChallengeProperties");
        constants.Should().NotContain("Cookies.Append").And.NotContain("Response.Cookies").And.NotContain("ISession").And.NotContain("HttpContext.Session").And.NotContain("localStorage");

        var endpoints = Source($"{Host}/ManagementAuthEndpoints.cs");
        var stepUp = endpoints[endpoints.IndexOf("private static async Task SignInStepUpAsync", StringComparison.Ordinal)..];
        stepUp.Should().NotContain("Response.Cookies").And.NotContain("QueryString");
    }

    // ---- the identity comparison is tenant + object id, never a name ----------------------------------------------------------

    [Fact]
    public void Step_up_identity_resolution_is_tenant_and_object_id_only()
    {
        var service = Source("src/Apperture.ControlPlane/Apperture.ControlPlane.Security/EntraStepUpService.cs");

        service.Should().Contain("ProviderIdentityKey.Entra(tokenTenantId!, objectId!)", "the identity key is Source + TenantId + ExternalId(oid)");
        service.Should().Contain("FindIdentityLinkAsync");
        foreach (var forbidden in new[] { "email", "upn", "UserPrincipalName", "preferred_username", "MailSnapshot", "FindByVerifiedContactEmail", "DisplayName" })
            Regex.IsMatch(service, forbidden, RegexOptions.IgnoreCase).Should().BeFalse($"'{forbidden}' is provenance, never an identity key");
        service.Should().NotContain("AddIdentityLink").And.NotContain("AddAsync", "step-up never links, relinks or provisions");

        var callback = Source($"{Host}/EntraOpenIdConnectOptionsConfigurer.cs");
        var stepUpPart = callback[callback.IndexOf("private async Task CompleteStepUpAsync", StringComparison.Ordinal)..];
        stepUpPart.Should().NotContain("AddIdentityLink").And.NotContain("CompleteLinkAsync").And.NotContain("CompleteSignInAsync");
    }

    [Fact]
    public void Local_sign_in_and_the_step_up_callback_share_one_local_session_identity_shape()
    {
        Source("src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/AuthController.cs")
            .Should().Contain("LocalSessionIdentity.Create(user.DisplayName, normalizedUsername)");
        Source($"{Host}/EntraOpenIdConnectOptionsConfigurer.cs")
            .Should().Contain("LocalSessionIdentity.Create(user.DisplayName, localLink.ExternalId)");
    }
}
