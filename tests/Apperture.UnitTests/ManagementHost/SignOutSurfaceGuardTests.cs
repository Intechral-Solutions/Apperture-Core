using System.Reflection;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Client;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Apperture.UnitTests.ManagementHost;

/// <summary>
/// Sign-out has one path: the UI's form POST to the host's <c>/signout</c> (design gate D2). The access-request session keeps its
/// own <c>POST access-requests/sign-out</c> under Rule U. No other API action or client call ends a session.
/// </summary>
public sealed class SignOutSurfaceGuardTests
{
    [Fact]
    public void No_api_action_signs_out_the_application_session()
    {
        var templates = typeof(AuthController).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>().Select(attribute => attribute.Template))
            .Where(template => template is not null)
            .ToArray();

        templates.Should().NotContain(t => t!.Contains("logout", StringComparison.OrdinalIgnoreCase));
        templates.Where(t => t!.Contains("sign-out", StringComparison.OrdinalIgnoreCase)).Should().Equal("sign-out");
        typeof(AccessRequestController).GetMethod(nameof(AccessRequestController.SignOutSession)).Should().NotBeNull();
    }

    [Fact]
    public void The_client_has_no_local_logout_call()
    {
        typeof(IManagementClient).GetMethods().Select(m => m.Name).Should().NotContain(n => n.Contains("Logout", StringComparison.Ordinal));
    }
}
