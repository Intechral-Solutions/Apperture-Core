using System.Data.Common;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Notifications;
using Apperture.Core.Security.Secrets.Providers;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.4 slice 3, work package 4b: the audited integration delete racing each writer that work package 3 put under the
/// integration row lock, in both orders, on a locking READ COMMITTED catalog and on a READ_COMMITTED_SNAPSHOT catalog. The order
/// is made deterministic, not timed: the side that must win is paused by an interceptor right after its own locked read of the
/// integration row, so it holds the lock while the other side is shown, by the server's own lock-wait list, to be waiting on that
/// row. Then the winner is released.
/// </summary>
public sealed partial class IntegrationRowLockSqlServerTests
{
    // ---- Order 1: the writer holds the lock first; the delete waits, then decides on what the writer committed ----

    [Theory]
    [MemberData(nameof(DeleteRaceCases))]
    public async Task When_the_writer_wins_the_lock_the_delete_waits_and_decides_on_what_the_writer_committed(WriterKind kind, bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integrationId = await NewIntegrationAsync(scratch);
        var (_, principal) = await AdminAsync(scratch);
        var definitionId = await NewPlatformDefinitionAsync(scratch);
        await PrepareAsync(kind, scratch, integrationId, principal, definitionId);

        var writerPause = new PauseAfterIntegrationLock();
        var writer = Task.Run(Writer(kind, scratch, integrationId, principal, definitionId, reauthorize: null, interceptor: writerPause));
        await writerPause.Reached.WaitAsync(TimeSpan.FromSeconds(30));

        var delete = Task.Run(() => DeleteIntegrationAsync(scratch, integrationId, principal));
        await WaitUntilAnyRequestWaitsOnIntegrationsAsync(scratch, delete);

        writerPause.Release();
        await writer;
        var outcome = await Capture(delete);

        if (AddsABlocker(kind))
        {
            // The blocker committed before the delete held the row, so the delete's count under the lock sees it.
            outcome.Should().BeOfType<IntegrationDeleteBlockedException>().Which.Blocker.Should().Be(BlockerOf(kind));
            (await IntegrationExistsAsync(scratch, integrationId)).Should().BeTrue();
            (await DeleteRowsAsync(scratch, integrationId)).Should().Be(0, "a refused delete commits no row");
        }
        else
        {
            outcome.Should().BeNull("a settings change is not a blocker; the delete removes whatever it left");
            (await IntegrationExistsAsync(scratch, integrationId)).Should().BeFalse();
            var after = await StateAsync(scratch, integrationId);
            after.Values.Should().Be(0, "the override the writer committed is removed by the delete that followed it");
            (await SettingsRemovedAsync(scratch, integrationId)).Should().Be(kind is WriterKind.SettingSet or WriterKind.SecretReplace ? 1 : 0);
            if (kind == WriterKind.SecretReplace)
                (await ActiveSettingSecretsAsync(scratch, integrationId)).Should().Be(0, "the secret the writer stored was retired by the delete");
        }
    }

    // ---- Order 2: the delete holds the lock first; the writer waits, then refuses, and leaves nothing behind ----

    [Theory]
    [MemberData(nameof(DeleteRaceCases))]
    public async Task When_the_delete_wins_the_lock_the_writer_waits_then_refuses_and_leaves_no_orphan_or_side_effect(WriterKind kind, bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integrationId = await NewIntegrationAsync(scratch);
        var (_, principal) = await AdminAsync(scratch);
        var definitionId = await NewPlatformDefinitionAsync(scratch);
        await PrepareAsync(kind, scratch, integrationId, principal, definitionId);
        var hadSecret = kind == WriterKind.SecretClear;

        var deletePause = new PauseAfterIntegrationLock();
        var delete = Task.Run(() => DeleteIntegrationAsync(scratch, integrationId, principal, deletePause));
        await deletePause.Reached.WaitAsync(TimeSpan.FromSeconds(30));
        var writer = Task.Run(Writer(kind, scratch, integrationId, principal, definitionId, reauthorize: null));
        await WaitUntilAnyRequestWaitsOnIntegrationsAsync(scratch, writer);
        var secretsBefore = await SecretCountsAsync(scratch);

        deletePause.Release();
        await delete;
        var refused = await Capture(writer);

        refused.Should().NotBeNull("the integration is gone by the time the writer holds its row");
        refused!.GetType().Should().Be(ExpectedRefusal(kind));

        (await IntegrationExistsAsync(scratch, integrationId)).Should().BeFalse();
        var after = await StateAsync(scratch, integrationId);
        after.Values.Should().Be(0, "no override survives or is added after the integration");
        after.Definitions.Should().Be(0);
        after.Subscriptions.Should().Be(0);
        after.Routings.Should().Be(0);
        after.Bindings.Should().Be(0, "no binding is added to a deleted integration");
        after.Schedules.Should().Be(0, "no schedule is added to a deleted integration");
        var secretsAfter = await SecretCountsAsync(scratch);
        secretsAfter.Descriptors.Should().Be(secretsBefore.Descriptors, "the writer stored no secret");
        secretsAfter.Versions.Should().Be(secretsBefore.Versions, "the writer added no version");
        (await WriterRowsAsync(scratch, integrationId)).Should().Be(0, "the writer recorded nothing");
        (await DeleteRowsAsync(scratch, integrationId)).Should().Be(1);
        if (hadSecret)
            (await ActiveSettingSecretsAsync(scratch, integrationId)).Should().Be(0, "the delete retired the secret the prepared override owned");
    }

    // ---- Order 2, refused: a delete that holds the lock and refuses lets the writer proceed normally ----

    [Theory]
    [MemberData(nameof(DeleteRaceCases))]
    public async Task When_the_delete_wins_the_lock_and_is_refused_the_writer_then_completes_normally(WriterKind kind, bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integrationId = await NewIntegrationAsync(scratch);
        var (_, principal) = await AdminAsync(scratch);
        var definitionId = await NewPlatformDefinitionAsync(scratch);
        await PrepareAsync(kind, scratch, integrationId, principal, definitionId);
        await NewIntegrationDefinitionAsync(scratch, integrationId); // a blocker the delete will find under the lock
        var before = await StateAsync(scratch, integrationId);

        var deletePause = new PauseAfterIntegrationLock();
        var delete = Task.Run(() => DeleteIntegrationAsync(scratch, integrationId, principal, deletePause));
        await deletePause.Reached.WaitAsync(TimeSpan.FromSeconds(30));
        var writer = Task.Run(Writer(kind, scratch, integrationId, principal, definitionId, reauthorize: null));
        await WaitUntilAnyRequestWaitsOnIntegrationsAsync(scratch, writer);

        deletePause.Release();
        (await Capture(delete)).Should().BeOfType<IntegrationDeleteBlockedException>();
        await writer;

        (await IntegrationExistsAsync(scratch, integrationId)).Should().BeTrue();
        AssertCompleted(kind, before, await StateAsync(scratch, integrationId));
        (await DeleteRowsAsync(scratch, integrationId)).Should().Be(0);
    }

    // ---- The routing clear and the delete: the override the clear removes is itself a blocker of the delete ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task When_the_clear_wins_the_lock_the_delete_waits_and_then_finds_no_blocker(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integrationId = await NewIntegrationAsync(scratch);
        var (_, principal) = await AdminAsync(scratch);
        var definitionId = await NewPlatformDefinitionAsync(scratch);
        await Writer(WriterKind.RoutingUpsert, scratch, integrationId, principal, definitionId, null)();

        var clearPause = new PauseAfterIntegrationLock();
        var clear = Task.Run(Writer(WriterKind.RoutingClear, scratch, integrationId, principal, definitionId, reauthorize: null, interceptor: clearPause));
        await clearPause.Reached.WaitAsync(TimeSpan.FromSeconds(30));
        var delete = Task.Run(() => DeleteIntegrationAsync(scratch, integrationId, principal));
        await WaitUntilAnyRequestWaitsOnIntegrationsAsync(scratch, delete);

        clearPause.Release();
        await clear;

        (await Capture(delete)).Should().BeNull("the override was cleared before the delete held the row, so nothing blocks it");
        (await IntegrationExistsAsync(scratch, integrationId)).Should().BeFalse();
        (await StateAsync(scratch, integrationId)).Routings.Should().Be(0);
        (await DeleteRowsAsync(scratch, integrationId)).Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task When_the_delete_wins_the_lock_and_the_override_blocks_it_the_clear_then_removes_the_override(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integrationId = await NewIntegrationAsync(scratch);
        var (_, principal) = await AdminAsync(scratch);
        var definitionId = await NewPlatformDefinitionAsync(scratch);
        await Writer(WriterKind.RoutingUpsert, scratch, integrationId, principal, definitionId, null)();

        var deletePause = new PauseAfterIntegrationLock();
        var delete = Task.Run(() => DeleteIntegrationAsync(scratch, integrationId, principal, deletePause));
        await deletePause.Reached.WaitAsync(TimeSpan.FromSeconds(30));
        var clear = Task.Run(Writer(WriterKind.RoutingClear, scratch, integrationId, principal, definitionId, reauthorize: null));
        await WaitUntilAnyRequestWaitsOnIntegrationsAsync(scratch, clear);

        deletePause.Release();
        (await Capture(delete)).Should().BeOfType<IntegrationDeleteBlockedException>().Which.Blocker
            .Should().Be(IntegrationDeleteBlocker.AttentionRoutings, "the override still existed when the delete counted");
        await clear;

        (await IntegrationExistsAsync(scratch, integrationId)).Should().BeTrue();
        (await StateAsync(scratch, integrationId)).Routings.Should().Be(0, "the clear that waited then removed the override");
        (await DeleteRowsAsync(scratch, integrationId)).Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task When_the_delete_wins_the_lock_and_commits_a_clear_with_nothing_to_clear_refuses_and_changes_nothing(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integrationId = await NewIntegrationAsync(scratch);
        var (_, principal) = await AdminAsync(scratch);
        var definitionId = await NewPlatformDefinitionAsync(scratch);

        var deletePause = new PauseAfterIntegrationLock();
        var delete = Task.Run(() => DeleteIntegrationAsync(scratch, integrationId, principal, deletePause));
        await deletePause.Reached.WaitAsync(TimeSpan.FromSeconds(30));
        var clear = Task.Run(Writer(WriterKind.RoutingClear, scratch, integrationId, principal, definitionId, reauthorize: null));
        await WaitUntilAnyRequestWaitsOnIntegrationsAsync(scratch, clear);

        deletePause.Release();
        await delete;

        var refused = await Capture(clear);
        refused.Should().NotBeNull("the integration is gone by the time the clear holds its row");
        refused!.GetType().Should().Be(typeof(InvalidOperationException));
        refused!.Message.Should().Contain(integrationId);
        (await StateAsync(scratch, integrationId)).Routings.Should().Be(0);
        (await DeleteRowsAsync(scratch, integrationId)).Should().Be(1);
    }

    // ---- Two deletes: the one that waits loses, and is told so ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_delete_that_waits_behind_a_committing_delete_is_not_found_and_never_a_second_success(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integrationId = await NewIntegrationAsync(scratch);
        var (_, principal) = await AdminAsync(scratch);

        var firstPause = new PauseAfterIntegrationLock();
        var first = Task.Run(() => DeleteIntegrationAsync(scratch, integrationId, principal, firstPause));
        await firstPause.Reached.WaitAsync(TimeSpan.FromSeconds(30));
        // The second passed its early check (the row still exists) and waits on the row inside its unit.
        var second = Task.Run(() => DeleteIntegrationAsync(scratch, integrationId, principal));
        await WaitUntilAnyRequestWaitsOnIntegrationsAsync(scratch, second);

        firstPause.Release();
        await first;

        (await Capture(second)).Should().BeOfType<IntegrationNotFoundException>("its own operation committed nothing; the integration's absence is not its success");
        (await DeleteRowsAsync(scratch, integrationId)).Should().Be(1, "one primary row, the first delete's");
    }

    // ---- An ownership move and the delete ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_ownership_move_that_commits_while_the_delete_waits_is_what_the_delete_authorizes_against(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integrationId = await NewIntegrationAsync(scratch);
        var (_, principal) = await AdminAsync(scratch);
        var authorization = new ContextAwareDeleteAuthorization(scratch, deniedContext: OtherContext);

        // The mover's order: the ownership gate, then the integration row, then the change.
        await using var mover = await Holder.TakeAsync(scratch.ConnectionString, integrationId);
        var delete = Task.Run(() => DeleteIntegrationAsync(scratch, integrationId, principal, authorization: authorization));
        await WaitUntilBlockedOnIntegrationsAsync(scratch, mover.SessionId, delete);
        await mover.ExecuteAsync("UPDATE [Integrations] SET [ResourceContextId] = @to WHERE [Id] = @id;", ("@to", OtherContext), ("@id", integrationId));
        await mover.CommitAsync();

        (await Capture(delete)).Should().BeOfType<UnauthorizedAccessException>("the caller may delete in Shared but not in the context the integration moved to");
        authorization.ContextsSeen.Should().Equal("shared", OtherContext);
        (await IntegrationExistsAsync(scratch, integrationId)).Should().BeTrue();
        (await DeleteRowsAsync(scratch, integrationId)).Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_mover_holding_the_ownership_gate_waits_for_a_delete_that_holds_the_row_and_no_deadlock_follows(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integrationId = await NewIntegrationAsync(scratch);
        var (_, principal) = await AdminAsync(scratch);

        var deletePause = new PauseAfterIntegrationLock();
        var delete = Task.Run(() => DeleteIntegrationAsync(scratch, integrationId, principal, deletePause));
        await deletePause.Reached.WaitAsync(TimeSpan.FromSeconds(30));

        // A mover in its own order: the gate first (which the delete never takes), then the integration row.
        var mover = Task.Run(async () =>
        {
            await using var db = scratch.NewDb();
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                await new SqlResourceOwnershipMutationGate(db).AcquireAsync();
                var row = await new SqlIntegrationRepository(db).GetByIdForUpdateAsync(new IntegrationId(integrationId));
                await tx.CommitAsync();
                return row;
            });
        });
        await WaitUntilAnyRequestWaitsOnIntegrationsAsync(scratch, mover);

        deletePause.Release();
        await delete;
        (await mover).Should().BeNull("the mover found the integration deleted once it held the row");
    }

    // ---- Many writers and a delete for one integration: no deadlock, and a consistent end state ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Writers_and_a_delete_racing_for_one_integration_end_consistent_with_no_deadlock_victim(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var kinds = new[]
        {
            WriterKind.SettingSet, WriterKind.SecretReplace, WriterKind.DefinitionCreate, WriterKind.SubscriptionAdd, WriterKind.RoutingUpsert,
            WriterKind.RoutingClear, WriterKind.IntegrationUpdate, WriterKind.BindingCreate, WriterKind.ScheduleCreate,
        };

        for (var round = 0; round < 6; round++)
        {
            var integrationId = await NewIntegrationAsync(scratch);
            var (_, principal) = await AdminAsync(scratch);
            var definitionId = await NewPlatformDefinitionAsync(scratch);
            var work = kinds.Select(k => Writer(k, scratch, integrationId, principal, definitionId, reauthorize: null))
                .Append(() => DeleteIntegrationAsync(scratch, integrationId, principal))
                .ToArray();

            var results = await Task.WhenAll(work.Select(w => Capture(Task.Run(w))));

            results.OfType<SqlException>().Should().BeEmpty($"round {round}: no deadlock victim ({DeadlockVictim}) or lock failure");
            results.OfType<DbUpdateException>().Should().BeEmpty($"round {round}");
            results.Where(r => r is not null).Should().OnlyContain(
                r => r is KeyNotFoundException || r is IntegrationDeleteBlockedException || r.GetType() == typeof(InvalidOperationException) || r is IntegrationNotFoundException,
                $"round {round}: every failure is a refusal by the protocol");

            var state = await StateAsync(scratch, integrationId);
            if (await IntegrationExistsAsync(scratch, integrationId))
            {
                (await DeleteRowsAsync(scratch, integrationId)).Should().Be(0);
            }
            else
            {
                (state.Values, state.Definitions, state.Subscriptions, state.Routings).Should().Be((0, 0, 0, 0),
                    $"round {round}: nothing refers to a deleted integration");
                (await ActiveSettingSecretsAsync(scratch, integrationId)).Should().Be(0);
                (await DeleteRowsAsync(scratch, integrationId)).Should().Be(1);
            }
        }
    }

    // ---- A subscription that names no integration is neither held up nor a blocker ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_subscription_without_an_integration_is_not_held_up_by_the_delete_and_does_not_block_it(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integrationId = await NewIntegrationAsync(scratch);
        var (_, principal) = await AdminAsync(scratch);
        var definitionId = await NewPlatformDefinitionAsync(scratch);

        var deletePause = new PauseAfterIntegrationLock();
        var delete = Task.Run(() => DeleteIntegrationAsync(scratch, integrationId, principal, deletePause));
        await deletePause.Reached.WaitAsync(TimeSpan.FromSeconds(30));

        var plain = Task.Run(async () =>
        {
            await using var db = scratch.NewDb();
            await Subscriptions(db, principal).AddRecipientAsync(
                definitionId, new NotificationSubscriptionAddRequest(null, NotificationRecipientKinds.Principal, "user-plain", null, false));
        });
        (await Task.WhenAny(plain, Task.Delay(TimeSpan.FromSeconds(20)))).Should().BeSameAs(plain, "it takes no integration lock");
        await plain;

        deletePause.Release();
        await delete;
        (await IntegrationExistsAsync(scratch, integrationId)).Should().BeFalse();
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM NotificationSubscriptions WHERE NotificationDefinitionId = N'{definitionId}' AND IntegrationId IS NULL;"))
            .Should().Be(1, "the integration-independent subscription is untouched");
    }

    // ---- wiring for the delete ----

    private static bool AddsABlocker(WriterKind kind) =>
        kind is WriterKind.DefinitionCreate or WriterKind.SubscriptionAdd or WriterKind.RoutingUpsert or WriterKind.BindingCreate or WriterKind.ScheduleCreate;

    private static IntegrationDeleteBlocker BlockerOf(WriterKind kind) => kind switch
    {
        WriterKind.DefinitionCreate => IntegrationDeleteBlocker.NotificationDefinitions,
        WriterKind.SubscriptionAdd => IntegrationDeleteBlocker.NotificationSubscriptions,
        WriterKind.BindingCreate => IntegrationDeleteBlocker.Bindings,
        WriterKind.ScheduleCreate => IntegrationDeleteBlocker.Schedules,
        _ => IntegrationDeleteBlocker.AttentionRoutings,
    };

    /// <summary>What a change of something that exists needs to exist first, made without contention.</summary>
    private async Task PrepareAsync(WriterKind kind, AuditSqlScratch scratch, string integrationId, Principal principal, string definitionId)
    {
        if (kind == WriterKind.SecretClear)
            await Writer(WriterKind.SecretReplace, scratch, integrationId, principal, definitionId, null)();
        if (kind == WriterKind.SettingReset)
            await Writer(WriterKind.SettingSet, scratch, integrationId, principal, definitionId, null)();
    }

    /// <summary>The production delete on its own context, with the real audited unit and the real H1 primitives on that context.</summary>
    private static async Task DeleteIntegrationAsync(
        AuditSqlScratch scratch,
        string integrationId,
        Principal principal,
        IInterceptor? interceptor = null,
        IAuthorizationEvaluationService? authorization = null,
        Func<AppertureDbContext>? dbFactory = null)
    {
        await using var db = dbFactory?.Invoke() ?? (interceptor is null ? scratch.NewDb() : scratch.NewDb(interceptor));
        var store = new SqlSecretStore(db, new PassThroughProtector(), new DefaultSecretAuditActorProvider());
        var service = new IntegrationManagementService(
            principal,
            authorization ?? new AllowAll(),
            new SqlIntegrationRepository(db),
            new ResourceContextService(new SqlResourceContextRepository(db), TimeProvider.System),
            db,
            new SqlResourceOwnershipMutationGate(db),
            new IntegrationSettingSecretClassifier(new IntegrationSettingSecretInventory(db), store, new TestRegistry()),
            new IntegrationSettingSecretRetirement(store),
            NewUnit(db, principal));
        await service.DeleteIntegrationAsync(new IntegrationId(integrationId));
    }

    private static async Task<Exception?> Capture(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static async Task<bool> IntegrationExistsAsync(AuditSqlScratch scratch, string integrationId) =>
        await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM Integrations WHERE Id = N'{integrationId}';") == 1;

    private static Task<int> DeleteRowsAsync(AuditSqlScratch scratch, string integrationId) =>
        scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Action = N'{AuditActionNames.IntegrationDeleted}' AND TargetId = N'{integrationId}';");

    private static async Task<int> SettingsRemovedAsync(AuditSqlScratch scratch, string integrationId)
    {
        var details = await scratch.ScalarAsync<string>(
            $"SELECT Details FROM AuditLogs WHERE Action = N'{AuditActionNames.IntegrationDeleted}' AND TargetId = N'{integrationId}';");
        using var document = System.Text.Json.JsonDocument.Parse(details);
        return document.RootElement.GetProperty("settingsRemoved").GetInt32();
    }

    /// <summary>The setting-change rows recorded for this integration's settings after its delete committed, by any other operation.</summary>
    private static Task<int> WriterRowsAsync(AuditSqlScratch scratch, string integrationId) =>
        scratch.ScalarAsync<int>(
            $"""
            SELECT COUNT(*) FROM AuditLogs
            WHERE (TargetId LIKE N'{integrationId}:%'
                   AND Action IN (N'{AuditActionNames.IntegrationSettingSet}', N'{AuditActionNames.IntegrationSettingReset}',
                                  N'{AuditActionNames.IntegrationSettingSecretStored}', N'{AuditActionNames.IntegrationSettingSecretRetired}')
                   OR TargetId = N'{integrationId}'
                   AND Action IN (N'{AuditActionNames.IntegrationCreated}', N'{AuditActionNames.IntegrationUpdated}',
                                  N'{AuditActionNames.IntegrationActivated}', N'{AuditActionNames.IntegrationDeactivated}',
                                  N'{AuditActionNames.IntegrationRetired}')
                   OR Action IN (N'{AuditActionNames.BindingCreated}', N'{AuditActionNames.ScheduleCreated}')
                   AND JSON_VALUE(Details, N'$.integrationId') = N'{integrationId}')
              AND OperationEventId <> (SELECT OperationEventId FROM AuditLogs d WHERE d.Action = N'{AuditActionNames.IntegrationDeleted}' AND d.TargetId = N'{integrationId}')
              AND Id > (SELECT Id FROM AuditLogs d WHERE d.Action = N'{AuditActionNames.IntegrationDeleted}' AND d.TargetId = N'{integrationId}');
            """);

    /// <summary>Usable settings-provisioned secrets still named by a setting row or created for this test's setting keys and still active.</summary>
    private static Task<int> ActiveSettingSecretsAsync(AuditSqlScratch scratch, string integrationId)
    {
        _ = integrationId;
        // Every secret these tests provision is for SecretKey; once the integration is deleted none of them may stay usable unless
        // another reference holds it, and none does here.
        return scratch.ScalarAsync<int>(
            $"""
            SELECT COUNT(*) FROM SecretDescriptors s
            WHERE s.Name = N'{PlatformSettingSecretMaterialProvisioner.BuildSecretName(SecretKey)}'
              AND s.State IN (0, 1)
              AND NOT EXISTS (SELECT 1 FROM PlatformSettingValues v WHERE v.ValueJson LIKE N'%' + CONVERT(NVARCHAR(36), s.Id) + N'%');
            """);
    }

    private static async Task<(int Descriptors, int Versions)> SecretCountsAsync(AuditSqlScratch scratch) =>
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM SecretDescriptors;"), await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM SecretVersions;"));

    /// <summary>
    /// Waits until the server lists a request waiting for a lock on a row of <c>Integrations</c>, held by another session. Fails if
    /// the task finishes first: a side that did not wait did not take the lock.
    /// </summary>
    private static async Task WaitUntilAnyRequestWaitsOnIntegrationsAsync(AuditSqlScratch scratch, Task waiter)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (waiter.IsCompleted)
            {
                var detail = waiter.IsFaulted ? waiter.Exception!.GetBaseException().Message : "it completed";
                throw new Xunit.Sdk.XunitException($"The second side did not wait for the integration row ({detail}).");
            }

            var blocked = await scratch.ScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM sys.dm_tran_locks AS l
                INNER JOIN sys.dm_exec_requests AS r ON r.session_id = l.request_session_id
                INNER JOIN sys.partitions AS p ON p.hobt_id = l.resource_associated_entity_id
                WHERE l.request_status = N'WAIT'
                  AND l.resource_database_id = DB_ID()
                  AND l.resource_type = N'KEY'
                  AND OBJECT_NAME(p.object_id) = N'Integrations'
                  AND r.blocking_session_id <> 0;
                """);
            if (blocked > 0)
                return;
            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException("No request was seen waiting on the Integrations row within 30 seconds.");
    }

    /// <summary>
    /// Pauses the first locked read of an integration row on its context until released, with the lock held: the transaction that
    /// took it stays open while the test arranges the other side.
    /// </summary>
    private sealed class PauseAfterIntegrationLock : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _paused;

        public Task Reached => _reached.Task;

        public void Release() => _release.TrySetResult();

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("[Integrations] WITH (UPDLOCK", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _paused, 1) == 0)
            {
                _reached.TrySetResult();
                await _release.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
            }

            return result;
        }
    }

    /// <summary>
    /// A context-sensitive policy: allows the delete except while the integration is owned by one resource context, reading the
    /// owner as committed at the moment of each check, and records what it saw. A delete that authorized only once, before the
    /// lock, would see only the first owner.
    /// </summary>
    private sealed class ContextAwareDeleteAuthorization(AuditSqlScratch scratch, string deniedContext) : IAuthorizationEvaluationService
    {
        public List<string> ContextsSeen { get; } = [];

        public async ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default)
        {
            var owner = await scratch.ScalarAsync<string>(
                $"SELECT ResourceContextId FROM Integrations WHERE Id = N'{context.ResourceIdentity!.ResourceId}';");
            ContextsSeen.Add(owner);
            return !string.Equals(owner, deniedContext, StringComparison.Ordinal);
        }
    }
}
