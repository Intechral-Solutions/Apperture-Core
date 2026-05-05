using Apperture.ControlPlane.Security;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class EntraAuthorityHelperTests
{
    [Fact]
    public void TryGetTenantSegmentFromAuthority_ReturnsGuidSegment()
    {
        EntraAuthorityHelper.TryGetTenantSegmentFromAuthority(
                "https://login.microsoftonline.com/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")
            .Should()
            .Be("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    }

    [Fact]
    public void IssuerMatchesAzureTenantId_WhenGuidMatchesIssuer_ReturnsTrue()
    {
        var issuer = "https://login.microsoftonline.com/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/v2.0";
        EntraAuthorityHelper.IssuerMatchesAzureTenantId(issuer, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void IssuerMatchesAzureTenantId_WhenAzureTenantIdNotGuid_SkipsStrictCheck()
    {
        EntraAuthorityHelper.IssuerMatchesAzureTenantId("https://wrong/v2.0", "not-a-guid").Should().BeTrue();
    }
}
