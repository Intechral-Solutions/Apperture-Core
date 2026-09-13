using Apperture.Core.Application.Integration;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Management.Api.Models;
using Apperture.ModuleSdk.Integration.Configuration;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class Sdk6dIntegrationConfigurationControllerTests
{
    [Fact]
    public void Declarations_mapper_uses_operator_capability_labels_not_clr_fqns()
    {
        var configuration = new ModuleIntegrationConfiguration(
            "Sample.ApiPolling",
            Array.Empty<IntegrationSettingDefinition>(),
            [new ConnectionRequirementDefinition
            {
                Key = "DirectoryApi",
                DisplayName = "Directory API",
                Description = "Sample directory API.",
                CapabilityContractType = typeof(object).FullName!,
                CapabilityKind = ConnectionCapabilityKinds.HttpApi,
            }],
            Array.Empty<ProcedureRequirementSet>());

        var dto = IntegrationConfigurationDtoMapper.ToDto(configuration);

        dto.Connections.Should().ContainSingle(c =>
            c.CapabilityKind == ConnectionCapabilityKinds.HttpApi
            && c.CapabilityLabel == "HTTP API"
            && c.TechnicalContractType == typeof(object).FullName
            && c.CapabilityLabel != c.TechnicalContractType);
    }

    [Fact]
    public void Integration_settings_get_dto_redacts_sensitive_current_value()
    {
        var def = new PlatformSettingDefinitionRegistry().TryGet(PlatformSettingKeys.NotificationsEmailSmtpPasswordRef)!;
        var secretGuid = Guid.NewGuid().ToString("D");
        var resolved = PlatformSettingResolvedValueFactory.Create(
            def,
            secretGuid,
            PlatformSettingValueSource.Database,
            rowVersion: [1, 2, 3, 4],
            valueScopeKind: PlatformSettingScopeConstants.IntegrationKind,
            valueScopeId: "int-sdk6d-secret-001");

        var dto = PlatformSettingsDtoMapper.ToDto(resolved);

        dto.IsSensitive.Should().BeTrue();
        dto.IsSecretConfigured.Should().BeTrue();
        dto.CurrentValue.Should().BeEmpty();
        dto.CurrentValue.Should().NotContain(secretGuid);
    }
}
