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
    public void TenantSegmentLooksLikeMalformedGuid_WhenExtraCharacterOnTenantId_IsTrue()
    {
        EntraAuthorityHelper.TenantSegmentLooksLikeMalformedGuid(
                "https://login.microsoftonline.com/f47559c4-15e1-4df2-8bf2-a31cd60330c6a")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void AuthorityTenantMatchesAzureTenantId_WhenSegmentHasExtraCharacter_IsFalse()
    {
        EntraAuthorityHelper.AuthorityTenantMatchesAzureTenantId(
                "https://login.microsoftonline.com/f47559c4-15e1-4df2-8bf2-a31cd60330c6a",
                "f47559c4-15e1-4df2-8bf2-a31cd60330c6")
            .Should()
            .BeFalse();
    }

    [Fact]
    public void ToMicrosoftIdentityPlatformAuthority_WhenTenantGuidWithoutV2_AppendsV2()
    {
        EntraAuthorityHelper.ToMicrosoftIdentityPlatformAuthority(
                "https://login.microsoftonline.com/f47559c4-15e1-4df2-8bf2-a31cd60330c6",
                azureTenantId: null)
            .Should()
            .Be("https://login.microsoftonline.com/f47559c4-15e1-4df2-8bf2-a31cd60330c6/v2.0");
    }

    [Fact]
    public void ToMicrosoftIdentityPlatformAuthority_WhenCommonAndAzureTenantId_UsesTenantV2()
    {
        EntraAuthorityHelper.ToMicrosoftIdentityPlatformAuthority(
                "https://login.microsoftonline.com/common",
                "f47559c4-15e1-4df2-8bf2-a31cd60330c6")
            .Should()
            .Be("https://login.microsoftonline.com/f47559c4-15e1-4df2-8bf2-a31cd60330c6/v2.0");
    }

    [Fact]
    public void AuthorityTenantMatchesAzureTenantId_WhenSegmentMatches_IsTrue()
    {
        EntraAuthorityHelper.AuthorityTenantMatchesAzureTenantId(
                "https://login.microsoftonline.com/f47559c4-15e1-4df2-8bf2-a31cd60330c6",
                "f47559c4-15e1-4df2-8bf2-a31cd60330c6")
            .Should()
            .BeTrue();
    }
}
