using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>Epic S7 — procedure/run launch operator UX helpers and page conventions.</summary>
public sealed class S7ProcedureLaunchOperatorUxTests
{
    [Fact]
    public void DescribeBlockedReason_uses_operator_language()
    {
        ProcedureLaunchOperatorUiHelpers.DescribeBlockedReason("integration_inactive")
            .Should().Contain("Activate");
        ProcedureLaunchOperatorUiHelpers.DescribeBlockedReason("insufficient_procedure_permission")
            .Should().Contain("permission");
    }

    [Fact]
    public void FormatLaunchError_maps_validation_failures()
    {
        ProcedureLaunchOperatorUiHelpers.FormatLaunchError("invalid_parameters", null)
            .Should().Contain("required parameters");
        ProcedureLaunchOperatorUiHelpers.FormatLaunchError("dispatch_failed", null)
            .Should().Contain("dispatch");
    }

    [Fact]
    public void ValidateProcedureParameters_enforces_required_fields()
    {
        var fields = new[]
        {
            new IntegrationProcedureParameterFieldDto("name", "string", true, null, null, null, null, null, "Name", null),
        };
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);

        ProcedureLaunchOperatorUiHelpers.ValidateProcedureParameters(fields, values)
            .Should().ContainSingle(e => e.Contains("Name"));
    }

    [Fact]
    public void BuildLaunchDisabledReason_surfaces_validation_first()
    {
        ProcedureLaunchOperatorUiHelpers.BuildLaunchDisabledReason(
                loading: false,
                launchable: true,
                integrationReady: true,
                blockedReasonCodes: [],
                validationErrors: ["Name is required."])
            .Should().Be("Name is required.");
    }

    [Fact]
    public void BuildExecuteHref_supports_procedure_deep_link()
    {
        ProcedureLaunchOperatorUiHelpers.BuildExecuteHref("int-1", "procedure", "proc-1")
            .Should().Be("/execute?integrationId=int-1&kind=procedure&procedureId=proc-1");
    }

    [Fact]
    public void GetRunDetailHref_points_to_run_detail()
    {
        ProcedureLaunchOperatorUiHelpers.GetRunDetailHref("run-42")
            .Should().Be("/runs/run-42");
    }

    [Fact]
    public void ExecutionStart_uses_operator_launch_framing()
    {
        var path = RepoPath("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Execution/ExecutionStart.razor");
        var content = File.ReadAllText(path);
        content.Should().Contain("PageHeader");
        content.Should().Contain("ProcedureLaunchOperatorUiHelpers.FormatLaunchError");
        content.Should().Contain("ManagementAdvancedSection Title=\"Advanced launch options\"");
        content.Should().Contain("procedureId");
    }

    [Fact]
    public void IntegrationDetail_procedure_section_links_to_execute()
    {
        var path = RepoPath("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationDetail.razor");
        var content = File.ReadAllText(path);
        content.Should().Contain("ProcedureLaunchOperatorUiHelpers.BuildExecuteHref");
        content.Should().Contain("RefreshProcedureValidation");
        content.Should().NotContain("DescribeBlockedReason(string code)");
    }

    [Fact]
    public void IntegrationSummaryBand_execute_includes_procedures()
    {
        var path = RepoPath("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Integrations/IntegrationSummaryBand.razor");
        var content = File.ReadAllText(path);
        content.Should().Contain("procedures");
    }

    private static string RepoPath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return Path.Combine(dir!.FullName, relative);
    }
}
