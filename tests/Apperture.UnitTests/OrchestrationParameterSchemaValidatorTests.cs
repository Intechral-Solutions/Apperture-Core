using Apperture.Core.Application.Validation;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests;

public sealed class OrchestrationParameterSchemaValidatorTests
{
    [Fact]
    public void TryParseFields_ParsesHelpLabelDefault()
    {
        const string schema = """{"fields":[{"key":"name","type":"string","required":true,"label":"Name","help":"Hint","default":"Ada"}]}""";

        OrchestrationParameterSchemaValidator.TryParseFields(schema, out var fields).Should().BeTrue();
        fields.Should().ContainSingle();
        var f = fields[0];
        f.Key.Should().Be("name");
        f.Label.Should().Be("Name");
        f.Help.Should().Be("Hint");
        f.DefaultJson.Should().Be("\"Ada\"");
    }

    [Fact]
    public void Validate_RequiredMissing_Throws()
    {
        const string schema = """{"fields":[{"key":"x","type":"string","required":true}]}""";

        var act = () => OrchestrationParameterSchemaValidator.Validate(schema, new Dictionary<string, object?>());
        act.Should().Throw<ArgumentException>().WithMessage("*Missing required field 'x'*");
    }

    [Fact]
    public void MergeDefaults_AppliesStringDefault()
    {
        const string schema = """{"fields":[{"key":"name","type":"string","required":false,"default":"World"}]}""";
        OrchestrationParameterSchemaValidator.TryParseFields(schema, out var fields).Should().BeTrue();
        var merged = OrchestrationParameterSchemaValidator.MergeDefaults(fields, null);
        merged["name"].Should().Be("World");
    }
}
