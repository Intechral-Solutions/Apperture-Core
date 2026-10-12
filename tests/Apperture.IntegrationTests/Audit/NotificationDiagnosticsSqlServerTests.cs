using System.Collections.Concurrent;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Delivery;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Domain.Notifications;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.NotificationSqlHarness;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.4 slice 6: the diagnostic live sends against SQL Server. The evidence is a best-effort external-attempt row of the operator
/// who asked, written after the observation: its shape (actor, target, basis, outcome, category) is checked as the real writer
/// stores it, a failed write is reported and changes nothing, and no address, subject, body or transport text can reach it. The
/// immediate dispatch of a pipeline test sends only the outbox rows of the instance it created: proven against the real claim,
/// with older pending rows of another instance in the way and with two dispatchers racing for the same rows. Fails, never skips,
/// without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class NotificationDiagnosticsSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private const string TransportErrorSentinel = "550 5.1.1 <private.recipient@example.com> user unknown at mx.internal.example";

    private readonly AuditIsolationCatalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public NotificationDiagnosticsSqlServerTests(AuditIsolationCatalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    // ---- the evidence rows, as the real writer stores them ----

    public static IEnumerable<object[]> EventCases() =>
        from rcsi in new[] { false, true }
        select new object[] { rcsi };

    [Theory]
    [MemberData(nameof(EventCases))]
    public async Task The_diagnostic_rows_are_stored_as_best_effort_external_attempts_of_the_operator(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (admin, principal) = await AdminAsync(scratch);
        var writer = new SqlAuditEventWriter(scratch.Options(), TimeProvider.System, NullLogger<SqlAuditEventWriter>.Instance, principal);
        var definitionId = "ndef_" + Guid.NewGuid().ToString("N")[..12];

        (await writer.TryWriteAsync(AuditEvents.NotificationSmtpTestAttempted("accepted"), AuditActor.CurrentPrincipal)).Should().BeTrue();
        (await writer.TryWriteAsync(AuditEvents.NotificationSmtpTestAttempted("no_answer"), AuditActor.CurrentPrincipal)).Should().BeTrue();
        (await writer.TryWriteAsync(
            AuditEvents.NotificationPipelineTestAttempted(definitionId, "int-x", true, 1, 3, 3, 1, 1, 0, 1, null), AuditActor.CurrentPrincipal)).Should().BeTrue();
        (await writer.TryWriteAsync(AuditEvents.NotificationDiagnosticsTestRefused("smtp", "not_configured"), AuditActor.CurrentPrincipal)).Should().BeTrue();

        var pipeline = (await scratch.RowsAsync($"SELECT * FROM AuditLogs WHERE TargetId = N'{definitionId}';")).Single();
        pipeline["Action"].Should().Be(AuditActionNames.NotificationPipelineTestAttempted);
        pipeline["Actor"].Should().Be(admin);
        pipeline["TargetType"].Should().Be(AuditTargetTypes.NotificationDefinition);
        pipeline["Basis"].Should().Be(AuditBases.ExternalAttempt);
        pipeline["Category"].Should().Be(AuditCategories.Configuration);
        pipeline["Outcome"].Should().Be(AuditOutcomeCodes.Unknown, "one send had no answer");
        Detail(pipeline, "sendsAttempted").Should().Be("3");
        Detail(pipeline, "sendsAccepted").Should().Be("1");
        Detail(pipeline, "sendsRejected").Should().Be("1");
        Detail(pipeline, "sendsUnknown").Should().Be("1");

        var smtp = await scratch.RowsAsync(
            $"SELECT TOP 2 * FROM AuditLogs WHERE Action = N'{AuditActionNames.NotificationSmtpTestAttempted}' AND Actor = N'{admin}' ORDER BY Id DESC;");
        smtp.Select(r => ((string)r["Outcome"]!, Detail(r, "result"))).Should().BeEquivalentTo(
            new[] { (AuditOutcomeCodes.Succeeded, "accepted"), (AuditOutcomeCodes.Unknown, "no_answer") });
        smtp.Should().OnlyContain(r => (string)r["Basis"]! == AuditBases.ExternalAttempt && (string)r["TargetType"]! == AuditTargetTypes.Instance);

        var refused = (await scratch.RowsAsync(
            $"SELECT TOP 1 * FROM AuditLogs WHERE Action = N'{AuditActionNames.NotificationDiagnosticsTestRefused}' AND Actor = N'{admin}' ORDER BY Id DESC;")).Single();
        refused["Basis"].Should().Be(AuditBases.Attempt);
        refused["Outcome"].Should().Be(AuditOutcomeCodes.Rejected);
        AuditText([pipeline, .. smtp, refused]).Should().NotContain("example.com").And.NotContain("user unknown");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Fact]
    public async Task A_failed_evidence_write_is_reported_false_and_never_throws()
    {
        var scratch = _catalogs.For(false);
        var (_, principal) = await AdminAsync(scratch);
        var failing = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        var writer = new SqlAuditEventWriter(scratch.Options(failing), TimeProvider.System, NullLogger<SqlAuditEventWriter>.Instance, principal);
        var before = (await AuditRowsAsync(scratch)).Count;

        (await writer.TryWriteAsync(AuditEvents.NotificationSmtpTestAttempted("accepted"), AuditActor.CurrentPrincipal)).Should().BeFalse();

        failing.Failures.Should().BeGreaterThan(0);
        (await AuditRowsAsync(scratch)).Should().HaveCount(before);
    }

    // ---- the immediate dispatch sends only what the request created ----

    private sealed class RecordingChannel(Func<string, EmailNotificationSendResult> answer) : IEmailNotificationChannel
    {
        public ConcurrentBag<string> Sent { get; } = [];

        public ValueTask<EmailNotificationSendResult> SendAsync(EmailNotificationSendRequest request, CancellationToken cancellationToken = default)
        {
            Sent.Add(request.Recipient);
            return ValueTask.FromResult(answer(request.Recipient));
        }
    }

    private static EmailNotificationSendResult Accepted() => new(true, null, null, EmailSendDisposition.Accepted);

    private static NotificationDispatchProcessor Processor(AppertureDbContext db, IEmailNotificationChannel channel) =>
        new(
            new SqlNotificationDeliveryOutboxStore(db),
            new SqlNotificationInstanceStore(db),
            channel,
            Options.Create(new NotificationDeliveryOptions { Enabled = true, BatchSize = 50, MaxAttempts = 5, InitialBackoffSeconds = 60, MaxBackoffSeconds = 3600 }),
            TimeProvider.System,
            NullLogger<NotificationDispatchProcessor>.Instance);

    private static async Task<string> SeedInstanceWithRowsAsync(AuditSqlScratch scratch, string stem, int rows, DateTimeOffset createdAt)
    {
        var instanceId = NotificationInstanceId.New().Value;
        await using var db = scratch.NewDb();
        db.NotificationInstances.Add(new NotificationInstanceEntity
        {
            Id = instanceId,
            NotificationDefinitionId = NotificationDefinitionIds.PlatformScheduleExpectedRunMissing,
            NotificationKind = NotificationKinds.Activation,
            Status = NotificationInstanceStatuses.Pending,
            RenderedSubject = "Subject",
            RenderedSummary = "Summary",
            RenderedBodyText = "Body",
            TemplateVersionHash = "hash",
            RenderContextJson = "{}",
            InstanceDedupeKey = "dedupe-" + instanceId,
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = createdAt,
        });
        for (var i = 0; i < rows; i++)
        {
            db.NotificationDeliveryOutbox.Add(new NotificationDeliveryOutboxEntity
            {
                Id = NotificationDeliveryOutboxId.New().Value,
                NotificationInstanceId = instanceId,
                RecipientEmail = $"{stem}{i}@example.com",
                Channel = NotificationChannels.Email,
                Status = NotificationOutboxStatuses.Pending,
                AttemptCount = 0,
                CreatedAtUtc = createdAt.AddSeconds(i),
                UpdatedAtUtc = createdAt.AddSeconds(i),
            });
        }

        await db.SaveChangesAsync();
        return instanceId;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_dispatch_for_one_instance_never_sends_the_older_pending_rows_of_another(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Aud14Diag", rcsi);
        var stem = "scope" + Guid.NewGuid().ToString("N")[..6];
        var oldInstance = await SeedInstanceWithRowsAsync(scratch, "other-" + stem, 2, DateTimeOffset.UtcNow.AddHours(-2));
        var ownInstance = await SeedInstanceWithRowsAsync(scratch, "own-" + stem, 2, DateTimeOffset.UtcNow);
        var channel = new RecordingChannel(_ => Accepted());
        var tally = new NotificationDispatchTally();

        await using (var db = scratch.NewDb())
            await Processor(db, channel).ProcessInstanceAsync(NotificationInstanceId.From(ownInstance), tally);

        channel.Sent.Should().BeEquivalentTo([$"own-{stem}0@example.com", $"own-{stem}1@example.com"], "only the request's own rows were sent");
        tally.Accepted.Should().Be(2);
        tally.Attempted.Should().Be(2);
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM NotificationDeliveryOutbox WHERE NotificationInstanceId = N'{oldInstance}' AND Status = N'{NotificationOutboxStatuses.Pending}';"))
            .Should().Be(2, "another instance's older rows are still pending for the background dispatcher");
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM NotificationDeliveryOutbox WHERE NotificationInstanceId = N'{ownInstance}' AND Status = N'{NotificationOutboxStatuses.Succeeded}';"))
            .Should().Be(2);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_dispatchers_for_the_same_instance_send_each_row_at_most_once(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Aud14Diag", rcsi);
        var stem = "race" + Guid.NewGuid().ToString("N")[..6];
        var instance = await SeedInstanceWithRowsAsync(scratch, stem, 6, DateTimeOffset.UtcNow);
        var channel = new RecordingChannel(_ => Accepted());
        var first = new NotificationDispatchTally();
        var second = new NotificationDispatchTally();

        Task Dispatch(NotificationDispatchTally tally) => Task.Run(async () =>
        {
            await using var db = scratch.NewDb();
            await Processor(db, channel).ProcessInstanceAsync(NotificationInstanceId.From(instance), tally);
        });

        await Task.WhenAll(Dispatch(first), Dispatch(second));

        channel.Sent.Should().OnlyHaveUniqueItems("a row claimed by one dispatcher is not sent by the other");
        channel.Sent.Count.Should().Be(first.Attempted + second.Attempted, "each tally counts exactly what its dispatcher sent");
        channel.Sent.Count.Should().Be(6);
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM NotificationDeliveryOutbox WHERE NotificationInstanceId = N'{instance}' AND Status = N'{NotificationOutboxStatuses.Succeeded}';")).Should().Be(6);
    }

    [Fact]
    public async Task What_the_transport_reports_is_counted_per_send_and_a_refusal_does_not_stop_the_batch()
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Aud14Diag", rcsi: false);
        var stem = "mixed" + Guid.NewGuid().ToString("N")[..6];
        var instance = await SeedInstanceWithRowsAsync(scratch, stem, 4, DateTimeOffset.UtcNow);
        var channel = new RecordingChannel(recipient => recipient switch
        {
            var r when r.StartsWith($"{stem}1", StringComparison.Ordinal) => new EmailNotificationSendResult(false, null, TransportErrorSentinel, EmailSendDisposition.Rejected),
            var r when r.StartsWith($"{stem}2", StringComparison.Ordinal) => new EmailNotificationSendResult(false, null, TransportErrorSentinel, EmailSendDisposition.Failed),
            var r when r.StartsWith($"{stem}3", StringComparison.Ordinal) => new EmailNotificationSendResult(false, null, "The operation was canceled.", EmailSendDisposition.NoAnswer),
            _ => Accepted(),
        });
        var tally = new NotificationDispatchTally();

        await using (var db = scratch.NewDb())
            await Processor(db, channel).ProcessInstanceAsync(NotificationInstanceId.From(instance), tally);

        (tally.Accepted, tally.Rejected, tally.Failed, tally.Unknown, tally.Attempted).Should().Be((1, 1, 1, 1, 4));
        var evt = AuditEvents.NotificationPipelineTestAttempted(
            "ndef_x", null, true, 1, 4, tally.Attempted, tally.Accepted, tally.Rejected, tally.Failed, tally.Unknown, null);
        evt.OutcomeCode.Should().Be(AuditOutcomeCodes.Unknown);
    }
}
