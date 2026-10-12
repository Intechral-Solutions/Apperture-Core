using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.IntegrationTests;

/// <summary>
/// Deterministic interleaving tests for CTX-3B Plan 2 (design gate §8.7, product decision P1). Every
/// test here drives a specific interleaving via <see cref="ResourceOwnershipMoveService.MidTransactionHook"/>
/// (Task 6, internal, reachable here only via the <c>InternalsVisibleTo</c> on
/// <c>Apperture.ControlPlane.Security.csproj</c>) rather than firing two tasks and hoping for a lucky
/// race: one operation is held open at a precisely known checkpoint (after validation, before mutation),
/// the second operation is started and observed for whether it blocks on the ownership gate, then the
/// first is released and both outcomes -- and the final persisted state -- are asserted.
/// </summary>
/// <remarks>
/// Every service under test here is the real production class (<see cref="ResourceOwnershipMoveService"/>,
/// <see cref="BindingService"/>, <see cref="SqlScheduleManagementService"/>,
/// <see cref="ResourceContextManagementService"/>), wired against a real, throwaway SQL Server catalog
/// (<see cref="SqlServerScratchDatabase"/>) -- never EF Core InMemory, which has no lock manager and no
/// real transactions and therefore cannot prove serialization at all (see
/// <c>ResourceOwnershipMoveMutationTests</c>, which proves only the <c>if (CanMove)</c> mutation guard).
/// Each concurrent participant gets its OWN <see cref="AppertureDbContext"/> (via <see cref="Env.Build"/>),
/// exactly as two independent HTTP requests would each get their own scoped DbContext -- sharing one
/// DbContext across "concurrent" calls would not exercise the gate at all, since EF Core DbContext is not
/// thread-safe and SQL Server transactions are connection-scoped.
/// </remarks>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ResourceOwnershipMoveConcurrencySqlServerTests
{
    private static readonly TimeSpan ObserveWindow = TimeSpan.FromMilliseconds(700);

    // ----------------------------------------------------------------------------------------------
    // Test 1
    // ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Integration_move_and_binding_creation_are_serialized()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_MoveRace1_");

        await using var setup = Env.Build(fixture);
        var source = await CreateApplicationAsync(setup, "Race1-Source");
        var target = await CreateApplicationAsync(setup, "Race1-Target");

        // --- Ordering A: bind commits first. The move must re-read the binding and be blocked. ---
        var integration = await CreateIntegrationAsync(setup, source.Id);
        var boundConnection = await CreateConnectionAsync(setup, source.Id);
        await setup.Bindings.CreateBindingAsync("Sample.Mod", "slot-a", "test.contract", boundConnection.Id, integration.Id);

        var blockedPreview = await setup.MoveService.MoveIntegrationAsync(integration.Id, target.Id);

        blockedPreview.CanMove.Should().BeFalse("an existing binding to a connection that would become unreachable must block the move");
        blockedPreview.Blockers.Should().Contain(b => b.Code == ResourceMoveBlockerCodes.BindingConnectionUnreachable);
        (await setup.IntegrationRepository.GetByIdAsync(integration.Id))!.ResourceContextId.Value.Should().Be(
            source.Id.Value, "a blocked move must mutate nothing");

        // --- Ordering B (raced): move commits first (holds the gate while binding creation blocks behind
        // it), so the binding attempt re-reads under the gate and is rejected by CanReference. ---
        var raceIntegration = await CreateIntegrationAsync(setup, source.Id);
        var raceConnection = await CreateConnectionAsync(setup, source.Id);

        await using var envA = Env.Build(fixture);
        await using var envB = Env.Build(fixture);

        var result = await RaceAsync<ResourceMovePreview, Binding>(
            checkpoint: "integration:validated",
            operationA: hook =>
            {
                envA.MoveService.MidTransactionHook = hook;
                return envA.MoveService.MoveIntegrationAsync(raceIntegration.Id, target.Id).AsTask();
            },
            operationB: () => envB.Bindings.CreateBindingAsync(
                "Sample.Mod", "slot-b", "test.contract", raceConnection.Id, raceIntegration.Id).AsTask(),
            observeWindow: ObserveWindow);

        AssertNoDeadlock(result.AException, result.BException);
        result.BCompletedWhileAHeld.Should().BeFalse("binding creation must block behind the move's ownership gate, not race it");
        result.AException.Should().BeNull();
        result.AResult!.CanMove.Should().BeTrue("the raced integration had no bindings yet, so its move is unblocked");
        result.BException.Should().BeOfType<InvalidOperationException>(
            "once the integration has moved, the still-source-owned connection is no longer reachable from it");

        (await setup.IntegrationRepository.GetByIdAsync(raceIntegration.Id))!.ResourceContextId.Value.Should().Be(target.Id.Value);
        (await setup.BindingRepository.GetByConnectionIdAsync(raceConnection.Id)).Should().BeEmpty(
            "the rejected binding attempt must not have persisted anything -- no cross-context binding may exist");

        await AssertBindingsAreContextValidAsync(fixture, boundConnection.Id);
        await AssertBindingsAreContextValidAsync(fixture, raceConnection.Id);
    }

    // ----------------------------------------------------------------------------------------------
    // Test 2
    // ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Connection_move_and_binding_creation_are_serialized()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_MoveRace2_");

        await using var setup = Env.Build(fixture);
        var baseContext = await CreateApplicationAsync(setup, "Race2-Base");
        var target = await CreateApplicationAsync(setup, "Race2-Target");

        // --- bind-first: the connection move sees the new binding and is blocked. ---
        var integration = await CreateIntegrationAsync(setup, baseContext.Id);
        var connection = await CreateConnectionAsync(setup, baseContext.Id);
        await setup.Bindings.CreateBindingAsync("Sample.Mod", "slot-a", "test.contract", connection.Id, integration.Id);

        var blockedPreview = await setup.MoveService.MoveConnectionAsync(connection.Id, target.Id);

        blockedPreview.CanMove.Should().BeFalse("the bound integration would no longer be able to reach this connection");
        blockedPreview.Blockers.Should().Contain(b => b.Code == ResourceMoveBlockerCodes.BindingIntegrationUnreachable);
        (await setup.ConnectionRepository.GetByIdAsync(connection.Id))!.ResourceContextId.Value.Should().Be(baseContext.Id.Value);

        // --- move-first (raced): the connection move holds the gate and commits first (no binding exists
        // yet); the subsequent bind attempt blocks behind it, then re-reads the MOVED connection and is
        // rejected by CanReference. ---
        var raceIntegration = await CreateIntegrationAsync(setup, baseContext.Id);
        var raceConnection = await CreateConnectionAsync(setup, baseContext.Id);

        await using var envA = Env.Build(fixture);
        await using var envB = Env.Build(fixture);

        var result = await RaceAsync<ResourceMovePreview, Binding>(
            checkpoint: "connection:validated",
            operationA: hook =>
            {
                envA.MoveService.MidTransactionHook = hook;
                return envA.MoveService.MoveConnectionAsync(raceConnection.Id, target.Id).AsTask();
            },
            operationB: () => envB.Bindings.CreateBindingAsync(
                "Sample.Mod", "slot-b", "test.contract", raceConnection.Id, raceIntegration.Id).AsTask(),
            observeWindow: ObserveWindow);

        AssertNoDeadlock(result.AException, result.BException);
        result.BCompletedWhileAHeld.Should().BeFalse("binding creation must block behind the connection move's ownership gate");
        result.AException.Should().BeNull();
        result.AResult!.CanMove.Should().BeTrue("the raced connection had no bindings yet, so its move is unblocked");
        result.BException.Should().BeOfType<InvalidOperationException>(
            "once the connection has moved, the still-source-owned integration can no longer reach it");

        (await setup.ConnectionRepository.GetByIdAsync(raceConnection.Id))!.ResourceContextId.Value.Should().Be(target.Id.Value);
        (await setup.BindingRepository.GetByConnectionIdAsync(raceConnection.Id)).Should().BeEmpty(
            "the rejected binding attempt must not have persisted anything -- no cross-context binding may exist");

        await AssertBindingsAreContextValidAsync(fixture, connection.Id);
        await AssertBindingsAreContextValidAsync(fixture, raceConnection.Id);
    }

    // ----------------------------------------------------------------------------------------------
    // Test 3a
    // ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Integration_move_then_connection_move_is_serialized()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_MoveRace3a_");

        await using var setup = Env.Build(fixture);
        var integrationHome = await CreateApplicationAsync(setup, "Race3a-IntHome");
        var integrationTarget = await CreateApplicationAsync(setup, "Race3a-IntTarget");

        var integration = await CreateIntegrationAsync(setup, integrationHome.Id);
        // The bound connection starts Shared, so the binding is valid regardless of where the Integration
        // moves (CanReference(anything, Shared) is always true) -- this is what lets the Integration move
        // succeed here at all, isolating the interleaving under test to the CONNECTION move afterward.
        var connection = await CreateConnectionAsync(setup, ResourceContextIds.Shared);
        await setup.Bindings.CreateBindingAsync("Sample.Mod", "slot-a", "test.contract", connection.Id, integration.Id);

        await using var envA = Env.Build(fixture);
        await using var envB = Env.Build(fixture);

        // Connection move target is the Integration's ORIGINAL home: valid if evaluated against stale
        // (pre-move) Integration data, invalid once evaluated against the fresh, post-move Integration --
        // exactly the case the first draft's ungated implementation would have gotten wrong.
        var result = await RaceAsync<ResourceMovePreview, ResourceMovePreview>(
            checkpoint: "integration:validated",
            operationA: hook =>
            {
                envA.MoveService.MidTransactionHook = hook;
                return envA.MoveService.MoveIntegrationAsync(integration.Id, integrationTarget.Id).AsTask();
            },
            operationB: () => envB.MoveService.MoveConnectionAsync(connection.Id, integrationHome.Id).AsTask(),
            observeWindow: ObserveWindow);

        AssertNoDeadlock(result.AException, result.BException);
        result.BCompletedWhileAHeld.Should().BeFalse("the connection move must block behind the integration move's ownership gate");
        result.AException.Should().BeNull();
        result.BException.Should().BeNull("a business-rule block returns CanMove=false, it does not throw");
        result.AResult!.CanMove.Should().BeTrue("the bound connection is Shared, so the integration move is never blocked by it");
        result.BResult!.CanMove.Should().BeFalse(
            "re-validated against the MOVED integration, the connection can no longer move into the integration's old home");
        result.BResult!.Blockers.Should().Contain(b => b.Code == ResourceMoveBlockerCodes.BindingIntegrationUnreachable);

        (await setup.IntegrationRepository.GetByIdAsync(integration.Id))!.ResourceContextId.Value.Should().Be(integrationTarget.Id.Value);
        (await setup.ConnectionRepository.GetByIdAsync(connection.Id))!.ResourceContextId.Value.Should().Be(
            ResourceContextIds.SharedValue, "the blocked connection move must mutate nothing");

        await AssertBindingsAreContextValidAsync(fixture, connection.Id);
    }

    // ----------------------------------------------------------------------------------------------
    // Test 3b
    // ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Connection_move_then_integration_move_is_serialized()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_MoveRace3b_");

        await using var setup = Env.Build(fixture);
        var integrationHome = await CreateApplicationAsync(setup, "Race3b-IntHome");
        var integrationTarget = await CreateApplicationAsync(setup, "Race3b-IntTarget");

        var integration = await CreateIntegrationAsync(setup, integrationHome.Id);
        // The connection starts Shared so the initial binding is valid, and moving it INTO the
        // integration's own home is always valid pre-move (CanReference(X, X) is trivially true) --
        // isolating the interleaving under test to the INTEGRATION move afterward.
        var connection = await CreateConnectionAsync(setup, ResourceContextIds.Shared);
        await setup.Bindings.CreateBindingAsync("Sample.Mod", "slot-a", "test.contract", connection.Id, integration.Id);

        await using var envA = Env.Build(fixture);
        await using var envB = Env.Build(fixture);

        var result = await RaceAsync<ResourceMovePreview, ResourceMovePreview>(
            checkpoint: "connection:validated",
            operationA: hook =>
            {
                envA.MoveService.MidTransactionHook = hook;
                return envA.MoveService.MoveConnectionAsync(connection.Id, integrationHome.Id).AsTask();
            },
            operationB: () => envB.MoveService.MoveIntegrationAsync(integration.Id, integrationTarget.Id).AsTask(),
            observeWindow: ObserveWindow);

        AssertNoDeadlock(result.AException, result.BException);
        result.BCompletedWhileAHeld.Should().BeFalse("the integration move must block behind the connection move's ownership gate");
        result.AException.Should().BeNull();
        result.BException.Should().BeNull("a business-rule block returns CanMove=false, it does not throw");
        result.AResult!.CanMove.Should().BeTrue("moving the Shared connection into the integration's own home is always valid");
        result.BResult!.CanMove.Should().BeFalse(
            "re-validated against the MOVED connection, the integration can no longer move away from the connection's new home");
        result.BResult!.Blockers.Should().Contain(b => b.Code == ResourceMoveBlockerCodes.BindingConnectionUnreachable);

        (await setup.ConnectionRepository.GetByIdAsync(connection.Id))!.ResourceContextId.Value.Should().Be(integrationHome.Id.Value);
        (await setup.IntegrationRepository.GetByIdAsync(integration.Id))!.ResourceContextId.Value.Should().Be(
            integrationHome.Id.Value, "the blocked integration move must mutate nothing");

        await AssertBindingsAreContextValidAsync(fixture, connection.Id);
    }

    // ----------------------------------------------------------------------------------------------
    // Test 4
    // ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Integration_move_and_attached_schedule_creation_are_serialized()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_MoveRace4_");

        await using var setup = Env.Build(fixture);
        var source = await CreateApplicationAsync(setup, "Race4-Source");
        var target = await CreateApplicationAsync(setup, "Race4-Target");
        var integration = await CreateIntegrationAsync(setup, source.Id);

        // A pre-existing attached schedule, created before the race, while the integration is still at
        // Source: this is the extra atomicity evidence for the deferred rollback-safety finding (see the
        // task report) -- it proves the Integration row and its Schedule rows commit as one unit, not that
        // an unrelated concurrent reader can observe the Integration having moved while its own existing
        // Schedule has not.
        var existingSchedule = await CreateAttachedScheduleAsync(setup, integration.Id);

        await using var envA = Env.Build(fixture);
        await using var envB = Env.Build(fixture);

        var result = await RaceAsync<ResourceMovePreview, ScheduleDefinition>(
            checkpoint: "integration:validated",
            operationA: hook =>
            {
                envA.MoveService.MidTransactionHook = hook;
                return envA.MoveService.MoveIntegrationAsync(integration.Id, target.Id).AsTask();
            },
            operationB: () => envB.Schedules.CreateScheduleAsync(NewAttachedScheduleDefinition(integration.Id)).AsTask(),
            observeWindow: ObserveWindow);

        AssertNoDeadlock(result.AException, result.BException);
        result.BCompletedWhileAHeld.Should().BeFalse("attached schedule creation must block behind the integration move's ownership gate");
        result.AException.Should().BeNull();
        result.BException.Should().BeNull();
        result.AResult!.CanMove.Should().BeTrue();

        (await setup.IntegrationRepository.GetByIdAsync(integration.Id))!.ResourceContextId.Value.Should().Be(target.Id.Value);
        result.BResult!.ResourceContextId!.Value.Should().Be(
            target.Id.Value, "the new schedule must inherit the POST-move context, never the stale pre-move context");
        (await setup.ScheduleRepository.GetDefinitionAsync(existingSchedule.Id))!.ResourceContextId!.Value.Should().Be(
            target.Id.Value, "the Integration's existing schedules must have moved atomically with it, in the same commit");
    }

    // ----------------------------------------------------------------------------------------------
    // Test 5
    // ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Move_into_target_and_archive_target_are_serialized()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_MoveRace5_");

        await using var setup = Env.Build(fixture);
        var source = await CreateApplicationAsync(setup, "Race5-Source");
        var target = await CreateApplicationAsync(setup, "Race5-Target");
        var integration = await CreateIntegrationAsync(setup, source.Id); // Active by default

        await using var envA = Env.Build(fixture);
        await using var envB = Env.Build(fixture);

        var result = await RaceAsync<ResourceMovePreview, ResourceContext>(
            checkpoint: "integration:validated",
            operationA: hook =>
            {
                envA.MoveService.MidTransactionHook = hook;
                return envA.MoveService.MoveIntegrationAsync(integration.Id, target.Id).AsTask();
            },
            operationB: () => envB.ResourceContextManagement.ArchiveAsync(target.Id).AsTask(),
            observeWindow: ObserveWindow);

        AssertNoDeadlock(result.AException, result.BException);
        result.BCompletedWhileAHeld.Should().BeFalse("archive must block behind the move's ownership gate");
        result.AException.Should().BeNull();
        result.AResult!.CanMove.Should().BeTrue("the empty target has nothing to block the move into it");
        result.BException.Should().BeOfType<InvalidOperationException>(
            "archive must see the newly-moved, still-live integration and refuse")
            .Which.Message.Should().Contain("1 integration not retired", "the refusal must explain what is still blocking archive");

        (await setup.IntegrationRepository.GetByIdAsync(integration.Id))!.ResourceContextId.Value.Should().Be(target.Id.Value);
        (await setup.ResourceContexts.GetAsync(target.Id))!.IsActive.Should().BeTrue(
            "no archived context may end up owning live resources -- so this one must have stayed Active");

        await AssertNoArchivedContextOwnsLiveResourcesAsync(fixture);
    }

    // ----------------------------------------------------------------------------------------------
    // Test 6
    // ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Two_concurrent_connection_moves_are_serialized()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_MoveRace6_");

        await using var setup = Env.Build(fixture);
        var source = await CreateApplicationAsync(setup, "Race6-Source");
        var target = await CreateApplicationAsync(setup, "Race6-Target");
        var connection = await CreateConnectionAsync(setup, source.Id);

        await using var envA = Env.Build(fixture);
        await using var envB = Env.Build(fixture);

        var result = await RaceAsync<ResourceMovePreview, ResourceMovePreview>(
            checkpoint: "connection:validated",
            operationA: hook =>
            {
                envA.MoveService.MidTransactionHook = hook;
                return envA.MoveService.MoveConnectionAsync(connection.Id, target.Id).AsTask();
            },
            operationB: () => envB.MoveService.MoveConnectionAsync(connection.Id, target.Id).AsTask(),
            observeWindow: ObserveWindow);

        AssertNoDeadlock(result.AException, result.BException);
        result.BCompletedWhileAHeld.Should().BeFalse("the second move must block behind the first's ownership gate, same-type or not");
        result.AException.Should().BeNull();
        result.BException.Should().BeNull("a business-rule block returns CanMove=false, it does not throw");
        result.AResult!.CanMove.Should().BeTrue("exactly one of the two identical moves must win");
        result.BResult!.CanMove.Should().BeFalse("the second move re-validates against the already-moved connection");
        result.BResult!.Blockers.Should().ContainSingle(b => b.Code == ResourceMoveBlockerCodes.AlreadyInTarget);

        (await setup.ConnectionRepository.GetByIdAsync(connection.Id))!.ResourceContextId.Value.Should().Be(target.Id.Value);

        await AssertBindingsAreContextValidAsync(fixture, connection.Id);
    }

    // ----------------------------------------------------------------------------------------------
    // Test 7
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// CTX-3B archive-concurrency integrity pass: this suite's direction is reversed from Tests 1-6 --
    /// ARCHIVE is the side that holds the gate (via <see cref="ResourceContextManagementService.MidTransactionHook"/>'s
    /// <c>"archive:validated"</c> checkpoint), while the blocker-creating operation (here, an Integration's
    /// Retired -&gt; Active reactivation) is the side that must block and then be rejected against the
    /// now-archived context.
    /// </summary>
    [Fact]
    public async Task Archive_and_integration_reactivation_are_serialized()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_ArchiveRace1_");

        await using var setup = Env.Build(fixture);
        var source = await CreateApplicationAsync(setup, "ArchiveRace1-Source");
        var integration = await CreateIntegrationAsync(setup, source.Id); // Active by default.
        await setup.Integrations.UpdateIntegrationAsync(integration.Id, new UpdateIntegrationRequest(State: IntegrationState.Retired));

        await using var envA = Env.Build(fixture);
        await using var envB = Env.Build(fixture);

        var result = await RaceAsync<ResourceContext, DomainIntegration>(
            checkpoint: "archive:validated",
            operationA: hook =>
            {
                envA.ResourceContextManagementConcrete.MidTransactionHook = hook;
                return envA.ResourceContextManagement.ArchiveAsync(source.Id).AsTask();
            },
            operationB: () => envB.Integrations.UpdateIntegrationAsync(
                integration.Id, new UpdateIntegrationRequest(State: IntegrationState.Active)).AsTask(),
            observeWindow: ObserveWindow);

        AssertNoDeadlock(result.AException, result.BException);
        result.BCompletedWhileAHeld.Should().BeFalse("the reactivation attempt must block behind the archive's ownership gate, not race it");
        result.AException.Should().BeNull();
        result.AResult!.IsActive.Should().BeFalse("the Retired integration does not block archive, so the context must have archived");
        result.BException.Should().BeOfType<ResourceContextArchivedException>(
            "once the context has archived, reactivating the integration must see the now-archived context and refuse -- never silently succeed");

        (await setup.ResourceContexts.GetAsync(source.Id))!.IsActive.Should().BeFalse();
        (await setup.IntegrationRepository.GetByIdAsync(integration.Id))!.State.Should().Be(
            IntegrationState.Retired, "the rejected reactivation must not have persisted anything -- the integration must remain Retired");
    }

    // ----------------------------------------------------------------------------------------------
    // Test 8
    // ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Archive_and_connection_creation_are_serialized()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_ArchiveRace2_");

        await using var setup = Env.Build(fixture);
        var context = await CreateApplicationAsync(setup, "ArchiveRace2-Empty"); // Owns nothing: archivable immediately.

        await using var envA = Env.Build(fixture);
        await using var envB = Env.Build(fixture);

        var result = await RaceAsync<ResourceContext, Connection>(
            checkpoint: "archive:validated",
            operationA: hook =>
            {
                envA.ResourceContextManagementConcrete.MidTransactionHook = hook;
                return envA.ResourceContextManagement.ArchiveAsync(context.Id).AsTask();
            },
            operationB: () => envB.Connections.CreateConnectionAsync(new CreateConnectionRequest(
                new ConnectorDefinitionId($"test.connector.{Guid.NewGuid():N}"),
                $"Connection-{Guid.NewGuid():N}",
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                ResourceContextId: context.Id)).AsTask(),
            observeWindow: ObserveWindow);

        AssertNoDeadlock(result.AException, result.BException);
        result.BCompletedWhileAHeld.Should().BeFalse("connection creation must block behind the archive's ownership gate, not race it");
        result.AException.Should().BeNull();
        result.AResult!.IsActive.Should().BeFalse("the empty context has nothing to block its archive");
        result.BException.Should().BeOfType<ResourceContextArchivedException>(
            "ResolveForNewOwnershipAsync must see the now-archived target inside the gated transaction and refuse -- never silently create into it");

        (await setup.ResourceContexts.GetAsync(context.Id))!.IsActive.Should().BeFalse();
        (await setup.ResourceContextManagement.GetOwnershipSummaryAsync(context.Id)).ActiveConnections.Should().Be(
            0, "the rejected creation must not have persisted a Connection into the now-archived context");
    }

    // ----------------------------------------------------------------------------------------------
    // Test 9
    // ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Archive_and_schedule_enable_are_serialized()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_ArchiveRace3_");

        await using var setup = Env.Build(fixture);
        var context = await CreateApplicationAsync(setup, "ArchiveRace3-Context");
        // Seeded before the race (standalone creation is the ungated path -- see
        // CreateStandaloneScheduleAsync's remarks): a Disabled schedule does not block archive.
        var schedule = await CreateStandaloneScheduleAsync(setup, context.Id);

        await using var envA = Env.Build(fixture);
        await using var envB = Env.Build(fixture);

        var result = await RaceAsync<ResourceContext, bool>(
            checkpoint: "archive:validated",
            operationA: hook =>
            {
                envA.ResourceContextManagementConcrete.MidTransactionHook = hook;
                return envA.ResourceContextManagement.ArchiveAsync(context.Id).AsTask();
            },
            operationB: async () =>
            {
                await envB.Schedules.EnableAsync(schedule.Id);
                return true;
            },
            observeWindow: ObserveWindow);

        AssertNoDeadlock(result.AException, result.BException);
        result.BCompletedWhileAHeld.Should().BeFalse("the enable attempt must block behind the archive's ownership gate, not race it");
        result.AException.Should().BeNull();
        result.AResult!.IsActive.Should().BeFalse("the Disabled schedule does not block archive, so the context must have archived");
        result.BException.Should().BeOfType<ResourceContextArchivedException>(
            "the reactivation guard must see the now-archived owning context and refuse to enable -- never silently succeed");

        (await setup.ResourceContexts.GetAsync(context.Id))!.IsActive.Should().BeFalse();
        (await setup.ScheduleRepository.GetDefinitionAsync(schedule.Id))!.Enabled.Should().BeFalse(
            "the rejected enable must not have persisted anything -- the schedule must remain Disabled");
    }

    // ================================================================================================
    // Shared race harness
    // ================================================================================================

    /// <summary>
    /// Holds operation A open at its mid-transaction checkpoint, starts operation B, observes whether B
    /// completes before A is released, then releases A and lets both finish. Every test asserts, from the
    /// returned <see cref="InterleavingResult{TA,TB}"/>: who blocked (<see cref="InterleavingResult{TA,TB}.BCompletedWhileAHeld"/>),
    /// what each side returned or threw, and (separately, per test) the final persisted invariant.
    /// </summary>
    private static async Task<InterleavingResult<TA, TB>> RaceAsync<TA, TB>(
        string checkpoint,
        Func<Func<string, CancellationToken, Task>, Task<TA>> operationA,
        Func<Task<TB>> operationB,
        TimeSpan observeWindow)
    {
        var aAtCheckpoint = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task Hook(string reachedCheckpoint, CancellationToken cancellationToken)
        {
            if (!string.Equals(reachedCheckpoint, checkpoint, StringComparison.Ordinal))
                return Task.CompletedTask;

            aAtCheckpoint.TrySetResult();
            return releaseA.Task;
        }

        TA? aResult = default;
        Exception? aException = null;
        var aTask = Task.Run(async () =>
        {
            try
            {
                aResult = await operationA(Hook);
            }
            catch (Exception ex)
            {
                aException = ex;
            }
        });

        // Wait for A to actually reach the checkpoint before starting B -- otherwise B might race A to the
        // gate rather than deterministically arrive after A already holds it. Race this against aTask
        // itself: if a regression makes operation A throw (not-found, unauthorized, gate-busy, ...) or
        // otherwise return BEFORE ever reaching the checkpoint -- including a checkpoint-name typo here --
        // aTask completes without aAtCheckpoint ever being signalled. Awaiting aAtCheckpoint.Task alone
        // would then hang forever waiting for a checkpoint that will never come (a "red but mute" failure:
        // the regression is real, but the test reports nothing and the CI job just hangs until killed).
        // Detect that case and fail loudly with A's actual exception instead of hanging.
        await Task.WhenAny(aAtCheckpoint.Task, aTask);
        if (!aAtCheckpoint.Task.IsCompleted)
        {
            // aTask finished first, without ever reaching the checkpoint. `await aTask` never throws itself
            // (the lambda above catches into aException) -- it is awaited only so the Task.Run's own
            // completion is observed before returning. B is never started: there is nothing for it to race.
            await aTask;
            return new InterleavingResult<TA, TB>(aResult, aException, default, null, BCompletedWhileAHeld: false);
        }

        TB? bResult = default;
        Exception? bException = null;
        var bTask = Task.Run(async () =>
        {
            try
            {
                bResult = await operationB();
            }
            catch (Exception ex)
            {
                bException = ex;
            }
        });

        var winner = await Task.WhenAny(bTask, Task.Delay(observeWindow));
        var bCompletedWhileAHeld = ReferenceEquals(winner, bTask);

        releaseA.SetResult();
        await aTask;
        await bTask;

        return new InterleavingResult<TA, TB>(aResult, aException, bResult, bException, bCompletedWhileAHeld);
    }

    /// <summary>Result of one <see cref="RaceAsync{TA,TB}"/> interleaving.</summary>
    private sealed record InterleavingResult<TA, TB>(
        TA? AResult,
        Exception? AException,
        TB? BResult,
        Exception? BException,
        bool BCompletedWhileAHeld);

    /// <summary>
    /// Fails loudly (rather than letting the retrying execution strategy hide it) if either side of an
    /// interleaving observed SQL error 1205 (deadlock victim) anywhere in its exception chain. A deadlock
    /// in any of these six interleavings means the ownership gate was not acquired first somewhere -- the
    /// exact regression this architecture exists to prevent (design gate §8.7, product decision P1).
    /// </summary>
    private static void AssertNoDeadlock(params Exception?[] exceptions)
    {
        foreach (var exception in exceptions)
        {
            for (var current = exception; current is not null; current = current.InnerException)
            {
                if (current is SqlException sql)
                {
                    sql.Number.Should().NotBe(1205,
                        "a deadlock anywhere means the gate was not acquired first somewhere -- fail loudly rather than " +
                        "relying on the retrying execution strategy to hide it");
                }
            }
        }
    }

    private static async Task<SqlServerScratchDatabase> RequireFixtureAsync(string catalogPrefix)
    {
        var fixture = await SqlServerScratchDatabase.CreateAsync(catalogPrefix);
        // Unlike the gate/locking probes, these six interleavings ARE the concurrency-closure evidence for
        // CTX-3B Plan 2 -- a soft skip here is explicitly called out as unacceptable closure evidence, so
        // this fails the test hard instead of silently returning when no SQL Server is reachable.
        fixture.Should().NotBeNull(
            "CTX-3B closure requires these interleaving tests to genuinely execute against SQL Server -- " +
            "a soft-skipped green suite is not acceptable evidence. " + SqlServerTestConfiguration.DescribeActiveConfiguration());
        return fixture!;
    }

    // ================================================================================================
    // Invariant sweeps (identical in shape across every test; see task-8-brief.md)
    // ================================================================================================

    /// <summary>
    /// For every persisted binding on <paramref name="connectionId"/> that names an Integration, the bound
    /// Integration must still be able to reference the (possibly moved) Connection under
    /// <see cref="ResourceContextReferenceRule.CanReference"/>. Asserted identically after every
    /// interleaving that produces or could produce a binding.
    /// </summary>
    private static async Task AssertBindingsAreContextValidAsync(SqlServerScratchDatabase fixture, ConnectionId connectionId)
    {
        await using var assertDb = fixture.NewDb();
        var bindings = await new SqlBindingRepository(assertDb).GetByConnectionIdAsync(connectionId);
        foreach (var binding in bindings.Where(b => b.IntegrationId is not null))
        {
            var boundIntegration = await new SqlIntegrationRepository(assertDb).GetByIdAsync(binding.IntegrationId!);
            var finalConnection = await new SqlConnectionRepository(assertDb).GetByIdAsync(connectionId);
            if (boundIntegration is null || finalConnection is null)
                continue; // a dangling edge is a different, pre-existing concern -- not this invariant.

            ResourceContextReferenceRule
                .CanReference(boundIntegration.ResourceContextId, finalConnection.ResourceContextId)
                .Should().BeTrue("no persisted binding may end up cross-context-invalid under any interleaving");
        }
    }

    /// <summary>
    /// Case 5's extra sweep: no ResourceContext whose state is not Active may own a non-Retired
    /// Integration, an Active Connection, or an Enabled Schedule.
    /// </summary>
    private static async Task AssertNoArchivedContextOwnsLiveResourcesAsync(SqlServerScratchDatabase fixture)
    {
        await using var db = fixture.NewDb();

        var nonActiveContextIds = await db.ResourceContexts.AsNoTracking()
            .Where(c => c.State != ResourceContextStates.Active)
            .Select(c => c.Id)
            .ToListAsync();
        if (nonActiveContextIds.Count == 0)
            return;

        var liveIntegrations = await db.Integrations.AsNoTracking()
            .Where(i => nonActiveContextIds.Contains(i.ResourceContextId) && i.State != (int)IntegrationState.Retired)
            .ToListAsync();
        liveIntegrations.Should().BeEmpty("a non-Active ResourceContext must own no non-Retired Integration");

        var activeConnections = await db.Connections.AsNoTracking()
            .Where(c => nonActiveContextIds.Contains(c.ResourceContextId) && c.State == (int)ConnectionState.Active)
            .ToListAsync();
        activeConnections.Should().BeEmpty("a non-Active ResourceContext must own no Active Connection");

        var enabledSchedules = await db.ScheduleDefinitions.AsNoTracking()
            .Where(s => s.ResourceContextId != null && nonActiveContextIds.Contains(s.ResourceContextId) && s.Enabled)
            .ToListAsync();
        enabledSchedules.Should().BeEmpty("a non-Active ResourceContext must own no Enabled Schedule");
    }

    // ================================================================================================
    // Fixture builders
    // ================================================================================================

    private static Task<ResourceContext> CreateApplicationAsync(Env env, string name) =>
        env.ResourceContexts.CreateApplicationContextAsync(name).AsTask();

    private static async Task<DomainIntegration> CreateIntegrationAsync(Env env, ResourceContextId contextId)
    {
        return await env.Integrations.CreateIntegrationAsync(
            new CreateIntegrationRequest($"Integration-{Guid.NewGuid():N}", "Sample.Mod", ResourceContextId: contextId));
    }

    private static async Task<Connection> CreateConnectionAsync(Env env, ResourceContextId contextId)
    {
        return await env.Connections.CreateConnectionAsync(new CreateConnectionRequest(
            new ConnectorDefinitionId($"test.connector.{Guid.NewGuid():N}"),
            $"Connection-{Guid.NewGuid():N}",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            ResourceContextId: contextId));
    }

    private static Task<ScheduleDefinition> CreateAttachedScheduleAsync(Env env, IntegrationId integrationId) =>
        env.Schedules.CreateScheduleAsync(NewAttachedScheduleDefinition(integrationId)).AsTask();

    private static ScheduleDefinition NewAttachedScheduleDefinition(IntegrationId integrationId) => new(
        new ScheduleId(Guid.NewGuid().ToString("N")[..24]),
        "Nightly",
        "0 0 * * *",
        "UTC",
        Enabled: true,
        new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
        ExecutionPolicyRef: null,
        Version: 1,
        integrationId);

    /// <summary>
    /// A standalone (non-Integration-attached), Disabled schedule directly owned by
    /// <paramref name="contextId"/>. Standalone creation is the ungated path
    /// (<see cref="SqlScheduleManagementService.CreateScheduleAsync"/>'s <c>IntegrationId is null</c>
    /// branch), so this is seeded BEFORE any race begins -- the race under test in
    /// <see cref="Archive_and_schedule_enable_are_serialized"/> is Archive vs. <c>EnableAsync</c> on this
    /// already-persisted row, not Archive vs. schedule creation itself.
    /// </summary>
    private static Task<ScheduleDefinition> CreateStandaloneScheduleAsync(Env env, ResourceContextId contextId) =>
        env.Schedules.CreateScheduleAsync(NewStandaloneScheduleDefinition(contextId)).AsTask();

    private static ScheduleDefinition NewStandaloneScheduleDefinition(ResourceContextId contextId) => new ScheduleDefinition(
        new ScheduleId(Guid.NewGuid().ToString("N")[..24]),
        "Standalone-Nightly",
        "0 0 * * *",
        "UTC",
        Enabled: false,
        new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
        ExecutionPolicyRef: null,
        Version: 1,
        IntegrationId: null)
    {
        ResourceContextId = contextId
    };

    // ================================================================================================
    // Environment: one full set of REAL production services wired against one AppertureDbContext.
    // ================================================================================================

    /// <summary>
    /// One participant's full set of real production services, wired against one real
    /// <see cref="AppertureDbContext"/> (a real SQL Server connection). Mirrors the shape of
    /// <c>ResourceOwnershipMoveTestHarness</c> (EF Core InMemory, <c>Apperture.UnitTests</c>), but that
    /// harness's private helper types cannot be reused across assemblies, and a single shared harness
    /// instance could not represent two independent concurrent callers anyway -- each caller needs its own
    /// DbContext/connection/transaction, exactly as two separate HTTP requests would.
    /// </summary>
    private sealed class Env : IAsyncDisposable
    {
        public required AppertureDbContext Db { get; init; }
        public required IResourceContextService ResourceContexts { get; init; }
        public required IIntegrationRepository IntegrationRepository { get; init; }
        public required IConnectionRepository ConnectionRepository { get; init; }
        public required IBindingRepository BindingRepository { get; init; }
        public required IScheduleRepository ScheduleRepository { get; init; }
        public required IIntegrationManagementService Integrations { get; init; }
        public required IConnectionManagementService Connections { get; init; }
        public required IBindingService Bindings { get; init; }
        public required IScheduleManagementService Schedules { get; init; }
        public required IResourceContextManagementService ResourceContextManagement { get; init; }

        /// <summary>
        /// Concrete type, deliberately not the <see cref="IResourceOwnershipMoveService"/> interface --
        /// <see cref="ResourceOwnershipMoveService.MidTransactionHook"/> is internal on the concrete class
        /// and is exactly the seam these tests need to control.
        /// </summary>
        public required ResourceOwnershipMoveService MoveService { get; init; }

        /// <summary>
        /// Same instance as <see cref="ResourceContextManagement"/>, exposed as its concrete type --
        /// mirrors <see cref="MoveService"/>'s reason for existing: <see cref="ResourceContextManagementService.MidTransactionHook"/>
        /// is internal on the concrete class, and is exactly the seam the Archive-vs-blocker-creation
        /// races below need to control.
        /// </summary>
        public required ResourceContextManagementService ResourceContextManagementConcrete { get; init; }

        public static Env Build(SqlServerScratchDatabase fixture)
        {
            var db = fixture.NewDb();

            var resourceContextRepository = new SqlResourceContextRepository(db);
            var resourceContexts = new ResourceContextService(resourceContextRepository, TimeProvider.System);
            var integrationRepository = new SqlIntegrationRepository(db);
            var connectionRepository = new SqlConnectionRepository(db);
            var bindingRepository = new SqlBindingRepository(db);
            var scheduleRepository = new SqlScheduleRepository(db);
            var gate = new SqlResourceOwnershipMutationGate(db);

            var principal = new FixedPrincipalContext();
            var authzAllow = new AlwaysAllowAuthorizationEvaluationService();
            var connectorRegistry = new PermissiveConnectorDefinitionRegistry();

            // These tests never delete an integration, so the delete's collaborators (the settings-secret
            // classifier and retirement) are not supplied; the delete would refuse to run without them. Creates and updates run in
            // a unit that opens the real transaction these races depend on (the gate must be taken inside one) but records no
            // audit rows: the races are about ownership serialization, and the audit behaviour has its own suites.
            var integrations = new IntegrationManagementService(
                principal, authzAllow, integrationRepository, resourceContexts, db, gate,
                auditedUnitOfWork: new TransactionOnlyUnit(db));

            var connections = new ConnectionManagementService(
                principal,
                authzAllow,
                connectionRepository,
                connectorRegistry,
                new NoOpConnectionVerificationService(),
                new ConnectionSecretMaterialProvisioner(new NullSecretStore()),
                resourceContexts,
                db,
                gate,
                new NullAuditService());

            var bindings = new BindingService(
                principal,
                new AlwaysAllowResourceAuthorizationService(),
                bindingRepository,
                connectionRepository,
                new BindingEligibilityService(connectionRepository, connectorRegistry),
                integrationRepository,
                db,
                gate,
                auditedUnitOfWork: new TransactionOnlyUnit(db));

            var schedules = new SqlScheduleManagementService(
                scheduleRepository,
                new StubClaimOptions(),
                TimeProvider.System,
                resourceContexts,
                integrationRepository,
                db,
                gate,
                principal,
                new TransactionOnlyUnit(db));

            var resourceContextManagement = new ResourceContextManagementService(
                resourceContexts,
                resourceContextRepository,
                principal,
                authzAllow,
                new SqlAuditService(db),
                db,
                gate);

            var moveService = new ResourceOwnershipMoveService(
                principal,
                authzAllow,
                integrationRepository,
                connectionRepository,
                scheduleRepository,
                bindingRepository,
                resourceContexts,
                db,
                gate,
                new SqlAuditService(db));

            return new Env
            {
                Db = db,
                ResourceContexts = resourceContexts,
                IntegrationRepository = integrationRepository,
                ConnectionRepository = connectionRepository,
                BindingRepository = bindingRepository,
                ScheduleRepository = scheduleRepository,
                Integrations = integrations,
                Connections = connections,
                Bindings = bindings,
                Schedules = schedules,
                ResourceContextManagement = resourceContextManagement,
                ResourceContextManagementConcrete = resourceContextManagement,
                MoveService = moveService,
            };
        }

        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }

    // ================================================================================================
    // Minimal test doubles (this project cannot see Apperture.UnitTests' private harness types, and Moq
    // is not referenced here -- see task-8-report.md).
    // ================================================================================================

    /// <summary>Swallows appended records; no test in this file asserts on audit output.</summary>
    private sealed class NullAuditService : IAuditService
    {
        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by these tests.");
    }

    /// <summary>
    /// A unit that does what the races need of one: it runs the attempt in a real transaction inside the context's execution
    /// strategy (clearing tracked state first, as the production unit does) and commits. It stages nothing and verifies nothing.
    /// </summary>
    private sealed class TransactionOnlyUnit(AppertureDbContext db) : IAuditedUnitOfWork
    {
        public Task<T> ExecuteAsync<T>(
            AuditedOperation operation,
            Func<IAuditedTransaction, CancellationToken, Task<T>> attempt,
            Func<AuditCommittedOperation, CancellationToken, Task<T>> reconstruct,
            CancellationToken cancellationToken = default) =>
            db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                var result = await attempt(new Transaction(), cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            });

        private sealed class Transaction : IAuditedTransaction
        {
            public string OperationEventId { get; } = Guid.NewGuid().ToString("N");

            public AuditActor Actor => AuditActor.System();

            public ValueTask StageAsync(Apperture.Core.Application.Audit.AuditEvent auditEvent, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

            public void DeclareNoOp()
            {
            }
        }
    }

    private sealed class FixedPrincipalContext : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new FixedActor());

        private sealed class FixedActor : IActor
        {
            public ActorId Id => new("concurrency-test-actor");
            public ActorKind Kind => ActorKind.Human;
            public string? DisplayName => "Concurrency Test Actor";
        }
    }

    private sealed class AlwaysAllowAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class AlwaysAllowResourceAuthorizationService : IAuthorizationService
    {
        public ValueTask<bool> EvaluateAsync(
            IActor actor, PermissionDescriptor permission, ResourceDescriptor? resource,
            IGovernedResourceIdentity? resourceIdentity = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class NoOpConnectionVerificationService : IConnectionVerificationService
    {
        public ValueTask<ConnectionVerificationResult> VerifyAsync(Connection connection, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ConnectionVerificationResult(ConnectionVerificationStatus.NeverTested, DateTimeOffset.UnixEpoch, string.Empty));
    }

    private sealed class StubClaimOptions : ISchedulerClaimOptions
    {
        public int ClaimLeaseMinutes => 5;
    }

    private sealed class PermissiveConnectorDefinitionRegistry : IConnectorDefinitionRegistry
    {
        private readonly Dictionary<string, ConnectorDefinition> _definitions = new(StringComparer.Ordinal);
        public void Register(ConnectorDefinition definition) => _definitions[definition.Id.Value] = definition;
        public IReadOnlyList<ConnectorDefinition> GetAll(string? moduleId = null) => _definitions.Values.ToList();

        public ConnectorDefinition? Get(ConnectorDefinitionId id)
        {
            if (!_definitions.TryGetValue(id.Value, out var definition))
            {
                definition = new ConnectorDefinition(id, id.Value, null, new Dictionary<string, string>());
                _definitions[id.Value] = definition;
            }
            return definition;
        }
    }

    /// <summary>
    /// Never actually invoked: every connection created by these tests supplies an empty
    /// <c>SecretValues</c> dictionary, so <see cref="ConnectionSecretMaterialProvisioner.ProvisionForCreateAsync"/>
    /// never iterates into a call against this store. Throws if that assumption is ever violated, rather
    /// than silently returning fabricated secret material.
    /// </summary>
    private sealed class NullSecretStore : ISecretStore
    {
        public ValueTask<SecretDescriptor> CreateAsync(SecretCreateRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Test double: no connection in these concurrency tests supplies secret values.");

        public ValueTask<SecretDescriptor?> GetAsync(SecretId secretId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyList<SecretDescriptor>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<string> AddVersionAsync(SecretId secretId, SecretMaterial material, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask ActivateVersionAsync(SecretId secretId, string version, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DisableAsync(SecretId secretId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask RevokeVersionAsync(SecretId secretId, string version, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<SecretAuditPage> GetAuditAsync(SecretAuditQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
