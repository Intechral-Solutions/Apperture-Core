using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// A session resolves by user id, then must still be bound to the identity link it was issued through: the exact
/// tenant id + object id link for Entra, any Local link for a local session. Only that link is touched.
/// </summary>
public sealed class SqlPrincipalToUserResolverTests
{
    private const string TenantA = "11111111-1111-1111-1111-111111111111";
    private const string TenantB = "22222222-2222-2222-2222-222222222222";
    private const string Oid = "33333333-3333-3333-3333-333333333333";
    private const string OtherOid = "44444444-4444-4444-4444-444444444444";

    private static readonly UserId UserOne = new("user-1");

    [Fact]
    public async Task Local_session_resolves_by_user_id_and_touches_the_local_link()
    {
        var repo = RepositoryWith(User(new IdentityLink(IdentitySource.Local, "pat", null), new IdentityLink(IdentitySource.Entra, Oid, null, TenantA)));

        var resolved = await new SqlPrincipalToUserResolver(repo.Object).ResolveUserIdAsync(LocalActor());

        resolved.Should().Be(UserOne);
        repo.Verify(r => r.UpdateLastAuthenticatedAtAsync(IdentitySource.Local, "pat", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.UpdateLastAuthenticatedAtAsync(It.IsAny<ProviderIdentityKey>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNoLinkLookup(repo);
    }

    [Fact]
    public async Task Local_session_survives_a_username_change_because_binding_is_by_link_existence()
    {
        // The Local link now carries a new name (it mirrors the username); the session never held the name.
        var repo = RepositoryWith(User(new IdentityLink(IdentitySource.Local, "pat.renamed", null)));

        var resolved = await new SqlPrincipalToUserResolver(repo.Object).ResolveUserIdAsync(LocalActor());

        resolved.Should().Be(UserOne);
        repo.Verify(r => r.UpdateLastAuthenticatedAtAsync(IdentitySource.Local, "pat.renamed", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Local_session_fails_when_the_user_no_longer_has_a_local_link()
    {
        var repo = RepositoryWith(User(new IdentityLink(IdentitySource.Entra, Oid, null, TenantA)));

        var resolved = await new SqlPrincipalToUserResolver(repo.Object).ResolveUserIdAsync(LocalActor());

        resolved.Should().BeNull();
        VerifyNothingTouched(repo);
    }

    [Fact]
    public async Task Entra_session_resolves_by_user_id_and_touches_the_exact_tenant_and_object_id_link()
    {
        var repo = RepositoryWith(User(new IdentityLink(IdentitySource.Local, "pat", null), new IdentityLink(IdentitySource.Entra, Oid, null, TenantA)));

        var resolved = await new SqlPrincipalToUserResolver(repo.Object).ResolveUserIdAsync(EntraActor(TenantA, Oid));

        resolved.Should().Be(UserOne);
        repo.Verify(
            r => r.UpdateLastAuthenticatedAtAsync(
                It.Is<ProviderIdentityKey>(k => k.Source == IdentitySource.Entra && k.TenantId == TenantA && k.ExternalId == Oid),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        repo.Verify(r => r.UpdateLastAuthenticatedAtAsync(IdentitySource.Local, It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNoLinkLookup(repo);
    }

    [Theory]
    [InlineData(TenantB, Oid)] // same oid in another tenant
    [InlineData(TenantA, OtherOid)] // another oid in the same tenant
    public async Task Entra_session_fails_when_the_users_link_does_not_match_the_sessions_tenant_and_object_id(string tenantId, string objectId)
    {
        var repo = RepositoryWith(User(new IdentityLink(IdentitySource.Local, "pat", null), new IdentityLink(IdentitySource.Entra, Oid, null, TenantA)));

        var resolved = await new SqlPrincipalToUserResolver(repo.Object).ResolveUserIdAsync(EntraActor(tenantId, objectId));

        resolved.Should().BeNull("a local link does not stand in for the Entra link the session came through");
        VerifyNothingTouched(repo);
    }

    [Fact]
    public async Task Entra_session_fails_when_its_link_now_belongs_to_another_user()
    {
        // The session's user no longer has the link; whoever holds it now is irrelevant, because links are read
        // only from the session's own user.
        var repo = RepositoryWith(User(new IdentityLink(IdentitySource.Local, "pat", null)));

        var resolved = await new SqlPrincipalToUserResolver(repo.Object).ResolveUserIdAsync(EntraActor(TenantA, Oid));

        resolved.Should().BeNull();
        VerifyNothingTouched(repo);
        VerifyNoLinkLookup(repo);
    }

    [Fact]
    public async Task Entra_session_without_link_provenance_fails()
    {
        var repo = RepositoryWith(User(new IdentityLink(IdentitySource.Entra, Oid, null, TenantA)));

        var resolved = await new SqlPrincipalToUserResolver(repo.Object)
            .ResolveUserIdAsync(new HumanActor(new ActorId(UserOne.Value), "Pat", IdentitySource.Entra, tenantId: TenantA));

        resolved.Should().BeNull();
        VerifyNothingTouched(repo);
    }

    [Fact]
    public async Task Pending_entra_link_applies_the_entra_primary_policy_after_binding()
    {
        // A null last-authenticated time is a pending (never proven) link.
        var repo = RepositoryWith(User(new IdentityLink(IdentitySource.Entra, Oid, LastAuthenticatedAt: null, TenantA)));
        var policy = new Mock<IEntraPrimaryAuthenticationPolicy>();

        var resolved = await new SqlPrincipalToUserResolver(repo.Object, policy.Object).ResolveUserIdAsync(EntraActor(TenantA, Oid));

        resolved.Should().Be(UserOne);
        policy.Verify(p => p.ApplyAfterEntraProvenAsync(UserOne, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Missing_user_fails_and_touches_nothing()
    {
        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var resolved = await new SqlPrincipalToUserResolver(repo.Object).ResolveUserIdAsync(LocalActor());

        resolved.Should().BeNull();
        VerifyNothingTouched(repo);
    }

    [Fact]
    public async Task Actor_without_an_identity_source_is_not_resolved_by_any_legacy_fallback()
    {
        // Before the user-id session, a source-less id was looked up as a Local username. That fallback is gone.
        var repo = RepositoryWith(User(new IdentityLink(IdentitySource.Local, "user-1", null)));

        var resolved = await new SqlPrincipalToUserResolver(repo.Object).ResolveUserIdAsync(new HumanActor(new ActorId(UserOne.Value)));

        resolved.Should().BeNull();
        repo.Verify(r => r.GetByIdAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNoLinkLookup(repo);
        VerifyNothingTouched(repo);
    }

    [Fact]
    public async Task Non_human_actor_is_never_resolved_to_a_user()
    {
        var repo = new Mock<IUserRepository>();
        var system = new Mock<IActor>();
        system.SetupGet(a => a.Kind).Returns(ActorKind.System);
        system.SetupGet(a => a.Id).Returns(new ActorId("system"));

        var resolved = await new SqlPrincipalToUserResolver(repo.Object).ResolveUserIdAsync(system.Object);

        resolved.Should().BeNull();
        repo.VerifyNoOtherCalls();
    }

    private static HumanActor LocalActor() =>
        new(new ActorId(UserOne.Value), "Pat", IdentitySource.Local);

    private static HumanActor EntraActor(string tenantId, string objectId) =>
        new(new ActorId(UserOne.Value), "Pat", IdentitySource.Entra, tenantId: tenantId, entraObjectId: objectId);

    private static User User(params IdentityLink[] links) =>
        new(UserOne, "Pat", links, [], false) { Username = "pat" };

    private static Mock<IUserRepository> RepositoryWith(User user)
    {
        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.GetByIdAsync(UserOne, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        return repo;
    }

    private static void VerifyNothingTouched(Mock<IUserRepository> repo)
    {
        repo.Verify(r => r.UpdateLastAuthenticatedAtAsync(It.IsAny<IdentitySource>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.UpdateLastAuthenticatedAtAsync(It.IsAny<ProviderIdentityKey>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static void VerifyNoLinkLookup(Mock<IUserRepository> repo)
    {
        repo.Verify(r => r.GetByIdentityLinkAsync(It.IsAny<IdentitySource>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.GetByIdentityLinkAsync(It.IsAny<ProviderIdentityKey>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
