using System.Reflection;
using System.Text;
using System.Text.Json;
using Apperture.Core.Application;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.OperationItems;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// Structured details (audit hardening design gate §12; A1.1-6, A1.1-9): only declared fields of declared kinds, sensitive
/// names refused, injection-proof JSON, and the overflow policy.
/// </summary>
public sealed class AuditDetailSchemaTests
{
    private static readonly AuditDetailSchema Schema = new(
    [
        new AuditFieldDefinition("roleId", AuditFieldKind.Id, Required: true),
        new AuditFieldDefinition("roleName", AuditFieldKind.NameSnapshot, Required: false, MaxLength: 20000, TruncatedLength: 64),
        new AuditFieldDefinition("targetName", AuditFieldKind.NameSnapshot, Required: false, MaxLength: 20000, TruncatedLength: 32),
        new AuditFieldDefinition("username", AuditFieldKind.Username, Required: false),
        new AuditFieldDefinition("reason", AuditFieldKind.OperatorText, Required: false, MaxLength: 30000),
        new AuditFieldDefinition("added", AuditFieldKind.IdList, Required: false, MaxItems: 1000),
    ]);

    [Fact]
    public void An_undeclared_field_a_missing_required_field_and_a_wrong_kind_are_refused()
    {
        var undeclared = () => Schema.Validate("x.y", Values(("roleId", "r1"), ("runId", "1")));
        var missing = () => Schema.Validate("x.y", Values(("roleName", "Admins")));
        var wrongKind = () => Schema.Validate("x.y", Values(("roleId", 42L)));

        undeclared.Should().Throw<ArgumentException>().WithMessage("*declares no field 'runId'*");
        missing.Should().Throw<ArgumentException>().WithMessage("*requires field 'roleId'*");
        wrongKind.Should().Throw<ArgumentException>().WithMessage("*must be text*");
    }

    [Theory]
    [InlineData("password")]
    [InlineData("newPasswordHash")]
    [InlineData("accessToken")]
    [InlineData("clientSecret")]
    [InlineData("secretReference")]
    [InlineData("connectionString")]
    [InlineData("searchText")]
    [InlineData("query")]
    [InlineData("filterValue")]
    [InlineData("recoveryCode")]
    [InlineData("totpSeed")]
    [InlineData("parameterValues")]
    public void A_schema_declaring_a_sensitive_field_is_refused(string name)
    {
        var act = () => new AuditDetailSchema([new AuditFieldDefinition(name, AuditFieldKind.Id, Required: false)]);

        act.Should().Throw<ArgumentException>().WithMessage("*sensitive*");
    }

    [Fact]
    public void No_field_kind_carries_a_credential_secret_or_search_value()
    {
        Enum.GetNames<AuditFieldKind>().Should().BeEquivalentTo(
            ["Id", "Enum", "Count", "Flag", "Timestamp", "Username", "NameSnapshot", "OperatorText", "IdList"]);
        var producers = typeof(AuditEvents).GetMethods(BindingFlags.Public | BindingFlags.Static);
        producers.SelectMany(m => m.GetParameters()).Select(p => p.ParameterType)
            .Should().OnlyContain(t => t == typeof(string) || t == typeof(UserId) || t == typeof(UsernameChangeOutcome),
                "producers take typed values, never an arbitrary field name or a credential type");
    }

    [Theory]
    [InlineData("x; runId=1")]
    [InlineData("Admins runId=run-1; schedule:s-1 module=m1")]
    public void Injection_text_in_a_name_stays_one_json_value_and_never_becomes_a_field(string roleName)
    {
        var details = Schema.Serialize("x.y", AuditWriteClass.RequiredMutation, Values(("roleId", "r1"), ("roleName", roleName)));

        using var json = JsonDocument.Parse(details.Json);
        json.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["roleId", "roleName"]);
        json.RootElement.GetProperty("roleName").GetString().Should().Be(roleName);
    }

    [Fact]
    public void A_username_containing_an_equals_sign_and_a_space_is_recorded_verbatim()
    {
        var events = AuditEvents.UsernameChanged(new UserId("u1"), "legacy user=admin", "legacy.user", UsernameChangeOutcome.Assigned);
        var definition = events.Definition;

        var details = definition.Details!.Serialize(definition.Action, definition.WriteClass, events.Fields);

        using var json = JsonDocument.Parse(details.Json);
        json.RootElement.GetProperty("previousUsername").GetString().Should().Be("legacy user=admin");
        json.RootElement.GetProperty("newUsername").GetString().Should().Be("legacy.user");
        json.RootElement.EnumerateObject().Should().HaveCount(4);
    }

    [Fact]
    public void Over_the_limit_name_snapshots_are_truncated_longest_first_to_their_budgets_and_listed()
    {
        var details = Schema.Serialize("x.y", AuditWriteClass.RequiredMutation, Values(
            ("roleId", "r1"),
            ("roleName", new string('r', 20000)),
            ("targetName", new string('t', 15000)),
            ("reason", new string('o', 1000))));

        Encoding.UTF8.GetByteCount(details.Json).Should().BeLessThanOrEqualTo(AuditDetailSchema.MaxDetailsBytes);
        details.Truncated.Should().Equal("roleName");
        using var json = JsonDocument.Parse(details.Json);
        json.RootElement.GetProperty("roleName").GetString().Should().HaveLength(64);
        json.RootElement.GetProperty("targetName").GetString().Should().HaveLength(15000, "it fit once the longest snapshot was cut");
        json.RootElement.GetProperty("reason").GetString().Should().HaveLength(1000, "operator text is never truncated");
        json.RootElement.GetProperty("_truncated").EnumerateArray().Select(e => e.GetString()).Should().Equal("roleName");
    }

    [Fact]
    public void Still_over_the_limit_a_W1_or_W2_write_throws_and_a_W3_or_W4_write_is_omitted()
    {
        var values = Values(("roleId", "r1"), ("reason", new string('o', 30000)), ("username", new string('u', 256)), ("added", Ids(200, 150)));

        var w1 = () => Schema.Serialize("x.y", AuditWriteClass.RequiredMutation, values);
        var w2 = () => Schema.Serialize("x.y", AuditWriteClass.RequiredSecurityEvent, values);
        w1.Should().Throw<AuditDetailsOverflowException>();
        w2.Should().Throw<AuditDetailsOverflowException>();

        var w3 = Schema.Serialize("x.y", AuditWriteClass.Attempt, values);
        var w4 = Schema.Serialize("x.y", AuditWriteClass.Telemetry, values);
        w3.Json.Should().Be("{\"_omitted\":\"size\"}");
        w3.Omitted.Should().BeTrue();
        w4.Json.Should().Be(w3.Json);
    }

    [Fact]
    public void Legitimate_maximums_fit_a_diff_of_every_permission_maximum_usernames_and_a_maximum_reason()
    {
        var permissions = typeof(ActionConstants).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();
        permissions.Should().NotBeEmpty();
        var schema = new AuditDetailSchema(
        [
            new AuditFieldDefinition("roleId", AuditFieldKind.Id, Required: true),
            new AuditFieldDefinition("added", AuditFieldKind.IdList, Required: false, MaxItems: 500),
            new AuditFieldDefinition("removed", AuditFieldKind.IdList, Required: false, MaxItems: 500),
            new AuditFieldDefinition("previousUsername", AuditFieldKind.Username, Required: true),
            new AuditFieldDefinition("newUsername", AuditFieldKind.Username, Required: true),
            // The bulk retry reason bound (SEC/BulkOperationItemRetryService.cs:146) is the operation's own input limit.
            new AuditFieldDefinition("reason", AuditFieldKind.OperatorText, Required: false, MaxLength: OperationItemRetryBounds.ReasonMaxLength),
        ]);

        var details = schema.Serialize("x.y", AuditWriteClass.RequiredMutation, Values(
            ("roleId", new string('r', AuditDetailSchema.MaxIdLength)),
            ("added", permissions),
            ("removed", permissions),
            ("previousUsername", new string('a', AuditDetailSchema.MaxUsernameLength)),
            ("newUsername", new string('b', AuditDetailSchema.MaxUsernameLength)),
            ("reason", new string('z', OperationItemRetryBounds.ReasonMaxLength))));

        details.Truncated.Should().BeEmpty();
        details.Omitted.Should().BeFalse();
        Encoding.UTF8.GetByteCount(details.Json).Should().BeLessThan(AuditDetailSchema.MaxDetailsBytes);
    }

    [Fact]
    public void The_username_producer_takes_the_transaction_result_and_refuses_an_unchanged_rename()
    {
        var reclaimed = AuditEvents.UsernameChanged(new UserId("u1"), "alice", "alice.old", UsernameChangeOutcome.Reclaimed);
        reclaimed.Fields["change"].Should().Be("reclaimed");
        reclaimed.Target.Should().Be(new AuditTarget(AuditTargetTypes.User, "u1"));
        reclaimed.OutcomeCode.Should().Be(AuditOutcomeCodes.Succeeded);

        var unchanged = () => AuditEvents.UsernameChanged(new UserId("u1"), "alice", "alice", UsernameChangeOutcome.Unchanged);
        unchanged.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void An_event_cannot_be_built_for_an_action_still_on_the_append_bridge()
    {
        var act = () => AuditEvent.Create(
            AuditActionCatalog.Default.Get("governance.role.created"), null, AuditOutcomeCodes.Succeeded, Values());

        act.Should().Throw<InvalidOperationException>().WithMessage("*append bridge*");
    }

    private static Dictionary<string, object?> Values(params (string Name, object? Value)[] values) =>
        values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);

    private static IReadOnlyList<string> Ids(int count, int length) =>
        Enumerable.Range(0, count).Select(i => i.ToString().PadLeft(length, 'p')).ToList();
}
