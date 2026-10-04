using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class SqlPrincipalToUserResolverTests
{
    private const string TenantA = "11111111-1111-1111-1111-111111111111";
    private const string TenantB = "22222222-2222-2222-2222-222222222222";
    private const string Oid = "33333333-3333-3333-3333-333333333333";

    [Fact]
    public async Task ResolveUserIdAsync_with_entra_link_updates_entra_timestamp_only()
    {
        var userId = new UserId("user-1");
        var user = new User(
            userId,
            "Operator",
            [new IdentityLink(IdentitySource.Entra, Oid, null, TenantA)],
            [],
            false)
        { Username = "operator" };
        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.GetByIdentityLinkAsync(
                It.Is<ProviderIdentityKey>(k =>
                    k.Source == IdentitySource.Entra
                    && k.TenantId == TenantA
                    && k.ExternalId == Oid),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var sut = new SqlPrincipalToUserResolver(repo.Object);
        var actor = new HumanActor(new ActorId(Oid), "oid", IdentitySource.Entra, tenantId: TenantA);

        var resolved = await sut.ResolveUserIdAsync(actor, CancellationToken.None);

        resolved.Should().Be(userId);
        repo.Verify(
            r => r.UpdateLastAuthenticatedAtAsync(
                It.Is<ProviderIdentityKey>(k => k.TenantId == TenantA && k.ExternalId == Oid),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        repo.Verify(
            r => r.UpdateLastAuthenticatedAtAsync(IdentitySource.Local, It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveUserIdAsync_does_not_resolve_the_same_oid_from_another_tenant()
    {
        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.GetByIdentityLinkAsync(It.IsAny<ProviderIdentityKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var sut = new SqlPrincipalToUserResolver(repo.Object);
        var actor = new HumanActor(new ActorId(Oid), "oid", IdentitySource.Entra, tenantId: TenantB);

        var resolved = await sut.ResolveUserIdAsync(actor, CancellationToken.None);

        resolved.Should().BeNull();
        repo.Verify(
            r => r.GetByIdentityLinkAsync(
                It.Is<ProviderIdentityKey>(k => k.TenantId == TenantB && k.ExternalId == Oid),
                It.IsAny<CancellationToken>()),
            Times.Once);
        repo.Verify(
            r => r.GetByIdentityLinkAsync(IdentitySource.Entra, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveUserIdAsync_with_preferred_source_updates_matching_link_only()
    {
        var userId = new UserId("user-1");
        var user = new User(userId, "Operator", [new IdentityLink(IdentitySource.Local, "local-1", null)], [], false) { Username = "local-1" };
        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.GetByIdentityLinkAsync(IdentitySource.Local, "local-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var sut = new SqlPrincipalToUserResolver(repo.Object);
        var actor = new HumanActor(new ActorId("local-1"), "local-1", preferredIdentitySource: IdentitySource.Local);

        var resolved = await sut.ResolveUserIdAsync(actor, CancellationToken.None);

        resolved.Should().Be(userId);
        repo.Verify(
            r => r.UpdateLastAuthenticatedAtAsync(IdentitySource.Local, "local-1", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);
        repo.Verify(
            r => r.GetByIdentityLinkAsync(IdentitySource.Entra, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
