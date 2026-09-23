using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// <c>/connections/new?connectorDefinitionId=…</c> preselects a connector. An unknown or missing id must
/// degrade to the normal, unselected form rather than throw or invent a selection.
/// </summary>
public sealed class ConnectionCreatePreselectionTests
{
    private static readonly string[] Available = ["Apperture.Platform.Sql", "Apperture.Platform.Sftp"];

    [Fact]
    public void A_known_connector_definition_id_is_preselected()
    {
        ConnectionCreatePreselection.ResolveConnectorDefinitionId("Apperture.Platform.Sftp", Available)
            .Should().Be("Apperture.Platform.Sftp");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Apperture.Platform.DoesNotExist")]
    [InlineData("apperture.platform.sql")]
    public void Missing_or_unknown_ids_leave_the_form_unselected(string? requested)
    {
        ConnectionCreatePreselection.ResolveConnectorDefinitionId(requested, Available).Should().Be("");
    }

    [Fact]
    public void No_available_definitions_leaves_the_form_unselected()
    {
        ConnectionCreatePreselection.ResolveConnectorDefinitionId("Apperture.Platform.Sql", []).Should().Be("");
    }
}
