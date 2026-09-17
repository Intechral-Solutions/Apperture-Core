using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

public sealed class ArchiveAfterMoveTests
{
    [Fact]
    public async Task Archive_succeeds_after_the_last_live_blocker_is_moved_out()
    {
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id); // Active: blocks archive

        var beforeMove = async () => await h.ArchiveAsync(source.Id);
        await beforeMove.Should().ThrowAsync<InvalidOperationException>("a live Integration blocks archive");

        await h.Service.MoveIntegrationAsync(integration.Id, target.Id);

        var archived = await h.ArchiveAsync(source.Id);
        archived.IsActive.Should().BeFalse("moving the last live resource out is exactly what CTX-3B unblocks");
    }
}
