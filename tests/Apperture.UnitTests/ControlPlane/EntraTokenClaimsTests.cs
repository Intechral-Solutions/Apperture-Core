using System.Security.Claims;
using Apperture.ControlPlane.Security;
using Apperture.Management.Host;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class EntraTokenClaimsTests
{
    private const string Oid = "33333333-3333-3333-3333-333333333333";
    private const string Tid = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public void Short_claim_names_are_read()
    {
        var principal = Principal((EntraTokenClaims.OidClaim, Oid), (EntraTokenClaims.TidClaim, Tid));

        EntraTokenClaims.TryGetObjectId(principal).Should().Be(Oid);
        EntraTokenClaims.TryGetTenantId(principal).Should().Be(Tid);
    }

    [Fact]
    public void Legacy_uri_claim_names_are_read()
    {
        var principal = Principal((EntraTokenClaims.OidClaimUri, Oid), (EntraTokenClaims.TidClaimUri, Tid));

        EntraTokenClaims.TryGetObjectId(principal).Should().Be(Oid);
        EntraTokenClaims.TryGetTenantId(principal).Should().Be(Tid);
    }

    [Fact]
    public void Non_guid_identifiers_are_refused()
    {
        var principal = Principal((EntraTokenClaims.OidClaim, "dana@contoso.com"), (EntraTokenClaims.TidClaim, "contoso.com"));

        EntraTokenClaims.TryGetObjectId(principal).Should().BeNull();
        EntraTokenClaims.TryGetTenantId(principal).Should().BeNull();
    }

    [Fact]
    public void Subject_is_available_for_diagnostics_only()
    {
        var principal = Principal((EntraTokenClaims.SubClaim, "sub-abc"));

        EntraTokenClaims.TryGetSubject(principal).Should().Be("sub-abc");
        EntraTokenClaims.TryGetObjectId(principal).Should().BeNull();
    }

    [Theory]
    [InlineData(Tid, Tid, true)]
    [InlineData("AAAAAAAA-1111-1111-1111-111111111111", "aaaaaaaa-1111-1111-1111-111111111111", true)]
    [InlineData(Tid, "22222222-2222-2222-2222-222222222222", false)]
    [InlineData(Tid, null, false)]
    [InlineData(null, Tid, false)]
    public void Tenant_match_requires_both_sides(string? token, string? expected, bool matches) =>
        EntraTokenClaims.TenantMatches(token, expected).Should().Be(matches);

    [Fact]
    public void Tenant_match_is_case_insensitive()
    {
        EntraTokenClaims.TenantMatches(Tid.ToUpperInvariant(), Tid).Should().BeTrue();
    }

    [Fact]
    public void Link_purpose_is_only_recognized_from_the_expected_item()
    {
        var linking = new Microsoft.AspNetCore.Authentication.AuthenticationProperties();
        linking.Items[EntraLinkFlowConstants.PurposeItemKey] = EntraLinkFlowConstants.LinkPurposeValue;
        linking.Items[EntraLinkFlowConstants.UserIdItemKey] = "usr-1";

        EntraLinkFlowConstants.IsLinkPurpose(linking).Should().BeTrue();
        EntraLinkFlowConstants.GetInitiatingUserId(linking).Should().Be("usr-1");
        EntraLinkFlowConstants.IsLinkPurpose(new Microsoft.AspNetCore.Authentication.AuthenticationProperties()).Should().BeFalse();
        EntraLinkFlowConstants.IsLinkPurpose(null).Should().BeFalse();
    }

    [Theory]
    [InlineData("/account/security", "/account/security")]
    [InlineData("https://evil.example/steal", null)]
    [InlineData("//evil.example", null)]
    [InlineData("account/security", null)]
    public void Only_same_site_relative_return_paths_are_accepted(string candidate, string? expected) =>
        ManagementUiRedirects.NormalizeRelativePath(candidate).Should().Be(expected);

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));
}
