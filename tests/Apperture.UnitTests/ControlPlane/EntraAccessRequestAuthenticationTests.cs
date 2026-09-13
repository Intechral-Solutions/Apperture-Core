using System.Reflection;
using Apperture.ControlPlane.Security;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class EntraAccessRequestAuthenticationTests
{
    private const string Oid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string Tid = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public void TryRead_accepts_a_well_formed_limited_principal()
    {
        var principal = EntraAccessRequestAuthentication.CreatePrincipal(
            Tid, Oid, "reg-1", new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero), "Jane", "jane@contoso.com", "jane@contoso.com");

        var read = EntraAccessRequestAuthentication.TryRead(principal);

        read.Should().NotBeNull();
        read!.ObjectId.Should().Be(Oid);
        read.TenantId.Should().Be(Tid);
        read.RegistrationId.Should().Be("reg-1");
        read.DisplayNameSnapshot.Should().Be("Jane");
    }

    [Fact]
    public void TryRead_rejects_email_as_object_id()
    {
        var principal = EntraAccessRequestAuthentication.CreatePrincipal(
            Tid, "jane@contoso.com", "reg-1", DateTimeOffset.UtcNow, "Jane", null, null);

        EntraAccessRequestAuthentication.TryRead(principal).Should().BeNull();
    }

    [Fact]
    public void Access_request_api_uses_only_the_limited_scheme()
    {
        var authorize = typeof(AccessRequestController).GetCustomAttribute<AuthorizeAttribute>();
        authorize.Should().NotBeNull();
        authorize!.AuthenticationSchemes.Should().Be(EntraAccessRequestAuthentication.Scheme);

        typeof(UsersEntraProvisioningController).GetCustomAttribute<AuthorizeAttribute>()
            .Should().NotBeNull();
        typeof(UsersEntraProvisioningController).GetCustomAttribute<AuthorizeAttribute>()!
            .AuthenticationSchemes.Should().BeNullOrEmpty();
        typeof(UsersController).GetCustomAttribute<AuthorizeAttribute>()
            .Should().NotBeNull();
    }
}
