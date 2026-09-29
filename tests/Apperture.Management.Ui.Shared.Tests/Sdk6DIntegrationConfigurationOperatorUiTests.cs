using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class Sdk6DIntegrationConfigurationOperatorUiTests
{
    [Fact]
    public void FormatCapabilityLabel_prefers_kind_over_contract_type()
    {
        IntegrationConfigurationOperatorUiHelpers.FormatCapabilityLabel("http-api", "Some.Type")
            .Should().Be("HTTP API");
        IntegrationConfigurationOperatorUiHelpers.FormatCapabilityLabel("relational-db", null)
            .Should().Be("Relational database");
        IntegrationConfigurationOperatorUiHelpers.FormatCapabilityLabel(null, typeof(System.Data.IDbConnection).FullName!)
            .Should().Be("Relational database");
        IntegrationConfigurationOperatorUiHelpers.FormatCapabilityLabel(null, "Apperture.Connectors.Sftp.ISftpFileClient")
            .Should().Be("SFTP");
    }

    [Fact]
    public void MapReadinessIssue_groups_unisa_blockers_independently()
    {
        var issues = new[]
        {
            new IntegrationReadinessIssueDto("missing_slot_binding", "CnsApi unbound", "CnsApi", null, null, "Blocking"),
            new IntegrationReadinessIssueDto("missing_slot_binding", "Sftp unbound", "Sftp", null, null, "Blocking"),
            new IntegrationReadinessIssueDto("missing_slot_binding", "IntegrationDb unbound", "IntegrationDb", null, null, "Blocking"),
        };

        var grouped = IntegrationConfigurationOperatorUiHelpers.GroupIssuesByCategory(issues, "int-1");
        grouped[IntegrationConfigurationOperatorUiHelpers.CategoryBindings].Should().HaveCount(3);
        grouped[IntegrationConfigurationOperatorUiHelpers.CategoryBindings]
            .Select(i => i.Title)
            .Should().OnlyContain(t => t.Contains("Connection not bound", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildRemediationHref_routes_settings_and_connections()
    {
        IntegrationConfigurationOperatorUiHelpers.BuildRemediationHref(
                "int-1",
                "missing_setting",
                null,
                "Module.ApiKey")
            .Should().Be("/integrations/int-1/settings#Module.ApiKey");

        IntegrationConfigurationOperatorUiHelpers.BuildRemediationHref(
                "int-1",
                "missing_slot_binding",
                "CnsApi",
                null)
            .Should().Be("/integrations/int-1#requirement-CnsApi");
    }

    [Fact]
    public void BuildRemediationHref_routes_binding_cross_context_to_connection_requirement_anchor()
    {
        IntegrationConfigurationOperatorUiHelpers.BuildRemediationHref(
                "int-1",
                "binding_cross_context",
                "CnsApi",
                null)
            .Should().Be("/integrations/int-1#requirement-CnsApi");
    }

    [Fact]
    public void MapIssueCategory_puts_binding_cross_context_in_bindings_not_module()
    {
        var issue = new IntegrationReadinessIssueDto(
            "binding_cross_context",
            "Binding for slot 'CnsApi' references connection 'conn-1' in ResourceContext 'ctx-b', which this integration (in 'ctx-a') cannot reach. Remove this binding, or move the integration or the connection so they share a context.",
            "CnsApi",
            "binding-1",
            "conn-1",
            "Blocking");

        var view = IntegrationConfigurationOperatorUiHelpers.MapReadinessIssue(issue, "int-1");

        view.Category.Should().Be(IntegrationConfigurationOperatorUiHelpers.CategoryBindings);
        view.Category.Should().NotBe(IntegrationConfigurationOperatorUiHelpers.CategoryModule);
    }

    [Fact]
    public void MapIssueCopy_for_binding_cross_context_has_dedicated_title_and_hint()
    {
        var message = "Binding for slot 'CnsApi' references connection 'conn-1' in ResourceContext 'ctx-b', which this integration (in 'ctx-a') cannot reach. Remove this binding, or move the integration or the connection so they share a context.";
        var issue = new IntegrationReadinessIssueDto(
            "binding_cross_context",
            message,
            "CnsApi",
            "binding-1",
            "conn-1",
            "Blocking");

        var view = IntegrationConfigurationOperatorUiHelpers.MapReadinessIssue(issue, "int-1");

        view.Title.Should().NotBe("Binding cross context");
        view.Title.Should().Be("Binding crosses contexts");
        view.Explanation.Should().Be(message);
        view.RemediationHint.Should().Contain("Move").And.Contain("remove", "hint should point at moving or removing the binding");
    }

    [Fact]
    public void MapIssueCopy_for_a_retired_connection_does_not_offer_activation()
    {
        var issue = new IntegrationReadinessIssueDto(
            "connection_inactive",
            "Connection 'Warehouse' (conn-1) for slot 'Sql' is retired and can no longer be used.",
            "Sql",
            "binding-1",
            "conn-1",
            "Blocking");

        var view = IntegrationConfigurationOperatorUiHelpers.MapReadinessIssue(issue, "int-1");

        view.Title.Should().Be("Connection retired");
        view.Explanation.Should().Contain("retired").And.Contain("can no longer be used");
        view.RemediationHint.Should().Contain("Bind a different connection");
        view.RemediationHint.Should().Contain("remove this binding");
        view.RemediationHint.Should().Contain("cannot be brought back");
        view.RemediationHint.Should().NotContainEquivalentOf("Activate");
        view.RemediationHint.Should().NotContainEquivalentOf("Enable");
        view.RemediationHint.Should().NotContainEquivalentOf("keep working");
    }

    [Fact]
    public void GroupIssuesByCategory_suppresses_binding_not_eligible_when_cross_context_shares_binding_id()
    {
        var issues = new[]
        {
            new IntegrationReadinessIssueDto(
                "binding_cross_context",
                "Binding for slot 'CnsApi' references connection 'conn-1' in ResourceContext 'ctx-b', which this integration (in 'ctx-a') cannot reach.",
                "CnsApi",
                "binding-1",
                "conn-1",
                "Blocking"),
            new IntegrationReadinessIssueDto(
                "binding_not_eligible",
                "Connection is not eligible for this slot.",
                "CnsApi",
                "binding-1",
                "conn-1",
                "Blocking"),
        };

        var grouped = IntegrationConfigurationOperatorUiHelpers.GroupIssuesByCategory(issues, "int-1");
        var bindingViews = grouped[IntegrationConfigurationOperatorUiHelpers.CategoryBindings];

        bindingViews.Should().ContainSingle();
        bindingViews.Should().Contain(v => v.Code == "binding_cross_context");
        bindingViews.Should().NotContain(v => v.Code == "binding_not_eligible");
    }

    [Fact]
    public void GroupIssuesByCategory_does_not_suppress_binding_not_eligible_for_unrelated_binding()
    {
        var issues = new[]
        {
            new IntegrationReadinessIssueDto(
                "binding_cross_context",
                "Binding for slot 'CnsApi' references connection 'conn-1' in ResourceContext 'ctx-b', which this integration (in 'ctx-a') cannot reach.",
                "CnsApi",
                "binding-1",
                "conn-1",
                "Blocking"),
            new IntegrationReadinessIssueDto(
                "binding_not_eligible",
                "Connection is not eligible for this slot.",
                "Sftp",
                "binding-2",
                "conn-2",
                "Blocking"),
        };

        var grouped = IntegrationConfigurationOperatorUiHelpers.GroupIssuesByCategory(issues, "int-1");
        var bindingViews = grouped[IntegrationConfigurationOperatorUiHelpers.CategoryBindings];

        bindingViews.Should().HaveCount(2);
        bindingViews.Should().Contain(v => v.Code == "binding_cross_context");
        bindingViews.Should().Contain(v => v.Code == "binding_not_eligible");
    }

    [Fact]
    public void GroupIssuesByCategory_does_not_suppress_binding_not_eligible_when_no_cross_context_issue_present()
    {
        var issues = new[]
        {
            new IntegrationReadinessIssueDto(
                "binding_not_eligible",
                "Connection is not eligible for this slot.",
                "Sftp",
                "binding-2",
                "conn-2",
                "Blocking"),
        };

        var grouped = IntegrationConfigurationOperatorUiHelpers.GroupIssuesByCategory(issues, "int-1");
        var bindingViews = grouped[IntegrationConfigurationOperatorUiHelpers.CategoryBindings];

        bindingViews.Should().ContainSingle();
        bindingViews.Should().Contain(v => v.Code == "binding_not_eligible");
    }

    [Fact]
    public void BuildScheduleExecutionPill_distinguishes_disabled_and_blocked()
    {
        IntegrationConfigurationOperatorUiHelpers.BuildScheduleExecutionPill(false, false, "int-1").Label
            .Should().Be("Disabled");

        IntegrationConfigurationOperatorUiHelpers.BuildScheduleExecutionPill(true, false, "int-1").Label
            .Should().Be("Blocked by configuration");

        IntegrationConfigurationOperatorUiHelpers.BuildScheduleExecutionPill(true, true, "int-1").Label
            .Should().Be("Ready");
    }

    [Fact]
    public void FormatSecretPresenceLabel_covers_operator_states()
    {
        IntegrationConfigurationOperatorUiHelpers.FormatSecretPresenceLabel(true, false)
            .Should().Be("Not configured");
        IntegrationConfigurationOperatorUiHelpers.FormatSecretPresenceLabel(true, true)
            .Should().Be("Configured");
        IntegrationConfigurationOperatorUiHelpers.FormatSecretPresenceLabel(true, false, invalidReference: true)
            .Should().Be("Invalid reference");
        IntegrationConfigurationOperatorUiHelpers.FormatSecretPresenceLabel(true, false, unavailable: true)
            .Should().Be("Unavailable");
    }

    [Fact]
    public void FormatVerificationStatusLabel_maps_operator_labels()
    {
        IntegrationConfigurationOperatorUiHelpers.FormatVerificationStatusLabel("Failed")
            .Should().Be("Verification failed");
        IntegrationConfigurationOperatorUiHelpers.FormatVerificationStatusLabel("NeverTested")
            .Should().Be("Not verified");
    }

    [Fact]
    public void BuildSettingsCompletionSummary_counts_required_configured()
    {
        var declarations = new List<IntegrationSettingDeclarationDto>
        {
            new("a", "A", "", "String", true, false, 0, null),
            new("b", "B", "", "String", true, true, 1, null),
        };
        var rows = new List<PlatformSettingResolvedDto>
        {
            new("a", "A", "", "General", "String", "Text", null, null, "value", "Database", true, false, null, null, null, null, IsSecretConfigured: false),
            new("b", "B", "", "General", "String", "Secret", null, null, "", "Database", true, false, null, null, null, null, IsSensitive: true, IsSecretConfigured: true),
        };

        IntegrationConfigurationOperatorUiHelpers.BuildSettingsCompletionSummary(declarations, rows)
            .Should().Be("2 / 2 required configured");
    }

    [Fact]
    public void ProcedureLaunchOperatorUiHelpers_describes_degraded_vs_blocked()
    {
        ProcedureLaunchOperatorUiHelpers.DescribeLaunchAvailability(
                integrationReady: true,
                launchable: true,
                blockedReasonCodes: [],
                advisoryIssueCount: 2)
            .Should().Contain("advisory");

        ProcedureLaunchOperatorUiHelpers.IsHardBlockedReason("integration_hard_blocked")
            .Should().BeTrue();
        ProcedureLaunchOperatorUiHelpers.IsHardBlockedReason("optional_dependency_unavailable")
            .Should().BeFalse();
    }

    [Fact]
    public void FormatBlockedByReadinessSummary_surfaces_safe_dispatch_evidence()
    {
        IntegrationConfigurationOperatorUiHelpers.FormatBlockedByReadinessSummary(
                "BlockedByReadiness",
                "Integration not runnable: missing_slot_binding")
            .Should().Contain("missing_slot_binding");

        IntegrationConfigurationOperatorUiHelpers.FormatBlockedByReadinessSummary("Completed", "ok")
            .Should().BeNull();
    }

    private const string SampleSecretPlaintext = "super-secret-api-key-value-6d";

    [Fact]
    public void Readiness_helper_surfaces_never_include_secret_plaintext()
    {
        var issues = new[]
        {
            new IntegrationReadinessIssueDto(
                "missing_secret",
                "Required secret for setting 'ApiKey' is not configured.",
                null,
                null,
                null,
                "Blocking"),
            new IntegrationReadinessIssueDto(
                "invalid_secret_reference",
                "Secret reference invalid for 'ClientSecret'.",
                null,
                null,
                null,
                "Blocking"),
            new IntegrationReadinessIssueDto(
                "missing_setting",
                "Setting 'EndpointUrl' is required.",
                null,
                null,
                null,
                "Blocking"),
        };

        var grouped = IntegrationConfigurationOperatorUiHelpers.GroupIssuesByCategory(issues, "int-1");
        var allSurfaces = grouped.Values
            .SelectMany(v => v)
            .SelectMany(v => new[] { v.Title, v.Explanation, v.RemediationHint ?? "", v.RemediationHref ?? "", v.SeverityLabel })
            .Concat([
                IntegrationConfigurationOperatorUiHelpers.BuildOverallReadinessPill(
                    new IntegrationReadinessDto("int-1", "mod", false, 3, 0, issues)).Label,
                IntegrationConfigurationOperatorUiHelpers.FormatBlockedByReadinessSummary(
                    "BlockedByReadiness",
                    "Integration not runnable: missing_secret"),
                ProcedureLaunchOperatorUiHelpers.DescribeLaunchAvailability(
                    integrationReady: false,
                    launchable: false,
                    blockedReasonCodes: ["integration_hard_blocked"],
                    advisoryIssueCount: 0),
            ])
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        var rowsWithSecretValue = new List<PlatformSettingResolvedDto>
        {
            new(
                "ApiKey",
                "API key",
                "",
                "General",
                "String",
                "Secret",
                null,
                null,
                SampleSecretPlaintext,
                "Database",
                true,
                false,
                null,
                null,
                null,
                null,
                IsSensitive: true,
                IsSecretConfigured: true),
        };
        allSurfaces.Add(
            IntegrationConfigurationOperatorUiHelpers.BuildSettingsCompletionSummary(
                [new IntegrationSettingDeclarationDto("ApiKey", "API key", "", "SensitiveText", true, true, 0, null)],
                rowsWithSecretValue));

        foreach (var surface in allSurfaces)
            surface.Should().NotContain(SampleSecretPlaintext);
    }

    [Fact]
    public void Secret_presence_labels_never_echo_plaintext_values()
    {
        var labels = new[]
        {
            IntegrationConfigurationOperatorUiHelpers.FormatSecretPresenceLabel(true, true),
            IntegrationConfigurationOperatorUiHelpers.FormatSecretPresenceLabel(true, false),
            IntegrationConfigurationOperatorUiHelpers.FormatSecretPresenceLabel(true, false, invalidReference: true),
            IntegrationConfigurationOperatorUiHelpers.FormatSecretPresenceLabel(true, false, unavailable: true),
            IntegrationConfigurationOperatorUiHelpers.FormatSecretPresenceLabel(false, true),
        };

        var allowedLabels = new HashSet<string>(StringComparer.Ordinal)
        {
            "Configured",
            "Not configured",
            "Invalid reference",
            "Unavailable",
        };

        labels.Should().OnlyContain(l => allowedLabels.Contains(l));
        labels.Should().NotContain(SampleSecretPlaintext);
    }
}
