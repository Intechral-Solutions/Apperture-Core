using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Notifications;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Shared.Kernel;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.NotificationSqlHarness;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.4 slice 6: the administrator subscription writers (add, add with a new managed contact, remove, patch, user opt-out set and
/// remove) as audited units against SQL Server on locking READ COMMITTED and READ_COMMITTED_SNAPSHOT catalogs. M7: the rows of every
/// action, including the managed contact row a subscription creates in the same unit, and the absence of every address and name.
/// M6 for every operation. Referential integrity against a definition delete and against an integration delete is proven with the
/// writers queued on the same rows in both orders. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class NotificationSubscriptionSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private readonly AuditIsolationCatalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public NotificationSubscriptionSqlServerTests(AuditIsolationCatalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    private static Task<AuditSqlScratch> NewScratchAsync(bool rcsi) => AuditIsolation.CreateAsync("Apperture_Aud14Subs", rcsi);

    public enum Op
    {
        Add,
        AddWithNewContact,
        Remove,
        PatchDisable,
        PatchMode,
        OptOutSet,
        OptOutRemove,
    }

    public static IEnumerable<object[]> Cases() =>
        from op in Enum.GetValues<Op>()
        from rcsi in new[] { false, true }
        select new object[] { op, rcsi };

    /// <summary>The M6 matrix runs on the operations that differ in kind: an addition, one that also creates a contact, a removal and an opt-out.</summary>
    private static readonly Op[] M6Ops = [Op.Add, Op.AddWithNewContact, Op.Remove, Op.OptOutSet];

    public static IEnumerable<object[]> Ops() => M6Ops.Select(op => new object[] { op });

    public static IEnumerable<object[]> OutcomeCases() =>
        from op in M6Ops
        from committed in new[] { true, false }
        select new object[] { op, committed };

    private sealed record Arranged(
        string DefinitionId,
        string Action,
        string[] AllActions,
        Func<AppertureDbContext, IAuditedUnitOfWork, Task> Run,
        Action<Dictionary<string, object?>> AssertDetails,
        string? Address = null);

    private static async Task<Arranged> ArrangeAsync(AuditSqlScratch scratch, Op op, IPrincipalContext principal)
    {
        var integrationId = await SeedIntegrationAsync(scratch);
        var definitionId = await SeedDefinitionAsync(scratch, integrationId);
        switch (op)
        {
            case Op.Add:
                return new Arranged(
                    definitionId, AuditActionNames.NotificationSubscriptionAdded, [AuditActionNames.NotificationSubscriptionAdded],
                    (db, unit) => Subscriptions(db, principal, unit).AddRecipientAsync(
                        definitionId, new NotificationSubscriptionAddRequest(integrationId, NotificationRecipientKinds.Principal, "user-new", null, false)).AsTask(),
                    row =>
                    {
                        Detail(row, "recipientKind").Should().Be(NotificationRecipientKinds.Principal);
                        Detail(row, "recipientRef").Should().Be("user-new");
                        Detail(row, "deliveryMode").Should().Be(NotificationDeliveryModes.Immediate);
                        Detail(row, "integrationId").Should().Be(integrationId);
                    });

            case Op.AddWithNewContact:
            {
                var address = $"sentinel.{Guid.NewGuid():N}@example.com";
                return new Arranged(
                    definitionId, AuditActionNames.NotificationSubscriptionAdded,
                    [AuditActionNames.NotificationSubscriptionAdded, AuditActionNames.ManagedContactCreated],
                    (db, unit) => Subscriptions(db, principal, unit).AddRecipientAsync(
                        definitionId,
                        new NotificationSubscriptionAddRequest(integrationId, null, null, $"{PersonSentinel} <{address}>", true)).AsTask(),
                    row =>
                    {
                        Detail(row, "recipientKind").Should().Be(NotificationRecipientKinds.ManagedContact);
                        Detail(row, "managedContactCreated").Should().Be("true");
                    },
                    address);
            }

            case Op.Remove:
            {
                var sub = await SeedSubscriptionAsync(scratch, definitionId, integrationId, "user-doomed");
                return new Arranged(
                    definitionId, AuditActionNames.NotificationSubscriptionRemoved, [AuditActionNames.NotificationSubscriptionRemoved],
                    (db, unit) => Subscriptions(db, principal, unit).RemoveAsync(definitionId, sub).AsTask(),
                    row => Detail(row, "recipientRef").Should().Be("user-doomed"));
            }

            case Op.PatchDisable:
            {
                var sub = await SeedSubscriptionAsync(scratch, definitionId, integrationId);
                return new Arranged(
                    definitionId, AuditActionNames.NotificationSubscriptionUpdated, [AuditActionNames.NotificationSubscriptionUpdated],
                    (db, unit) => Subscriptions(db, principal, unit).PatchAsync(definitionId, sub, new NotificationSubscriptionPatchRequest(false, null)).AsTask(),
                    row => Detail(row, "change").Should().Be("disabled"));
            }

            case Op.PatchMode:
            {
                var sub = await SeedSubscriptionAsync(scratch, definitionId, integrationId);
                return new Arranged(
                    definitionId, AuditActionNames.NotificationSubscriptionUpdated, [AuditActionNames.NotificationSubscriptionUpdated],
                    (db, unit) => Subscriptions(db, principal, unit).PatchAsync(definitionId, sub, new NotificationSubscriptionPatchRequest(null, NotificationDeliveryModes.Muted)).AsTask(),
                    row =>
                    {
                        Detail(row, "change").Should().Be("delivery_mode");
                        Detail(row, "deliveryMode").Should().Be(NotificationDeliveryModes.Muted);
                    });
            }

            case Op.OptOutSet:
                return new Arranged(
                    definitionId, AuditActionNames.NotificationSubscriptionUserOptOutSet, [AuditActionNames.NotificationSubscriptionUserOptOutSet],
                    (db, unit) => Subscriptions(db, principal, unit).SetUserOptOutAsync(definitionId, new NotificationUserOptOutRequest("user-out", "team-1")).AsTask(),
                    row =>
                    {
                        Detail(row, "userId").Should().Be("user-out");
                        Detail(row, "operationalTeamId").Should().Be("team-1");
                    });

            default:
                await SeedOptOutAsync(scratch, definitionId, "user-out", "team-1");
                return new Arranged(
                    definitionId, AuditActionNames.NotificationSubscriptionUserOptOutRemoved, [AuditActionNames.NotificationSubscriptionUserOptOutRemoved],
                    (db, unit) => Subscriptions(db, principal, unit).RemoveUserOptOutAsync(definitionId, new NotificationUserOptOutRequest("user-out", "team-1")).AsTask(),
                    row => Detail(row, "userId").Should().Be("user-out"));
        }
    }

    // ---- M7 ----

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_committed_operation_records_its_rows_attributed_to_the_administrator_without_any_address_or_name(Op op, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (admin, principal) = await AdminAsync(scratch);
        var arranged = await ArrangeAsync(scratch, op, principal);
        var before = await SnapshotAsync(scratch);

        await using (var db = scratch.NewDb())
            await arranged.Run(db, NewUnit(db, principal));

        (await SnapshotAsync(scratch)).Should().NotBe(before);
        var primary = (await scratch.RowsAsync(
            $"SELECT * FROM AuditLogs WHERE Action = N'{arranged.Action}' AND JSON_VALUE(Details, '$.definitionId') = N'{arranged.DefinitionId}';")).Single();
        var rows = await scratch.RowsAsync($"SELECT * FROM AuditLogs WHERE OperationEventId = N'{primary["EventId"]}' ORDER BY Id;");
        rows.Select(r => (string)r["Action"]!).Should().BeEquivalentTo(arranged.AllActions, "one primary row and, for a subscription that creates its contact, the contact's row");
        primary["OperationEventId"].Should().Be(primary["EventId"]);
        foreach (var row in rows)
        {
            row["Actor"].Should().Be(admin);
            row["Outcome"].Should().Be(AuditOutcomeCodes.Succeeded);
            row["Category"].Should().Be(AuditCategories.Configuration);
            row["Severity"].Should().Be(AuditSeverities.Standard);
            row["Basis"].Should().Be(AuditBases.Change);
            row["OperationEventId"].Should().Be(primary["EventId"]);
        }

        primary["TargetType"].Should().Be(AuditTargetTypes.NotificationSubscription);
        primary["ResourceContextId"].Should().Be(ResourceContextIds.SharedValue);
        arranged.AssertDetails(primary);
        if (op == Op.AddWithNewContact)
        {
            var contact = rows.Single(r => (string)r["Action"]! == AuditActionNames.ManagedContactCreated);
            contact["TargetType"].Should().Be(AuditTargetTypes.ManagedContact);
            Detail(contact, "via").Should().Be("subscription");
            Detail(primary, "recipientRef").Should().Be((string)contact["TargetId"]!, "the subscription names the contact the unit created");
        }

        var text = AuditText(rows);
        text.Should().NotContain(AddressSentinel).And.NotContain(PersonSentinel).And.NotContain("example.com");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- M6 ----

    private async Task<AuditedOperationM6Runner> RunnerAsync(AuditSqlScratch scratch, Op op, IPrincipalContext principal)
    {
        var arranged = await ArrangeAsync(scratch, op, principal);
        return new AuditedOperationM6Runner(scratch, () => SnapshotAsync(scratch), arranged.Action, arranged.Run, PersonSentinel);
    }

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task An_audit_insert_failure_leaves_nothing_not_even_a_contact(Op op)
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, op, principal)).AuditFailureLeavesNothingAsync(principal);
    }

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task A_lost_acknowledgement_is_verified_and_reconciled_without_running_the_attempt_again(Op op)
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, op, principal)).LostAcknowledgementIsVerifiedAndReconciledAsync(principal);
    }

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task A_confirmed_rollback_reruns_the_operation_once(Op op)
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, op, principal)).ConfirmedRollbackRerunsOnceAsync(principal);
    }

    [Theory]
    [MemberData(nameof(OutcomeCases))]
    public async Task An_unknown_outcome_is_wholly_committed_or_wholly_absent(Op op, bool committed)
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, op, principal)).UnknownOutcomeIsWhollyCommittedOrWhollyAbsentAsync(principal, committed);
    }

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task An_operation_lock_that_cannot_be_taken_is_unknown_and_changes_nothing(Op op)
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, op, principal)).OperationLockNotGrantedIsUnknownAndChangesNothingAsync(principal);
    }

    // ---- refusals and no-ops write nothing ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Requests_that_change_nothing_or_are_refused_write_nothing_and_record_nothing(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var integrationId = await SeedIntegrationAsync(scratch);
        var definitionId = await SeedDefinitionAsync(scratch, integrationId);
        var sub = await SeedSubscriptionAsync(scratch, definitionId, integrationId, "user-1");
        await SeedOptOutAsync(scratch, definitionId, "user-2");
        var before = await SnapshotAsync(scratch);
        var auditBefore = (await AuditRowsAsync(scratch)).Count;

        await using (var db = scratch.NewDb())
        {
            var service = Subscriptions(db, principal, NewUnit(db, principal));
            await service.PatchAsync(definitionId, sub, new NotificationSubscriptionPatchRequest(true, null));
            await service.PatchAsync(definitionId, sub, new NotificationSubscriptionPatchRequest(null, NotificationDeliveryModes.Immediate));
            await service.PatchAsync(definitionId, sub, new NotificationSubscriptionPatchRequest(null, null));
            await service.SetUserOptOutAsync(definitionId, new NotificationUserOptOutRequest("user-2", null));
            (await service.RemoveUserOptOutAsync(definitionId, new NotificationUserOptOutRequest("user-missing", null))).Should().BeFalse();
            (await service.RemoveAsync(definitionId, "nsub_missing")).Should().BeFalse();
            await FluentActions.Awaiting(() => service.AddRecipientAsync(
                definitionId, new NotificationSubscriptionAddRequest(integrationId, NotificationRecipientKinds.Principal, "user-1", null, false)).AsTask())
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*already subscribed*");
            await FluentActions.Awaiting(() => service.AddRecipientAsync(
                definitionId, new NotificationSubscriptionAddRequest(integrationId, null, null, "nobody@example.com", false)).AsTask())
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*No managed contact found*");
        }

        (await SnapshotAsync(scratch)).Should().Be(before);
        (await AuditRowsAsync(scratch)).Should().HaveCount(auditBefore);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- races, queued on the integration row in both orders ----

    private sealed record Outcome<T>(T? Value, Exception? Error);

    private static async Task<Outcome<T>> RunAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return new Outcome<T>(await Task.Run(operation), null);
        }
        catch (Exception ex)
        {
            return new Outcome<T>(default, ex);
        }
    }

    public static IEnumerable<object[]> Orders() =>
        from addFirst in new[] { true, false }
        from rcsi in new[] { false, true }
        select new object[] { addFirst, rcsi };

    [Theory]
    [MemberData(nameof(Orders))]
    public async Task A_subscription_racing_a_definition_delete_never_leaves_an_orphan_in_either_order(bool addFirst, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var integrationId = await SeedIntegrationAsync(scratch);
        var definitionId = await SeedDefinitionAsync(scratch, integrationId);

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, "Integrations", integrationId);
        Task<Outcome<NotificationSubscription>> Add() => RunAsync(async () =>
        {
            await using var db = scratch.NewDb();
            return await Subscriptions(db, principal, NewUnit(db, principal)).AddRecipientAsync(
                definitionId, new NotificationSubscriptionAddRequest(integrationId, NotificationRecipientKinds.Principal, "user-raced", null, false));
        });
        Task<Outcome<bool>> Delete() => RunAsync(async () =>
        {
            await using var db = scratch.NewDb();
            return await Definitions(db, principal, NewUnit(db, principal)).DeleteAsync(definitionId);
        });

        Task<Outcome<NotificationSubscription>> add;
        Task<Outcome<bool>> delete;
        if (addFirst)
        {
            add = Add();
            await holder.WaitUntilBlockedAsync(scratch, "Integrations", add);
            delete = Delete();
            await holder.WaitUntilBlockedAsync(scratch, "Integrations", Task.WhenAny(add, delete), minimumWaiters: 2);
        }
        else
        {
            delete = Delete();
            await holder.WaitUntilBlockedAsync(scratch, "Integrations", delete);
            add = Add();
            await holder.WaitUntilBlockedAsync(scratch, "Integrations", Task.WhenAny(add, delete), minimumWaiters: 2);
        }

        await holder.CommitAsync();
        var added = await add;
        var deleted = await delete;

        deleted.Error.Should().BeNull();
        deleted.Value.Should().BeTrue();
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM NotificationSubscriptions WHERE NotificationDefinitionId = N'{definitionId}';"))
            .Should().Be(0, "no subscription survives its definition, whichever committed first");
        var rows = (await AuditRowsAsync(scratch)).Where(r => Detail(r, "definitionId") == definitionId || (string)r["TargetId"]! == definitionId).ToList();
        if (addFirst)
        {
            added.Error.Should().BeNull();
            rows.Select(r => (string)r["Action"]!).Should().Equal(AuditActionNames.NotificationSubscriptionAdded, AuditActionNames.NotificationDefinitionDeleted);
            Detail(rows.Single(r => (string)r["Action"]! == AuditActionNames.NotificationDefinitionDeleted), "subscriptionsRemoved").Should().Be("1");
        }
        else
        {
            added.Error.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("not found");
            rows.Select(r => (string)r["Action"]!).Should().Equal(AuditActionNames.NotificationDefinitionDeleted);
            Detail(rows.Single(), "subscriptionsRemoved").Should().Be("0");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_subscriptions_that_create_the_same_contact_in_different_integrations_make_one_contact(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var firstIntegration = await SeedIntegrationAsync(scratch);
        var secondIntegration = await SeedIntegrationAsync(scratch);
        var firstDefinition = await SeedDefinitionAsync(scratch, firstIntegration);
        var secondDefinition = await SeedDefinitionAsync(scratch, secondIntegration);
        var address = $"shared.{Guid.NewGuid():N}@example.com";

        Task<Outcome<NotificationSubscription>> Add(string integrationId, string definitionId) => RunAsync(async () =>
        {
            await using var db = scratch.NewDb();
            return await Subscriptions(db, principal, NewUnit(db, principal)).AddRecipientAsync(
                definitionId, new NotificationSubscriptionAddRequest(integrationId, null, null, address, true));
        });

        await using var hold = await ManagedContactEmailLockRace.HoldAsync(scratch);
        var first = Add(firstIntegration, firstDefinition);
        await ManagedContactEmailLockRace.WaitForWaitersAsync(scratch, 1, first);
        var second = Add(secondIntegration, secondDefinition);
        await ManagedContactEmailLockRace.WaitForWaitersAsync(scratch, 2, first, second);
        await hold.ReleaseAsync();
        var one = await first;
        var two = await second;

        one.Error.Should().BeNull();
        two.Error.Should().BeNull();
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM ManagedContacts WHERE EmailAddress = N'{address}';")).Should().Be(1, "the second subscription found the contact the first created");
        one.Value!.RecipientRef.Should().Be(two.Value!.RecipientRef);
        var created = (await scratch.RowsAsync($"SELECT * FROM AuditLogs WHERE Action = N'{AuditActionNames.ManagedContactCreated}' AND TargetId = N'{one.Value.RecipientRef}';"));
        created.Should().ContainSingle("only the writer that created the contact recorded its creation");
    }
}
