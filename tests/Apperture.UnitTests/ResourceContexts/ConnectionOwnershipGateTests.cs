using Apperture.Core.Application.Connector;
using Apperture.Core.Domain.Connector;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// CTX-3B follow-on: <see cref="Apperture.ControlPlane.Security.ConnectionManagementService.CreateConnectionAsync"/>
/// is a new <c>IResourceOwnershipMutationGate</c> consumer, closing the create-vs-archive race a
/// follow-on audit found (a concurrent <c>CreateConnectionAsync</c> could interleave with
/// <c>ResourceContextManagementService.ArchiveAsync</c> so Archive's ownership summary read misses the
/// new Active Connection, leaving an archived context owning a live Connection).
/// </summary>
public sealed class ConnectionOwnershipGateTests
{
    [Fact]
    public async Task CreateConnection_UnderAnArchivedContext_IsRejected()
    {
        await using var harness = ManagementServiceTestHarness.WithRecordingGate();
        var app = await harness.Service.CreateAsync("CampusNexus", slug: null, description: null, CancellationToken.None);
        await harness.Service.ArchiveAsync(app.Id, CancellationToken.None);

        var act = async () => await harness.Connections.CreateConnectionAsync(
            new CreateConnectionRequest(
                new ConnectorDefinitionId("test.connector"),
                "Connection A",
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                ResourceContextId: app.Id),
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*archived*");
    }

    /// <summary>
    /// Documents the contract at the unit level: CreateConnectionAsync must not evaluate/resolve context
    /// ownership before acquiring the gate. Without this, a concurrent Archive could interleave between
    /// the (ungated) ownership read and the Connection insert, producing an archived context that owns a
    /// live Connection -- the exact race this task closes. This test pins the call order so a refactor
    /// cannot silently drop the gate; the real interleaving is proven only against SQL Server elsewhere.
    /// </summary>
    [Fact]
    public async Task CreateConnection_reads_ownership_under_the_ownership_gate()
    {
        await using var harness = ManagementServiceTestHarness.WithRecordingGate();
        var app = await harness.Service.CreateAsync("CampusNexus", slug: null, description: null, CancellationToken.None);

        await harness.Connections.CreateConnectionAsync(
            new CreateConnectionRequest(
                new ConnectorDefinitionId("test.connector"),
                "Connection A",
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                ResourceContextId: app.Id),
            CancellationToken.None);

        harness.Gate!.AcquiredBeforeFirstOwnershipRead.Should().BeTrue();
    }
}
