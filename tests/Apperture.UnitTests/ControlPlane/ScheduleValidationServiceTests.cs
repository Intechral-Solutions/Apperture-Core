using Apperture.ControlPlane.Security;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Epic 38: Schedule validation service tests.
/// </summary>
public sealed class ScheduleValidationServiceTests
{
    private readonly ScheduleValidationService _sut = new();

    [Fact]
    public void ValidateCreate_ValidWorkflow_ReturnsOk()
    {
        var result = _sut.ValidateCreate(
            "Hourly",
            "0 * * * *",
            "UTC",
            true,
            0,
            "{\"workflowDefinitionId\":\"Mod.Ns.Workflow\"}");

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateCreate_ValidOperation_ReturnsOk()
    {
        var result = _sut.ValidateCreate(
            "Daily",
            "0 0 * * *",
            "America/New_York",
            true,
            1,
            "{\"operationDefinitionId\":\"Module.Op.Daily\"}");

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateCreate_EmptyName_ReturnsError()
    {
        var result = _sut.ValidateCreate("", "0 * * * *", "UTC", true, 0, "{\"workflowDefinitionId\":\"wf1\"}");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Name"));
    }

    [Fact]
    public void ValidateCreate_InvalidCron_ReturnsError()
    {
        var result = _sut.ValidateCreate("X", "not cron", "UTC", true, 0, "{\"workflowDefinitionId\":\"wf1\"}");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("cron", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateCreate_InvalidTimeZone_ReturnsError()
    {
        var result = _sut.ValidateCreate("X", "0 * * * *", "Invalid/TZ", true, 0, "{\"workflowDefinitionId\":\"wf1\"}");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("time zone", StringComparison.OrdinalIgnoreCase) || e.Contains("Unknown time zone", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateCreate_InvalidTargetJobType_ReturnsError()
    {
        var result = _sut.ValidateCreate("X", "0 * * * *", "UTC", true, 99, "{}");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TargetJobType"));
    }

    [Fact]
    public void ValidateCreate_WorkflowWithoutWorkflowDefinitionId_ReturnsError()
    {
        var result = _sut.ValidateCreate("X", "0 * * * *", "UTC", true, 0, "{\"other\":\"value\"}");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("workflowDefinitionId"));
    }

    [Fact]
    public void ValidateUpdate_Valid_ReturnsOk()
    {
        var result = _sut.ValidateUpdate("NewName", "0 0 * * *", "UTC", true);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateUpdate_EmptyName_ReturnsError()
    {
        var result = _sut.ValidateUpdate("", null, null, null);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Name"));
    }

    [Fact]
    public void ValidateUpdate_InvalidCron_ReturnsError()
    {
        var result = _sut.ValidateUpdate(null, "bad cron", null, null);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("cron", StringComparison.OrdinalIgnoreCase));
    }
}
