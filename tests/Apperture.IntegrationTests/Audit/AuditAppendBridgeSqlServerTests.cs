using Apperture.Core.Application;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// The transitional <c>AppendAsync</c> bridge against SQL Server (audit hardening design gate §14; A1.1-11): unconverted
/// writers keep working, and their rows carry the producer, event id and operation event id, keep the details exactly as
/// given, and are inserted either complete or wholly unclassified. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class AuditAppendBridgeSqlServerTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private AuditSqlScratch _db = null!;

    public AuditAppendBridgeSqlServerTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync() => _db = await AuditSqlScratch.CreateAsync("Apperture_Aud11Append_");

    public async Task DisposeAsync()
    {
        _output.WriteLine(_db.ExecutionMarker);
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task An_append_row_carries_its_producer_and_ids_keeps_its_details_and_is_complete_at_insert()
    {
        var actor = NewUserId();
        var actorName = NewUsername("admin");
        await _db.AddUserAsync(actor, actorName);
        var recordId = Guid.NewGuid().ToString("N");

        await using (var db = _db.NewDb())
        {
            await new SqlAuditService(db).AppendAsync(new AuditRecord(
                new AuditRecordId(recordId),
                new ActorId(actor),
                "governance.role.assignment.created",
                new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance),
                "auth-admin",
                DateTimeOffset.UtcNow,
                new CorrelationId(null),
                "userId=u1 roleId=r1"));
        }

        var row = (await _db.RowsAsync($"SELECT * FROM AuditLogs WHERE EventId = N'{recordId}';")).Should().ContainSingle().Which;
        row["Producer"].Should().Be("append");
        row["OperationEventId"].Should().Be(recordId);
        row["Details"].Should().Be("userId=u1 roleId=r1", "the bridge no longer appends a Resource= suffix");
        row["DetailsFormat"].Should().BeNull();
        row["ClassificationState"].Should().Be("complete");
        row["ActorKind"].Should().Be("user");
        row["ActorUserId"].Should().Be(actor);
        row["ActorUsername"].Should().Be(actorName, "append rows read the username inside their own write");
        row["ActorAttribution"].Should().Be("recorded_user_id");
        row["Category"].Should().Be("governance");
        row["Basis"].Should().Be("change");
        row["OutcomeProvenance"].Should().Be("recorded");
        row["OutcomeCode"].Should().Be("unknown", "auth-admin is a category label, and this action's name states no result");
        row["Outcome"].Should().Be("auth-admin");
        _output.WriteLine(_db.ExecutionMarker);
    }

    [Fact]
    public async Task When_an_insert_time_lookup_cannot_run_the_row_is_stored_wholly_unclassified_and_completed_at_the_next_start()
    {
        var recordId = Guid.NewGuid().ToString("N");
        var failingLookup = new FailingCommand(sql => sql.Contains("FROM [UsernameLedger]", StringComparison.Ordinal));

        await using (var db = _db.NewDb(failingLookup))
        {
            await new SqlAuditService(db).AppendAsync(Record(recordId, "someone.legacy", "identity_link.added", "auth-admin"));
        }

        failingLookup.Failures.Should().Be(1);
        var row = (await _db.RowsAsync($"SELECT * FROM AuditLogs WHERE EventId = N'{recordId}';")).Single();
        row["Producer"].Should().Be("append");
        foreach (var column in new[]
        {
            "ClassificationState", "ActorKind", "ActorUserId", "ActorUsername", "ActorAttribution", "AttributionIssue",
            "TargetType", "TargetId", "Category", "Severity", "Visibility", "Basis", "OutcomeCode", "OutcomeProvenance",
        })
        {
            row[column].Should().BeNull($"{column} is part of the set, which is never partially classified");
        }

        await using (var db = _db.NewDb())
            await AuditClassification.RunAsync(db, NullLogger.Instance);

        var completed = (await _db.RowsAsync($"SELECT * FROM AuditLogs WHERE EventId = N'{recordId}';")).Single();
        completed["ClassificationState"].Should().Be("complete");
        completed["ActorKind"].Should().Be("unresolved");
        completed["OutcomeProvenance"].Should().Be("recorded");
    }

    [Fact]
    public async Task A_retried_append_whose_row_already_exists_is_not_written_twice()
    {
        var recordId = Guid.NewGuid().ToString("N");
        await using var db = _db.NewDb();
        var audit = new SqlAuditService(db);

        await audit.AppendAsync(Record(recordId, "system", "runtime.composition.invalid_at_startup", "Degraded"));
        await audit.AppendAsync(Record(recordId, "system", "runtime.composition.invalid_at_startup", "Degraded"));

        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE EventId = N'{recordId}';")).Should().Be(1);
    }

    [Fact]
    public async Task Append_rows_apply_the_unknown_protection_literal_kinds_defects_and_the_unknown_action_rule()
    {
        await _db.AddUserAsync(NewUserId(), "unknown");
        var unknown = Guid.NewGuid().ToString("N");
        var worker = Guid.NewGuid().ToString("N");
        var invitation = Guid.NewGuid().ToString("N");
        var undeclared = Guid.NewGuid().ToString("N");

        await using (var db = _db.NewDb())
        {
            var audit = new SqlAuditService(db);
            await audit.AppendAsync(Record(unknown, "unknown", "execution.operation_item.bulk_retry.requested", "success"));
            await audit.AppendAsync(Record(worker, "system-worker", "execution.workflow_run.failed_definition_unavailable", "Blocked"));
            await audit.AppendAsync(Record(invitation, "invitee.person", "account.invitation.sent", "account"));
            await audit.AppendAsync(Record(undeclared, "system", "not.a.declared.action", "succeeded"));
        }

        var rows = (await _db.RowsAsync("SELECT * FROM AuditLogs WHERE Producer = N'append';")).ToDictionary(r => (string)r["EventId"]!);
        rows[unknown]["ActorKind"].Should().Be("unresolved");
        rows[unknown]["ActorUserId"].Should().BeNull("the placeholder is never looked up, even with a user named unknown");
        rows[unknown]["OutcomeCode"].Should().Be("requested");
        rows[worker]["ActorKind"].Should().Be("worker");
        rows[worker]["OutcomeCode"].Should().Be("unknown");
        rows[invitation]["AttributionIssue"].Should().Be("subject_recorded_as_actor");
        rows[invitation]["AttributionIssueCertainty"].Should().Be("definite");
        rows[undeclared]["Category"].Should().Be("other");
        rows[undeclared]["Basis"].Should().Be("unspecified");
        rows[undeclared]["ClassificationState"].Should().Be("complete");
    }

    private static AuditRecord Record(string id, string actor, string action, string outcome) =>
        new(new AuditRecordId(id), new ActorId(actor), action, null, outcome, DateTimeOffset.UtcNow, new CorrelationId(null), "details text");
}
