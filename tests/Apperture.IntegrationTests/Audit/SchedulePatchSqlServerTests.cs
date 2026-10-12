using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.4 slice 4 remediation of review finding M1: the schedule PATCH applies only the fields it carries, over the schedule row
/// it has locked. Real SQL Server, a competing session that holds the row (or real competing units queued behind it), and the
/// server's own wait list, so each patch is shown to wait on the row before it reads anything it decides on. Each case runs on a
/// catalog with the default locking READ COMMITTED and on one with READ_COMMITTED_SNAPSHOT on. Fails, never skips, without SQL
/// Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class SchedulePatchSqlServerTests : IClassFixture<SchedulePatchSqlServerTests.Catalogs>
{
    private const string Table = "ScheduleDefinitions";
    private const string Recurrence = "17 4 * * *";
    private const string TargetJson = "{\"parameter\":\"x\"}";
    private const string ArchivedContext = "app-patch-archived";

    private readonly Catalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public SchedulePatchSqlServerTests(Catalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    // ---- A patch that waits on the row while a disable commits keeps the disable ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_rename_that_waits_while_a_disable_commits_leaves_the_schedule_disabled(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var principal = await AdminAsync(scratch);
        var id = await SeedScheduleAsync(scratch, enabled: true);

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, Table, id);
        var patch = Patch(scratch, principal, id, new SchedulePatch(Name: "Renamed"));
        await holder.WaitUntilBlockedAsync(scratch, Table, patch);

        await holder.ExecuteAsync("UPDATE [ScheduleDefinitions] SET [Enabled] = 0 WHERE [Id] = @id;", ("@id", id));
        await holder.CommitAsync();
        var result = await patch;

        var stored = await StoredAsync(scratch, id);
        stored.Enabled.Should().BeFalse("the rename did not carry the enabled flag, so the disable that committed first stands");
        stored.Name.Should().Be("Renamed");
        result.Should().BeEquivalentTo(stored, "the result is the schedule as committed");
        var row = (await AuditRowsAsync(scratch, id)).Should().ContainSingle().Subject;
        row["Action"].Should().Be(AuditActionNames.ScheduleUpdated);
        Changed(row).Should().Equal("name");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_retime_that_waits_while_a_disable_commits_leaves_the_schedule_disabled(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var principal = await AdminAsync(scratch);
        var id = await SeedScheduleAsync(scratch, enabled: true);

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, Table, id);
        var patch = Patch(scratch, principal, id, new SchedulePatch(RecurrenceExpression: "5 5 * * *", TimeZone: "Europe/London"));
        await holder.WaitUntilBlockedAsync(scratch, Table, patch);

        await holder.ExecuteAsync("UPDATE [ScheduleDefinitions] SET [Enabled] = 0 WHERE [Id] = @id;", ("@id", id));
        await holder.CommitAsync();
        var result = await patch;

        var stored = await StoredAsync(scratch, id);
        stored.Enabled.Should().BeFalse();
        stored.RecurrenceExpression.Should().Be("5 5 * * *");
        stored.TimeZone.Should().Be("Europe/London");
        result.Should().BeEquivalentTo(stored);
        var row = (await AuditRowsAsync(scratch, id)).Should().ContainSingle().Subject;
        row["Action"].Should().Be(AuditActionNames.ScheduleUpdated);
        Changed(row).Should().Equal("recurrence", "timeZone");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    /// <summary>
    /// The production disable and the patch, both queued on the row: whichever the server grants first, the schedule ends
    /// renamed and disabled, and each unit records exactly its own change.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_rename_and_a_disable_queued_on_the_row_keep_both_in_either_order(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var principal = await AdminAsync(scratch);
        var id = await SeedScheduleAsync(scratch, enabled: true);

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, Table, id);
        var patch = Patch(scratch, principal, id, new SchedulePatch(Name: "Renamed"));
        await holder.WaitUntilBlockedAsync(scratch, Table, patch);
        var disable = Disable(scratch, principal, id);
        await holder.WaitUntilBlockedAsync(scratch, Table, Task.WhenAny(patch, disable), minimumWaiters: 2);

        await holder.CommitAsync();
        await Task.WhenAll(patch, disable);

        var stored = await StoredAsync(scratch, id);
        stored.Enabled.Should().BeFalse();
        stored.Name.Should().Be("Renamed");
        var rows = await AuditRowsAsync(scratch, id);
        rows.Select(r => (string)r["Action"]!).Should().BeEquivalentTo(AuditActionNames.ScheduleUpdated, AuditActionNames.ScheduleDisabled);
        Changed(rows.Single(r => (string)r["Action"]! == AuditActionNames.ScheduleUpdated)).Should().Equal("name");
        Changed(rows.Single(r => (string)r["Action"]! == AuditActionNames.ScheduleDisabled)).Should().BeEmpty();
        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- Explicit enable ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_explicit_enable_that_waits_while_a_rename_commits_enables_and_keeps_the_rename(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var principal = await AdminAsync(scratch);
        var id = await SeedScheduleAsync(scratch, enabled: false);

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, Table, id);
        var patch = Patch(scratch, principal, id, new SchedulePatch(Enabled: true));
        await holder.WaitUntilBlockedAsync(scratch, Table, patch);

        await holder.ExecuteAsync("UPDATE [ScheduleDefinitions] SET [Name] = N'Renamed elsewhere' WHERE [Id] = @id;", ("@id", id));
        await holder.CommitAsync();
        var result = await patch;

        var stored = await StoredAsync(scratch, id);
        stored.Enabled.Should().BeTrue();
        stored.Name.Should().Be("Renamed elsewhere");
        stored.Lifecycle!.LastScheduleEnabledAtUtc.Should().NotBeNull();
        result.Should().BeEquivalentTo(stored);
        var row = (await AuditRowsAsync(scratch, id)).Should().ContainSingle().Subject;
        row["Action"].Should().Be(AuditActionNames.ScheduleEnabled);
        Changed(row).Should().BeEmpty("the name the patch did not carry is not one of its changes");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_explicit_enable_under_an_archived_context_is_refused_and_changes_nothing(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var principal = await AdminAsync(scratch);
        var id = await SeedScheduleAsync(scratch, enabled: false, ArchivedContext);
        var before = await scratch.RowsAsync($"SELECT * FROM ScheduleDefinitions WHERE Id = N'{id}';");

        var act = () => Patch(scratch, principal, id, new SchedulePatch(Name: "Renamed", Enabled: true));

        await act.Should().ThrowAsync<ResourceContextArchivedException>();
        (await scratch.RowsAsync($"SELECT * FROM ScheduleDefinitions WHERE Id = N'{id}';")).Should().BeEquivalentTo(before);
        (await AuditRowsAsync(scratch, id)).Should().BeEmpty();

        // The same rename without the enable makes nothing live and is allowed.
        var renamed = await Patch(scratch, principal, id, new SchedulePatch(Name: "Renamed"));
        renamed.Enabled.Should().BeFalse();
        renamed.Name.Should().Be("Renamed");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    /// <summary>
    /// The enabled flag is an explicit field like any other: an enabling patch and the production disable queued on the row end
    /// with the value of whichever committed last, and the audit rows say which that was.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_explicit_enable_and_a_disable_queued_on_the_row_leave_the_last_writers_flag(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var principal = await AdminAsync(scratch);
        var id = await SeedScheduleAsync(scratch, enabled: false);

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, Table, id);
        var disable = Disable(scratch, principal, id);
        await holder.WaitUntilBlockedAsync(scratch, Table, disable);
        var enable = Patch(scratch, principal, id, new SchedulePatch(Enabled: true));
        await holder.WaitUntilBlockedAsync(scratch, Table, Task.WhenAny(disable, enable), minimumWaiters: 2);

        await holder.CommitAsync();
        await Task.WhenAll(disable, enable);

        // The schedule started disabled: the enable always changes it, and the disable records a row only if it runs second.
        var rows = await AuditRowsAsync(scratch, id);
        var last = (string)rows[^1]["Action"]!;
        (await StoredAsync(scratch, id)).Enabled.Should().Be(last == AuditActionNames.ScheduleEnabled);
        rows.Select(r => (string)r["Action"]!).Should().Contain(AuditActionNames.ScheduleEnabled);
        _output.WriteLine($"{scratch.ExecutionMarker}; last writer: {last}");
    }

    // ---- Two patches serialized by the row lock ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_patches_of_different_fields_queued_on_the_row_keep_both_changes(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var principal = await AdminAsync(scratch);
        var id = await SeedScheduleAsync(scratch, enabled: true);

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, Table, id);
        var rename = Patch(scratch, principal, id, new SchedulePatch(Name: "Renamed"));
        await holder.WaitUntilBlockedAsync(scratch, Table, rename);
        var retime = Patch(scratch, principal, id, new SchedulePatch(RecurrenceExpression: "5 5 * * *"));
        await holder.WaitUntilBlockedAsync(scratch, Table, Task.WhenAny(rename, retime), minimumWaiters: 2);

        await holder.CommitAsync();
        await Task.WhenAll(rename, retime);

        var stored = await StoredAsync(scratch, id);
        stored.Name.Should().Be("Renamed");
        stored.RecurrenceExpression.Should().Be("5 5 * * *");
        stored.Enabled.Should().BeTrue();
        var rows = await AuditRowsAsync(scratch, id);
        rows.Should().HaveCount(2).And.OnlyContain(r => (string)r["Action"]! == AuditActionNames.ScheduleUpdated);
        rows.Select(Changed).Select(c => string.Join(",", c)).Should().BeEquivalentTo("name", "recurrence");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_patches_of_the_same_field_queued_on_the_row_leave_the_last_writers_value(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var principal = await AdminAsync(scratch);
        var id = await SeedScheduleAsync(scratch, enabled: true);

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, Table, id);
        var first = Patch(scratch, principal, id, new SchedulePatch(Name: "First"));
        await holder.WaitUntilBlockedAsync(scratch, Table, first);
        var second = Patch(scratch, principal, id, new SchedulePatch(Name: "Second"));
        await holder.WaitUntilBlockedAsync(scratch, Table, Task.WhenAny(first, second), minimumWaiters: 2);

        await holder.CommitAsync();
        await Task.WhenAll(first, second);

        var rows = await AuditRowsAsync(scratch, id);
        rows.Should().HaveCount(2);
        rows.Select(Changed).Should().OnlyContain(c => c.SequenceEqual(new[] { "name" }));
        (await StoredAsync(scratch, id)).Name.Should().Be(Detail(rows[^1], "name"), "the value of the patch that committed last");
        _output.WriteLine($"{scratch.ExecutionMarker}; last writer: {Detail(rows[^1], "name")}");
    }

    // ---- What changed is decided against the locked row ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_patch_whose_values_were_committed_while_it_waited_is_a_no_op_that_records_nothing(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var principal = await AdminAsync(scratch);
        var id = await SeedScheduleAsync(scratch, enabled: true);

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, Table, id);
        var patch = Patch(scratch, principal, id, new SchedulePatch(Name: "Renamed"));
        await holder.WaitUntilBlockedAsync(scratch, Table, patch);

        await holder.ExecuteAsync("UPDATE [ScheduleDefinitions] SET [Name] = N'Renamed' WHERE [Id] = @id;", ("@id", id));
        await holder.CommitAsync();
        var before = await scratch.RowsAsync($"SELECT * FROM ScheduleDefinitions WHERE Id = N'{id}';");
        var result = await patch;

        result.Name.Should().Be("Renamed");
        (await scratch.RowsAsync($"SELECT * FROM ScheduleDefinitions WHERE Id = N'{id}';")).Should().BeEquivalentTo(before);
        (await AuditRowsAsync(scratch, id)).Should().BeEmpty();
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_changed_fields_recorded_are_the_differences_from_the_locked_row(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var principal = await AdminAsync(scratch);
        var id = await SeedScheduleAsync(scratch, enabled: true);

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, Table, id);
        var patch = Patch(scratch, principal, id, new SchedulePatch(Name: "Renamed", RecurrenceExpression: "5 5 * * *", Enabled: true));
        await holder.WaitUntilBlockedAsync(scratch, Table, patch);

        await holder.ExecuteAsync("UPDATE [ScheduleDefinitions] SET [Name] = N'Renamed' WHERE [Id] = @id;", ("@id", id));
        await holder.CommitAsync();
        await patch;

        var row = (await AuditRowsAsync(scratch, id)).Should().ContainSingle().Subject;
        row["Action"].Should().Be(AuditActionNames.ScheduleUpdated, "the schedule was already enabled");
        Changed(row).Should().Equal("recurrence");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- wiring ----

    private AuditSqlScratch Scratch(bool rcsi) => rcsi ? _catalogs.Rcsi : _catalogs.Locking;

    private static SqlScheduleManagementService Service(AppertureDbContext db, IPrincipalContext principal) =>
        new(
            new SqlScheduleRepository(db),
            new StubClaimOptions(),
            TimeProvider.System,
            new ResourceContextService(new SqlResourceContextRepository(db), TimeProvider.System),
            new SqlIntegrationRepository(db),
            db,
            new SqlResourceOwnershipMutationGate(db),
            principal,
            NewUnit(db, principal));

    private static Task<ScheduleDefinition> Patch(AuditSqlScratch scratch, IPrincipalContext principal, string id, SchedulePatch patch) =>
        Task.Run(async () =>
        {
            await using var db = scratch.NewDb();
            return await Service(db, principal).PatchScheduleAsync(new ScheduleId(id), patch);
        });

    private static Task Disable(AuditSqlScratch scratch, IPrincipalContext principal, string id) =>
        Task.Run(async () =>
        {
            await using var db = scratch.NewDb();
            await Service(db, principal).DisableAsync(new ScheduleId(id));
        });

    private static async Task<ScheduleDefinition> StoredAsync(AuditSqlScratch scratch, string id)
    {
        await using var db = scratch.NewDb();
        return (await new SqlScheduleRepository(db).GetDefinitionAsync(new ScheduleId(id)))!;
    }

    private static async Task<string> SeedScheduleAsync(AuditSqlScratch scratch, bool enabled, string resourceContextId = ResourceContextIds.SharedValue)
    {
        var id = "sch-" + Guid.NewGuid().ToString("N")[..12];
        await scratch.ExecuteAsync(
            $"""
            INSERT INTO ScheduleDefinitions (Id, IntegrationId, ResourceContextId, Name, RecurrenceExpression, TimeZone, Enabled, TargetJobType, TargetJobConfigurationJson, ExecutionPolicyRef, Version)
            VALUES (N'{id}', NULL, N'{resourceContextId}', N'Nightly import', N'{Recurrence}', N'UTC', {(enabled ? 1 : 0)}, 0, N'{TargetJson}', NULL, 1);
            """);
        return id;
    }

    private static async Task<IPrincipalContext> AdminAsync(AuditSqlScratch scratch)
    {
        var id = NewUserId();
        await scratch.AddUserAsync(id, NewUsername("admin"));
        return new Principal(id);
    }

    private static Task<List<Dictionary<string, object?>>> AuditRowsAsync(AuditSqlScratch scratch, string scheduleId) =>
        scratch.RowsAsync($"SELECT * FROM AuditLogs WHERE TargetId = N'{scheduleId}' ORDER BY Id;");

    private static string? Detail(Dictionary<string, object?> row, string name)
    {
        using var details = JsonDocument.Parse((string)row["Details"]!);
        return details.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static IReadOnlyList<string> Changed(Dictionary<string, object?> row)
    {
        using var details = JsonDocument.Parse((string)row["Details"]!);
        return details.RootElement.TryGetProperty("changed", out var changed)
            ? changed.EnumerateArray().Select(e => e.GetString()!).ToList()
            : [];
    }

    private sealed class StubClaimOptions : ISchedulerClaimOptions
    {
        public int ClaimLeaseMinutes => 5;
    }

    // ---- catalogs ----

    /// <summary>
    /// Two migrated throwaway catalogs: the default locking READ COMMITTED, and one with READ_COMMITTED_SNAPSHOT on. Each has an
    /// archived Application context for the reactivation-guard case.
    /// </summary>
    public sealed class Catalogs : IAsyncLifetime
    {
        public AuditSqlScratch Locking { get; private set; } = null!;
        public AuditSqlScratch Rcsi { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            Locking = await AuditSqlScratch.CreateAsync("Apperture_Aud14PatchA_");
            Rcsi = await AuditSqlScratch.CreateAsync("Apperture_Aud14PatchB_");
            await EnableRcsiAsync(Rcsi);

            (await Locking.ScalarAsync<bool>(RcsiQuery(Locking))).Should().BeFalse();
            (await Rcsi.ScalarAsync<bool>(RcsiQuery(Rcsi))).Should().BeTrue("the second catalog must really run under row versioning");

            foreach (var scratch in new[] { Locking, Rcsi })
            {
                await scratch.ExecuteAsync(
                    $"""
                    INSERT INTO ResourceContexts (Id, Name, Slug, Kind, State, IsReserved, CreatedAtUtc, UpdatedAtUtc)
                    VALUES (N'{ArchivedContext}', N'Archived Patch App', N'{ArchivedContext}', N'{ResourceContextKinds.Application}', N'{ResourceContextStates.Archived}', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
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
            await using (var connection = new SqlConnection(new SqlConnectionStringBuilder(scratch.ConnectionString) { InitialCatalog = "master" }.ConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"ALTER DATABASE [{scratch.Catalog}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;";
                await command.ExecuteNonQueryAsync();
            }

            // The switch closed this catalog's idle pooled connections; drop that pool so none is reused half-dead.
            SqlConnection.ClearPool(new SqlConnection(scratch.ConnectionString));
        }
    }
}
