using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Management.Api.Models;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.EntraRegistrationSqlHarness;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// SEC-2.4 (finding F2, acceptance A4.1 and A4.4): an Entra probe (OpenID validation or a directory search) is operational
/// telemetry. Against SQL Server, on locking READ COMMITTED and READ_COMMITTED_SNAPSHOT catalogs:
/// <list type="bullet">
/// <item>A probe changes no column of the registration row, including <c>UpdatedUtc</c> and <c>rowversion</c>, so an edit form opened
/// before a probe saves without a spurious 409, and a genuine competing edit still conflicts.</item>
/// <item>A probe's write (the repository's update of an existing diagnostics row) is not blocked by a lock held on the registration
/// row; the controller's own read of the registration afterwards is a separate read and, under locking READ COMMITTED, still waits
/// for an in-flight save, as it always did. Probes and configuration edits that run together do not overwrite each other's results.
/// The fallback that creates a missing row reads the registration, so it does wait for an in-flight change to it (record, L3).</item>
/// <item>The diagnostics row is created with the registration and removed with it; a registration inserted without one reads the
/// defaults and the first probes create the row, even when they race.</item>
/// <item>A probe writes no audit row and stores no search value; a caller without the probe's own permission gets 403 and no probe
/// runs. The directory searches need <c>auth.directory.search</c> (IAM-10.2a) and the OpenID validation needs <c>auth.entra.manage</c>.</item>
/// </list>
/// Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class EntraProbeTelemetrySqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private readonly AuditIsolationCatalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public EntraProbeTelemetrySqlServerTests(AuditIsolationCatalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    public static IEnumerable<object[]> Isolation() => [[false], [true]];

    private AuditSqlScratch Scratch(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        _output.WriteLine(scratch.ExecutionMarker + (rcsi ? " (READ_COMMITTED_SNAPSHOT)" : " (locking READ COMMITTED)"));
        return scratch;
    }

    // ---- A4.1: configuration isolation ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_probe_changes_nothing_on_the_registration_row_and_the_open_edit_form_still_saves(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var admin = await AddAdministratorAsync(scratch);
        var id = await CreateRegistrationAsync(scratch, admin);

        // The administrator opens the edit form: it carries the registration's rowversion V.
        var form = await GetAsync(scratch, id, admin);
        var before = await ConfigurationRowAsync(scratch, id);
        form.RowVersionBase64.Should().Be(Convert.ToBase64String((byte[])before["RowVersion"]!));

        // An OpenID validation and both directory searches run while the form is open.
        await using (var db = scratch.NewDb())
        {
            var validator = new ScriptedValidator
            {
                Script = _ => new EntraOpenIdConnectValidationResult(false, OpenIdConfigurationStatus.IssuerMismatch, "issuer_mismatch", "The issuer does not match.", "expected X, found Y"),
            };
            (await Controller(scratch, db, admin, validator).ValidateOpenId(id, CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>();
        }

        await using (var db = scratch.NewDb())
            (await Controller(scratch, db, admin).SearchDirectoryUsers(id, new EntraDirectorySearchRequestBody(SearchSentinel, 5), CancellationToken.None))
                .Result.Should().BeOfType<OkObjectResult>();
        await using (var db = scratch.NewDb())
        {
            var discovery = new ScriptedDiscovery { Script = _ => (false, DiscoveryFailureCategory.Permissions, "Graph refused the request.", "Authorization_RequestDenied") };
            (await Controller(scratch, db, admin, discovery: discovery).SearchDirectoryGroups(id, new EntraDirectorySearchRequestBody(SearchSentinel, 5), CancellationToken.None))
                .Result.Should().BeOfType<OkObjectResult>();
        }

        // The probes left the registration row exactly as it was: every configuration column, UpdatedUtc and the rowversion.
        (await ConfigurationRowAsync(scratch, id)).Should().BeEquivalentTo(before);

        // They recorded their outcomes on the diagnostics row.
        var diagnostics = (await DiagnosticsRowAsync(scratch, id))!;
        diagnostics["OpenIdConfigurationStatus"].Should().Be((int)OpenIdConfigurationStatus.IssuerMismatch);
        diagnostics["LastOpenIdValidationSummary"].Should().Be("The issuer does not match.");
        diagnostics["DirectoryDiscoveryStatus"].Should().Be((int)DirectoryDiscoveryStatus.FailedPermissions);
        diagnostics["LastDirectoryDiscoverySummary"].Should().Be("Graph refused the request.");

        // The form opened before the probes saves with its rowversion: no spurious 409.
        await using (var db = scratch.NewDb())
        {
            var saved = await Controller(scratch, db, admin).Patch(id, UpdateBody(form, displayName: "Renamed"), CancellationToken.None);
            saved.Result.Should().BeOfType<OkObjectResult>("the probes did not change the token the form carries");
            var item = (EntraTenantListItemResponse)((OkObjectResult)saved.Result!).Value!;
            item.DisplayName.Should().Be("Renamed");
            // The registration the save answers with shows the diagnostics the probes recorded.
            item.OpenIdConfigurationStatus.Should().Be(nameof(OpenIdConfigurationStatus.IssuerMismatch));
            item.DirectoryDiscoveryStatus.Should().Be(nameof(DirectoryDiscoveryStatus.FailedPermissions));
        }

        // The edit changed the configuration row and left the telemetry alone.
        var after = await ConfigurationRowAsync(scratch, id);
        after["DisplayName"].Should().Be("Renamed");
        ((byte[])after["RowVersion"]!).Should().NotEqual((byte[])before["RowVersion"]!, "a configuration edit does replace the rowversion");
        (await DiagnosticsRowAsync(scratch, id)).Should().BeEquivalentTo(diagnostics, "a configuration edit does not overwrite the probe results");
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_genuine_competing_configuration_edit_still_conflicts_whatever_the_probes_do(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var admin = await AddAdministratorAsync(scratch);
        var id = await CreateRegistrationAsync(scratch, admin);
        var first = await GetAsync(scratch, id, admin);
        var second = await GetAsync(scratch, id, admin);

        await using (var db = scratch.NewDb())
            (await Controller(scratch, db, admin).Patch(id, UpdateBody(first, displayName: "First"), CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>();
        await using (var db = scratch.NewDb())
            (await Controller(scratch, db, admin).ValidateOpenId(id, CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>();

        await using (var db = scratch.NewDb())
            (await Controller(scratch, db, admin).Patch(id, UpdateBody(second, displayName: "Second"), CancellationToken.None))
                .Result.Should().BeOfType<ConflictObjectResult>("the second administrator's token predates a real configuration edit");
        (await ConfigurationRowAsync(scratch, id))["DisplayName"].Should().Be("First");
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_probe_is_not_blocked_by_a_lock_held_on_the_registration_row_and_leaves_the_row_alone(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var admin = await AddAdministratorAsync(scratch);
        var id = await CreateRegistrationAsync(scratch, admin);
        var before = await ConfigurationRowAsync(scratch, id);

        // An administrator's save is in flight: a transaction holds the registration row's exclusive lock.
        await using var holder = new SqlConnection(scratch.ConnectionString);
        await holder.OpenAsync();
        await using var transaction = (SqlTransaction)await holder.BeginTransactionAsync();
        await using (var hold = holder.CreateCommand())
        {
            hold.Transaction = transaction;
            hold.CommandText = $"UPDATE EntraInstanceRegistrations SET DisplayName = DisplayName + N' (being saved)' WHERE Id = N'{id}';";
            await hold.ExecuteNonQueryAsync();
        }

        // Control: a write to the registration row, which is what a probe used to do, waits on that lock. Without this the
        // case below could pass vacuously.
        await using (var contender = new SqlConnection(scratch.ConnectionString))
        {
            await contender.OpenAsync();
            await using var command = contender.CreateCommand();
            command.CommandText = $"SET LOCK_TIMEOUT 400; UPDATE EntraInstanceRegistrations SET UpdatedUtc = UpdatedUtc WHERE Id = N'{id}';";
            var blocked = async () => await command.ExecuteNonQueryAsync();
            (await blocked.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(1222, "the lock is really held");
        }

        // The probes write the diagnostics row only, so they finish while the lock is held.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using (var db = scratch.NewDb())
        {
            var repository = new SqlEntraInstanceRegistrationRepository(db);
            await repository.UpdateOpenIdValidationOutcomeAsync(id, OpenIdConfigurationStatus.Valid, DateTimeOffset.UtcNow, "valid while locked", null, timeout.Token);
            await repository.UpdateDirectoryDiscoveryOutcomeAsync(id, DirectoryDiscoveryStatus.Success, DateTimeOffset.UtcNow, "found while locked", null, timeout.Token);
        }

        await transaction.RollbackAsync();

        (await ConfigurationRowAsync(scratch, id)).Should().BeEquivalentTo(before);
        var diagnostics = (await DiagnosticsRowAsync(scratch, id))!;
        diagnostics["LastOpenIdValidationSummary"].Should().Be("valid while locked");
        diagnostics["LastDirectoryDiscoverySummary"].Should().Be("found while locked");
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task Probes_and_a_configuration_edit_that_run_together_do_not_overwrite_each_other(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var admin = await AddAdministratorAsync(scratch);
        var id = await CreateRegistrationAsync(scratch, admin);
        var form = await GetAsync(scratch, id, admin);

        // Each probe writes a consistent group: the number in the summary says which probe wrote the status beside it.
        var validator = new ScriptedValidator
        {
            Script = n => new EntraOpenIdConnectValidationResult(
                n % 2 == 0, n % 2 == 0 ? OpenIdConfigurationStatus.Valid : OpenIdConfigurationStatus.IssuerMismatch, null, $"openid-{n}", null),
        };
        var discovery = new ScriptedDiscovery
        {
            Script = n => n % 2 == 0
                ? (true, DiscoveryFailureCategory.None, $"discovery-{n}", null)
                : (false, DiscoveryFailureCategory.Throttled, $"discovery-{n}", null),
        };

        var work = new List<Task>();
        for (var i = 0; i < 4; i++)
        {
            work.Add(Task.Run(async () =>
            {
                await using var db = scratch.NewDb();
                (await Controller(scratch, db, admin, validator).ValidateOpenId(id, CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>();
            }));
            work.Add(Task.Run(async () =>
            {
                await using var db = scratch.NewDb();
                (await Controller(scratch, db, admin, discovery: discovery).SearchDirectoryUsers(id, new EntraDirectorySearchRequestBody("dana", 5), CancellationToken.None))
                    .Result.Should().BeOfType<OkObjectResult>();
            }));
        }

        work.Add(Task.Run(async () =>
        {
            await using var db = scratch.NewDb();
            (await Controller(scratch, db, admin).Patch(id, UpdateBody(form, displayName: "Edited while probing"), CancellationToken.None))
                .Result.Should().BeOfType<OkObjectResult>("probes in flight do not invalidate the form's token");
        }));

        await Task.WhenAll(work);

        (await ConfigurationRowAsync(scratch, id))["DisplayName"].Should().Be("Edited while probing");
        var diagnostics = (await DiagnosticsRowAsync(scratch, id))!;

        // Both groups were recorded, and each is the whole outcome of one probe: never a status from one and a summary from another.
        var openIdNumber = int.Parse(((string)diagnostics["LastOpenIdValidationSummary"]!)["openid-".Length..]);
        diagnostics["OpenIdConfigurationStatus"].Should().Be(openIdNumber % 2 == 0 ? (int)OpenIdConfigurationStatus.Valid : (int)OpenIdConfigurationStatus.IssuerMismatch);
        var discoveryNumber = int.Parse(((string)diagnostics["LastDirectoryDiscoverySummary"]!)["discovery-".Length..]);
        diagnostics["DirectoryDiscoveryStatus"].Should().Be(discoveryNumber % 2 == 0 ? (int)DirectoryDiscoveryStatus.Success : (int)DirectoryDiscoveryStatus.Throttled);
    }

    // ---- the diagnostics row's lifecycle ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task The_diagnostics_row_is_created_with_the_registration_and_removed_with_it(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var admin = await AddAdministratorAsync(scratch);
        var id = await CreateRegistrationAsync(scratch, admin);

        // Created with the registration, with the defaults a new registration has always read.
        var created = (await DiagnosticsRowAsync(scratch, id))!;
        created["OpenIdConfigurationStatus"].Should().Be((int)OpenIdConfigurationStatus.NotValidated);
        created["DirectoryDiscoveryStatus"].Should().Be((int)DirectoryDiscoveryStatus.NotAttempted);
        created.Where(c => c.Key is not ("OpenIdConfigurationStatus" or "DirectoryDiscoveryStatus")).Should().OnlyContain(c => c.Value == null);
        var item = await GetAsync(scratch, id, admin);
        item.OpenIdConfigurationStatus.Should().Be(nameof(OpenIdConfigurationStatus.NotValidated));
        item.DirectoryDiscoveryStatus.Should().Be(nameof(DirectoryDiscoveryStatus.NotAttempted));
        item.LastOpenIdValidationUtc.Should().BeNull();
        item.LastDirectoryDiscoveryUtc.Should().BeNull();

        // Deleted (it was never activated, so the delete guard allows it): the diagnostics row goes with the registration.
        await using (var db = scratch.NewDb())
            (await Controller(scratch, db, admin).Delete(id, CancellationToken.None)).Should().BeOfType<NoContentResult>();

        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM EntraInstanceRegistrations WHERE Id = N'{id}';")).Should().Be(0);
        (await DiagnosticsRowCountAsync(scratch, id)).Should().Be(0, "no diagnostics row outlives its registration");
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_registration_inserted_without_a_diagnostics_row_reads_the_defaults_and_racing_first_probes_create_one_row(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var admin = await AddAdministratorAsync(scratch);
        var id = await InsertRegistrationWithoutDiagnosticsAsync(scratch);
        var before = await ConfigurationRowAsync(scratch, id);
        (await DiagnosticsRowCountAsync(scratch, id)).Should().Be(0);

        var untouched = await GetAsync(scratch, id, admin);
        untouched.OpenIdConfigurationStatus.Should().Be(nameof(OpenIdConfigurationStatus.NotValidated));
        untouched.DirectoryDiscoveryStatus.Should().Be(nameof(DirectoryDiscoveryStatus.NotAttempted));

        // Eight first probes, four of each kind, race to create the row.
        var start = new TaskCompletionSource();
        var work = Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
        {
            await start.Task;
            await using var db = scratch.NewDb();
            var repository = new SqlEntraInstanceRegistrationRepository(db);
            if (i % 2 == 0)
                await repository.UpdateOpenIdValidationOutcomeAsync(id, OpenIdConfigurationStatus.Valid, DateTimeOffset.UtcNow, $"openid-{i}", null);
            else
                await repository.UpdateDirectoryDiscoveryOutcomeAsync(id, DirectoryDiscoveryStatus.Success, DateTimeOffset.UtcNow, $"discovery-{i}", null);
        })).ToArray();
        start.SetResult();
        await Task.WhenAll(work);

        (await DiagnosticsRowCountAsync(scratch, id)).Should().Be(1);
        var diagnostics = (await DiagnosticsRowAsync(scratch, id))!;
        diagnostics["OpenIdConfigurationStatus"].Should().Be((int)OpenIdConfigurationStatus.Valid, "a racing first probe is not lost");
        diagnostics["DirectoryDiscoveryStatus"].Should().Be((int)DirectoryDiscoveryStatus.Success, "a racing first probe is not lost");
        ((string)diagnostics["LastOpenIdValidationSummary"]!).Should().StartWith("openid-");
        ((string)diagnostics["LastDirectoryDiscoverySummary"]!).Should().StartWith("discovery-");
        (await ConfigurationRowAsync(scratch, id)).Should().BeEquivalentTo(before);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_first_probe_that_loses_the_race_to_create_the_row_still_records_its_outcome(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var id = await InsertRegistrationWithoutDiagnosticsAsync(scratch);
        var before = await ConfigurationRowAsync(scratch, id);

        // Our OpenID probe starts and its update finds no row. It is held just before its insert.
        var hook = new InsertHook();
        await using var db = scratch.NewDb(hook);
        var probe = Task.Run(() => new SqlEntraInstanceRegistrationRepository(db)
            .UpdateOpenIdValidationOutcomeAsync(id, OpenIdConfigurationStatus.Valid, DateTimeOffset.UtcNow, "loser", null));
        await hook.AtInsert.WaitAsync(TimeSpan.FromSeconds(15));

        // Meanwhile a concurrent first probe (a directory search) inserts the row, in a transaction that has not committed yet.
        await using var winner = new SqlConnection(scratch.ConnectionString);
        await winner.OpenAsync();
        await using var winnerTransaction = (SqlTransaction)await winner.BeginTransactionAsync();
        await using (var insert = winner.CreateCommand())
        {
            insert.Transaction = winnerTransaction;
            insert.CommandText = $"INSERT INTO EntraRegistrationDiagnostics (RegistrationId, DirectoryDiscoveryStatus, LastDirectoryDiscoverySummary) VALUES (N'{id}', {(int)DirectoryDiscoveryStatus.Success}, N'winner');";
            await insert.ExecuteNonQueryAsync();
        }

        // Locking READ COMMITTED: the loser's insert waits to read the winner's key. READ_COMMITTED_SNAPSHOT: it cannot see the
        // uncommitted row, so its insert goes ahead and waits on the winner's key.
        hook.Release();
        await WaitUntilAStatementIsBlockedAsync(scratch);
        probe.IsCompleted.Should().BeFalse("the probe is waiting on the winner");

        await winnerTransaction.CommitAsync();
        await probe;

        (await DiagnosticsRowCountAsync(scratch, id)).Should().Be(1);
        var diagnostics = (await DiagnosticsRowAsync(scratch, id))!;
        diagnostics["LastOpenIdValidationSummary"].Should().Be("loser", "the probe that lost the race still recorded its outcome");
        diagnostics["OpenIdConfigurationStatus"].Should().Be((int)OpenIdConfigurationStatus.Valid);
        diagnostics["LastDirectoryDiscoverySummary"].Should().Be("winner", "and it did not overwrite the other probe's outcome");
        diagnostics["DirectoryDiscoveryStatus"].Should().Be((int)DirectoryDiscoveryStatus.Success);
        (await ConfigurationRowAsync(scratch, id)).Should().BeEquivalentTo(before);

        // Under row versioning the loser's insert really collided on the key and recovered; under locking it read the committed row.
        hook.DuplicateKeyFailures.Should().Be(rcsi ? 1 : 0, rcsi
            ? "the insert met the winner's key and the duplicate was handled"
            : "the insert waited for the winner, saw its row and wrote nothing");
    }

    private static async Task WaitUntilAStatementIsBlockedAsync(AuditSqlScratch scratch)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var blocked = await scratch.ScalarAsync<int>($"""
                SELECT COUNT(*) FROM sys.dm_exec_requests r
                JOIN sys.databases d ON d.database_id = r.database_id
                WHERE d.name = N'{scratch.Catalog}' AND r.blocking_session_id <> 0;
                """);
            if (blocked > 0)
                return;
            await Task.Delay(25);
        }

        throw new TimeoutException("No statement became blocked on the winner's uncommitted row.");
    }

    /// <summary>Holds the diagnostics insert until released, says when it has been reached, and counts duplicate-key failures.</summary>
    private sealed class InsertHook : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        private readonly TaskCompletionSource _atInsert = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _duplicateKeyFailures;
        private int _foreignKeyFailures;

        public Task AtInsert => _atInsert.Task;

        public int DuplicateKeyFailures => Volatile.Read(ref _duplicateKeyFailures);

        public int ForeignKeyFailures => Volatile.Read(ref _foreignKeyFailures);

        public void Release() => _release.TrySetResult();

        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO [EntraRegistrationDiagnostics]", StringComparison.Ordinal))
            {
                _atInsert.TrySetResult();
                await _release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }

            return result;
        }

        public override Task CommandFailedAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Exception is SqlException { Number: 2627 or 2601 })
                Interlocked.Increment(ref _duplicateKeyFailures);
            if (eventData.Exception is SqlException { Number: 547 })
                Interlocked.Increment(ref _foreignKeyFailures);
            return Task.CompletedTask;
        }
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_first_probe_whose_registration_is_deleted_while_it_creates_the_row_records_nothing_and_does_not_fail(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var id = await InsertRegistrationWithoutDiagnosticsAsync(scratch);

        // The probe's update finds no row and it is held just before its insert.
        var hook = new InsertHook();
        await using var db = scratch.NewDb(hook);
        var probe = Task.Run(() => new SqlEntraInstanceRegistrationRepository(db)
            .UpdateOpenIdValidationOutcomeAsync(id, OpenIdConfigurationStatus.Valid, DateTimeOffset.UtcNow, "too late", null));
        await hook.AtInsert.WaitAsync(TimeSpan.FromSeconds(15));

        // An administrator deletes the registration, in a transaction that has not committed yet.
        await using var deleter = new SqlConnection(scratch.ConnectionString);
        await deleter.OpenAsync();
        await using var deleteTransaction = (SqlTransaction)await deleter.BeginTransactionAsync();
        await using (var delete = deleter.CreateCommand())
        {
            delete.Transaction = deleteTransaction;
            delete.CommandText = $"DELETE FROM EntraInstanceRegistrations WHERE Id = N'{id}';";
            await delete.ExecuteNonQueryAsync();
        }

        // Locking: the insert waits to read the registration. Row versioning: it reads the committed registration, inserts, and
        // its foreign key check waits for the delete.
        hook.Release();
        await WaitUntilAStatementIsBlockedAsync(scratch);
        await deleteTransaction.CommitAsync();
        await probe;

        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM EntraInstanceRegistrations WHERE Id = N'{id}';")).Should().Be(0);
        (await DiagnosticsRowCountAsync(scratch, id)).Should().Be(0, "a probe never leaves a diagnostics row behind for a deleted registration");
        hook.ForeignKeyFailures.Should().Be(rcsi ? 1 : 0, rcsi
            ? "the insert met the delete at its foreign key check and the failure was handled"
            : "the insert waited, found no registration and wrote nothing");
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task Over_long_remote_text_is_stored_cut_to_the_columns_on_both_write_paths(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var admin = await AddAdministratorAsync(scratch);
        var withRow = await CreateRegistrationAsync(scratch, admin);
        var withoutRow = await InsertRegistrationWithoutDiagnosticsAsync(scratch);
        var summary = new string('s', 700);
        var detail = new string('d', 6000);

        foreach (var id in new[] { withRow, withoutRow })
        {
            await using var db = scratch.NewDb();
            var repository = new SqlEntraInstanceRegistrationRepository(db);
            await repository.UpdateOpenIdValidationOutcomeAsync(id, OpenIdConfigurationStatus.TransientFailure, DateTimeOffset.UtcNow, summary, detail);
            await repository.UpdateDirectoryDiscoveryOutcomeAsync(id, DirectoryDiscoveryStatus.TransientFailure, DateTimeOffset.UtcNow, summary, detail);

            var stored = (await DiagnosticsRowAsync(scratch, id))!;
            stored["LastOpenIdValidationSummary"].Should().Be(summary[..512], id);
            stored["LastDirectoryDiscoverySummary"].Should().Be(summary[..512], id);
            stored["LastOpenIdValidationDetail"].Should().Be(detail[..4000], id);
            stored["LastDirectoryDiscoveryDetail"].Should().Be(detail[..4000], id);
        }
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_probe_of_a_registration_that_does_not_exist_records_nothing(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        const string missing = "no-such-registration";
        await using var db = scratch.NewDb();
        var repository = new SqlEntraInstanceRegistrationRepository(db);

        await repository.UpdateOpenIdValidationOutcomeAsync(missing, OpenIdConfigurationStatus.Valid, DateTimeOffset.UtcNow, "x", null);
        await repository.UpdateDirectoryDiscoveryOutcomeAsync(missing, DirectoryDiscoveryStatus.Success, DateTimeOffset.UtcNow, "x", null);

        (await DiagnosticsRowCountAsync(scratch, missing)).Should().Be(0, "a probe never creates a diagnostics row for a registration that is not there");
    }

    // ---- telemetry, not audit; no search value stored ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_probe_writes_no_audit_row_and_stores_no_search_value(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var admin = await AddAdministratorAsync(scratch);
        var id = await CreateRegistrationAsync(scratch, admin);
        var auditBefore = await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;");
        var discovery = new ScriptedDiscovery();

        await using (var db = scratch.NewDb())
            (await Controller(scratch, db, admin).ValidateOpenId(id, CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>();
        await using (var db = scratch.NewDb())
            (await Controller(scratch, db, admin, discovery: discovery).SearchDirectoryUsers(id, new EntraDirectorySearchRequestBody(SearchSentinel, 5), CancellationToken.None))
                .Result.Should().BeOfType<OkObjectResult>();
        await using (var db = scratch.NewDb())
        {
            var failing = new ScriptedDiscovery { Script = _ => (false, DiscoveryFailureCategory.Transient, "Graph is unavailable.", "HTTP 503") };
            (await Controller(scratch, db, admin, discovery: failing).SearchDirectoryGroups(id, new EntraDirectorySearchRequestBody(SearchSentinel, 5), CancellationToken.None))
                .Result.Should().BeOfType<OkObjectResult>();
        }

        discovery.LastSearch.Should().Be(SearchSentinel, "the search value reached the directory call");
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;")).Should().Be(auditBefore, "probe outcomes are telemetry: no W1, W3 or W4 row");
        (await DiagnosticsRowAsync(scratch, id))!.Values.OfType<string>().Should().NotContain(v => v.Contains(SearchSentinel, StringComparison.Ordinal));
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Details LIKE N'%{SearchSentinel}%';")).Should().Be(0);
    }

    // ---- A4.4: authorization ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task Each_probe_needs_its_own_permission_and_a_refused_probe_neither_runs_nor_is_recorded(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var admin = await AddAdministratorAsync(scratch);
        var id = await CreateRegistrationAsync(scratch, admin);
        var diagnosticsBefore = await DiagnosticsRowAsync(scratch, id);
        var validator = new ScriptedValidator();
        var discovery = new ScriptedDiscovery();

        // No permission at all.
        await using (var db = scratch.NewDb())
        {
            var controller = Controller(scratch, db, admin, validator, discovery, new Allow());
            (await controller.ValidateOpenId(id, CancellationToken.None)).Result.Should().BeOfType<ForbidResult>();
            (await controller.SearchDirectoryUsers(id, new EntraDirectorySearchRequestBody("x", 5), CancellationToken.None)).Result.Should().BeOfType<ForbidResult>();
            (await controller.SearchDirectoryGroups(id, new EntraDirectorySearchRequestBody("x", 5), CancellationToken.None)).Result.Should().BeOfType<ForbidResult>();
        }

        // auth.configuration.read reaches none of the three: the old implicit authority for the searches is gone, and there is no
        // period in which it is accepted. Neither the validator nor Graph is called, and no telemetry is written.
        await using (var db = scratch.NewDb())
        {
            var reader = Controller(scratch, db, admin, validator, discovery, new Allow(ActionConstants.AuthConfigurationRead));
            (await reader.ValidateOpenId(id, CancellationToken.None)).Result.Should().BeOfType<ForbidResult>("validation needs auth.entra.manage");
            (await reader.SearchDirectoryUsers(id, new EntraDirectorySearchRequestBody("x", 5), CancellationToken.None)).Result.Should().BeOfType<ForbidResult>("the user search needs auth.directory.search");
            (await reader.SearchDirectoryGroups(id, new EntraDirectorySearchRequestBody("x", 5), CancellationToken.None)).Result.Should().BeOfType<ForbidResult>("the group search needs auth.directory.search");
        }

        // auth.entra.manage alone no longer reaches the searches, and neither do both old permissions together.
        await using (var db = scratch.NewDb())
        {
            foreach (var oldPermissions in new[] { new[] { ActionConstants.AuthEntraManage }, [ActionConstants.AuthConfigurationRead, ActionConstants.AuthEntraManage] })
            {
                var holder = Controller(scratch, db, admin, validator, discovery, new Allow(oldPermissions));
                var held = string.Join(" + ", oldPermissions);
                (await holder.SearchDirectoryUsers(id, new EntraDirectorySearchRequestBody("x", 5), CancellationToken.None)).Result.Should().BeOfType<ForbidResult>(held);
                (await holder.SearchDirectoryGroups(id, new EntraDirectorySearchRequestBody("x", 5), CancellationToken.None)).Result.Should().BeOfType<ForbidResult>(held);
            }
        }

        validator.Calls.Should().Be(0);
        discovery.Calls.Should().Be(0, "nothing above reached Graph");
        (await DiagnosticsRowAsync(scratch, id)).Should().BeEquivalentTo(diagnosticsBefore, "a refused request updates no telemetry");

        // auth.directory.search opens the two searches and nothing else: it does not authorize the OpenID validation.
        await using (var db = scratch.NewDb())
        {
            var searcher = Controller(scratch, db, admin, validator, discovery, new Allow(ActionConstants.AuthDirectorySearch));
            (await searcher.ValidateOpenId(id, CancellationToken.None)).Result.Should().BeOfType<ForbidResult>("searching the directory is not validating the registration");
            validator.Calls.Should().Be(0);
            (await DiagnosticsRowAsync(scratch, id)).Should().BeEquivalentTo(diagnosticsBefore, "a refused validation updates no telemetry");

            (await searcher.SearchDirectoryUsers(id, new EntraDirectorySearchRequestBody("x", 5), CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>();
            (await searcher.SearchDirectoryGroups(id, new EntraDirectorySearchRequestBody("x", 5), CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>();
            discovery.Calls.Should().Be(2);
        }

        // auth.entra.manage keeps the OpenID validation.
        await using (var db = scratch.NewDb())
        {
            var manager = Controller(scratch, db, admin, validator, discovery, new Allow(ActionConstants.AuthEntraManage));
            (await manager.ValidateOpenId(id, CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>();
            validator.Calls.Should().Be(1);
            discovery.Calls.Should().Be(2, "the validation does not search the directory");
        }
    }
}
