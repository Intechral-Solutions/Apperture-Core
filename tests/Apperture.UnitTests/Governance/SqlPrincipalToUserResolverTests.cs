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
    [Fact]
    public async Task ResolveUserIdAsync_with_entra_link_updates_entra_timestamp_only()
    {
        var userId = new UserId("user-1");
        var user = new User(userId, "Operator", [new IdentityLink(IdentitySource.Entra, "oid-1", null)], [], false);
        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.GetByIdentityLinkAsync(IdentitySource.Entra, "oid-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var sut = new SqlPrincipalToUserResolver(repo.Object);
        var actor = new HumanActor(new ActorId("oid-1"), "oid-1");

        var resolved = await sut.ResolveUserIdAsync(actor, CancellationToken.None);

        resolved.Should().Be(userId);
        repo.Verify(
            r => r.UpdateLastAuthenticatedAtAsync(IdentitySource.Entra, "oid-1", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);
        repo.Verify(
            r => r.UpdateLastAuthenticatedAtAsync(IdentitySource.Local, It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveUserIdAsync_with_preferred_source_updates_matching_link_only()
    {
        var userId = new UserId("user-1");
        var user = new User(userId, "Operator", [new IdentityLink(IdentitySource.Local, "local-1", null)], [], false);
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
