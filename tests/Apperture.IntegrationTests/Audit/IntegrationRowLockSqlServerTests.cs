using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Notifications;
using Apperture.Core.Domain.Scheduling;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Core.Security.Secrets.Providers;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.4 slice 3, work package 3: the integration row lock in the writers that refer to an integration by id with no foreign
/// key (the four integration setting changes, notification definition create, notification subscription add, attention-routing
/// upsert). Real SQL Server, a competing session that holds the row, and a check of the server's own wait list, so a writer is
/// shown to be waiting on the Integrations row and not merely slow. Each case runs on a catalog with the default locking
/// READ COMMITTED and on one with READ_COMMITTED_SNAPSHOT on. Fails, never skips, without SQL Server.
/// </summary>
/// <remarks>
/// These tests prove the writers take and respect the lock. The delete side (an integration delete racing each writer, in both
/// orders) belongs to the integration delete's own tests: it needs the audited integration delete.
/// </remarks>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed partial class IntegrationRowLockSqlServerTests : IClassFixture<IntegrationRowLockSqlServerTests.Catalogs>
{
    private const string TextKey = "Test.W3.Integration.Text";
    private const string SecretKey = "Test.W3.Integration.Secret";
    private const string OtherContext = "app-w3b";
    private const int DeadlockVictim = 1205;

    private static readonly string Sentinel = "sentinel-w3-secret-" + Guid.NewGuid().ToString("N");

    private readonly Catalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public IntegrationRowLockSqlServerTests(Catalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    public enum WriterKind
    {
        SettingSet,
        SecretReplace,
        SecretClear,
        SettingReset,
        DefinitionCreate,
        SubscriptionAdd,
        RoutingUpsert,
        RoutingClear,
        IntegrationUpdate,
        BindingCreate,
        ScheduleCreate,
    }

    public static IEnumerable<object[]> Cases() =>
        from kind in Enum.GetValues<WriterKind>()
        from rcsi in new[] { false, true }
        select new object[] { kind, rcsi };

    /// <summary>
    /// The writers that can race an integration delete in the generic delete-race cases. The routing clear is left out: it needs an
    /// override to clear, and that override is itself a blocker of the delete, so its two orders have their own cases.
    /// </summary>
    public static IEnumerable<object[]> DeleteRaceCases() =>
        from kind in Enum.GetValues<WriterKind>()
        where kind != WriterKind.RoutingClear
        from rcsi in new[] { false, true }
        select new object[] { kind, rcsi };

    /// <summary>The writers whose own caller supplies a check to run under the lock (a platform-scoped subscription has none).</summary>
    public static IEnumerable<object[]> RecheckingCases() =>
        from kind in new[]
        {
            WriterKind.SettingSet, WriterKind.SecretReplace, WriterKind.SettingReset, WriterKind.DefinitionCreate, WriterKind.RoutingUpsert,
            WriterKind.RoutingClear,
        }
        from rcsi in new[] { false, true }
        select new object[] { kind, rcsi };

    // ---- A and C: the writer waits for the row, then completes when the integration survives ----

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_writer_waits_for_the_integration_row_before_changing_anything_then_completes_when_the_integration_survives(
        WriterKind kind, bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var ctx = await ArrangeAsync(scratch, kind);
        var baseline = await StateAsync(scratch, ctx.IntegrationId);

        await using var holder = await Holder.TakeAsync(scratch.ConnectionString, ctx.IntegrationId);
        var writer = Task.Run(ctx.Writer);
        await WaitUntilBlockedOnIntegrationsAsync(scratch, holder.SessionId, writer);

        writer.IsCompleted.Should().BeFalse("the writer is waiting for the integration row");
        // Read with a short lock timeout: a writer that had already changed something would hold locks that this read hits.
        (await StateAsync(scratch, ctx.IntegrationId)).Should().Be(baseline, "nothing was changed or staged before the lock was granted");

        await holder.CommitAsync();
        await writer;

        AssertCompleted(kind, baseline, await StateAsync(scratch, ctx.IntegrationId));
        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- B: the integration is removed while the writer waits ----

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_writer_that_wakes_to_a_removed_integration_refuses_and_leaves_no_row_secret_or_audit_row(
        WriterKind kind, bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var ctx = await ArrangeAsync(scratch, kind);
        var baseline = await StateAsync(scratch, ctx.IntegrationId);

        await using var holder = await Holder.TakeAsync(scratch.ConnectionString, ctx.IntegrationId);
        var writer = Task.Run(ctx.Writer);
        await WaitUntilBlockedOnIntegrationsAsync(scratch, holder.SessionId, writer);

        await holder.ExecuteAsync("DELETE FROM [Integrations] WHERE [Id] = @id;", ("@id", ctx.IntegrationId));
        await holder.CommitAsync();

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => writer);
        thrown.GetType().Should().Be(
            ExpectedRefusal(kind),
            "a removed integration is a plain refusal, never an audit invariant or a database error");
        thrown.Message.Should().Contain(ctx.IntegrationId);

        // Row counts, every secret's state and version count, the store's evidence and the audit rows: all as they were.
        (await StateAsync(scratch, ctx.IntegrationId)).Should().Be(baseline, "no dependent row, new secret, secret evidence or audit row");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- A refusal by the re-run authorization leaves nothing either ----

    [Theory]
    [MemberData(nameof(RecheckingCases))]
    public async Task A_writer_whose_authorization_fails_once_the_lock_is_held_changes_nothing(WriterKind kind, bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var ctx = await ArrangeAsync(
            scratch, kind, reauthorize: (_, _, _) => throw new UnauthorizedAccessException("no longer permitted"));
        var baseline = await StateAsync(scratch, ctx.IntegrationId);

        var thrown = await Assert.ThrowsAnyAsync<Exception>(ctx.Writer);

        thrown.Should().BeOfType<UnauthorizedAccessException>();
        (await StateAsync(scratch, ctx.IntegrationId)).Should().Be(baseline);
    }

    /// <summary>
    /// A subscription under an integration-scoped definition is authorized against that integration, so the service runs the same
    /// check again once the store holds the row: twice in all, and a refusal the second time stores nothing.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_integration_scoped_definitions_subscription_is_authorized_again_under_the_lock(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integrationId = await NewIntegrationAsync(scratch);
        var (_, principal) = await AdminAsync(scratch);
        var definitionId = await NewIntegrationDefinitionAsync(scratch, integrationId);
        var request = new NotificationSubscriptionAddRequest(integrationId, NotificationRecipientKinds.Principal, "user-scoped", null, false);

        var allowing = new AllowAll();
        await using (var db = scratch.NewDb())
            await Subscriptions(db, principal, allowing).AddRecipientAsync(definitionId, request);
        allowing.IntegrationUpdateChecks.Should().Be(2, "once at the top of the service and once after the store took the integration row lock");

        var denying = new AllowAll { DenyIntegrationUpdateAfterTheFirst = true };
        var before = await StateAsync(scratch, integrationId);
        await using (var db = scratch.NewDb())
        {
            var act = () => Subscriptions(db, principal, denying).AddRecipientAsync(definitionId, request).AsTask();
            await act.Should().ThrowAsync<UnauthorizedAccessException>();
        }

        (await StateAsync(scratch, integrationId)).Should().Be(before);
    }

    // ---- An ownership move that commits while the writer waits is seen by the re-run authorization ----

    [Theory]
    [MemberData(nameof(RecheckingCases))]
    public async Task An_ownership_move_that_commits_before_the_lock_is_granted_is_what_the_rerun_authorization_reads(
        WriterKind kind, bool rcsi)
    {
        var scratch = Scratch(rcsi);
        string? seenByAuthorization = null;
        var ctx = await ArrangeAsync(
            scratch,
            kind,
            reauthorize: async (db, integrationId, ct) =>
            {
                // What the real checks do: read the integration (on the writer's own transaction) and evaluate against it.
                var integration = await new SqlIntegrationRepository(db).GetByIdAsync(new IntegrationId(integrationId), ct);
                seenByAuthorization = integration?.ResourceContextId.Value;
            });

        await using var holder = await Holder.TakeAsync(scratch.ConnectionString, ctx.IntegrationId);
        var writer = Task.Run(ctx.Writer);
        await WaitUntilBlockedOnIntegrationsAsync(scratch, holder.SessionId, writer);

        await holder.ExecuteAsync(
            "UPDATE [Integrations] SET [ResourceContextId] = @to WHERE [Id] = @id;",
            ("@to", OtherContext),
            ("@id", ctx.IntegrationId));
        await holder.CommitAsync();
        await writer;

        seenByAuthorization.Should().Be(OtherContext, "the check ran after the move committed and sees the integration as it now is");
    }

    // ---- D: ordinary writes keep working, and a write that names no integration takes no integration lock ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Routing_inserts_then_updates_one_override_and_subscriptions_without_an_integration_complete_while_the_row_is_held(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var integrationId = await NewIntegrationAsync(scratch);
        var (_, principal) = await AdminAsync(scratch);
        var definitionId = await NewPlatformDefinitionAsync(scratch);

        // Insert, then update: one row, the second command's values.
        await using (var db = scratch.NewDb())
            await Routing(db).UpsertIntegrationRoutingAsync(new IntegrationId(integrationId), AttentionConditionKeys.ExpectedRunMissing, RoutingCommand(enabled: true));
        await using (var db = scratch.NewDb())
            await Routing(db).UpsertIntegrationRoutingAsync(new IntegrationId(integrationId), AttentionConditionKeys.ExpectedRunMissing, RoutingCommand(enabled: false));
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM IntegrationAttentionRoutings WHERE IntegrationId = N'{integrationId}';")).Should().Be(1);
        (await scratch.ScalarAsync<bool>($"SELECT IsEnabled FROM IntegrationAttentionRoutings WHERE IntegrationId = N'{integrationId}';")).Should().BeFalse();

        // While another session holds the integration row, a subscription that names no integration is not held up by it.
        await using var holder = await Holder.TakeAsync(scratch.ConnectionString, integrationId);
        var added = Task.Run(async () =>
        {
            await using var db = scratch.NewDb();
            await Subscriptions(db, principal).AddRecipientAsync(
                definitionId,
                new NotificationSubscriptionAddRequest(null, NotificationRecipientKinds.Principal, "user-plain", null, false));
        });
        (await Task.WhenAny(added, Task.Delay(TimeSpan.FromSeconds(20)))).Should().BeSameAs(added, "it takes no lock on the integration");
        await added;
        await holder.CommitAsync();

        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM NotificationSubscriptions WHERE NotificationDefinitionId = N'{definitionId}' AND IntegrationId IS NULL;"))
            .Should().Be(1);
    }

    // ---- E: a completed sensitive change is atomic with its secret and its audit rows ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_sensitive_replace_that_waited_commits_its_row_its_secret_and_its_audit_rows_together(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var ctx = await ArrangeAsync(scratch, WriterKind.SecretReplace);
        var before = await StateAsync(scratch, ctx.IntegrationId);

        await using var holder = await Holder.TakeAsync(scratch.ConnectionString, ctx.IntegrationId);
        var writer = Task.Run(ctx.Writer);
        await WaitUntilBlockedOnIntegrationsAsync(scratch, holder.SessionId, writer);
        await holder.CommitAsync();
        await writer;

        var after = await StateAsync(scratch, ctx.IntegrationId);
        after.Values.Should().Be(before.Values + 1);
        after.Versions.Should().Be(before.Versions + 1);
        after.AuditLogs.Should().Be(before.AuditLogs + 2, "the setting row and the secret-stored row");
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Details LIKE N'%{Sentinel}%';")).Should().Be(0);
        (await scratch.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM AuditLogs WHERE TargetId LIKE N'%{ctx.IntegrationId}%' AND OperationEventId IS NOT NULL;")).Should().Be(2);
    }

    // ---- F: the lock order introduces no deadlock ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Every_writer_racing_for_one_integration_completes_without_a_deadlock_victim(bool rcsi)
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
            var (adminId, principal) = await AdminAsync(scratch);
            var definitionId = await NewPlatformDefinitionAsync(scratch);
            var writers = kinds.Select(k => Writer(k, scratch, integrationId, principal, definitionId, reauthorize: null)).ToArray();

            var results = await Task.WhenAll(writers.Select(async w =>
            {
                try
                {
                    await Task.Run(w);
                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }));

            results.Where(r => r is not null).Should().BeEmpty($"round {round}: no writer may fail, in particular as a deadlock victim ({DeadlockVictim})");
            _ = adminId;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_writer_is_not_held_up_by_the_ownership_gate_which_it_does_not_take(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var ctx = await ArrangeAsync(scratch, WriterKind.SettingSet);

        // A mover holds the gate (the application lock every ownership change takes first) but not this integration's row.
        await using var mover = scratch.NewDb();
        await using var moverTx = await mover.Database.BeginTransactionAsync();
        await new SqlResourceOwnershipMutationGate(mover).AcquireAsync();

        var writer = Task.Run(ctx.Writer);
        (await Task.WhenAny(writer, Task.Delay(TimeSpan.FromSeconds(20)))).Should().BeSameAs(writer, "the writer takes no gate");
        await writer;
        await moverTx.CommitAsync();
    }

    // ---- wiring ----

    private AuditSqlScratch Scratch(bool rcsi) => rcsi ? _catalogs.Rcsi : _catalogs.Locking;

    /// <summary>The exact refusal a writer that wakes to a removed integration throws.</summary>
    private static Type ExpectedRefusal(WriterKind kind) =>
        IsSettingsWriter(kind) ? typeof(KeyNotFoundException)
        : kind == WriterKind.IntegrationUpdate ? typeof(IntegrationNotFoundException)
        : typeof(InvalidOperationException);

    private static bool IsSettingsWriter(WriterKind kind) =>
        kind is WriterKind.SettingSet or WriterKind.SecretReplace or WriterKind.SecretClear or WriterKind.SettingReset;

    private sealed record Arranged(string IntegrationId, Func<Task> Writer);

    /// <param name="reauthorize">The caller's check, run again under the lock; receives the writer's context and the integration id.</param>
    private async Task<Arranged> ArrangeAsync(
        AuditSqlScratch scratch,
        WriterKind kind,
        Func<AppertureDbContext, string, CancellationToken, ValueTask>? reauthorize = null)
    {
        var integrationId = await NewIntegrationAsync(scratch);
        var (_, principal) = await AdminAsync(scratch);
        var definitionId = await NewPlatformDefinitionAsync(scratch);

        // A change of something that already exists needs it to exist first, made without contention.
        if (kind == WriterKind.SecretClear)
            await Writer(WriterKind.SecretReplace, scratch, integrationId, principal, definitionId, null)();
        if (kind == WriterKind.SettingReset)
            await Writer(WriterKind.SettingSet, scratch, integrationId, principal, definitionId, null)();
        if (kind == WriterKind.RoutingClear)
            await Writer(WriterKind.RoutingUpsert, scratch, integrationId, principal, definitionId, null)();

        return new Arranged(integrationId, Writer(kind, scratch, integrationId, principal, definitionId, reauthorize));
    }

    private Func<Task> Writer(
        WriterKind kind,
        AuditSqlScratch scratch,
        string integrationId,
        Principal principal,
        string definitionId,
        Func<AppertureDbContext, string, CancellationToken, ValueTask>? reauthorize,
        Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor? interceptor = null)
    {
        return async () =>
        {
            await using var db = interceptor is null ? scratch.NewDb() : scratch.NewDb(interceptor);
            Func<CancellationToken, ValueTask>? recheck = reauthorize is null ? null : ct => reauthorize(db, integrationId, ct);
            switch (kind)
            {
                case WriterKind.SettingSet:
                    await Settings(scratch, db, principal).SetAsync(
                        integrationId, TextKey, new PlatformSettingUpdatePayload { ScalarValue = "value-" + Guid.NewGuid().ToString("N")[..6] }, null,
                        reauthorize: recheck);
                    break;
                case WriterKind.SecretReplace:
                    await Settings(scratch, db, principal).SetAsync(
                        integrationId,
                        SecretKey,
                        new PlatformSettingUpdatePayload { SecretOperation = PlatformSettingSecretOperations.Replace, SecretValue = Sentinel },
                        null,
                        reauthorize: recheck);
                    break;
                case WriterKind.SecretClear:
                    await Settings(scratch, db, principal).SetAsync(
                        integrationId,
                        SecretKey,
                        new PlatformSettingUpdatePayload { SecretOperation = PlatformSettingSecretOperations.Clear },
                        null,
                        reauthorize: recheck);
                    break;
                case WriterKind.SettingReset:
                    await Settings(scratch, db, principal).ResetAsync(integrationId, TextKey, reauthorize: recheck);
                    break;
                case WriterKind.DefinitionCreate:
                    await new SqlNotificationDefinitionStore(db).CreateUserDefinitionAsync(
                        new NotificationDefinitionCreateCommand(
                            "Alert " + Guid.NewGuid().ToString("N")[..8], null, integrationId, "{}", "{}", true, null, "actor-w3"),
                        reauthorize: recheck);
                    break;
                case WriterKind.SubscriptionAdd:
                    // A platform-scoped definition with an integration id on the subscription: the case that had no existence check.
                    // Its authorization does not depend on the integration, so there is nothing for the lock to re-run.
                    await Subscriptions(db, principal).AddRecipientAsync(
                        definitionId,
                        new NotificationSubscriptionAddRequest(integrationId, NotificationRecipientKinds.Principal, "user-" + Guid.NewGuid().ToString("N")[..6], null, false));
                    break;
                case WriterKind.RoutingUpsert:
                    await Routing(db).UpsertIntegrationRoutingAsync(
                        new IntegrationId(integrationId), AttentionConditionKeys.ExpectedRunMissing, RoutingCommand(enabled: true), reauthorize: recheck);
                    break;
                case WriterKind.RoutingClear:
                    await Routing(db).ClearIntegrationRoutingAsync(
                        new IntegrationId(integrationId), AttentionConditionKeys.ExpectedRunMissing, reauthorize: recheck);
                    break;
                case WriterKind.BindingCreate:
                    // The audited binding create: operation lock, ownership gate, then the integration row, then the connection row.
                    await EnsureSharedConnectionAsync(scratch);
                    await new BindingService(
                            principal,
                            new AllowAll(),
                            new SqlBindingRepository(db),
                            new SqlConnectionRepository(db),
                            new EveryConnectionEligible(db),
                            new SqlIntegrationRepository(db),
                            db,
                            new SqlResourceOwnershipMutationGate(db),
                            NewUnit(db, principal))
                        .CreateBindingAsync(
                            "Sample.Mod", "slot-" + Guid.NewGuid().ToString("N")[..6], "Contract.Type", new ConnectionId(SharedConnectionId), new IntegrationId(integrationId));
                    break;
                case WriterKind.ScheduleCreate:
                    // The audited schedule create with its initial assignment: operation lock, ownership gate, then the integration row.
                    await new SqlScheduleManagementService(
                            new SqlScheduleRepository(db),
                            new RaceClaimOptions(),
                            TimeProvider.System,
                            new ResourceContextService(new SqlResourceContextRepository(db), TimeProvider.System),
                            new SqlIntegrationRepository(db),
                            db,
                            new SqlResourceOwnershipMutationGate(db),
                            principal,
                            NewUnit(db, principal))
                        .CreateScheduleWithAssignmentAsync(
                            new ScheduleDefinition(
                                new ScheduleId("sch-w3-" + Guid.NewGuid().ToString("N")[..10]),
                                "Race schedule",
                                "0 0 * * *",
                                "UTC",
                                Enabled: false,
                                new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
                                ExecutionPolicyRef: null,
                                Version: 1,
                                new IntegrationId(integrationId)),
                            new RuntimePlaneId("local"),
                            null);
                    break;
                case WriterKind.IntegrationUpdate:
                    // The audited update: its own operation lock, then the integration row (a rename takes no ownership gate).
                    await new IntegrationManagementService(
                            principal,
                            new AllowAll(),
                            new SqlIntegrationRepository(db),
                            new ResourceContextService(new SqlResourceContextRepository(db), TimeProvider.System),
                            db,
                            new SqlResourceOwnershipMutationGate(db),
                            auditedUnitOfWork: NewUnit(db, principal))
                        .UpdateIntegrationAsync(
                            new IntegrationId(integrationId), new UpdateIntegrationRequest(DisplayName: "Renamed " + Guid.NewGuid().ToString("N")[..6]));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        };
    }

    private static void AssertCompleted(WriterKind kind, State before, State after)
    {
        switch (kind)
        {
            case WriterKind.SettingSet:
                after.Values.Should().Be(before.Values + 1);
                after.AuditLogs.Should().Be(before.AuditLogs + 1);
                break;
            case WriterKind.SecretReplace:
                after.Values.Should().Be(before.Values + 1);
                after.Versions.Should().Be(before.Versions + 1);
                after.AuditLogs.Should().Be(before.AuditLogs + 2);
                break;
            case WriterKind.SecretClear:
                after.Values.Should().Be(before.Values - 1);
                after.Secrets.Should().NotBe(before.Secrets, "the secret the setting owned was disabled");
                after.AuditLogs.Should().Be(before.AuditLogs + 2, "the setting row and the secret-retired row");
                break;
            case WriterKind.SettingReset:
                after.Values.Should().Be(before.Values - 1);
                after.AuditLogs.Should().Be(before.AuditLogs + 1);
                break;
            case WriterKind.DefinitionCreate:
                after.Definitions.Should().Be(before.Definitions + 1);
                break;
            case WriterKind.SubscriptionAdd:
                after.Subscriptions.Should().Be(before.Subscriptions + 1);
                break;
            case WriterKind.RoutingUpsert:
                after.Routings.Should().Be(before.Routings + 1);
                break;
            case WriterKind.RoutingClear:
                after.Routings.Should().Be(before.Routings - 1, "the override that existed before the writer waited was removed");
                break;
            case WriterKind.IntegrationUpdate:
                after.AuditLogs.Should().Be(before.AuditLogs + 1, "the one integration.updated row");
                break;
            case WriterKind.ScheduleCreate:
                after.Schedules.Should().Be(before.Schedules + 1);
                after.AuditLogs.Should().Be(before.AuditLogs + 1, "the one schedule.created row, which covers the assignment");
                break;
            case WriterKind.BindingCreate:
                after.Bindings.Should().Be(before.Bindings + 1);
                after.AuditLogs.Should().Be(before.AuditLogs + 1, "the one binding.created row");
                break;
        }
    }

    private static IntegrationSettingsService Settings(AuditSqlScratch scratch, AppertureDbContext db, Principal principal)
    {
        _ = scratch;
        var registry = new TestRegistry();
        var repository = new SqlPlatformSettingValueRepository(db);
        var integrations = new SqlIntegrationRepository(db);
        var coordinator = new PlatformSettingOptionProviderCoordinator(
            new PlatformSettingOptionProviderRegistry([]),
            new PlatformSettingOptionProviderAuthorization(principal, new AllowAll(), integrations),
            new IntegrationSettingOptionProviderAuthorization(principal, new AllowAll(), integrations));
        return new IntegrationSettingsService(
            registry,
            repository,
            new IntegrationSettingsResolver(registry, repository),
            coordinator,
            new PlatformSettingSecretMaterialProvisioner(new SqlSecretStore(db, new PassThroughProtector(), new DefaultSecretAuditActorProvider())),
            integrations,
            NewUnit(db, principal));
    }

    private static NotificationSubscriptionService Subscriptions(AppertureDbContext db, Principal principal, AllowAll? authorization = null)
    {
        // The resolver and the managed-contact provisioner are not touched when the recipient is given by kind and reference.
        var allow = authorization ?? new AllowAll();
        return new NotificationSubscriptionService(
            new SqlNotificationSubscriptionStore(db),
            new SqlNotificationDefinitionStore(db),
            null!,
            null!,
            new NotificationDefinitionAuthorization(principal, allow, allow, new SqlIntegrationRepository(db)),
            principal);
    }

    private static AttentionRoutingManagementService Routing(AppertureDbContext db) =>
        new(db, new SqlIntegrationRepository(db), null!);

    private static AttentionRoutingUpsertCommand RoutingCommand(bool enabled) =>
        new(enabled, true, true, true, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), null, null);

    // ---- data ----

    private const string SharedConnectionId = "conn-w3-shared";
    private const string EmptyJson = "{}";

    /// <summary>One Active connection in the Shared context that every binding writer of a catalog binds. Idempotent.</summary>
    private static Task EnsureSharedConnectionAsync(AuditSqlScratch scratch) =>
        scratch.ExecuteAsync(
            $"""
            IF NOT EXISTS (SELECT 1 FROM Connections WHERE Id = N'{SharedConnectionId}')
              INSERT INTO Connections (Id, ConnectorDefinitionId, Name, ConfigJson, SecretRefsJson, ResourceContextId, State, LastVerificationStatus)
              VALUES (N'{SharedConnectionId}', N'test.connector', N'Shared Connection', N'{EmptyJson}', N'{EmptyJson}', N'shared', 0, 0);
            """);

    private sealed class RaceClaimOptions : ISchedulerClaimOptions
    {
        public int ClaimLeaseMinutes => 5;
    }

    /// <summary>Every stored connection is eligible: these races are about locks, not about connector contracts.</summary>
    private sealed class EveryConnectionEligible(AppertureDbContext db) : IBindingEligibilityService
    {
        public async ValueTask<IReadOnlyList<Connection>> GetEligibleConnectionsAsync(
            string moduleId,
            string slotName,
            string contractType,
            ResourceContextId? owningResourceContextId = null,
            CancellationToken cancellationToken = default)
        {
            var repository = new SqlConnectionRepository(db);
            var connection = await repository.GetByIdAsync(new ConnectionId(SharedConnectionId), cancellationToken);
            return connection is null ? [] : [connection];
        }
    }

    private static async Task<string> NewIntegrationAsync(AuditSqlScratch scratch)
    {
        var id = "int-w3-" + Guid.NewGuid().ToString("N")[..10];
        await scratch.ExecuteAsync(
            $"""
            INSERT INTO Integrations (Id, DisplayName, ModuleId, ScopeKey, ResourceContextId, State, CreatedAt, UpdatedAt)
            VALUES (N'{id}', N'Lock Test Integration', N'Sample.Mod', N'instance', N'shared', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);
        return id;
    }

    private static async Task<(string UserId, Principal Principal)> AdminAsync(AuditSqlScratch scratch)
    {
        var id = NewUserId();
        await scratch.AddUserAsync(id, NewUsername("admin"));
        return (id, new Principal(id));
    }

    private static async Task<string> NewPlatformDefinitionAsync(AuditSqlScratch scratch)
    {
        var id = "ndef-w3-" + Guid.NewGuid().ToString("N")[..10];
        await using var db = scratch.NewDb();
        db.NotificationDefinitions.Add(new NotificationDefinitionEntity
        {
            Id = id,
            DisplayName = "Platform alert " + id,
            SourceKind = NotificationSourceKinds.PlatformEvent,
            Origin = NotificationDefinitionOrigins.PlatformBuiltIn,
            Scope = NotificationDefinitionScopes.Platform,
            Mutability = NotificationDefinitionMutabilities.ReadOnly,
            IsEnabled = true,
            DefaultSeverity = "warning",
            RuleJson = "{}",
            TemplateJson = "{}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<string> NewIntegrationDefinitionAsync(AuditSqlScratch scratch, string integrationId)
    {
        var id = "ndef-w3i-" + Guid.NewGuid().ToString("N")[..10];
        await using var db = scratch.NewDb();
        db.NotificationDefinitions.Add(new NotificationDefinitionEntity
        {
            Id = id,
            DisplayName = "Integration alert " + id,
            SourceKind = NotificationSourceKinds.PlatformEvent,
            Origin = NotificationDefinitionOrigins.UserCreated,
            Scope = NotificationDefinitionScopes.Integration,
            Mutability = NotificationDefinitionMutabilities.FullyEditable,
            IntegrationId = integrationId,
            IsEnabled = true,
            DefaultSeverity = "warning",
            RuleJson = "{}",
            TemplateJson = "{}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    // ---- state and blocking ----

    private sealed record State(
        int Values, string Secrets, int Versions, int SecretAudits, int AuditLogs, int Definitions, int Subscriptions, int Routings, int Bindings, int Schedules);

    private static async Task<State> StateAsync(AuditSqlScratch scratch, string integrationId)
    {
        // A short lock timeout: if the writer had changed anything before it held the row, this read would run into its locks.
        var rows = await scratch.RowsAsync(
            $"""
            SET LOCK_TIMEOUT 3000;
            SELECT
              (SELECT COUNT(*) FROM PlatformSettingValues WHERE ScopeKind = N'{PlatformSettingScopeConstants.IntegrationKind}' AND ScopeId = N'{integrationId}') AS Vals,
              ISNULL((SELECT STRING_AGG(CONCAT(Id, N':', State, N':', ISNULL(ActiveVersion, N'')), N'|') WITHIN GROUP (ORDER BY Id) FROM SecretDescriptors), N'') AS Secrets,
              (SELECT COUNT(*) FROM SecretVersions) AS Versions,
              (SELECT COUNT(*) FROM SecretAudits) AS SecretAudits,
              (SELECT COUNT(*) FROM AuditLogs) AS AuditLogs,
              (SELECT COUNT(*) FROM NotificationDefinitions WHERE IntegrationId = N'{integrationId}') AS Defs,
              (SELECT COUNT(*) FROM NotificationSubscriptions WHERE IntegrationId = N'{integrationId}') AS Subs,
              (SELECT COUNT(*) FROM IntegrationAttentionRoutings WHERE IntegrationId = N'{integrationId}') AS Routs,
              (SELECT COUNT(*) FROM Bindings WHERE IntegrationId = N'{integrationId}') AS Binds,
              (SELECT COUNT(*) FROM ScheduleDefinitions WHERE IntegrationId = N'{integrationId}') AS Scheds;
            """);
        var row = rows.Single();
        return new State(
            (int)row["Vals"]!, (string)row["Secrets"]!, (int)row["Versions"]!, (int)row["SecretAudits"]!,
            (int)row["AuditLogs"]!, (int)row["Defs"]!, (int)row["Subs"]!, (int)row["Routs"]!, (int)row["Binds"]!, (int)row["Scheds"]!);
    }

    /// <summary>
    /// Waits until the server itself lists a request, blocked by <paramref name="holderSession"/>, waiting for a lock on a row of
    /// <c>Integrations</c>. Fails if the writer finishes first: a writer that did not wait did not take the lock.
    /// </summary>
    private static async Task WaitUntilBlockedOnIntegrationsAsync(AuditSqlScratch scratch, int holderSession, Task writer)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (writer.IsCompleted)
            {
                var detail = writer.IsFaulted ? writer.Exception!.GetBaseException().Message : "it completed";
                throw new Xunit.Sdk.XunitException($"The writer did not wait for the integration row ({detail}).");
            }

            var blocked = await scratch.ScalarAsync<int>(
                $"""
                SELECT COUNT(*)
                FROM sys.dm_tran_locks AS l
                INNER JOIN sys.dm_exec_requests AS r ON r.session_id = l.request_session_id
                INNER JOIN sys.partitions AS p ON p.hobt_id = l.resource_associated_entity_id
                WHERE l.request_status = N'WAIT'
                  AND l.resource_database_id = DB_ID()
                  AND l.resource_type = N'KEY'
                  AND OBJECT_NAME(p.object_id) = N'Integrations'
                  AND r.blocking_session_id = {holderSession};
                """);
            if (blocked > 0)
                return;
            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException("No request was seen waiting on the Integrations row within 30 seconds.");
    }

    /// <summary>A second session that holds the integration row (<c>UPDLOCK</c>, as every locking writer takes it) in an open transaction.</summary>
    private sealed class Holder : IAsyncDisposable
    {
        private readonly SqlConnection _connection;
        private SqlTransaction? _transaction;

        private Holder(SqlConnection connection, SqlTransaction transaction, int sessionId)
        {
            _connection = connection;
            _transaction = transaction;
            SessionId = sessionId;
        }

        public int SessionId { get; }

        public static async Task<Holder> TakeAsync(string connectionString, string integrationId)
        {
            var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
            await using (var lockCommand = connection.CreateCommand())
            {
                lockCommand.Transaction = transaction;
                lockCommand.CommandText = "SELECT [Id] FROM [Integrations] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = @id;";
                lockCommand.Parameters.AddWithValue("@id", integrationId);
                (await lockCommand.ExecuteScalarAsync()).Should().NotBeNull("the integration exists and is now held");
            }

            await using var spid = connection.CreateCommand();
            spid.Transaction = transaction;
            spid.CommandText = "SELECT CAST(@@SPID AS int);";
            return new Holder(connection, transaction, (int)(await spid.ExecuteScalarAsync())!);
        }

        public async Task ExecuteAsync(string sql, params (string Name, string Value)[] parameters)
        {
            await using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value);
            await command.ExecuteNonQueryAsync();
        }

        public async Task CommitAsync()
        {
            await _transaction!.CommitAsync();
            _transaction = null;
        }

        public async ValueTask DisposeAsync()
        {
            if (_transaction is not null)
            {
                try
                {
                    await _transaction.RollbackAsync();
                }
                catch
                {
                    // The connection is closed next either way.
                }
            }

            await _connection.DisposeAsync();
        }
    }

    // ---- catalogs ----

    /// <summary>Two migrated throwaway catalogs: the default locking READ COMMITTED, and one with READ_COMMITTED_SNAPSHOT on.</summary>
    public sealed class Catalogs : IAsyncLifetime
    {
        public AuditSqlScratch Locking { get; private set; } = null!;
        public AuditSqlScratch Rcsi { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            Locking = await AuditSqlScratch.CreateAsync("Apperture_Aud14LockA_");
            Rcsi = await AuditSqlScratch.CreateAsync("Apperture_Aud14LockB_");
            await EnableRcsiAsync(Rcsi);

            (await Locking.ScalarAsync<bool>(RcsiQuery(Locking))).Should().BeFalse();
            (await Rcsi.ScalarAsync<bool>(RcsiQuery(Rcsi))).Should().BeTrue("the second catalog must really run under row versioning");

            foreach (var scratch in new[] { Locking, Rcsi })
            {
                // A second context for the ownership-move case. "shared" is seeded by the migrations.
                await scratch.ExecuteAsync(
                    $"""
                    INSERT INTO ResourceContexts (Id, Name, Slug, Kind, State, IsReserved, CreatedAtUtc, UpdatedAtUtc)
                    VALUES (N'{OtherContext}', N'App W3 B', N'{OtherContext}', N'{ResourceContextKinds.Application}', N'{ResourceContextStates.Active}', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
                    """);
            }
        }

        public async Task DisposeAsync()
        {
            await Locking.DisposeAsync();
            await Rcsi.DisposeAsync();
        }

        private static string RcsiQuery(AuditSqlScratch scratch) =>
            $"SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = N'{scratch.Catalog}';";

        private static async Task EnableRcsiAsync(AuditSqlScratch scratch)
        {
            var scratchConnection = new SqlConnectionStringBuilder(scratch.ConnectionString);
            await using (var connection = new SqlConnection(new SqlConnectionStringBuilder(scratch.ConnectionString) { InitialCatalog = "master" }.ConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"ALTER DATABASE [{scratch.Catalog}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;";
                await command.ExecuteNonQueryAsync();
            }

            // The switch closed this catalog's idle pooled connections; drop that pool so none is reused half-dead.
            SqlConnection.ClearPool(new SqlConnection(scratchConnection.ConnectionString));
        }
    }

    // ---- doubles ----

    private sealed class TestRegistry : IIntegrationSettingDefinitionRegistry
    {
        private static readonly PlatformSettingValueScopeKind[] IntegrationScope = [PlatformSettingValueScopeKind.Integration];

        private static readonly IReadOnlyList<PlatformSettingDefinition> All =
        [
            new()
            {
                Key = TextKey,
                DisplayName = "Integration text (test)",
                Description = "Plain integration-scoped string.",
                Category = PlatformSettingCategory.PlatformEvents,
                ValueType = PlatformSettingValueType.String,
                InputKind = PlatformSettingInputKind.Text,
                AllowedValueScopes = IntegrationScope,
                ConfigurationPath = "Test:W3:Integration:Text",
                DefaultValue = string.Empty,
                AuditValue = PlatformSettingAuditValuePolicy.Record,
            },
            new()
            {
                Key = SecretKey,
                DisplayName = "Integration secret (test)",
                Description = "Sensitive integration-scoped setting.",
                Category = PlatformSettingCategory.PlatformEvents,
                ValueType = PlatformSettingValueType.String,
                InputKind = PlatformSettingInputKind.Secret,
                IsSensitive = true,
                AllowedValueScopes = IntegrationScope,
                ConfigurationPath = "Test:W3:Integration:Secret",
                DefaultValue = string.Empty,
            },
        ];

        public IReadOnlyList<PlatformSettingDefinition> GetAll() => All;

        public PlatformSettingDefinition? TryGet(string key) => All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));
    }

    /// <summary>Allows everything, counts the integration-update checks, and can be told to deny them after the first.</summary>
    private sealed class AllowAll : IAuthorizationService, IAuthorizationEvaluationService
    {
        public int IntegrationUpdateChecks { get; private set; }

        public bool DenyIntegrationUpdateAfterTheFirst { get; set; }

        public ValueTask<bool> EvaluateAsync(
            IActor actor,
            PermissionDescriptor permission,
            ResourceDescriptor? resource,
            IGovernedResourceIdentity? resourceIdentity = null,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(true);

        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default)
        {
            if (string.Equals(context.Permission.Action, ActionConstants.IntegrationUpdate, StringComparison.Ordinal))
            {
                IntegrationUpdateChecks++;
                if (DenyIntegrationUpdateAfterTheFirst && IntegrationUpdateChecks > 1)
                    return ValueTask.FromResult(false);
            }

            return ValueTask.FromResult(true);
        }
    }

    private sealed class PassThroughProtector : ISecretProtector
    {
        public ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, string keyId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(plaintext.ToArray());

        public ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedPayload, string keyId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(protectedPayload.ToArray());
    }
}
