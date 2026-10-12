using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.ConnectionSqlHarness;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.4 slice 5: Application (resource context) create, update, archive and unarchive against SQL Server, on a locking READ
/// COMMITTED catalog and on a READ_COMMITTED_SNAPSHOT catalog (design gate §13.4, §13.5, M6, M7). Each is one audited unit, so the
/// change and its one audit row commit together or not at all; the update that used to commit its rename before a refused re-slug
/// is now all or nothing. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ResourceContextSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private readonly AuditIsolationCatalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public ResourceContextSqlServerTests(AuditIsolationCatalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    public enum Op
    {
        Create,
        Update,
        Archive,
        Unarchive,
    }

    public static IEnumerable<object[]> Cases() =>
        from op in Enum.GetValues<Op>()
        from rcsi in new[] { false, true }
        select new object[] { op, rcsi };

    /// <summary>The M6 matrix is about the unit, not about isolation: it runs on a locking catalog of its own.</summary>
    public static IEnumerable<object[]> Ops() => Enum.GetValues<Op>().Select(op => new object[] { op });

    public static IEnumerable<object[]> OutcomeCases() =>
        from op in new[] { Op.Create, Op.Update, Op.Archive }
        from committed in new[] { true, false }
        select new object[] { op, committed };

    // ---- M7: the committed operation ----

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_committed_operation_records_one_row_attributed_to_the_administrator(Op op, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (admin, principal) = await AdminAsync(scratch);
        var arranged = await ArrangeAsync(scratch, op, principal);
        var before = await SnapshotAsync(scratch);

        await using (var db = scratch.NewDb())
            await arranged.Run(db, NewUnit(db, principal));

        (await SnapshotAsync(scratch)).Should().NotBe(before);
        var contextId = arranged.ContextId ?? await scratch.ScalarAsync<string>($"SELECT Id FROM ResourceContexts WHERE Slug = N'{arranged.CreatedSlug}';");
        var row = (await AuditRowsAsync(scratch, contextId)).Should().ContainSingle().Subject;
        row["Action"].Should().Be(arranged.Action);
        row["Actor"].Should().Be(admin);
        row["Category"].Should().Be(AuditCategories.Governance);
        row["Severity"].Should().Be(AuditSeverities.Standard);
        row["Basis"].Should().Be(AuditBases.Change);
        row["TargetType"].Should().Be(AuditTargetTypes.ResourceContext);
        row["TargetId"].Should().Be(contextId);
        row["ResourceContextId"].Should().Be(contextId, "an application event is about the application itself");
        row["Outcome"].Should().Be(AuditOutcomeCodes.Succeeded);
        row["OperationEventId"].Should().Be(row["EventId"], "it is the primary row of its own operation");
        arranged.AssertDetails(row);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- M6 ----

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task An_audit_insert_failure_leaves_no_partial_change(Op op)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Aud14Ctx", rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, op, principal)).AuditFailureLeavesNothingAsync(principal);
    }

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task A_lost_acknowledgement_is_verified_and_reconciled_without_running_the_attempt_again(Op op)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Aud14Ctx", rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, op, principal)).LostAcknowledgementIsVerifiedAndReconciledAsync(principal);
    }

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task A_confirmed_rollback_reruns_the_operation_once_with_one_row(Op op)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Aud14Ctx", rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, op, principal)).ConfirmedRollbackRerunsOnceAsync(principal);
    }

    [Theory]
    [MemberData(nameof(OutcomeCases))]
    public async Task An_unknown_outcome_is_wholly_committed_or_wholly_absent(Op op, bool committed)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Aud14Ctx", rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, op, principal)).UnknownOutcomeIsWhollyCommittedOrWhollyAbsentAsync(principal, committed);
    }

    [Theory]
    [InlineData(Op.Create)]
    [InlineData(Op.Archive)]
    public async Task An_operation_lock_that_cannot_be_taken_is_unknown_and_changes_nothing(Op op)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Aud14Ctx", rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, op, principal)).OperationLockNotGrantedIsUnknownAndChangesNothingAsync(principal);
    }

    // ---- The update is all or nothing ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_refused_slug_change_rolls_back_the_rename_made_in_the_same_request(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var canvas = await SeedContextAsync(scratch, "Canvas");
        var other = await SeedContextAsync(scratch, "Other");
        var before = await SnapshotAsync(scratch);

        await using (var db = scratch.NewDb())
        {
            var act = () => Service(db, principal, NewUnit(db, principal)).UpdateAsync(
                new ResourceContextId(canvas.Id), "Renamed", other.Slug, "A new description", CancellationToken.None).AsTask();
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already in use*");
        }

        (await SnapshotAsync(scratch)).Should().Be(before, "neither the rename nor the description survives the refused slug change");
        (await AuditRowsAsync(scratch, canvas.Id)).Should().BeEmpty();
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Requests_that_change_nothing_write_nothing_and_record_nothing(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var app = await SeedContextAsync(scratch, "Quiet");
        var before = await SnapshotAsync(scratch);

        await using (var db = scratch.NewDb())
        {
            var service = Service(db, principal, NewUnit(db, principal));
            var id = new ResourceContextId(app.Id);
            await service.UpdateAsync(id, "Quiet", app.Slug, null, CancellationToken.None);
            await service.UpdateAsync(id, null, app.Slug.ToUpperInvariant(), null, CancellationToken.None); // normalizes to the stored slug
            await service.UnarchiveAsync(id, CancellationToken.None);
            await service.UnarchiveAsync(ResourceContextIds.Shared, CancellationToken.None);
        }

        (await SnapshotAsync(scratch)).Should().Be(before, "UpdatedAtUtc and the row version did not move");
        (await AuditRowsAsync(scratch, app.Id)).Should().BeEmpty();
    }

    // ---- Create ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_create_that_loses_the_slug_race_is_the_slug_in_use_refusal_not_a_server_error(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var slug = "raced-" + Guid.NewGuid().ToString("N")[..8];

        // Another session has inserted this slug and not committed yet. Under locking READ COMMITTED the availability read waits
        // for it; under row versioning it does not see it and the insert itself waits on the unique index. Either way the create
        // ends when the other session commits, and must answer as a slug in use.
        await using var holder = await LockedRowHolder.BeginAsync(scratch.ConnectionString);
        await holder.ExecuteAsync(
            $"""
            INSERT INTO ResourceContexts (Id, Name, Slug, Kind, State, IsReserved, CreatedAtUtc, UpdatedAtUtc)
            VALUES (N'{Guid.NewGuid():N}', N'Winner', N'{slug}', N'{ResourceContextKinds.Application}', N'{ResourceContextStates.Active}', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);
        var loser = Task.Run(async () =>
        {
            await using var db = scratch.NewDb();
            await Service(db, principal, NewUnit(db, principal)).CreateAsync("Loser", slug, null, CancellationToken.None);
        });
        await holder.WaitUntilBlockedAsync(scratch, "ResourceContexts", loser);

        await holder.CommitAsync();
        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => loser);

        thrown.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("already in use");
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM ResourceContexts WHERE Slug = N'{slug}';")).Should().Be(1);
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Action = N'{AuditActionNames.ResourceContextCreated}' AND Details LIKE N'%Loser%';")).Should().Be(0);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- Archive ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_archive_refused_for_live_ownership_changes_nothing_and_records_nothing(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var app = await SeedContextAsync(scratch, "Busy");
        await SeedIntegrationAsync(scratch, app.Id);
        var before = await SnapshotAsync(scratch);

        await using (var db = scratch.NewDb())
        {
            var act = () => Service(db, principal, NewUnit(db, principal)).ArchiveAsync(new ResourceContextId(app.Id), CancellationToken.None).AsTask();
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*integration not retired*");
        }

        (await SnapshotAsync(scratch)).Should().Be(before);
        (await AuditRowsAsync(scratch, app.Id)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_and_unarchive_are_not_held_up_by_the_ownership_gate_but_archive_waits_for_it(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var archived = await SeedContextAsync(scratch, "Was archived", ResourceContextStates.Archived);
        var active = await SeedContextAsync(scratch, "Active one");

        // A mover holds the gate (the application lock every ownership change takes first) but neither context row.
        await using var mover = scratch.NewDb();
        await using var moverTransaction = await mover.Database.BeginTransactionAsync();
        await new SqlResourceOwnershipMutationGate(mover).AcquireAsync();

        var unarchive = Task.Run(async () =>
        {
            await using var db = scratch.NewDb();
            await Service(db, principal, NewUnit(db, principal)).UnarchiveAsync(new ResourceContextId(archived.Id), CancellationToken.None);
        });
        var update = Task.Run(async () =>
        {
            await using var db = scratch.NewDb();
            await Service(db, principal, NewUnit(db, principal)).UpdateAsync(new ResourceContextId(active.Id), "Renamed while gated", null, null, CancellationToken.None);
        });
        var both = Task.WhenAll(unarchive, update);
        (await Task.WhenAny(both, Task.Delay(TimeSpan.FromSeconds(20)))).Should().BeSameAs(both, "neither takes the gate");
        await both;

        var archive = Task.Run(async () =>
        {
            await using var db = scratch.NewDb();
            await Service(db, principal, NewUnit(db, principal)).ArchiveAsync(new ResourceContextId(active.Id), CancellationToken.None);
        });
        (await Task.WhenAny(archive, Task.Delay(TimeSpan.FromSeconds(2)))).Should().NotBeSameAs(archive, "archive waits for the gate");

        await moverTransaction.CommitAsync();
        await archive;

        (await scratch.ScalarAsync<string>($"SELECT State FROM ResourceContexts WHERE Id = N'{active.Id}';")).Should().Be(ResourceContextStates.Archived);
        (await scratch.ScalarAsync<string>($"SELECT Name FROM ResourceContexts WHERE Id = N'{active.Id}';")).Should().Be("Renamed while gated");
        (await scratch.ScalarAsync<string>($"SELECT State FROM ResourceContexts WHERE Id = N'{archived.Id}';")).Should().Be(ResourceContextStates.Active);
    }

    // ---- Waiting on the context row ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_updates_of_different_fields_queued_on_the_row_keep_both_changes(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var app = await SeedContextAsync(scratch, "Queued");

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, "ResourceContexts", app.Id);
        var rename = Task.Run(async () =>
        {
            await using var db = scratch.NewDb();
            await Service(db, principal, NewUnit(db, principal)).UpdateAsync(new ResourceContextId(app.Id), "Renamed", null, null, CancellationToken.None);
        });
        await holder.WaitUntilBlockedAsync(scratch, "ResourceContexts", rename);
        var describe = Task.Run(async () =>
        {
            await using var db = scratch.NewDb();
            await Service(db, principal, NewUnit(db, principal)).UpdateAsync(new ResourceContextId(app.Id), null, null, "Described", CancellationToken.None);
        });
        await holder.WaitUntilBlockedAsync(scratch, "ResourceContexts", Task.WhenAny(rename, describe), minimumWaiters: 2);

        await holder.CommitAsync();
        await Task.WhenAll(rename, describe);

        (await scratch.ScalarAsync<string>($"SELECT Name FROM ResourceContexts WHERE Id = N'{app.Id}';")).Should().Be("Renamed");
        (await scratch.ScalarAsync<string>($"SELECT Description FROM ResourceContexts WHERE Id = N'{app.Id}';")).Should().Be("Described");
        var rows = await AuditRowsAsync(scratch, app.Id);
        rows.Select(r => (string)r["Action"]!).Should().Equal(Enumerable.Repeat(AuditActionNames.ResourceContextUpdated, 2));
        rows.Select(r => string.Join(",", Changed(r))).Should().BeEquivalentTo("name", "description");
    }

    // ---- wiring ----

    private static ResourceContextManagementService Service(AppertureDbContext db, IPrincipalContext principal, IAuditedUnitOfWork unit) =>
        new(
            new ResourceContextService(new SqlResourceContextRepository(db), TimeProvider.System),
            new SqlResourceContextRepository(db),
            principal,
            new AllowAll(),
            new SqlResourceOwnershipMutationGate(db),
            unit);

    private sealed record Seeded(string Id, string Slug);

    /// <summary>Seeds a context straight into the table, so the case starts with no audit row (the audit table is append-only).</summary>
    private static async Task<Seeded> SeedContextAsync(AuditSqlScratch scratch, string name, string state = ResourceContextStates.Active)
    {
        var id = Guid.NewGuid().ToString("N")[..24];
        var slug = name.ToLowerInvariant().Replace(' ', '-') + "-" + id[..6];
        await scratch.ExecuteAsync(
            $"""
            INSERT INTO ResourceContexts (Id, Name, Slug, Kind, State, IsReserved, CreatedAtUtc, UpdatedAtUtc)
            VALUES (N'{id}', N'{name}', N'{slug}', N'{ResourceContextKinds.Application}', N'{state}', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);
        return new Seeded(id, slug);
    }

    private static Task SeedIntegrationAsync(AuditSqlScratch scratch, string contextId) =>
        scratch.ExecuteAsync(
            $"""
            INSERT INTO Integrations (Id, DisplayName, ModuleId, ScopeKey, ResourceContextId, State, CreatedAt, UpdatedAt)
            VALUES (N'{"int-" + Guid.NewGuid().ToString("N")[..10]}', N'Live integration', N'Sample.Mod', N'instance', N'{contextId}', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);

    private sealed record Arranged(
        string? ContextId,
        string Action,
        Func<AppertureDbContext, IAuditedUnitOfWork, Task> Run,
        Action<Dictionary<string, object?>> AssertDetails,
        string? CreatedSlug = null);

    private async Task<Arranged> ArrangeAsync(AuditSqlScratch scratch, Op op, IPrincipalContext principal)
    {
        switch (op)
        {
            case Op.Create:
            {
                var slug = "created-" + Guid.NewGuid().ToString("N")[..8];
                return new Arranged(
                    null,
                    AuditActionNames.ResourceContextCreated,
                    (db, unit) => Service(db, principal, unit).CreateAsync("Created", slug, "sentinel-description", CancellationToken.None).AsTask(),
                    row =>
                    {
                        Detail(row, "name").Should().Be("Created");
                        Detail(row, "slug").Should().Be(slug);
                        ((string)row["Details"]!).Should().NotContain("sentinel-description");
                    },
                    slug);
            }

            case Op.Update:
            {
                var app = await SeedContextAsync(scratch, "To update");
                return new Arranged(
                    app.Id,
                    AuditActionNames.ResourceContextUpdated,
                    (db, unit) => Service(db, principal, unit).UpdateAsync(new ResourceContextId(app.Id), "Updated", app.Slug + "-v2", "sentinel-description", CancellationToken.None).AsTask(),
                    row =>
                    {
                        Changed(row).Should().Equal("name", "slug", "description");
                        Detail(row, "name").Should().Be("Updated");
                        ((string)row["Details"]!).Should().NotContain("sentinel-description").And.NotContain(app.Slug);
                    });
            }

            case Op.Archive:
            {
                var app = await SeedContextAsync(scratch, "To archive");
                return new Arranged(
                    app.Id,
                    AuditActionNames.ResourceContextArchived,
                    (db, unit) => Service(db, principal, unit).ArchiveAsync(new ResourceContextId(app.Id), CancellationToken.None).AsTask(),
                    row => Detail(row, "name").Should().Be("To archive"));
            }

            default:
            {
                var app = await SeedContextAsync(scratch, "To unarchive", ResourceContextStates.Archived);
                return new Arranged(
                    app.Id,
                    AuditActionNames.ResourceContextUnarchived,
                    (db, unit) => Service(db, principal, unit).UnarchiveAsync(new ResourceContextId(app.Id), CancellationToken.None).AsTask(),
                    row => Detail(row, "name").Should().Be("To unarchive"));
            }
        }
    }

    private async Task<AuditedOperationM6Runner> RunnerAsync(AuditSqlScratch scratch, Op op, IPrincipalContext principal)
    {
        var arranged = await ArrangeAsync(scratch, op, principal);
        return new AuditedOperationM6Runner(scratch, () => SnapshotAsync(scratch), arranged.Action, arranged.Run, "sentinel-description");
    }

    /// <summary>Every row an operation can change, as one comparable text.</summary>
    private static async Task<string> SnapshotAsync(AuditSqlScratch scratch)
    {
        var rows = await scratch.RowsAsync("SELECT * FROM ResourceContexts ORDER BY Id;");
        return string.Join("|", rows.Select(r => string.Join(",", r.Values.Select(v => v is byte[] b ? Convert.ToBase64String(b) : v?.ToString()))));
    }

    private static IReadOnlyList<string> Changed(Dictionary<string, object?> row)
    {
        using var details = JsonDocument.Parse((string)row["Details"]!);
        return details.RootElement.TryGetProperty("changed", out var changed)
            ? changed.EnumerateArray().Select(e => e.GetString()!).ToList()
            : [];
    }
}
