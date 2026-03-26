using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Integration;

public sealed class AttentionContactGroupManagementServiceTests
{
    private static AppertureDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    [Fact]
    public async Task Create_and_list_roundtrip_when_authorized()
    {
        await using var db = CreateDb();
        var actor = new Mock<IActor>();
        actor.SetupGet(x => x.Id).Returns(new ActorId("a1"));
        actor.SetupGet(x => x.Kind).Returns(ActorKind.Human);
        actor.SetupGet(x => x.DisplayName).Returns("tester");
        var principal = new Mock<IPrincipalContext>();
        principal.Setup(x => x.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(actor.Object);
        var auth = new Mock<IAuthorizationEvaluationService>();
        auth.Setup(x => x.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var time = new FixedTime(DateTimeOffset.Parse("2025-06-01T12:00:00Z"));

        var sut = new AttentionContactGroupManagementService(db, principal.Object, auth.Object, time);

        var created = await sut.CreateAsync(new AttentionContactGroupCreateCommand("Ops", "desc", true));
        created.Name.Should().Be("Ops");

        var list = await sut.ListAsync(includeDisabled: true);
        list.Should().ContainSingle(x => x.Id == created.Id && x.Name == "Ops");
    }

    private sealed class FixedTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
