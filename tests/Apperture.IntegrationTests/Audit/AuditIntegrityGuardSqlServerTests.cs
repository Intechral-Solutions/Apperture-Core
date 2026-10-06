using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// The AUD-1.1 integrity guard on a real SQL Server (audit hardening design gate §11.8, §19.2; A1.1-13, A1.1-19, M8):
/// the legitimate transitions succeed; any change to evidence, any change to a completed classification set (including
/// filling an intentionally null column), partial or invalid transitions, and every delete are refused.
/// </summary>
/// <remarks>
/// Where the classification check constraint already rejects a value combination, the refusal is the constraint's
/// (error 547). Each frozen-set case also has a variant the constraint allows, so the trigger (error 51001) is proven
/// on its own. Fails, never skips, without SQL Server.
/// </remarks>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class AuditIntegrityGuardSqlServerTests : IAsyncLifetime
{
    private const int CheckViolation = 547;
    private readonly ITestOutputHelper _output;
    private AuditSqlScratch _db = null!;

    public AuditIntegrityGuardSqlServerTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync() => _db = await AuditSqlScratch.CreateAsync("Apperture_Aud11Guard_");

    public async Task DisposeAsync()
    {
        _output.WriteLine(_db.ExecutionMarker);
        await _db.DisposeAsync();
    }

    // ---- Legitimate transitions (A1.1-13) ----

    [Fact]
    public async Task A_recorder_row_inserts_complete_and_a_legacy_row_inserts_unclassified_then_completes_once()
    {
        var recorder = await InsertCompleteUserRowAsync();
        var legacy = await InsertLegacyRowAsync();

        await _db.ExecuteAsync(CompleteSql(legacy, kind: "unresolved", userId: null, attribution: "unresolved"));

        (await _db.ScalarAsync<string>($"SELECT ClassificationState FROM AuditLogs WHERE Id = {legacy};")).Should().Be("complete");
        (await _db.ScalarAsync<string>($"SELECT ClassificationState FROM AuditLogs WHERE Id = {recorder};")).Should().Be("complete");
        _output.WriteLine(_db.ExecutionMarker);
    }

    [Fact]
    public async Task Rewriting_identical_values_into_a_completed_row_changes_nothing_and_is_allowed()
    {
        var id = await InsertCompleteUserRowAsync();

        await _db.ExecuteAsync($"UPDATE AuditLogs SET Category = Category, Details = Details WHERE Id = {id};");

        (await _db.ScalarAsync<string>($"SELECT Category FROM AuditLogs WHERE Id = {id};")).Should().Be("identity");
    }

    // ---- Frozen classification (A1.1-19) ----

    [Fact]
    public async Task A_completed_user_row_cannot_gain_external_columns_or_any_other_value()
    {
        var id = await InsertCompleteUserRowAsync();

        await RefusedAsync(
            $"UPDATE AuditLogs SET ActorExternalProvider = N'entra', ActorExternalTenantId = N't', ActorExternalSubjectId = N's' WHERE Id = {id};",
            AuditLogSchema.UpdateRefusedError, CheckViolation);
        // Allowed by the check constraint (a user row may carry an issue), refused by the trigger alone.
        await RefusedAsync(
            $"UPDATE AuditLogs SET AttributionIssue = N'possible_placeholder', AttributionIssueCertainty = N'possible' WHERE Id = {id};",
            AuditLogSchema.UpdateRefusedError);
        await RefusedAsync($"UPDATE AuditLogs SET TargetType = N'role', TargetId = N'r1' WHERE Id = {id};", AuditLogSchema.UpdateRefusedError);
        await AssertUnchangedAsync(id);
    }

    [Fact]
    public async Task A_completed_external_row_cannot_gain_a_user_id()
    {
        var id = await InsertAsync(CompleteRowSql(
            kind: "external", userId: null, username: null, attribution: "recorded",
            external: ("entra", "11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222")));

        await RefusedAsync($"UPDATE AuditLogs SET ActorUserId = N'u1' WHERE Id = {id};", AuditLogSchema.UpdateRefusedError, CheckViolation);
        // A whole consistent rewrite to a user row passes the check constraint and is refused by the trigger.
        await RefusedAsync(
            $"UPDATE AuditLogs SET ActorKind = N'user', ActorUserId = N'u1', ActorExternalProvider = NULL, ActorExternalTenantId = NULL, ActorExternalSubjectId = NULL WHERE Id = {id};",
            AuditLogSchema.UpdateRefusedError);
        (await _db.ScalarAsync<string>($"SELECT ActorUserId FROM AuditLogs WHERE Id = {id};")).Should().BeNull();
    }

    [Fact]
    public async Task A_completed_unresolved_row_cannot_gain_a_user_id_or_a_target()
    {
        var legacy = await InsertLegacyRowAsync();
        await _db.ExecuteAsync(CompleteSql(legacy, kind: "unresolved", userId: null, attribution: "unresolved"));

        await RefusedAsync($"UPDATE AuditLogs SET ActorUserId = N'u1' WHERE Id = {legacy};", AuditLogSchema.UpdateRefusedError, CheckViolation);
        await RefusedAsync($"UPDATE AuditLogs SET ActorKind = N'user', ActorUserId = N'u1' WHERE Id = {legacy};", AuditLogSchema.UpdateRefusedError);
        await RefusedAsync($"UPDATE AuditLogs SET TargetType = N'user', TargetId = N'u1' WHERE Id = {legacy};", AuditLogSchema.UpdateRefusedError);
        (await _db.ScalarAsync<string>($"SELECT ActorKind FROM AuditLogs WHERE Id = {legacy};")).Should().Be("unresolved");
        (await _db.ScalarAsync<string>($"SELECT TargetType FROM AuditLogs WHERE Id = {legacy};")).Should().BeNull();
    }

    [Fact]
    public async Task A_second_classification_and_a_reset_to_unclassified_are_refused()
    {
        var legacy = await InsertLegacyRowAsync();
        await _db.ExecuteAsync(CompleteSql(legacy, kind: "unresolved", userId: null, attribution: "unresolved"));

        await RefusedAsync($"UPDATE AuditLogs SET Category = N'identity' WHERE Id = {legacy};", AuditLogSchema.UpdateRefusedError);

        // A second classifier's conditional transition matches nothing; an unconditional one is refused by the guard.
        await _db.ExecuteAsync(CompleteSql(legacy, kind: "system", userId: null, attribution: "recorded_literal"));
        (await _db.ScalarAsync<string>($"SELECT ActorKind FROM AuditLogs WHERE Id = {legacy};")).Should().Be("unresolved");
        await RefusedAsync(
            CompleteSql(legacy, kind: "system", userId: null, attribution: "recorded_literal").Replace(" AND ClassificationState IS NULL", ""),
            AuditLogSchema.UpdateRefusedError);
        await RefusedAsync(
            $"""
            UPDATE AuditLogs SET ClassificationState = NULL, ActorKind = NULL, ActorAttribution = NULL, Category = NULL, Severity = NULL,
                Visibility = NULL, Basis = NULL, OutcomeCode = NULL, OutcomeProvenance = NULL WHERE Id = {legacy};
            """,
            AuditLogSchema.UpdateRefusedError);
    }

    [Fact]
    public async Task Partial_and_invalid_transitions_of_an_unclassified_row_are_refused()
    {
        var legacy = await InsertLegacyRowAsync();

        await RefusedAsync($"UPDATE AuditLogs SET Category = N'identity' WHERE Id = {legacy};", AuditLogSchema.UpdateRefusedError, CheckViolation);
        await RefusedAsync(
            CompleteSql(legacy, kind: "unresolved", userId: null, attribution: "unresolved").Replace("N'complete'", "N'Complete'"),
            AuditLogSchema.UpdateRefusedError, CheckViolation);
        await RefusedAsync(
            CompleteSql(legacy, kind: "unresolved", userId: null, attribution: "unresolved").Replace("Basis = N'unspecified'", "Basis = NULL"),
            CheckViolation);
        (await _db.ScalarAsync<string>($"SELECT ClassificationState FROM AuditLogs WHERE Id = {legacy};")).Should().BeNull();
    }

    [Fact]
    public async Task Evidence_columns_never_change_even_by_letter_case_or_trailing_space()
    {
        var legacy = await InsertLegacyRowAsync();

        foreach (var change in new[]
        {
            "Actor = N'ALICE'",
            "Details = Details + N' '",
            "Outcome = N'Succeeded'",
            "Action = N'other.action'",
            "At = DATEADD(second, 1, At)",
            "RunId = N'run-1'",
            "EventId = N'e-new'",
            "Producer = N'append'",
        })
        {
            await RefusedAsync($"UPDATE AuditLogs SET {change} WHERE Id = {legacy};", AuditLogSchema.UpdateRefusedError, CheckViolation);
        }

        var row = (await _db.RowsAsync($"SELECT Actor, Details, Outcome FROM AuditLogs WHERE Id = {legacy};")).Single();
        row["Actor"].Should().Be("alice");
        row["Details"].Should().Be("userId=u1 previousUsername=a");
        row["Outcome"].Should().Be("succeeded");
    }

    [Fact]
    public async Task Every_delete_is_refused()
    {
        var recorder = await InsertCompleteUserRowAsync();
        var legacy = await InsertLegacyRowAsync();

        await RefusedAsync($"DELETE FROM AuditLogs WHERE Id = {legacy};", AuditLogSchema.DeleteRefusedError);
        await RefusedAsync($"DELETE FROM AuditLogs WHERE Id IN ({legacy}, {recorder});", AuditLogSchema.DeleteRefusedError);

        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Id IN ({legacy}, {recorder});")).Should().Be(2);
    }

    [Fact]
    public async Task Partially_classified_inserts_and_unclassified_recorder_rows_are_refused()
    {
        await RefusedAsync(
            "INSERT INTO AuditLogs (At, Actor, Action, ClassificationState, ActorKind) VALUES (SYSDATETIMEOFFSET(), N'a', N'x.y', N'complete', N'user');",
            CheckViolation);
        await RefusedAsync(
            "INSERT INTO AuditLogs (At, Actor, Action, Category) VALUES (SYSDATETIMEOFFSET(), N'a', N'x.y', N'identity');",
            CheckViolation);
        await RefusedAsync(
            "INSERT INTO AuditLogs (At, Actor, Action, EventId, OperationEventId, Producer, DetailsFormat) VALUES (SYSDATETIMEOFFSET(), N'a', N'x.y', N'e1', N'e1', N'recorder', N'json/1');",
            CheckViolation);
        await RefusedAsync(
            "INSERT INTO AuditLogs (At, Actor, Action, Producer) VALUES (SYSDATETIMEOFFSET(), N'a', N'x.y', N'append');",
            CheckViolation);
    }

    [Fact]
    public async Task An_earlier_program_versions_insert_still_works_and_its_row_waits_unclassified()
    {
        // I-5. EF Core inserts with OUTPUT INSERTED.<key> unless the model declares the table's triggers. SQL Server refuses
        // that only when the table has a trigger for the same action, and the guard has none for INSERT. So an earlier
        // program version (whose model knows nothing of the triggers) can still write audit rows after a program-only
        // rollback; they have no producer and are classified at the next start of this version.
        var id = await _db.ScalarAsync<long>(
            "INSERT INTO AuditLogs (At, Actor, Action) OUTPUT INSERTED.Id VALUES (SYSDATETIMEOFFSET(), N'system', N'legacy.write');");
        (await _db.ScalarAsync<string>($"SELECT ClassificationState FROM AuditLogs WHERE Id = {id};")).Should().BeNull();

        // The current model declares the triggers and inserts through EF as well.
        await using var current = _db.NewDb();
        current.AuditLogs.Add(new AuditLogEntity { At = DateTimeOffset.UtcNow, Actor = "system", Action = "legacy.write" });
        await current.SaveChangesAsync();
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = N'legacy.write';")).Should().Be(2);
    }

    // ---- helpers ----

    private async Task RefusedAsync(string sql, params int[] numbers)
    {
        var act = () => _db.ExecuteAsync(sql);
        var thrown = await act.Should().ThrowAsync<SqlException>();
        thrown.Which.Number.Should().BeOneOf(numbers, because: sql);
    }

    private async Task AssertUnchangedAsync(long id)
    {
        var row = (await _db.RowsAsync(
            $"SELECT ActorExternalProvider, AttributionIssue, TargetType, ActorUserId FROM AuditLogs WHERE Id = {id};")).Single();
        row["ActorExternalProvider"].Should().BeNull();
        row["AttributionIssue"].Should().BeNull();
        row["TargetType"].Should().BeNull();
        row["ActorUserId"].Should().Be("u1");
    }

    private Task<long> InsertCompleteUserRowAsync() =>
        InsertAsync(CompleteRowSql(kind: "user", userId: "u1", username: null, attribution: "recorded", external: null));

    private Task<long> InsertLegacyRowAsync() =>
        InsertAsync("""
            INSERT INTO AuditLogs (At, Actor, Action, Details, Outcome)
            VALUES (SYSDATETIMEOFFSET(), N'alice', N'governance.user.username.changed', N'userId=u1 previousUsername=a', N'succeeded');
            """);

    private async Task<long> InsertAsync(string insertSql) =>
        Convert.ToInt64(await _db.ScalarAsync<decimal>(insertSql + " SELECT SCOPE_IDENTITY();"));

    private static string CompleteRowSql(
        string kind,
        string? userId,
        string? username,
        string attribution,
        (string Provider, string Tenant, string Subject)? external)
    {
        var eventId = Guid.NewGuid().ToString("N");
        return $"""
            INSERT INTO AuditLogs (At, Actor, Action, Details, Outcome, EventId, OperationEventId, Producer, DetailsFormat,
                ClassificationState, ActorKind, ActorUserId, ActorUsername, ActorExternalProvider, ActorExternalTenantId, ActorExternalSubjectId,
                ActorAttribution, Category, Severity, Visibility, Basis, OutcomeCode, OutcomeProvenance)
            VALUES (SYSDATETIMEOFFSET(), N'{userId ?? "entra/x/y"}', N'governance.user.username.changed', N'json-details', N'succeeded',
                N'{eventId}', N'{eventId}', N'recorder', N'json/1',
                N'complete', N'{kind}', {Sql(userId)}, {Sql(username)}, {Sql(external?.Provider)}, {Sql(external?.Tenant)}, {Sql(external?.Subject)},
                N'{attribution}', N'identity', N'elevated', N'default', N'change', N'succeeded', N'recorded');
            """;
    }

    private static string CompleteSql(long id, string kind, string? userId, string attribution) =>
        $"""
        UPDATE AuditLogs SET ClassificationState = N'complete', ActorKind = N'{kind}', ActorUserId = {Sql(userId)},
            ActorAttribution = N'{attribution}', Category = N'other', Severity = N'standard', Visibility = N'default',
            Basis = N'unspecified', OutcomeCode = N'unknown', OutcomeProvenance = N'indeterminate'
        WHERE Id = {id} AND ClassificationState IS NULL;
        """;

    private static string Sql(string? value) => value is null ? "NULL" : $"N'{value}'";
}
