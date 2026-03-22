using Apperture.ControlPlane.Security.Audit;
using Apperture.Core.Application.Services;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class AuditDetailParserTests
{
    [Fact]
    public void ParseOutcome_EmptyDetails_ReturnsEmpty()
    {
        AuditDetailParser.ParseOutcome(null).Should().Be("");
        AuditDetailParser.ParseOutcome("").Should().Be("");
    }

    [Fact]
    public void ParseOutcome_SingleOutcomeSegment_ReturnsIt()
    {
        AuditDetailParser.ParseOutcome("blocked").Should().Be("blocked");
        AuditDetailParser.ParseOutcome("success").Should().Be("success");
        AuditDetailParser.ParseOutcome("requested").Should().Be("requested");
    }

    [Fact]
    public void ParseOutcome_OutcomeThenDetails_ReturnsOutcome()
    {
        AuditDetailParser.ParseOutcome("blocked; schedule:123; Workflow not executable")
            .Should().Be("blocked");
    }

    [Fact]
    public void ParseOutcome_KeyValueFirstSegment_ReturnsEmpty()
    {
        AuditDetailParser.ParseOutcome("runId=abc;definitionId=def").Should().Be("");
    }

    [Theory]
    [InlineData("runId=abc", "run", "abc")]
    [InlineData("runId=abc;definitionId=def", "run", "abc")]
    [InlineData("schedule:sched-1;RunId=run-2", "schedule", "sched-1")]
    [InlineData("definitionId=wf-1;reason=blocked", "workflow_definition", "wf-1")]
    [InlineData("workflowId=wf-x", "workflow_definition", "wf-x")]
    [InlineData("operationDefinitionId=op-1", "operation_definition", "op-1")]
    [InlineData("module=Sample.Module;state=Enabled", "module", "Sample.Module")]
    [InlineData("runtimePlaneId=local", "runtime_plane", "local")]
    [InlineData("originalRunId=old;newRunId=new;definitionId=wf", "run", "old")]
    public void ParseRelatedReferences_ExtractsExpectedRefs(string details, string kind, string id)
    {
        var refs = AuditDetailParser.ParseRelatedReferences(details);
        refs.Should().Contain(r => r.Kind == kind && r.Id == id);
    }

    [Fact]
    public void ParseRelatedReferences_EmptyDetails_ReturnsEmpty()
    {
        AuditDetailParser.ParseRelatedReferences(null).Should().BeEmpty();
        AuditDetailParser.ParseRelatedReferences("").Should().BeEmpty();
    }

    [Fact]
    public void BuildSummary_WithOutcome_Combines()
    {
        AuditDetailParser.BuildSummary("scheduler.schedule.dispatched", "success")
            .Should().Be("scheduler.schedule.dispatched: success");
    }

    [Fact]
    public void BuildSummary_WithoutOutcome_ReturnsAction()
    {
        AuditDetailParser.BuildSummary("scheduler.schedule.dispatched", "")
            .Should().Be("scheduler.schedule.dispatched");
    }
}
