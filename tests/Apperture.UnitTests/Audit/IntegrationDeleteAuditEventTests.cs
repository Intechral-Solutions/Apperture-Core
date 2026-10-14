using System.Text.Json;
using Apperture.Core.Application.Audit;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// AUD-1.4 slice 3, work package 4a: the catalog entry, detail schema and typed producer of <c>integration.deleted</c> (design gate
/// §11, §12, §13.1; M7), and the retirement reason an integration delete adds to the integration setting's Secrets row. The
/// delete itself is work package 4b: nothing here emits the event from the delete.
/// </summary>
public sealed class IntegrationDeleteAuditEventTests
{
    private const string IntegrationId = "int-0123456789ab";

    [Fact]
    public void The_deletion_is_a_required_configuration_change_with_a_schema_and_the_integration_as_its_target()
    {
        var definition = AuditActionCatalog.Default.Get(AuditActionNames.IntegrationDeleted);

        AuditActionNames.IntegrationDeleted.Should().Be("integration.deleted");
        definition.WriteClass.Should().Be(AuditWriteClass.RequiredMutation);
        definition.Basis.Should().Be(AuditBases.Change);
        definition.Category.Should().Be(AuditCategories.Configuration);
        definition.Severity.Should().Be(AuditSeverities.Standard);
        definition.TargetType.Should().Be(AuditTargetTypes.Integration);
        definition.Details.Should().NotBeNull("a W1 action the recorder stages needs a detail schema");
        definition.Details!.Fields.Select(f => f.Name).Should().BeEquivalentTo("name", "moduleId", "settingsRemoved");
        definition.Details.Fields.Where(f => f.Required).Select(f => f.Name).Should().BeEquivalentTo("name", "settingsRemoved");
    }

    [Fact]
    public void No_field_can_carry_a_setting_value_a_secret_a_reference_or_free_text()
    {
        var fields = AuditActionCatalog.Default.Get(AuditActionNames.IntegrationDeleted).Details!.Fields;

        fields.Should().OnlyContain(f => f.Kind == AuditFieldKind.NameSnapshot || f.Kind == AuditFieldKind.Id || f.Kind == AuditFieldKind.Count);
        fields.Select(f => f.Name).Should().NotContain(n => AuditDetailSchema.IsSensitiveFieldName(n)
            || n.Contains("value", StringComparison.OrdinalIgnoreCase)
            || n.Contains("ref", StringComparison.OrdinalIgnoreCase)
            || n.Contains("key", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_deletion_names_the_integration_as_target_and_keeps_its_module_and_context_as_related_references()
    {
        var deleted = AuditEvents.IntegrationDeleted(IntegrationId, "Sample API Polling", "Sample.ApiPolling", settingsRemoved: 3, resourceContextId: "ctx-1");

        deleted.Action.Should().Be("integration.deleted");
        deleted.OutcomeCode.Should().Be(AuditOutcomeCodes.Succeeded);
        deleted.Target.Should().Be(new AuditTarget(AuditTargetTypes.Integration, IntegrationId));
        deleted.Related.ResourceContextId.Should().Be("ctx-1");
        deleted.Related.ModuleId.Should().Be("Sample.ApiPolling");
        deleted.Fields.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["name"] = "Sample API Polling",
            ["moduleId"] = "Sample.ApiPolling",
            ["settingsRemoved"] = 3,
        });
    }

    [Fact]
    public void Zero_removed_settings_is_a_value_and_an_absent_module_or_context_is_left_out()
    {
        var deleted = AuditEvents.IntegrationDeleted(IntegrationId, "Sample", moduleId: null, settingsRemoved: 0, resourceContextId: null);

        deleted.Fields["settingsRemoved"].Should().Be(0);
        deleted.Fields.Should().NotContainKey("moduleId");
        deleted.Related.ModuleId.Should().BeNull();
        deleted.Related.ResourceContextId.Should().BeNull();
    }

    [Fact]
    public void The_serialized_details_hold_only_the_declared_fields()
    {
        var deleted = AuditEvents.IntegrationDeleted(IntegrationId, "Sample", "Sample.Mod", 2, "ctx-1");

        var details = deleted.Definition.Details!.Serialize(deleted.Action, deleted.Definition.WriteClass, deleted.Fields);

        using var document = JsonDocument.Parse(details.Json);
        document.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("name", "moduleId", "settingsRemoved");
        details.Truncated.Should().BeEmpty();
        details.Omitted.Should().BeFalse();
    }

    [Fact]
    public void The_producer_refuses_what_the_schema_cannot_hold()
    {
        var negative = () => AuditEvents.IntegrationDeleted(IntegrationId, "Sample", null, settingsRemoved: -1, resourceContextId: null);
        var nameLongerThanStored = () => AuditEvents.IntegrationDeleted(IntegrationId, new string('n', 257), null, 0, null);

        negative.Should().Throw<ArgumentOutOfRangeException>();
        nameLongerThanStored.Should().Throw<ArgumentException>("an integration name is at most 256 characters in the database");
    }

    [Fact]
    public void The_schema_refuses_an_undeclared_field_so_nothing_else_can_be_added_by_a_caller()
    {
        var schema = AuditActionCatalog.Default.Get(AuditActionNames.IntegrationDeleted).Details!;
        var values = new Dictionary<string, object?> { ["name"] = "Sample", ["settingsRemoved"] = 1, ["settingValue"] = "x" };

        var act = () => schema.Validate(AuditActionNames.IntegrationDeleted, values);

        act.Should().Throw<ArgumentException>().WithMessage("*declares no field 'settingValue'*");
    }

    [Fact]
    public void The_longest_stored_values_fit_and_an_oversized_context_or_module_is_left_out_of_the_related_reference()
    {
        var longest = AuditEvents.IntegrationDeleted(
            new string('i', 64), new string('n', 256), new string('m', 128), int.MaxValue, new string('c', 64));
        var details = longest.Definition.Details!.Serialize(longest.Action, longest.Definition.WriteClass, longest.Fields);

        details.Json.Length.Should().BeLessThan(32 * 1024);
        longest.Related.ModuleId.Should().Be(new string('m', 128));
        longest.Related.ResourceContextId.Should().Be(new string('c', 64));

        var oversized = AuditEvents.IntegrationDeleted(IntegrationId, "Sample", null, 0, resourceContextId: new string('c', 65));
        oversized.Related.ResourceContextId.Should().BeNull();
    }

    [Fact]
    public void An_integration_delete_retires_a_setting_secret_under_a_reason_of_its_own_and_records_neither_secret_nor_reference()
    {
        var retired = AuditEvents.IntegrationSettingSecretRetired(
            IntegrationId, "Some.Setting.Key", AuditEvents.IntegrationDeletedSecretRetirementReason, "ctx-1");

        AuditEvents.IntegrationDeletedSecretRetirementReason.Should().Be("integration_deleted");
        retired.Action.Should().Be("integration.settings.secret.retired");
        retired.Target.Should().Be(new AuditTarget(AuditTargetTypes.IntegrationSetting, $"{IntegrationId}:Some.Setting.Key"));
        retired.Fields.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["integrationId"] = IntegrationId,
            ["key"] = "Some.Setting.Key",
            ["reason"] = "integration_deleted",
        });

        var existing = AuditActionCatalog.Default.Get(AuditActionNames.IntegrationSettingSecretRetired).Details!.Fields.Single(f => f.Name == "reason");
        // "replaced" is SEC-2.3: a setting's reference was released by re-entering its value, and the legacy secret it named was retired.
        existing.EnumValues.Should().BeEquivalentTo("cleared", "reset", "integration_deleted", "replaced");
    }

    [Fact]
    public void An_unknown_retirement_reason_is_still_refused()
    {
        var act = () => AuditEvents.IntegrationSettingSecretRetired(IntegrationId, "Some.Setting.Key", "because");

        act.Should().Throw<ArgumentException>();
    }
}
