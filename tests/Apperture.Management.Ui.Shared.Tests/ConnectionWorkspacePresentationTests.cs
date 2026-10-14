using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// Presentation rules for the Connection workspace. The rows and health are a pure function of the Connection read
/// contract plus the connector-declared fields, so "no secret value, no secret reference" is provable here rather than
/// merely hoped for in markup.
/// </summary>
public sealed class ConnectionWorkspacePresentationTests
{
    private const string SecretGuid = "3f2a9c1e-77aa-4c1d-9b1e-0a4d0d5b7c11";

    private static ConnectorDefinitionCatalogDto Definition() =>
        new(
            "Apperture.Platform.Sql",
            "Platform SQL",
            null,
            "SQL Server connection",
            [
                new ConnectionFieldCatalogDto("Password", "Password", true, "Stored securely.", true),
                new ConnectionFieldCatalogDto("Host", "Server", false, "Host name or address.", true),
                new ConnectionFieldCatalogDto("Database", "Database", false, null),
            ],
            "SupportsConnectionVerification");

    private static ConnectionDto Connection(
        IReadOnlyDictionary<string, string>? config = null,
        IReadOnlyList<ConnectionSecretFieldDto>? secretFields = null,
        string status = "NeverTested",
        bool supportsVerification = true,
        string? connectorDisplayName = "Platform SQL") =>
        new(
            "c-1", "Apperture.Platform.Sql", "Warehouse",
            config ?? new Dictionary<string, string> { ["Host"] = "db.example", ["Database"] = "Ops" },
            secretFields ?? [new ConnectionSecretFieldDto("Password", true)],
            null, null, "Active", supportsVerification, status, null, null, [],
            ConnectorDisplayName: connectorDisplayName);

    [Fact]
    public void Rows_follow_the_connector_declared_order_and_use_declared_labels_and_descriptions()
    {
        var rows = ConnectionWorkspacePresentation.ConfigurationRows(Connection(), Definition());

        rows.Select(r => r.Label).Should().Equal("Password", "Server", "Database");
        rows.Single(r => r.Key == "Host").Description.Should().Be("Host name or address.");
        rows.Single(r => r.Key == "Host").Value.Should().Be("db.example");
    }

    [Fact]
    public void A_secret_field_is_only_present_or_absent_and_never_carries_a_value_or_reference()
    {
        var present = ConnectionWorkspacePresentation.ConfigurationRows(Connection(), Definition()).Single(r => r.IsSecret);
        var absent = ConnectionWorkspacePresentation.ConfigurationRows(
            Connection(secretFields: []), Definition()).Single(r => r.IsSecret);

        present.IsSet.Should().BeTrue();
        absent.IsSet.Should().BeFalse();
        present.Value.Should().BeNull("a secret row never carries a value");
        absent.Value.Should().BeNull();
        System.Text.Json.JsonSerializer.Serialize(present).Should().NotContain(SecretGuid);
        ConnectionWorkspacePresentation.SecretPresence(true).Should().Be("Set");
        ConnectionWorkspacePresentation.SecretPresence(false).Should().Be("Not set");
    }

    [Fact]
    public void A_secret_field_the_server_reports_as_not_configured_is_absent()
    {
        var rows = ConnectionWorkspacePresentation.ConfigurationRows(
            Connection(secretFields: [new ConnectionSecretFieldDto("Password", false)]), Definition());

        rows.Single(r => r.IsSecret).IsSet.Should().BeFalse();
    }

    [Fact]
    public void A_config_value_stored_under_a_declared_secret_key_is_never_shown()
    {
        var rows = ConnectionWorkspacePresentation.ConfigurationRows(
            Connection(config: new Dictionary<string, string> { ["Password"] = "leaked-in-config", ["Host"] = "h" }, secretFields: []),
            Definition());

        System.Text.Json.JsonSerializer.Serialize(rows).Should().NotContain("leaked-in-config");
    }

    [Fact]
    public void A_stored_secret_that_shares_its_key_with_a_plain_field_still_gets_a_secret_row_that_can_be_removed()
    {
        // A legacy row: the connector declares Host as plain config, and the connection also stores a secret reference under "Host".
        var rows = ConnectionWorkspacePresentation.ConfigurationRows(
            Connection(secretFields: [new ConnectionSecretFieldDto("Password", true), new ConnectionSecretFieldDto("Host", true)]),
            Definition());

        rows.Where(r => r.Key == "Host").Should().HaveCount(2);
        rows.Single(r => r.Key == "Host" && !r.IsSecret).Value.Should().Be("db.example");
        var secret = rows.Single(r => r.Key == "Host" && r.IsSecret);
        secret.IsUndeclared.Should().BeTrue();
        secret.IsSet.Should().BeTrue();
        secret.Value.Should().BeNull();
        secret.Label.Should().NotBe("Server", "it is told apart from the plain field");
    }

    [Fact]
    public void Stored_keys_the_connector_does_not_declare_are_kept_visible_by_their_key()
    {
        var rows = ConnectionWorkspacePresentation.ConfigurationRows(
            Connection(config: new Dictionary<string, string> { ["Host"] = "h", ["Legacy"] = "old" },
                secretFields: [new ConnectionSecretFieldDto("Password", true), new ConnectionSecretFieldDto("LegacySecret", true)]),
            Definition());

        rows.Single(r => r.Key == "Legacy").Label.Should().Be("Legacy");
        rows.Single(r => r.Key == "Legacy").Value.Should().Be("old");
        rows.Single(r => r.Key == "LegacySecret").IsSecret.Should().BeTrue();
        rows.Single(r => r.Key == "LegacySecret").IsSet.Should().BeTrue();
        rows.Single(r => r.Key == "LegacySecret").Value.Should().BeNull();
        rows.Single(r => r.Key == "LegacySecret").IsUndeclared.Should().BeTrue("a stored secret key the connector does not declare can only be removed");
        rows.Single(r => r.Key == "Password").IsUndeclared.Should().BeFalse();
        rows.Where(r => !r.IsSecret).Should().OnlyContain(r => !r.IsUndeclared);
    }

    [Fact]
    public void Without_a_definition_the_stored_settings_still_render_by_key_and_secrets_stay_present_or_absent()
    {
        var rows = ConnectionWorkspacePresentation.ConfigurationRows(Connection(), null);

        rows.Select(r => r.Label).Should().BeEquivalentTo("Host", "Database", "Password");
        rows.Single(r => r.Key == "Password").Value.Should().BeNull();
        rows.Single(r => r.Key == "Password").IsSet.Should().BeTrue();
        rows.Should().OnlyContain(r => r.Description == null);
    }

    [Fact]
    public void A_declared_field_with_no_stored_value_is_listed_as_not_set()
    {
        var rows = ConnectionWorkspacePresentation.ConfigurationRows(
            Connection(config: new Dictionary<string, string> { ["Host"] = "h" }), Definition());

        var database = rows.Single(r => r.Key == "Database");
        database.Value.Should().BeNull();
        database.IsSet.Should().BeFalse();
    }

    [Theory]
    [InlineData("NeverTested", true, "Not tested yet", ManagementStatusSemantic.Neutral)]
    [InlineData("Success", true, "Verified", ManagementStatusSemantic.Success)]
    [InlineData("Failed", true, "Verification failed", ManagementStatusSemantic.Error)]
    [InlineData("Unavailable", true, "Verification unavailable", ManagementStatusSemantic.Warning)]
    [InlineData("Success", false, "Not testable", ManagementStatusSemantic.Neutral)]
    [InlineData("NeverTested", false, "Not testable", ManagementStatusSemantic.Neutral)]
    public void Health_is_a_legible_point_in_time_status(string status, bool supported, string label, ManagementStatusSemantic semantic)
    {
        var health = ConnectionWorkspacePresentation.Health(Connection(status: status, supportsVerification: supported));

        health.Label.Should().Be(label);
        health.Semantic.Should().Be(semantic);
    }

    [Fact]
    public void A_retired_connection_never_points_the_operator_at_a_test_action_it_no_longer_has()
    {
        var retired = Connection(status: "NeverTested") with { State = "Retired" };

        var health = ConnectionWorkspacePresentation.Health(retired);

        health.Label.Should().Be("Never tested");
        health.Detail.Should().NotContain("Run Test connection");
        ConnectionWorkspacePresentation.IsRetired(retired).Should().BeTrue();
        ConnectionWorkspacePresentation.IsRetired(Connection()).Should().BeFalse();
        ConnectionWorkspacePresentation.RetiredNotice.Should().Contain("retired").And.Contain("read-only");
    }

    [Fact]
    public void Retired_copy_says_bindings_stay_recorded_and_do_not_keep_working()
    {
        var notice = ConnectionWorkspacePresentation.RetiredNotice;
        notice.Should().Contain("no longer available for active use");
        notice.Should().Contain("no longer eligible for new bindings");
        notice.Should().Contain("may still be listed");
        notice.Should().Contain("stay in place and do not resolve until they are changed");
        notice.Should().NotContainEquivalentOf("keep working");
        notice.Should().NotContainEquivalentOf("un-retire");
        notice.Should().NotContainEquivalentOf("restore");
        notice.Should().NotContainEquivalentOf("secret");

        var confirm = ConnectionWorkspacePresentation.RetireConfirmConsequence
            + " "
            + ConnectionWorkspacePresentation.RetireConfirmDetails;
        confirm.Should().Contain("cannot be undone");
        confirm.Should().Contain("not deleted");
        confirm.Should().Contain("stay recorded");
        confirm.Should().Contain("will not be able to use this connection");
        confirm.Should().Contain("History for this connection remains");
        confirm.Should().NotContainEquivalentOf("keep working");
        confirm.Should().NotContainEquivalentOf("automatically removed");
        confirm.Should().NotContainEquivalentOf("secret");
        confirm.Should().NotContainEquivalentOf("un-retire");
    }

    [Fact]
    public void A_failed_verification_is_reported_as_a_fact_not_a_gate()
    {
        var health = ConnectionWorkspacePresentation.Health(Connection(status: "Failed"));

        health.Detail.Should().NotBeNullOrWhiteSpace();
        health.Detail.Should().NotContainEquivalentOf("disabled").And.NotContainEquivalentOf("blocked");
    }

    [Fact]
    public void An_unrecognised_status_is_shown_verbatim_and_neutral_never_thrown_on()
    {
        var health = ConnectionWorkspacePresentation.Health(Connection(status: "SomethingNew"));

        health.Label.Should().Be("SomethingNew");
        health.Semantic.Should().Be(ManagementStatusSemantic.Neutral);
    }

    [Fact]
    public void A_connector_is_unregistered_exactly_when_the_server_supplied_no_display_name()
    {
        ConnectionWorkspacePresentation.IsConnectorRegistered(Connection(connectorDisplayName: "Platform SQL")).Should().BeTrue();
        ConnectionWorkspacePresentation.IsConnectorRegistered(Connection(connectorDisplayName: null)).Should().BeFalse();
        ConnectionWorkspacePresentation.IsConnectorRegistered(Connection(connectorDisplayName: " ")).Should().BeFalse();
        ConnectionWorkspacePresentation.UnregisteredConnectorNotice.Should().Contain("not registered");
    }

    [Theory]
    [InlineData("Active", ManagementStatusSemantic.Success)]
    [InlineData("Retired", ManagementStatusSemantic.Neutral)]
    [InlineData("Whatever", ManagementStatusSemantic.Neutral)]
    public void State_has_a_semantic(string state, ManagementStatusSemantic expected) =>
        ConnectionWorkspacePresentation.StateSemantic(state).Should().Be(expected);

    [Fact]
    public void An_unregistered_connector_is_described_as_unregistered_not_as_lacking_test_support()
    {
        var health = ConnectionWorkspacePresentation.Health(Connection(connectorDisplayName: null, supportsVerification: false));

        health.Label.Should().Be("Not testable");
        health.Detail.Should().Contain("not registered").And.NotContain("does not support");
    }
}
