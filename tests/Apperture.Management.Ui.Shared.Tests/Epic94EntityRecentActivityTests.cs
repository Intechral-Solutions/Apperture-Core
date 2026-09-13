using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Epic94EntityRecentActivityTests
{
    [Fact]
    public void FromRun_maps_drill_in_and_status()
    {
        var run = new RunSummaryDto(
            RunId: "run-1",
            ExecutionInstanceId: "wi",
            ExecutionDefinitionId: "def-a",
            Status: "Failed",
            CreatedAt: new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
            CompletedAt: null,
            CorrelationKey: null,
            RetryOfRunId: null,
            ExecutionKind: "workflow",
            IntegrationId: "int-1",
            ProcedureId: "proc-1");

        var item = EntityRecentActivityComposer.FromRun(run);

        item.SourceKind.Should().Be("Run");
        item.StatusOrOutcome.Should().Be("Failed");
        item.DrillInHref.Should().Be("/runs/run-1");
        item.Summary.Should().Contain("def-a");
        item.Summary.Should().Contain("proc-1");
    }

    [Fact]
    public void FromAudit_prefers_run_related_reference_for_drill_in()
    {
        var entry = new AuditTimelineEntryDto(
            Id: "a1",
            Timestamp: DateTimeOffset.UtcNow,
            Actor: "operator",
            Action: "module.lifecycle.enable",
            Category: "module",
            Outcome: "Succeeded",
            Summary: "Enabled module",
            Details: null,
            RelatedReferences: [new AuditRelatedReferenceDto("run", "run-99")]);

        var item = EntityRecentActivityComposer.FromAudit(entry, "mod-x");

        item.DrillInHref.Should().Be("/runs/run-99");
        item.DrillInLabel.Should().Be("View run");
        item.Actor.Should().Be("operator");
    }

    [Fact]
    public void FromAudit_falls_back_to_module_audit_link()
    {
        var entry = new AuditTimelineEntryDto(
            Id: "a2",
            Timestamp: DateTimeOffset.UtcNow,
            Actor: "system",
            Action: "module.lifecycle.install",
            Category: "module",
            Outcome: "Succeeded",
            Summary: "Installed",
            Details: null,
            RelatedReferences: []);

        var item = EntityRecentActivityComposer.FromAudit(entry, "mod-x");

        item.DrillInHref.Should().Contain("moduleId=mod-x");
        item.DrillInLabel.Should().Be("View audit");
    }

    [Fact]
    public void MergeAndTake_orders_newest_first_and_caps()
    {
        var items = new[]
        {
            new EntityRecentActivityItem(DateTimeOffset.UtcNow.AddHours(-2), "Run", "w", "old", null, null, null, "x"),
            new EntityRecentActivityItem(DateTimeOffset.UtcNow, "Audit", "a", "new", null, null, null, "x"),
            new EntityRecentActivityItem(DateTimeOffset.UtcNow.AddHours(-1), "Notification", "n", "mid", null, null, null, "x"),
        };

        var merged = EntityRecentActivityComposer.MergeAndTake(items, maxItems: 2);

        merged.Should().HaveCount(2);
        merged[0].Summary.Should().Be("new");
        merged[1].Summary.Should().Be("mid");
    }

    [Fact]
    public void IntegrationDetail_includes_recent_activity_section()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        var path = Path.Combine(dir!.FullName, "src", "Apperture.ControlPlane", "Apperture.Management.Ui.Client", "Pages", "Integrations", "IntegrationDetail.razor");
        var content = File.ReadAllText(path);
        content.Should().Contain("Title=\"Recent activity\"");
        content.Should().Contain("EntityRecentActivityPanel");
        content.Should().Contain("LoadRecentActivityAsync");
    }
}
