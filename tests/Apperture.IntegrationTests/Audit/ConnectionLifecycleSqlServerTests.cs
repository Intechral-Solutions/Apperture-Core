using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Core.Security.Secrets.Providers;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.ConnectionSqlHarness;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.4 slice 5: connection create, update (including secret rotation) and retire against SQL Server, on a locking READ
/// COMMITTED catalog and on a READ_COMMITTED_SNAPSHOT catalog (design gate §13.4, §13.5, M6, M7). Each is one audited unit that
/// the real SQL secret store joins, so the change, its secret material and its audit rows commit together or not at all. The
/// live-secret-first defect (a rotation activated a new version on the live secret before validation and the row save) is proven
/// gone by resolving the credential through the store after every way a rotation can fail. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ConnectionLifecycleSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private readonly AuditIsolationCatalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public ConnectionLifecycleSqlServerTests(AuditIsolationCatalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    /// <summary>
    /// A catalog of this case's own, for the cases that need an empty audit table (the M6 matrix). The audit table is
    /// append-only, so cases cannot clean up after each other; every other case shares the class catalogs and asserts on the
    /// rows of its own connection.
    /// </summary>
    private static Task<AuditSqlScratch> NewScratchAsync(bool rcsi) => AuditIsolation.CreateAsync("Apperture_Aud14Conn", rcsi);

    public enum Op
    {
        CreatePlain,
        CreateWithMaterial,
        Rename,
        RotateMaterial,
        RotateRef,
        Retire,
    }

    public static IEnumerable<object[]> Cases() =>
        from op in Enum.GetValues<Op>()
        from rcsi in new[] { false, true }
        select new object[] { op, rcsi };

    /// <summary>The M6 matrix is about the unit, not about isolation: it runs on the locking catalog.</summary>
    public static IEnumerable<object[]> Ops() => Enum.GetValues<Op>().Select(op => new object[] { op });

    public static IEnumerable<object[]> OutcomeCases() =>
        from op in Enum.GetValues<Op>()
        from committed in new[] { true, false }
        select new object[] { op, committed };

    // ---- M7: the committed operation ----

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_committed_operation_records_its_rows_attributed_to_the_administrator_and_nothing_secret(Op op, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (admin, principal) = await AdminAsync(scratch);
        var arranged = await ArrangeAsync(scratch, op, principal);
        var before = await SnapshotAsync(scratch);

        await using (var db = scratch.NewDb())
            await arranged.Run(db, NewUnit(db, principal));

        (await SnapshotAsync(scratch)).Should().NotBe(before);
        var connectionId = arranged.ConnectionId ?? await scratch.ScalarAsync<string>($"SELECT Id FROM Connections WHERE Name = N'{arranged.CreatedName}';");
        var rows = await AuditRowsAsync(scratch, connectionId);
        rows.Select(r => (string)r["Action"]!).Should().BeEquivalentTo(arranged.Actions);
        var primary = rows.Single(r => (string)r["Action"]! == arranged.Actions[0]);
        primary["OperationEventId"].Should().Be(primary["EventId"], "the first row is the primary row of its own operation");
        foreach (var row in rows)
        {
            row["Actor"].Should().Be(admin);
            row["TargetType"].Should().Be(AuditTargetTypes.Connection);
            row["TargetId"].Should().Be(connectionId);
            row["Outcome"].Should().Be(AuditOutcomeCodes.Succeeded);
            row["OperationEventId"].Should().Be(primary["EventId"], "every row of the unit belongs to its operation");
            var secrets = (string)row["Action"]! is AuditActionNames.ConnectionSecretRotated or AuditActionNames.ConnectionSecretStored;
            row["Category"].Should().Be(secrets ? AuditCategories.Secrets : AuditCategories.Configuration);
            row["Severity"].Should().Be(secrets ? AuditSeverities.Elevated : AuditSeverities.Standard);
            row["Basis"].Should().Be(AuditBases.Change);
            row["ResourceContextId"].Should().Be(ResourceContextIds.SharedValue);
        }

        arranged.AssertDetails(rows);
        var auditText = JsonSerializer.Serialize(rows.Select(r => r.Values.Select(v => v?.ToString())));
        auditText.Should().NotContain(NewSecret).And.NotContain(OldSecret).And.NotContain(HostSentinel);
        foreach (var reference in await ConnectionRefsAsync(scratch, connectionId))
            auditText.Should().NotContain(reference, "a secret reference is never recorded in the audit log");
        // The store's own evidence is keyed by the secret's id (that is what it is for), but holds no material.
        JsonSerializer.Serialize(await scratch.RowsAsync("SELECT * FROM SecretAudits;")).Should().NotContain(NewSecret).And.NotContain(OldSecret);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- M6 ----

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task An_audit_insert_failure_leaves_no_partial_change_and_no_secret(Op op)
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
    public async Task A_confirmed_rollback_reruns_the_operation_once_with_its_rows(Op op)
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
    [InlineData(Op.CreateWithMaterial)]
    [InlineData(Op.RotateMaterial)]
    public async Task An_operation_lock_that_cannot_be_taken_is_unknown_and_changes_nothing(Op op)
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, op, principal)).OperationLockNotGrantedIsUnknownAndChangesNothingAsync(principal);
    }

    // ---- The live-secret-first defect ----

    public enum RotationFailure
    {
        AuditInsertFails,
        RowUpdateFails,
        MaterialCheckFails,
    }

    public static IEnumerable<object[]> FailureCases() =>
        from failure in Enum.GetValues<RotationFailure>()
        from rcsi in new[] { false, true }
        select new object[] { failure, rcsi };

    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task A_rotation_that_fails_leaves_the_previous_credential_live_and_nothing_of_the_new_one(RotationFailure failure, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: true);
        var before = await SnapshotAsync(scratch);
        (await ResolveAsync(scratch, seeded.SecretRef!)).Should().Be(OldSecret, "precondition: the connection resolves its credential");

        var intercept = failure switch
        {
            RotationFailure.AuditInsertFails => new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal)),
            RotationFailure.RowUpdateFails => new FailingCommand(sql => sql.Contains("UPDATE [Connections]", StringComparison.Ordinal)),
            _ => null,
        };

        await using (var db = intercept is null ? scratch.NewDb() : scratch.NewDb(intercept))
        {
            var service = Service(db, principal, NewUnit(db, principal));
            var request = failure == RotationFailure.MaterialCheckFails
                // Replacing the config with an empty one drops the required host, which the material check refuses only after the
                // new material was handed to the store.
                ? new UpdateConnectionRequest(null, new Dictionary<string, string>(), new Dictionary<string, string> { ["Password"] = NewSecret })
                // A rename too, so the connection row itself changes (a rotation of the same secret changes no connection column).
                : new UpdateConnectionRequest("Renamed in the same request", null, new Dictionary<string, string> { ["Password"] = NewSecret });
            var act = () => service.UpdateConnectionAsync(new ConnectionId(seeded.ConnectionId), request).AsTask();
            await act.Should().ThrowAsync<Exception>();
        }

        intercept?.Failures.Should().BeGreaterThan(0, "the failure was really injected");
        (await ResolveAsync(scratch, seeded.SecretRef!)).Should().Be(OldSecret, "the new version never became live");
        (await SnapshotAsync(scratch)).Should().Be(before, "no secret version, descriptor change, evidence row, connection change or audit row survives");
        (await AuditRowsAsync(scratch, seeded.ConnectionId)).Should().BeEmpty();
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_committed_rotation_makes_the_new_credential_live_as_a_second_version_and_records_two_rows(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: true);

        await using (var db = scratch.NewDb())
            await Service(db, principal, NewUnit(db, principal)).UpdateConnectionAsync(
                new ConnectionId(seeded.ConnectionId),
                new UpdateConnectionRequest(null, null, new Dictionary<string, string> { ["Password"] = NewSecret }));

        (await ResolveAsync(scratch, seeded.SecretRef!)).Should().Be(NewSecret);
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM SecretVersions WHERE SecretId = '{seeded.SecretRef}';")).Should().Be(2);
        (await AuditRowsAsync(scratch, seeded.ConnectionId)).Select(r => (string)r["Action"]!).Should().BeEquivalentTo(
            AuditActionNames.ConnectionSecretRotated, AuditActionNames.ConnectionSecretStored);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_creation_into_a_context_that_cannot_own_it_stores_no_secret_and_no_row(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var before = await SnapshotAsync(scratch);
        var auditBefore = await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;");

        await using (var db = scratch.NewDb())
        {
            var act = () => Service(db, principal, NewUnit(db, principal)).CreateConnectionAsync(new CreateConnectionRequest(
                Sql, "Doomed", new Dictionary<string, string> { ["Host"] = HostSentinel }, new Dictionary<string, string>(),
                SecretValues: new Dictionary<string, string> { ["Password"] = NewSecret },
                ResourceContextId: new ResourceContextId("no-such-context"))).AsTask();
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        (await SnapshotAsync(scratch)).Should().Be(before, "the refusal came before any secret was stored or any row written");
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;")).Should().Be(auditBefore);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_creation_that_fails_its_material_check_after_storing_secrets_rolls_them_back(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var before = await SnapshotAsync(scratch);
        var auditBefore = await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;");

        await using (var db = scratch.NewDb())
        {
            // The required host is missing: refused by the material check, after the secret was handed to the store.
            var act = () => Service(db, principal, NewUnit(db, principal)).CreateConnectionAsync(new CreateConnectionRequest(
                Sql, "Doomed", new Dictionary<string, string>(), new Dictionary<string, string>(),
                SecretValues: new Dictionary<string, string> { ["Password"] = NewSecret })).AsTask();
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Host*");
        }

        (await SnapshotAsync(scratch)).Should().Be(before, "the store joined the unit, so the refused creation left no secret, version or evidence behind");
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;")).Should().Be(auditBefore);
    }

    // ---- No-ops ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Requests_that_change_nothing_write_nothing_and_record_nothing(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: true);
        await using (var db = scratch.NewDb())
            await Service(db, principal, NewUnit(db, principal)).RetireConnectionAsync(new ConnectionId(seeded.ConnectionId));
        var connectionId = new ConnectionId(seeded.ConnectionId);
        var rowsBefore = await AuditRowsAsync(scratch, seeded.ConnectionId);
        var before = await SnapshotAsync(scratch);

        await using (var db = scratch.NewDb())
        {
            var service = Service(db, principal, NewUnit(db, principal));
            await service.RetireConnectionAsync(connectionId);
            var current = (await service.GetConnectionAsync(connectionId))!;
            await service.UpdateConnectionAsync(connectionId, new UpdateConnectionRequest(current.Name, null, null));
        }

        (await SnapshotAsync(scratch)).Should().Be(before);
        (await AuditRowsAsync(scratch, seeded.ConnectionId)).Should().HaveCount(rowsBefore.Count, "neither the second retirement nor the unchanged update recorded anything");
    }

    // ---- Secret reference ownership: a reference proves nothing but the secret it names ----

    private sealed record Victim(string ConnectionId, string SecretRef);

    private static void RevealsNothing(Exception ex, params string[] sentinels)
    {
        ex.Should().BeOfType<InvalidOperationException>();
        foreach (var sentinel in sentinels)
            ex.ToString().Should().NotContain(sentinel);
    }

    private static async Task<Exception> RefusalOf(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            return ex;
        }

        throw new Xunit.Sdk.XunitException("the operation was expected to be refused");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Another_connections_secret_can_be_neither_attached_nor_rotated_through_any_request_shape(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var victim = await SeedConnectionAsync(scratch, principal, withMaterial: true);
        var attacker = await SeedConnectionAsync(scratch, principal, withMaterial: true);
        var victimRef = victim.SecretRef!;
        var before = await SnapshotAsync(scratch);
        var auditRowsBefore = (await AuditRowsAsync(scratch)).Count;
        (await ResolveAsync(scratch, victimRef)).Should().Be(OldSecret, "precondition");

        var shapes = new (string Name, Func<ConnectionManagementService, Task> Run)[]
        {
            ("create naming it", s => s.CreateConnectionAsync(new CreateConnectionRequest(
                Sql, "Stolen", new Dictionary<string, string> { ["Host"] = HostSentinel }, new Dictionary<string, string> { ["Password"] = victimRef })).AsTask()),
            ("create naming it with material", s => s.CreateConnectionAsync(new CreateConnectionRequest(
                Sql, "Stolen", new Dictionary<string, string> { ["Host"] = HostSentinel }, new Dictionary<string, string> { ["Password"] = victimRef },
                SecretValues: new Dictionary<string, string> { ["Password"] = NewSecret })).AsTask()),
            // An update cannot name a reference at all (SEC-2.2): the request type has no member for one, and the API refuses a
            // PATCH that sends secretRefs. Only creation still receives a reference, to refuse it.
        };

        foreach (var (name, run) in shapes)
        {
            await using var db = scratch.NewDb();
            var ex = await RefusalOf(() => run(Service(db, principal, NewUnit(db, principal))));
            RevealsNothing(ex, victimRef, victimRef.Replace("-", string.Empty), NewSecret, victim.ConnectionId);
            (await SnapshotAsync(scratch)).Should().Be(before, $"{name}: nothing was created, attached, versioned or activated");
            (await AuditRowsAsync(scratch)).Should().HaveCount(auditRowsBefore, $"{name}: a refused request records nothing, a refused create included");
        }

        (await ResolveAsync(scratch, victimRef)).Should().Be(OldSecret, "the victim still resolves its own credential");
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM SecretVersions WHERE SecretId = '{victimRef}';")).Should().Be(1);
        (await AuditRowsAsync(scratch, attacker.ConnectionId)).Should().BeEmpty();
        (await AuditRowsAsync(scratch, victim.ConnectionId)).Should().BeEmpty();
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_row_that_already_names_another_connections_secret_can_still_be_edited_and_is_repaired_by_a_value_without_touching_it(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var victim = await SeedConnectionAsync(scratch, principal, withMaterial: true);
        var poisoned = await SeedConnectionAsync(scratch, principal, withMaterial: true);
        var victimRef = victim.SecretRef!;
        var poisonedId = new ConnectionId(poisoned.ConnectionId);
        await scratch.ExecuteAsync(
            $"UPDATE Connections SET SecretRefsJson = N'{{\"Password\":\"{victimRef}\"}}' WHERE Id = N'{poisoned.ConnectionId}';");
        var before = await SnapshotAsync(scratch);

        // Config decides where the credential is sent, so a config change is refused while the row names a secret that is not its own.
        await using (var db = scratch.NewDb())
        {
            var ex = await RefusalOf(() => Service(db, principal, NewUnit(db, principal)).UpdateConnectionAsync(
                poisonedId, new UpdateConnectionRequest(null, new Dictionary<string, string> { ["Host"] = "attacker.example" }, null)).AsTask());
            RevealsNothing(ex, victimRef);
        }

        (await SnapshotAsync(scratch)).Should().Be(before);

        // A request that only edits metadata is not turned away by a legacy reference, and never touches the secret.
        await using (var db = scratch.NewDb())
        {
            await Service(db, principal, NewUnit(db, principal)).UpdateConnectionAsync(
                poisonedId, new UpdateConnectionRequest("Renamed legacy", null, null));
        }

        (await ResolveAsync(scratch, victimRef)).Should().Be(OldSecret);
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM SecretVersions WHERE SecretId = '{victimRef}';")).Should().Be(1);

        // Supplying a value gives the field a secret of its own, repointed atomically, and leaves the victim alone. No reference is sent.
        await using (var db = scratch.NewDb())
        {
            await Service(db, principal, NewUnit(db, principal)).UpdateConnectionAsync(
                poisonedId, new UpdateConnectionRequest(null, null, new Dictionary<string, string> { ["Password"] = NewSecret }));
        }

        var repaired = (await ConnectionRefsAsync(scratch, poisoned.ConnectionId)).Single();
        repaired.Should().NotBe(victimRef);
        (await ResolveAsync(scratch, repaired)).Should().Be(NewSecret);
        (await scratch.ScalarAsync<string>($"SELECT Name FROM SecretDescriptors WHERE Id = '{repaired}';")).Should().Be($"connection:{poisoned.ConnectionId}:Password");
        (await scratch.ScalarAsync<string>($"SELECT Provider FROM SecretDescriptors WHERE Id = '{repaired}';")).Should().Be("platform-connection");
        (await ResolveAsync(scratch, victimRef)).Should().Be(OldSecret, "the victim's credential was never touched");
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM SecretVersions WHERE SecretId = '{victimRef}';")).Should().Be(1);
        (await AuditRowsAsync(scratch, poisoned.ConnectionId)).Select(r => (string)r["Action"]!).Should().Contain(AuditActionNames.ConnectionSecretStored);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [InlineData(false, "platform-setting", "connection:{0}:Password")]
    [InlineData(false, "platform-connection", "connection:{0}:Database")]
    [InlineData(true, "platform-connection", "connection:{0}:password")]
    [InlineData(true, "platform-connection", "connection:{0}:Password:x")]
    public async Task A_secret_with_the_wrong_provider_or_a_different_field_name_is_not_this_connections_secret_and_is_never_rotated(bool rcsi, string provider, string nameFormat)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: true);
        var lookalike = await SeedSecretAsync(scratch, provider, string.Format(nameFormat, seeded.ConnectionId), OldSecret);
        await scratch.ExecuteAsync(
            $"UPDATE Connections SET SecretRefsJson = N'{{\"Password\":\"{lookalike}\"}}' WHERE Id = N'{seeded.ConnectionId}';");
        var lookalikeBefore = await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM SecretVersions WHERE SecretId = '{lookalike}';");

        await using (var db = scratch.NewDb())
        {
            await Service(db, principal, NewUnit(db, principal)).UpdateConnectionAsync(
                new ConnectionId(seeded.ConnectionId),
                new UpdateConnectionRequest(null, null, new Dictionary<string, string> { ["Password"] = NewSecret }));
        }

        var repaired = (await ConnectionRefsAsync(scratch, seeded.ConnectionId)).Single();
        repaired.Should().NotBe(lookalike, "the field was repointed to a secret of its own");
        (await ResolveAsync(scratch, repaired)).Should().Be(NewSecret);
        (await ResolveAsync(scratch, lookalike)).Should().Be(OldSecret, "the lookalike keeps its material");
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM SecretVersions WHERE SecretId = '{lookalike}';")).Should().Be(lookalikeBefore, "no version was added to it");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task First_time_provisioning_creates_a_secret_named_for_the_connection_and_its_field(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: false);

        await using (var db = scratch.NewDb())
        {
            await Service(db, principal, NewUnit(db, principal)).UpdateConnectionAsync(
                new ConnectionId(seeded.ConnectionId), new UpdateConnectionRequest(null, null, new Dictionary<string, string> { ["Password"] = NewSecret }));
        }

        var created = (await ConnectionRefsAsync(scratch, seeded.ConnectionId)).Single();
        (await ResolveAsync(scratch, created)).Should().Be(NewSecret);
        (await scratch.ScalarAsync<string>($"SELECT Name FROM SecretDescriptors WHERE Id = '{created}';")).Should().Be($"connection:{seeded.ConnectionId}:Password");
        (await scratch.ScalarAsync<string>($"SELECT Provider FROM SecretDescriptors WHERE Id = '{created}';")).Should().Be("platform-connection");

        // And the next rotation is an ordinary rotation of that secret: the reference now proves itself.
        await using (var db = scratch.NewDb())
        {
            await Service(db, principal, NewUnit(db, principal)).UpdateConnectionAsync(
                new ConnectionId(seeded.ConnectionId), new UpdateConnectionRequest(null, null, new Dictionary<string, string> { ["Password"] = OldSecret }));
        }

        (await ResolveAsync(scratch, created)).Should().Be(OldSecret);
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM SecretVersions WHERE SecretId = '{created}';")).Should().Be(2);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- Waiting on the connection row (the lock a move and every connection writer take) ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_rotations_queued_on_the_row_serialize_and_both_versions_and_rows_survive(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: true);

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, "Connections", seeded.ConnectionId);
        var first = Rotate(scratch, principal, seeded.ConnectionId, "sentinel-first-rotation");
        await holder.WaitUntilBlockedAsync(scratch, "Connections", first);
        var second = Rotate(scratch, principal, seeded.ConnectionId, "sentinel-second-rotation");
        await holder.WaitUntilBlockedAsync(scratch, "Connections", Task.WhenAny(first, second), minimumWaiters: 2);

        await holder.CommitAsync();
        await Task.WhenAll(first, second);

        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM SecretVersions WHERE SecretId = '{seeded.SecretRef}';")).Should().Be(3);
        var live = await ResolveAsync(scratch, seeded.SecretRef!);
        live.Should().BeOneOf("sentinel-first-rotation", "sentinel-second-rotation");
        var rotations = (await AuditRowsAsync(scratch, seeded.ConnectionId)).Where(r => (string)r["Action"]! == AuditActionNames.ConnectionSecretRotated).ToList();
        rotations.Should().HaveCount(2);
        _output.WriteLine($"{scratch.ExecutionMarker}; last writer: {live}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_rotation_and_a_retirement_queued_on_the_row_both_commit_in_either_order(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: true);

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, "Connections", seeded.ConnectionId);
        var rotation = Rotate(scratch, principal, seeded.ConnectionId, NewSecret);
        await holder.WaitUntilBlockedAsync(scratch, "Connections", rotation);
        var retirement = Task.Run(async () =>
        {
            await using var db = scratch.NewDb();
            await Service(db, principal, NewUnit(db, principal)).RetireConnectionAsync(new ConnectionId(seeded.ConnectionId));
        });
        await holder.WaitUntilBlockedAsync(scratch, "Connections", Task.WhenAny(rotation, retirement), minimumWaiters: 2);

        await holder.CommitAsync();
        await Task.WhenAll(rotation, retirement);

        (await scratch.ScalarAsync<int>($"SELECT State FROM Connections WHERE Id = N'{seeded.ConnectionId}';")).Should().Be((int)ConnectionState.Retired);
        (await ResolveAsync(scratch, seeded.SecretRef!)).Should().Be(NewSecret);
        (await AuditRowsAsync(scratch, seeded.ConnectionId)).Select(r => (string)r["Action"]!).Should().BeEquivalentTo(
            AuditActionNames.ConnectionSecretRotated, AuditActionNames.ConnectionSecretStored, AuditActionNames.ConnectionRetired);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_rename_and_a_config_change_queued_on_the_row_keep_both(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: false);
        var id = new ConnectionId(seeded.ConnectionId);

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, "Connections", seeded.ConnectionId);
        var rename = Task.Run(async () =>
        {
            await using var db = scratch.NewDb();
            await Service(db, principal, NewUnit(db, principal)).UpdateConnectionAsync(id, new UpdateConnectionRequest("Renamed", null, null));
        });
        await holder.WaitUntilBlockedAsync(scratch, "Connections", rename);
        var config = Task.Run(async () =>
        {
            await using var db = scratch.NewDb();
            await Service(db, principal, NewUnit(db, principal)).UpdateConnectionAsync(
                id, new UpdateConnectionRequest(null, new Dictionary<string, string> { ["Host"] = HostSentinel, ["Database"] = "Ops" }, null));
        });
        await holder.WaitUntilBlockedAsync(scratch, "Connections", Task.WhenAny(rename, config), minimumWaiters: 2);

        await holder.CommitAsync();
        await Task.WhenAll(rename, config);

        await using var read = scratch.NewDb();
        var stored = (await new SqlConnectionRepository(read).GetByIdAsync(id))!;
        stored.Name.Should().Be("Renamed");
        stored.Config.Should().ContainKey("Database").WhoseValue.Should().Be("Ops");
        (await AuditRowsAsync(scratch, seeded.ConnectionId)).Select(r => (string)r["Action"]!).Should().Equal(
            Enumerable.Repeat(AuditActionNames.ConnectionUpdated, 2), "each recorded exactly its own change");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_rotation_that_waits_while_an_ownership_move_commits_is_recorded_in_the_new_owning_context(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: true);
        const string target = "app-conn-target";
        await scratch.ExecuteAsync(
            $"""
            INSERT INTO ResourceContexts (Id, Name, Slug, Kind, State, IsReserved, CreatedAtUtc, UpdatedAtUtc)
            VALUES (N'{target}', N'Conn Target', N'{target}', N'{ResourceContextKinds.Application}', N'{ResourceContextStates.Active}', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, "Connections", seeded.ConnectionId);
        var rotation = Rotate(scratch, principal, seeded.ConnectionId, NewSecret);
        await holder.WaitUntilBlockedAsync(scratch, "Connections", rotation);
        await holder.ExecuteAsync(
            "UPDATE [Connections] SET [ResourceContextId] = @to WHERE [Id] = @id;", ("@to", target), ("@id", seeded.ConnectionId));
        await holder.CommitAsync();
        await rotation;

        var rows = await AuditRowsAsync(scratch, seeded.ConnectionId);
        rows.Should().NotBeEmpty().And.OnlyContain(r => (string)r["ResourceContextId"]! == target,
            "the decision and the evidence read the connection as it was when the lock was granted, after the move");
        (await ResolveAsync(scratch, seeded.SecretRef!)).Should().Be(NewSecret);
    }

    // ---- wiring ----

    private static Task Rotate(AuditSqlScratch scratch, IPrincipalContext principal, string connectionId, string material) =>
        Task.Run(async () =>
        {
            await using var db = scratch.NewDb();
            await Service(db, principal, NewUnit(db, principal)).UpdateConnectionAsync(
                new ConnectionId(connectionId),
                new UpdateConnectionRequest(null, null, new Dictionary<string, string> { ["Password"] = material }));
        });

    private sealed record Arranged(
        string? ConnectionId,
        IReadOnlyList<string> Actions,
        Func<AppertureDbContext, IAuditedUnitOfWork, Task> Run,
        Action<List<Dictionary<string, object?>>> AssertDetails,
        string? CreatedName = null);

    private async Task<Arranged> ArrangeAsync(AuditSqlScratch scratch, Op op, IPrincipalContext principal)
    {
        switch (op)
        {
            case Op.CreatePlain:
            {
                var name = "Plain-" + Guid.NewGuid().ToString("N")[..8];
                return new Arranged(
                    null,
                    [AuditActionNames.ConnectionCreated],
                    (db, unit) => Service(db, principal, unit).CreateConnectionAsync(new CreateConnectionRequest(
                        Sql, name, new Dictionary<string, string> { ["Host"] = HostSentinel },
                        new Dictionary<string, string>())).AsTask(),
                    rows =>
                    {
                        Detail(rows.Single(), "fieldsStored").Should().Be("0");
                        Detail(rows.Single(), "name").Should().Be(name);
                        Detail(rows.Single(), "connectorId").Should().Be(Sql.Value);
                    },
                    name);
            }

            case Op.CreateWithMaterial:
            {
                var name = "WithMaterial-" + Guid.NewGuid().ToString("N")[..8];
                return new Arranged(
                    null,
                    [AuditActionNames.ConnectionCreated, AuditActionNames.ConnectionSecretStored],
                    (db, unit) => Service(db, principal, unit).CreateConnectionAsync(new CreateConnectionRequest(
                        Sql, name, new Dictionary<string, string> { ["Host"] = HostSentinel }, new Dictionary<string, string>(),
                        SecretValues: new Dictionary<string, string> { ["Password"] = NewSecret })).AsTask(),
                    rows =>
                    {
                        Detail(rows.Single(r => (string)r["Action"]! == AuditActionNames.ConnectionCreated), "fieldsStored").Should().Be("1");
                        var stored = rows.Single(r => (string)r["Action"]! == AuditActionNames.ConnectionSecretStored);
                        Detail(stored, "fieldKey").Should().Be("Password");
                        Detail(stored, "change").Should().Be("created");
                    },
                    name);
            }

            case Op.Rename:
            {
                var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: false);
                return new Arranged(
                    seeded.ConnectionId,
                    [AuditActionNames.ConnectionUpdated],
                    (db, unit) => Service(db, principal, unit).UpdateConnectionAsync(
                        new ConnectionId(seeded.ConnectionId), new UpdateConnectionRequest("Renamed", null, null)).AsTask(),
                    rows =>
                    {
                        Detail(rows.Single(), "nameChanged").Should().Be("true");
                        Detail(rows.Single(), "name").Should().Be("Renamed");
                    });
            }

            case Op.RotateMaterial:
            {
                var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: true);
                return new Arranged(
                    seeded.ConnectionId,
                    [AuditActionNames.ConnectionSecretRotated, AuditActionNames.ConnectionSecretStored],
                    (db, unit) => Service(db, principal, unit).UpdateConnectionAsync(
                        new ConnectionId(seeded.ConnectionId),
                        new UpdateConnectionRequest(null, null, new Dictionary<string, string> { ["Password"] = NewSecret })).AsTask(),
                    rows =>
                    {
                        Detail(rows.Single(r => (string)r["Action"]! == AuditActionNames.ConnectionSecretRotated), "fieldsRotated").Should().Be("1");
                        Detail(rows.Single(r => (string)r["Action"]! == AuditActionNames.ConnectionSecretStored), "change").Should().Be("replaced");
                    });
            }

            case Op.RotateRef:
            {
                var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: true);

                // A request cannot name a reference any more (SEC-2.2); the way a reference changes without new material is clearing the field.
                return new Arranged(
                    seeded.ConnectionId,
                    [AuditActionNames.ConnectionSecretRotated],
                    (db, unit) => Service(db, principal, unit).UpdateConnectionAsync(
                        new ConnectionId(seeded.ConnectionId),
                        new UpdateConnectionRequest(ClearSecretFields: ["Password"])).AsTask(),
                    rows => Detail(rows.Single(), "fieldsRotated").Should().Be("1"));
            }

            default:
            {
                var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: true);
                return new Arranged(
                    seeded.ConnectionId,
                    [AuditActionNames.ConnectionRetired],
                    (db, unit) => Service(db, principal, unit).RetireConnectionAsync(new ConnectionId(seeded.ConnectionId)).AsTask(),
                    rows => Detail(rows.Single(), "name").Should().Be("Seeded connection"));
            }
        }
    }

    private async Task<AuditedOperationM6Runner> RunnerAsync(AuditSqlScratch scratch, Op op, IPrincipalContext principal)
    {
        var arranged = await ArrangeAsync(scratch, op, principal);
        return new AuditedOperationM6Runner(scratch, () => SnapshotAsync(scratch), arranged.Actions[0], arranged.Run, NewSecret);
    }

    private static async Task<List<string>> ConnectionRefsAsync(AuditSqlScratch scratch, string connectionId)
    {
        var json = await scratch.ScalarAsync<string>($"SELECT SecretRefsJson FROM Connections WHERE Id = N'{connectionId}';");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)!.Values.ToList();
    }
}
