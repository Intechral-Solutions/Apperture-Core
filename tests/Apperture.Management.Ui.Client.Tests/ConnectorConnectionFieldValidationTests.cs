using Apperture.Management.Client;
using Apperture.Management.Ui.Client.Molecules;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>
/// HSK-A Task 4: covers the shared "all connector-required fields are filled" predicate used by both
/// <c>ConnectionNew.razor</c> (standalone create) and <c>IntegrationConnections.razor</c> (in-flow
/// create) for both live Create-button eligibility and submit-time validation.
/// </summary>
public class ConnectorConnectionFieldValidationTests
{
    private static ConnectionFieldCatalogDto Field(string key, bool isRequired, bool isSecret = false) =>
        new(key, key, isSecret, Description: null, IsRequired: isRequired);

    private static ConnectorDefinitionCatalogDto Definition(params ConnectionFieldCatalogDto[] fields) =>
        new("connector-1", "Connector One", ModuleId: null, Description: null, Fields: fields, Capabilities: "");

    [Fact]
    public void AllRequiredFieldsFilled_AllRequiredFieldsHaveValues_ReturnsTrue()
    {
        var definition = Definition(Field("host", isRequired: true), Field("port", isRequired: true));
        var values = new Dictionary<string, string> { ["host"] = "db.example.com", ["port"] = "5432" };

        var result = ConnectorConnectionFieldValidation.AllRequiredFieldsFilled(definition, f => Get(values, f));

        result.Should().BeTrue();
    }

    [Fact]
    public void AllRequiredFieldsFilled_OneRequiredFieldEmpty_ReturnsFalse()
    {
        var definition = Definition(Field("host", isRequired: true), Field("port", isRequired: true));
        var values = new Dictionary<string, string> { ["host"] = "db.example.com", ["port"] = "" };

        var result = ConnectorConnectionFieldValidation.AllRequiredFieldsFilled(definition, f => Get(values, f));

        result.Should().BeFalse();
    }

    [Fact]
    public void AllRequiredFieldsFilled_OptionalFieldsEmptyButRequiredFilled_ReturnsTrue()
    {
        var definition = Definition(
            Field("host", isRequired: true),
            Field("nickname", isRequired: false));
        var values = new Dictionary<string, string> { ["host"] = "db.example.com", ["nickname"] = "" };

        var result = ConnectorConnectionFieldValidation.AllRequiredFieldsFilled(definition, f => Get(values, f));

        result.Should().BeTrue();
    }

    [Fact]
    public void AllRequiredFieldsFilled_NoFieldsDefined_ReturnsTrue()
    {
        var definition = Definition();

        var result = ConnectorConnectionFieldValidation.AllRequiredFieldsFilled(definition, _ => null);

        result.Should().BeTrue();
    }

    [Fact]
    public void AllRequiredFieldsFilled_NullDefinition_ReturnsTrue()
    {
        var result = ConnectorConnectionFieldValidation.AllRequiredFieldsFilled(null, _ => null);

        result.Should().BeTrue();
    }

    [Fact]
    public void GetMissingRequiredFields_ReturnsOnlyTheEmptyRequiredFields_PreservingDisplayNameForMessages()
    {
        var missingField = Field("apiKey", isRequired: true, isSecret: true);
        var definition = Definition(Field("host", isRequired: true), missingField);
        var values = new Dictionary<string, string> { ["host"] = "db.example.com" };

        var missing = ConnectorConnectionFieldValidation.GetMissingRequiredFields(definition, f => Get(values, f));

        missing.Should().ContainSingle().Which.Key.Should().Be("apiKey");
    }

    private static string? Get(Dictionary<string, string> values, ConnectionFieldCatalogDto field) =>
        values.TryGetValue(field.Key, out var v) ? v : null;
}
