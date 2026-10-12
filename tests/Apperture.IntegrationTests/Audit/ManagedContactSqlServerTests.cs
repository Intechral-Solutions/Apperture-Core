using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.NotificationSqlHarness;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.4 slice 6: the managed contact writers (create, update, enable, link, unlink, Entra user import, Entra group import) as
/// audited units against SQL Server on locking READ COMMITTED and READ_COMMITTED_SNAPSHOT catalogs. A contact is a third party: the
/// rows carry an id, kinds and flags and never an address, a name, a directory identity or a principal name. M7 for every action; M6
/// for every operation; the address-uniqueness rule, which is an application rule and not an index, is proven reliable by queueing
/// writers on the email lock. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ManagedContactSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private readonly AuditIsolationCatalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public ManagedContactSqlServerTests(AuditIsolationCatalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    private static Task<AuditSqlScratch> NewScratchAsync(bool rcsi) => AuditIsolation.CreateAsync("Apperture_Aud14Contacts", rcsi);

    public enum Op
    {
        Create,
        Update,
        Disable,
        Link,
        Unlink,
        ImportCreate,
        ImportUpdate,
        GroupImport,
    }

    public static IEnumerable<object[]> Cases() =>
        from op in Enum.GetValues<Op>()
        from rcsi in new[] { false, true }
        select new object[] { op, rcsi };

    /// <summary>The M6 matrix runs on the operations that differ in kind: a create, an update, a link, an import and a group import.</summary>
    private static readonly Op[] M6Ops = [Op.Create, Op.Update, Op.Link, Op.ImportCreate, Op.GroupImport];

    public static IEnumerable<object[]> Ops() => M6Ops.Select(op => new object[] { op });

    /// <summary>
    /// The operations whose failures reach the caller as exceptions. A directory import keeps per-user results, so its failures are
    /// that user's result and have their own cases below.
    /// </summary>
    public static IEnumerable<object[]> ThrowingOps() => M6Ops.Where(op => op != Op.ImportCreate).Select(op => new object[] { op });

    public static IEnumerable<object[]> OutcomeCases() =>
        from op in M6Ops.Where(op => op != Op.ImportCreate)
        from committed in new[] { true, false }
        select new object[] { op, committed };

    private sealed record Arranged(
        string Action,
        Func<AppertureDbContext, IAuditedUnitOfWork, Task> Run,
        Func<Task<string>> TargetId,
        Action<Dictionary<string, object?>> AssertDetails);

    private static string Unique(string stem) => $"{stem}.{Guid.NewGuid():N}";

    private static async Task<Arranged> ArrangeAsync(AuditSqlScratch scratch, Op op, IPrincipalContext principal, string adminId)
    {
        await SeedRegistrationAsync(scratch);
        switch (op)
        {
            case Op.Create:
            {
                var address = Unique("create") + "@example.com";
                return new Arranged(
                    AuditActionNames.ManagedContactCreated,
                    (db, unit) => Contacts(db, principal, unit).CreateCustomAsync(new ManagedContactCreateCommand(PersonSentinel, address, true)).AsTask(),
                    () => scratch.ScalarAsync<string>($"SELECT Id FROM ManagedContacts WHERE EmailAddress = N'{address}';"),
                    row =>
                    {
                        Detail(row, "source").Should().Be("custom");
                        Detail(row, "via").Should().Be("admin");
                        Detail(row, "enabled").Should().Be("true");
                        Detail(row, "hasEmail").Should().Be("true");
                    });
            }

            case Op.Update:
            {
                var id = await SeedContactAsync(scratch, "Before", Unique("update") + "@example.com");
                var address = Unique("updated") + "@example.com";
                return new Arranged(
                    AuditActionNames.ManagedContactUpdated,
                    (db, unit) => Contacts(db, principal, unit).UpdateCustomAsync(id, new ManagedContactUpdateCommand(PersonSentinel, address, true)).AsTask(),
                    () => Task.FromResult(id),
                    row =>
                    {
                        Detail(row, "changedFields").Should().Contain("displayName").And.Contain("email");
                        Detail(row, "source").Should().Be("custom");
                    });
            }

            case Op.Disable:
            {
                var id = await SeedContactAsync(scratch, "To disable", Unique("disable") + "@example.com");
                return new Arranged(
                    AuditActionNames.ManagedContactUpdated,
                    (db, unit) => Contacts(db, principal, unit).SetEnabledAsync(id, false).AsTask(),
                    () => Task.FromResult(id),
                    row =>
                    {
                        Detail(row, "changedFields").Should().Be("[\"enabled\"]");
                        Detail(row, "enabled").Should().Be("false");
                    });
            }

            case Op.Link:
            {
                var id = await SeedContactAsync(scratch, "To link", Unique("link") + "@example.com");
                return new Arranged(
                    AuditActionNames.ManagedContactUserLinked,
                    (db, unit) => Contacts(db, principal, unit).LinkToUserAsync(id, adminId).AsTask(),
                    () => Task.FromResult(id),
                    row => Detail(row, "userId").Should().Be(adminId));
            }

            case Op.Unlink:
            {
                var id = await SeedContactAsync(scratch, "To unlink", Unique("unlink") + "@example.com");
                await using (var db = scratch.NewDb())
                    await new SqlManagedContactRepository(db).SetLinkedUserIdAsync(id, adminId, DateTimeOffset.UtcNow);
                return new Arranged(
                    AuditActionNames.ManagedContactUserUnlinked,
                    (db, unit) => Contacts(db, principal, unit).UnlinkUserAsync(id).AsTask(),
                    () => Task.FromResult(id),
                    row => Detail(row, "previousUserId").Should().Be(adminId));
            }

            case Op.ImportCreate:
            {
                var oid = Unique("oid-new");
                var directory = new FakeDirectory();
                directory.Users[oid] = new EntraDirectoryUser(oid, PersonSentinel, Unique("upn") + "@corp.example", Unique("mail") + "@example.com");
                return new Arranged(
                    AuditActionNames.ManagedContactCreated,
                    async (db, unit) =>
                    {
                        var result = await Contacts(db, principal, unit, directory).ImportEntraUsersAsync(RegistrationId, [oid]);
                        result.Items.Single().Success.Should().BeTrue(result.Items.Single().ErrorMessage);
                    },
                    () => scratch.ScalarAsync<string>($"SELECT ManagedContactId FROM ManagedContactIdentityLinks WHERE ExternalObjectId = N'{oid}';"),
                    row =>
                    {
                        Detail(row, "source").Should().Be("entra");
                        Detail(row, "via").Should().Be("entra_import");
                    });
            }

            case Op.ImportUpdate:
            {
                var oid = Unique("oid-update");
                var upn = Unique("upn") + "@corp.example";
                var mail = Unique("mail") + "@example.com";
                await using (var db = scratch.NewDb())
                    await new SqlManagedContactRepository(db).UpsertFromEntraDirectoryUserAsync("Old name", mail, upn, oid, DateTimeOffset.UtcNow);
                var directory = new FakeDirectory();
                directory.Users[oid] = new EntraDirectoryUser(oid, PersonSentinel, upn, mail);
                return new Arranged(
                    AuditActionNames.ManagedContactUpdated,
                    async (db, unit) =>
                    {
                        var result = await Contacts(db, principal, unit, directory).ImportEntraUsersAsync(RegistrationId, [oid]);
                        result.Items.Single().Success.Should().BeTrue(result.Items.Single().ErrorMessage);
                    },
                    () => scratch.ScalarAsync<string>($"SELECT ManagedContactId FROM ManagedContactIdentityLinks WHERE ExternalObjectId = N'{oid}';"),
                    row =>
                    {
                        Detail(row, "changedFields").Should().Be("[\"displayName\"]");
                        Detail(row, "source").Should().Be("entra");
                    });
            }

            default:
            {
                var oid = Unique("group-oid");
                var directory = new FakeDirectory();
                directory.Groups[oid] = new EntraDirectoryGroup(oid, "Mailing List " + PersonSentinel, null, null);
                return new Arranged(
                    AuditActionNames.ManagedContactGroupImported,
                    async (db, unit) => (await Contacts(db, principal, unit, directory).ImportEntraGroupsAsync(RegistrationId, [oid])).Should().ContainSingle(),
                    () => scratch.ScalarAsync<string>($"SELECT Id FROM ManagedContactGroups WHERE EntraGroupObjectId = N'{oid}';"),
                    row => Detail(row, "created").Should().Be("true"));
            }
        }
    }

    // ---- M7 ----

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_committed_operation_records_one_row_attributed_to_the_administrator_without_any_trace_of_the_person(Op op, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (admin, principal) = await AdminAsync(scratch);
        var arranged = await ArrangeAsync(scratch, op, principal, admin);
        var before = await SnapshotAsync(scratch);

        await using (var db = scratch.NewDb())
            await arranged.Run(db, NewUnit(db, principal));

        (await SnapshotAsync(scratch)).Should().NotBe(before);
        var targetId = await arranged.TargetId();
        var rows = await AuditRowsAsync(scratch, targetId);
        rows.Should().ContainSingle("one primary row per completed operation");
        var row = rows.Single();
        row["Action"].Should().Be(arranged.Action);
        row["OperationEventId"].Should().Be(row["EventId"]);
        row["Actor"].Should().Be(admin);
        row["TargetType"].Should().Be(AuditTargetTypes.ManagedContact);
        row["Outcome"].Should().Be(AuditOutcomeCodes.Succeeded);
        row["Category"].Should().Be(AuditCategories.Configuration);
        row["Severity"].Should().Be(arranged.Action is AuditActionNames.ManagedContactUserLinked or AuditActionNames.ManagedContactUserUnlinked
            ? AuditSeverities.Elevated
            : AuditSeverities.Standard);
        row["Basis"].Should().Be(AuditBases.Change);
        arranged.AssertDetails(row);
        AuditText(rows).Should().NotContain(PersonSentinel).And.NotContain("example.com").And.NotContain("corp.example").And.NotContain("oid-");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- M6 ----

    private async Task<AuditedOperationM6Runner> RunnerAsync(AuditSqlScratch scratch, Op op, IPrincipalContext principal, string adminId)
    {
        var arranged = await ArrangeAsync(scratch, op, principal, adminId);
        return new AuditedOperationM6Runner(scratch, () => SnapshotAsync(scratch), arranged.Action, arranged.Run, PersonSentinel);
    }

    [Theory]
    [MemberData(nameof(ThrowingOps))]
    public async Task An_audit_insert_failure_leaves_nothing(Op op)
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (admin, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, op, principal, admin)).AuditFailureLeavesNothingAsync(principal);
    }

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task A_lost_acknowledgement_is_verified_and_reconciled_without_running_the_attempt_again(Op op)
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (admin, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, op, principal, admin)).LostAcknowledgementIsVerifiedAndReconciledAsync(principal);
    }

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task A_confirmed_rollback_reruns_the_operation_once(Op op)
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (admin, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, op, principal, admin)).ConfirmedRollbackRerunsOnceAsync(principal);
    }

    [Theory]
    [MemberData(nameof(OutcomeCases))]
    public async Task An_unknown_outcome_is_wholly_committed_or_wholly_absent(Op op, bool committed)
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (admin, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, op, principal, admin)).UnknownOutcomeIsWhollyCommittedOrWhollyAbsentAsync(principal, committed);
    }

    [Theory]
    [MemberData(nameof(ThrowingOps))]
    public async Task An_operation_lock_that_cannot_be_taken_is_unknown_and_changes_nothing(Op op)
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (admin, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, op, principal, admin)).OperationLockNotGrantedIsUnknownAndChangesNothingAsync(principal);
    }

    // ---- M6 for the directory import: a failure is that user's result, never a success and never the whole request ----

    private static async Task<(Func<AppertureDbContext, IAuditedUnitOfWork, Task<ImportEntraUsersResult>> Run, string Oid)> ImportAsync(
        AuditSqlScratch scratch, IPrincipalContext principal)
    {
        await SeedRegistrationAsync(scratch);
        var oid = Unique("oid-m6");
        var directory = new FakeDirectory();
        directory.Users[oid] = new EntraDirectoryUser(oid, PersonSentinel, Unique("upn") + "@corp.example", Unique("mail") + "@example.com");
        return ((db, unit) => Contacts(db, principal, unit, directory).ImportEntraUsersAsync(RegistrationId, [oid]).AsTask(), oid);
    }

    [Fact]
    public async Task An_import_whose_audit_insert_fails_reports_that_user_as_failed_and_leaves_nothing()
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        var (run, oid) = await ImportAsync(scratch, principal);
        var before = await SnapshotAsync(scratch);
        var fail = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));

        ImportEntraUsersResult result;
        await using (var db = scratch.NewDb(fail))
            result = await run(db, NewUnit(db, principal));

        fail.Failures.Should().BeGreaterThan(0);
        result.Items.Single().Success.Should().BeFalse();
        result.Items.Single().OutcomeCode.Should().Be(EntraUserImportOutcomeCodes.ImportFailed);
        (await SnapshotAsync(scratch)).Should().Be(before, "no contact and no link survive a failed audit write");
        (await AuditRowsAsync(scratch)).Should().BeEmpty();
        oid.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_import_with_an_unknown_outcome_is_not_reported_as_a_success_and_is_wholly_committed_or_wholly_absent(bool committed)
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        var (run, _) = await ImportAsync(scratch, principal);
        var before = await SnapshotAsync(scratch);
        var fault = committed ? (Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor)new LostCommitAcknowledgement() : new RolledBackCommit();

        ImportEntraUsersResult result;
        await using (var db = scratch.NewDb(fault))
            result = await run(db, NewUnit(db, principal, verification: new UnreachableVerification()));

        result.Items.Single().Success.Should().BeFalse("the outcome could not be established, so the platform does not claim it");
        result.Items.Single().ErrorMessage.Should().Contain("could not be established").And.Contain("operation ");
        var rows = await AuditRowsAsync(scratch);
        if (committed)
        {
            (await SnapshotAsync(scratch)).Should().NotBe(before);
            rows.Should().ContainSingle(r => (string)r["Action"]! == AuditActionNames.ManagedContactCreated);
        }
        else
        {
            (await SnapshotAsync(scratch)).Should().Be(before);
            rows.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task An_import_whose_operation_lock_cannot_be_taken_reports_that_user_as_not_established_and_changes_nothing()
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        var (run, _) = await ImportAsync(scratch, principal);
        var before = await SnapshotAsync(scratch);
        var operationId = Guid.NewGuid().ToString("N");
        await using var held = await HeldLock.TakeAsync(scratch.ConnectionString, "apperture.audit-op." + operationId);

        ImportEntraUsersResult result;
        await using (var db = scratch.NewDb())
            result = await run(db, NewUnit(db, principal, eventIds: new FixedEventIds(operationId)));

        result.Items.Single().Success.Should().BeFalse();
        result.Items.Single().ErrorMessage.Should().Contain(operationId, "the report names the operation to check");
        (await SnapshotAsync(scratch)).Should().Be(before);
        (await AuditRowsAsync(scratch)).Should().BeEmpty();
    }

    // ---- no-ops and refusals ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Requests_that_change_nothing_or_are_refused_write_nothing_and_record_nothing(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (admin, principal) = await AdminAsync(scratch);
        var address = Unique("noop") + "@example.com";
        var id = await SeedContactAsync(scratch, "Stays the same", address);
        var other = await SeedContactAsync(scratch, "Other", Unique("other") + "@example.com");
        var before = await SnapshotAsync(scratch);
        var auditBefore = (await AuditRowsAsync(scratch)).Count;

        await using (var db = scratch.NewDb())
        {
            var service = Contacts(db, principal, NewUnit(db, principal));
            await service.UpdateCustomAsync(id, new ManagedContactUpdateCommand("Stays the same", address, true));
            await service.SetEnabledAsync(id, true);
            await service.UnlinkUserAsync(id);
            await service.UnlinkUserAsync("mcontact_missing");
            await FluentActions.Awaiting(() => service.CreateCustomAsync(new ManagedContactCreateCommand("Duplicate", address.ToUpperInvariant(), true)).AsTask())
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*already uses this email*");
            await FluentActions.Awaiting(() => service.UpdateCustomAsync(other, new ManagedContactUpdateCommand("Other", address, true)).AsTask())
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*already uses this email*");
        }

        (await SnapshotAsync(scratch)).Should().Be(before, "not even the update timestamp moved");
        (await AuditRowsAsync(scratch)).Should().HaveCount(auditBefore);
        admin.Should().NotBeNullOrEmpty();
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Re_importing_an_unchanged_directory_user_records_nothing_and_still_refreshes_the_sync_time(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        await SeedRegistrationAsync(scratch);
        var oid = Unique("oid-same");
        var upn = Unique("upn") + "@corp.example";
        var mail = Unique("mail") + "@example.com";
        await using (var db = scratch.NewDb())
            await new SqlManagedContactRepository(db).UpsertFromEntraDirectoryUserAsync("Same name", mail, upn, oid, DateTimeOffset.UtcNow.AddHours(-3));
        var directory = new FakeDirectory();
        directory.Users[oid] = new EntraDirectoryUser(oid, "Same name", upn, mail);
        var contactId = await scratch.ScalarAsync<string>($"SELECT ManagedContactId FROM ManagedContactIdentityLinks WHERE ExternalObjectId = N'{oid}';");
        var syncedBefore = await scratch.ScalarAsync<DateTimeOffset>($"SELECT LastSyncedAtUtc FROM ManagedContactIdentityLinks WHERE ExternalObjectId = N'{oid}';");

        await using (var db = scratch.NewDb())
        {
            var result = await Contacts(db, principal, NewUnit(db, principal), directory).ImportEntraUsersAsync(RegistrationId, [oid]);
            result.Items.Single().OutcomeCode.Should().Be(EntraUserImportOutcomeCodes.AlreadyLinked);
        }

        (await AuditRowsAsync(scratch, contactId)).Should().BeEmpty();
        (await scratch.ScalarAsync<DateTimeOffset>($"SELECT LastSyncedAtUtc FROM ManagedContactIdentityLinks WHERE ExternalObjectId = N'{oid}';"))
            .Should().BeAfter(syncedBefore, "the sync timestamp is refreshed on its own");
    }

    // ---- the address-uniqueness rule, queued on the email lock ----

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_creations_of_the_same_address_leave_one_contact_and_a_refusal(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var address = Unique("race") + "@example.com";

        Task<Outcome<ManagedContactDetail>> Create(string name) => RunAsync(async () =>
        {
            await using var db = scratch.NewDb();
            return await Contacts(db, principal, NewUnit(db, principal)).CreateCustomAsync(new ManagedContactCreateCommand(name, address, true));
        });

        await using var hold = await ManagedContactEmailLockRace.HoldAsync(scratch);
        var first = Create("Racer one");
        await ManagedContactEmailLockRace.WaitForWaitersAsync(scratch, 1, first);
        var second = Create("Racer two");
        await ManagedContactEmailLockRace.WaitForWaitersAsync(scratch, 2, first, second);
        await hold.ReleaseAsync();
        var one = await first;
        var two = await second;

        one.Error.Should().BeNull("the writer that arrived first wins");
        two.Error.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("already uses this email");
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM ManagedContacts WHERE EmailAddress = N'{address}';")).Should().Be(1);
        (await AuditRowsAsync(scratch, one.Value!.Id)).Should().ContainSingle();
    }

    public static IEnumerable<object[]> Orders() =>
        from importFirst in new[] { true, false }
        from rcsi in new[] { false, true }
        select new object[] { importFirst, rcsi };

    [Theory]
    [MemberData(nameof(Orders))]
    public async Task A_directory_import_racing_a_manual_creation_of_the_same_address_never_makes_two_contacts(bool importFirst, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        await SeedRegistrationAsync(scratch);
        var address = Unique("shared") + "@example.com";
        var oid = Unique("oid-race");
        var directory = new FakeDirectory();
        directory.Users[oid] = new EntraDirectoryUser(oid, "Directory person", Unique("upn") + "@corp.example", address);

        Task<Outcome<ImportEntraUsersResult>> Import() => RunAsync(async () =>
        {
            await using var db = scratch.NewDb();
            return await Contacts(db, principal, NewUnit(db, principal), directory).ImportEntraUsersAsync(RegistrationId, [oid]);
        });
        Task<Outcome<ManagedContactDetail>> Create() => RunAsync(async () =>
        {
            await using var db = scratch.NewDb();
            return await Contacts(db, principal, NewUnit(db, principal)).CreateCustomAsync(new ManagedContactCreateCommand("Manual person", address, true));
        });

        await using var hold = await ManagedContactEmailLockRace.HoldAsync(scratch);
        Task<Outcome<ImportEntraUsersResult>> import;
        Task<Outcome<ManagedContactDetail>> create;
        if (importFirst)
        {
            import = Import();
            await ManagedContactEmailLockRace.WaitForWaitersAsync(scratch, 1, import);
            create = Create();
            await ManagedContactEmailLockRace.WaitForWaitersAsync(scratch, 2, import, create);
        }
        else
        {
            create = Create();
            await ManagedContactEmailLockRace.WaitForWaitersAsync(scratch, 1, create);
            import = Import();
            await ManagedContactEmailLockRace.WaitForWaitersAsync(scratch, 2, import, create);
        }

        await hold.ReleaseAsync();
        var imported = await import;
        var created = await create;

        imported.Error.Should().BeNull();
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM ManagedContacts WHERE EmailAddress = N'{address}';")).Should().Be(1, "the address check cannot race");
        if (importFirst)
        {
            imported.Value!.Items.Single().Success.Should().BeTrue();
            created.Error.Should().BeOfType<InvalidOperationException>();
        }
        else
        {
            created.Error.Should().BeNull();
            imported.Value!.Items.Single().OutcomeCode.Should().Be(EntraUserImportOutcomeCodes.EmailConflictExistingContact, "a directory user never silently merges into an unlinked contact");
        }
    }
}
