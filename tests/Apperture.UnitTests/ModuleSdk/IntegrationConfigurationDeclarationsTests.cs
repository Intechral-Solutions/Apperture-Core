using Apperture.Core.Application.PlatformSettings;
using Apperture.ModuleSdk.Integration.Configuration;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ModuleSdk;

public sealed class IntegrationConfigurationDeclarationsTests
{
    private sealed class TestContributor(
        IReadOnlyList<IntegrationSettingDefinition>? settings = null,
        IReadOnlyList<ConnectionRequirementDefinition>? connections = null,
        IReadOnlyList<ProcedureRequirementSet>? procedures = null) : IIntegrationConfigurationContributor
    {
        public IReadOnlyList<IntegrationSettingDefinition> GetSettingDefinitions() =>
            settings ?? Array.Empty<IntegrationSettingDefinition>();

        public IReadOnlyList<ConnectionRequirementDefinition> GetConnectionRequirements() =>
            connections ?? Array.Empty<ConnectionRequirementDefinition>();

        public IReadOnlyList<ProcedureRequirementSet> GetProcedureRequirements() =>
            procedures ?? Array.Empty<ProcedureRequirementSet>();
    }

    [Fact]
    public void Validate_rejects_duplicate_setting_keys()
    {
        var contributor = new TestContributor(
            settings:
            [
                new IntegrationSettingDefinition
                {
                    Key = "Dup",
                    DisplayName = "One",
                    Description = "One",
                    ValueKind = IntegrationSettingValueKind.String,
                },
                new IntegrationSettingDefinition
                {
                    Key = "Dup",
                    DisplayName = "Two",
                    Description = "Two",
                    ValueKind = IntegrationSettingValueKind.String,
                },
            ]);

        var act = () => IntegrationConfigurationDeclarations.Validate(contributor);
        act.Should().Throw<IntegrationConfigurationException>()
            .Which.Code.Should().Be("duplicate_declaration");
    }

    [Fact]
    public void Validate_rejects_forbidden_secret_default()
    {
        var contributor = new TestContributor(
            settings:
            [
                new IntegrationSettingDefinition
                {
                    Key = "Secret",
                    DisplayName = "Secret",
                    Description = "Secret",
                    ValueKind = IntegrationSettingValueKind.SensitiveText,
                    DefaultValue = "not-allowed",
                },
            ]);

        var act = () => IntegrationConfigurationDeclarations.Validate(contributor);
        act.Should().Throw<IntegrationConfigurationException>()
            .Which.Code.Should().Be("forbidden_secret_default");
    }

    [Fact]
    public void ToPlatformSettingDefinition_maps_sensitive_and_choice()
    {
        var definition = new IntegrationSettingDefinition
        {
            Key = "Mode",
            DisplayName = "Mode",
            Description = "Mode",
            ValueKind = IntegrationSettingValueKind.Choice,
            AllowedValues =
            [
                new IntegrationSettingAllowedValue("A", "Alpha"),
            ],
        };

        var mapped = IntegrationConfigurationDeclarations.ToPlatformSettingDefinition(definition, "Sample.Mod");
        mapped.Key.Should().Be("Mode");
        mapped.InputKind.Should().Be(PlatformSettingInputKind.Select);
        mapped.AllowedValueScopes.Should().Contain(PlatformSettingValueScopeKind.Integration);
        mapped.ConfigurationPath.Should().Be("Modules:Sample.Mod:Settings:Mode");
    }

    [Fact]
    public void ToPlatformSettingDefinition_maps_sensitive_text_to_secret_input()
    {
        var definition = new IntegrationSettingDefinition
        {
            Key = "Token",
            DisplayName = "Token",
            Description = "Token",
            ValueKind = IntegrationSettingValueKind.SensitiveText,
        };

        var mapped = IntegrationConfigurationDeclarations.ToPlatformSettingDefinition(definition, "Sample.Mod");
        mapped.IsSensitive.Should().BeTrue();
        mapped.InputKind.Should().Be(PlatformSettingInputKind.Secret);
        mapped.DefaultValue.Should().Be("");
    }
}
