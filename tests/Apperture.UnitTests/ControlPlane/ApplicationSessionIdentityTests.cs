using System.Security.Claims;
using Apperture.ControlPlane.Security;
using Apperture.Core.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// The application session identity (IAM-9.2): every session is identified by the user id, marked with the session
/// format, and maps to an actor whose id is that user id. Provider identifiers are provenance only.
/// </summary>
public sealed class ApplicationSessionIdentityTests
{
    private static readonly UserId User = new("usr_session_test_0001");
    private const string Tenant = "5b1c2d3e-4f50-4a61-8b72-9c83d4e5f607";
    private const string Oid = "3d1b7e42-6a55-4f6e-9c0d-1e2f3a4b5c6d";

    [Fact]
    public void A_local_session_is_identified_by_user_id_and_carries_the_username_for_display_only()
    {
        var identity = LocalSessionIdentity.Create(User, "pat.lee", "Pat Lee");

        Single(identity, ClaimTypes.NameIdentifier).Should().Be(User.Value);
        Single(identity, ApplicationSessionClaims.SessionFormatClaimType).Should().Be("2");
        Single(identity, ApplicationSessionClaims.UsernameClaimType).Should().Be("pat.lee");
        Single(identity, ClaimTypes.Name).Should().Be("Pat Lee");
        Single(identity, ClaimsPrincipalToActorMapper.IdentitySourceClaimType).Should().Be("Local");
        identity.HasClaim(c => c.Type == ClaimsPrincipalToActorMapper.BreakGlassClaimType).Should().BeFalse();
        identity.AuthenticationType.Should().Be("Cookies");
    }

    [Fact]
    public void Applying_the_session_identity_replaces_any_existing_copy_of_its_claims()
    {
        var identity = new ClaimsIdentity("oidc");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "someone-else"));
        identity.AddClaim(new Claim(ClaimTypes.Name, "Token Name"));
        identity.AddClaim(new Claim(ApplicationSessionClaims.SessionFormatClaimType, "1"));
        identity.AddClaim(new Claim(ApplicationSessionClaims.UsernameClaimType, "admin"));
        identity.AddClaim(new Claim(ClaimsPrincipalToActorMapper.IdentitySourceClaimType, "Local"));
        identity.AddClaim(new Claim("oid", Oid));

        ApplicationSessionClaims.Apply(identity, User, "pat.lee", "Pat Lee", ApplicationSessionClaims.EntraSource);

        Single(identity, ClaimTypes.NameIdentifier).Should().Be(User.Value);
        Single(identity, ClaimTypes.Name).Should().Be("Pat Lee");
        Single(identity, ApplicationSessionClaims.SessionFormatClaimType).Should().Be("2");
        Single(identity, ApplicationSessionClaims.UsernameClaimType).Should().Be("pat.lee");
        Single(identity, ClaimsPrincipalToActorMapper.IdentitySourceClaimType).Should().Be("Entra");
        Single(identity, "oid").Should().Be(Oid, "provider provenance is kept");
    }

    [Fact]
    public void A_local_session_maps_to_a_human_actor_whose_id_is_the_user_id()
    {
        var actor = ClaimsPrincipalToActorMapper.ToActor(new ClaimsPrincipal(LocalSessionIdentity.Create(User, "pat.lee", "Pat Lee")));

        var human = actor.Should().BeOfType<HumanActor>().Subject;
        human.Id.Value.Should().Be(User.Value);
        human.DisplayName.Should().Be("Pat Lee");
        human.PreferredIdentitySource.Should().Be(IdentitySource.Local);
        human.IsBreakGlassSession.Should().BeFalse();
        human.TenantId.Should().BeNull();
        human.EntraObjectId.Should().BeNull();
    }

    [Fact]
    public void An_entra_session_maps_to_the_same_user_id_actor_with_its_link_provenance()
    {
        var actor = ClaimsPrincipalToActorMapper.ToActor(EntraSession());

        var human = actor.Should().BeOfType<HumanActor>().Subject;
        human.Id.Value.Should().Be(User.Value, "never the oid");
        human.PreferredIdentitySource.Should().Be(IdentitySource.Entra);
        human.TenantId.Should().Be(Tenant);
        human.EntraObjectId.Should().Be(Oid);
    }

    [Fact]
    public void Local_and_entra_sessions_of_one_user_produce_the_same_actor_id()
    {
        var local = ClaimsPrincipalToActorMapper.ToActor(new ClaimsPrincipal(LocalSessionIdentity.Create(User, "pat.lee", "Pat Lee")))!;
        var entra = ClaimsPrincipalToActorMapper.ToActor(EntraSession())!;

        entra.Id.Should().Be(local.Id);
        entra.Kind.Should().Be(local.Kind);
    }

    [Fact]
    public void The_break_glass_marker_is_preserved_on_the_actor()
    {
        var identity = LocalSessionIdentity.Create(User, "breakglass", "Recovery Access");
        identity.AddClaim(new Claim(ClaimsPrincipalToActorMapper.BreakGlassClaimType, "1"));

        var human = (HumanActor)ClaimsPrincipalToActorMapper.ToActor(new ClaimsPrincipal(identity))!;

        human.Id.Value.Should().Be(User.Value);
        human.IsBreakGlassSession.Should().BeTrue();
        human.PreferredIdentitySource.Should().Be(IdentitySource.Local);
    }

    [Fact]
    public void A_pre_upgrade_local_session_keyed_by_username_maps_to_no_actor()
    {
        var identity = new ClaimsIdentity("Cookies");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "pat.lee"));
        identity.AddClaim(new Claim(ClaimTypes.Name, "Pat Lee"));
        identity.AddClaim(new Claim(ClaimsPrincipalToActorMapper.IdentitySourceClaimType, "Local"));

        ClaimsPrincipalToActorMapper.ToActor(new ClaimsPrincipal(identity)).Should().BeNull();
    }

    [Fact]
    public void A_pre_upgrade_entra_session_keyed_by_oid_maps_to_no_actor()
    {
        var identity = new ClaimsIdentity("Cookies");
        identity.AddClaim(new Claim("oid", Oid));
        identity.AddClaim(new Claim("tid", Tenant));
        identity.AddClaim(new Claim("sub", "subject-1"));
        identity.AddClaim(new Claim("name", "Pat Lee"));
        identity.AddClaim(new Claim("preferred_username", "pat@contoso.com"));
        identity.AddClaim(new Claim(ClaimsPrincipalToActorMapper.IdentitySourceClaimType, "Entra"));

        ClaimsPrincipalToActorMapper.ToActor(new ClaimsPrincipal(identity)).Should().BeNull("no fallback to oid, sub, name or preferred_username");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1")]
    [InlineData("3")]
    [InlineData("")]
    public void Only_the_current_session_format_maps_to_an_actor(string? format)
    {
        var identity = LocalSessionIdentity.Create(User, "pat.lee", "Pat Lee");
        foreach (var claim in identity.FindAll(ApplicationSessionClaims.SessionFormatClaimType).ToList())
            identity.RemoveClaim(claim);
        if (format is not null)
            identity.AddClaim(new Claim(ApplicationSessionClaims.SessionFormatClaimType, format));

        ClaimsPrincipalToActorMapper.ToActor(new ClaimsPrincipal(identity)).Should().BeNull();
    }

    [Fact]
    public void A_current_format_session_without_a_user_id_maps_to_no_actor_and_never_falls_back_to_other_names()
    {
        var identity = LocalSessionIdentity.Create(User, "pat.lee", "Pat Lee");
        identity.RemoveClaim(identity.FindFirst(ClaimTypes.NameIdentifier));
        identity.AddClaim(new Claim("oid", Oid));
        identity.AddClaim(new Claim("sub", "subject-1"));
        identity.AddClaim(new Claim("preferred_username", "pat@contoso.com"));

        ClaimsPrincipalToActorMapper.ToActor(new ClaimsPrincipal(identity)).Should().BeNull(
            "the username claim, the display name, oid, sub and preferred_username are never the actor id");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Unknown")]
    public void A_session_without_a_recognised_identity_source_maps_to_no_actor(string? source)
    {
        var identity = LocalSessionIdentity.Create(User, "pat.lee", "Pat Lee");
        identity.RemoveClaim(identity.FindFirst(ClaimsPrincipalToActorMapper.IdentitySourceClaimType));
        if (source is not null)
            identity.AddClaim(new Claim(ClaimsPrincipalToActorMapper.IdentitySourceClaimType, source));

        ClaimsPrincipalToActorMapper.ToActor(new ClaimsPrincipal(identity)).Should().BeNull();
    }

    [Fact]
    public void An_unauthenticated_principal_maps_to_no_actor()
    {
        var unauthenticated = new ClaimsIdentity();
        ApplicationSessionClaims.Apply(unauthenticated, User, "pat.lee", "Pat Lee", ApplicationSessionClaims.LocalSource);

        ClaimsPrincipalToActorMapper.ToActor(new ClaimsPrincipal(unauthenticated)).Should().BeNull();
        ClaimsPrincipalToActorMapper.ToActor(null).Should().BeNull();
    }

    [Fact]
    public void The_entra_access_request_principal_is_not_an_application_session_and_maps_to_no_actor()
    {
        var accessRequest = EntraAccessRequestAuthentication.CreatePrincipal(Tenant, Oid, "reg-1", DateTimeOffset.UtcNow, "Pat", "pat@contoso.com", null);

        ClaimsPrincipalToActorMapper.ToActor(accessRequest).Should().BeNull("it is not a platform user and carries no session format");
        ApplicationSessionClaims.IsCurrentFormat(accessRequest).Should().BeFalse();
    }

    [Fact]
    public void The_token_subject_is_read_from_sub_only_never_from_the_session_user_id()
    {
        var principal = EntraSession();

        EntraTokenClaims.TryGetSubject(principal).Should().BeNull("this session has no sub; NameIdentifier is the user id, not a subject");

        var withSub = EntraSession();
        ((ClaimsIdentity)withSub.Identity!).AddClaim(new Claim("sub", "subject-1"));
        EntraTokenClaims.TryGetSubject(withSub).Should().Be("subject-1");
    }

    private static ClaimsPrincipal EntraSession()
    {
        var identity = new ClaimsIdentity("AuthenticationTypes.Federation");
        identity.AddClaim(new Claim("oid", Oid));
        identity.AddClaim(new Claim("tid", Tenant));
        identity.AddClaim(new Claim("name", "Pat Lee"));
        ApplicationSessionClaims.Apply(identity, User, "pat.lee", "Pat Lee", ApplicationSessionClaims.EntraSource);
        return new ClaimsPrincipal(identity);
    }

    private static string Single(ClaimsIdentity identity, string type) =>
        identity.FindAll(type).Should().ContainSingle().Subject.Value;
}
