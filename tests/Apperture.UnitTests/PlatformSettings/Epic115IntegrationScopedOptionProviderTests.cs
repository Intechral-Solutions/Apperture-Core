using Apperture.ControlPlane.Security;
using Apperture.UnitTests.Audit;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Api.Models;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.UnitTests.PlatformSettings;

public sealed class Epic115IntegrationScopedOptionProviderTests
{
    private const string IntegrationId = "int-test-001";

    [Fact]
    public async Task Provider_receives_expected_integration_id_in_context()
    {
        PlatformSettingOptionProviderContext? captured = null;
        var provider = new Mock<IPlatformSettingOptionProvider>();
        provider.SetupGet(p => p.ProviderKey).Returns(PlatformSettingTestDefinitions.IntegrationProviderKey);
        provider.Setup(p => p.GetOptionsAsync(
                It.IsAny<PlatformSettingDefinition>(),
                It.IsAny<PlatformSettingOptionProviderContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<PlatformSettingDefinition, PlatformSettingOptionProviderContext, CancellationToken>(
                (_, context, _) => captured = context)
            .ReturnsAsync([]);

        var coordinator = CreateCoordinator(provider.Object, integrationExists: true);
        var def = PlatformSettingTestDefinitions.IntegrationProviderBackedSelectSample;

        await coordinator.GetOptionsForReadAsync(def, IntegrationId, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.ContextKind.Should().Be(PlatformSettingOptionProviderContextKind.Integration);
        captured.IntegrationId.Should().Be(IntegrationId);
        captured.SettingKey.Should().Be(def.Key);
    }

    [Fact]
    public async Task Options_endpoint_returns_bad_request_when_integration_id_missing_for_integration_context_setting()
    {
        var def = PlatformSettingTestDefinitions.IntegrationProviderBackedSelectSample;
        var controller = CreateController(def, new TestIntegrationOptionsOptionProvider());

        var result = await controller.Options(def.Key, null, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Options_endpoint_returns_bad_request_when_integration_id_supplied_for_instance_context_setting()
    {
        var def = PlatformSettingTestDefinitions.ProviderBackedSelectSample;
        var controller = CreateController(def, CreateInstanceProvider());

        var result = await controller.Options(def.Key, IntegrationId, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Options_endpoint_returns_not_found_when_integration_does_not_exist()
    {
        var def = PlatformSettingTestDefinitions.IntegrationProviderBackedSelectSample;
        var controller = CreateController(def, new TestIntegrationOptionsOptionProvider(), integrationExists: false);

        var result = await controller.Options(def.Key, IntegrationId, CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Options_endpoint_returns_forbidden_when_integration_read_denied()
    {
        var def = PlatformSettingTestDefinitions.IntegrationProviderBackedSelectSample;
        var (_, _, authorization) = PlatformSettingOptionProviderTestHarness.CreateDefaultAuthMocks(allowAuthorization: false);
        var controller = CreateController(
            def,
            new TestIntegrationOptionsOptionProvider(),
            authorization: authorization,
            integrationExists: true);

        var result = await controller.Options(def.Key, IntegrationId, CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Options_endpoint_returns_integration_specific_options()
    {
        var def = PlatformSettingTestDefinitions.IntegrationProviderBackedSelectSample;
        var controller = CreateController(def, new TestIntegrationOptionsOptionProvider(), integrationExists: true);

        var result = await controller.Options(def.Key, IntegrationId, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var dtos = (IReadOnlyList<PlatformSettingAllowedValueDto>)ok.Value!;
        dtos.Should().HaveCount(2);
        dtos[0].Value.Should().Be($"{IntegrationId}:alpha");
        dtos[1].Value.Should().Be($"{IntegrationId}:beta");
    }

    [Fact]
    public async Task SetAsync_fails_when_integration_id_missing_for_integration_context_setting()
    {
        var def = PlatformSettingTestDefinitions.IntegrationProviderBackedSelectSample;
        var sut = CreateService(def, new TestIntegrationOptionsOptionProvider(), integrationExists: true);

        var act = () => sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload { ScalarValue = $"{IntegrationId}:alpha" },
            null,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*IntegrationId is required*");
    }

    [Fact]
    public async Task SetAsync_rejects_value_not_available_for_integration()
    {
        var def = PlatformSettingTestDefinitions.IntegrationProviderBackedSelectSample;
        var sut = CreateService(def, new TestIntegrationOptionsOptionProvider(), integrationExists: true);

        var act = () => sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload
            {
                ScalarValue = "other-integration:alpha",
                IntegrationId = IntegrationId
            },
            null,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*not an allowed option*");
    }

    [Fact]
    public async Task SetAsync_accepts_valid_integration_context_value()
    {
        var def = PlatformSettingTestDefinitions.IntegrationProviderBackedSelectSample;
        var sut = CreateService(def, new TestIntegrationOptionsOptionProvider(), integrationExists: true);

        await sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload
            {
                ScalarValue = $"{IntegrationId}:alpha",
                IntegrationId = IntegrationId
            },
            null,
            CancellationToken.None);
    }

    [Fact]
    public async Task Instance_provider_still_works_without_integration_id()
    {
        var def = PlatformSettingTestDefinitions.ProviderBackedSelectSample;
        var controller = CreateController(def, CreateInstanceProvider());

        var result = await controller.Options(def.Key, null, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var dtos = (IReadOnlyList<PlatformSettingAllowedValueDto>)ok.Value!;
        dtos.Should().HaveCount(1);
        dtos[0].Value.Should().Be("alpha");
    }

    private static ConfigPlatformSettingsController CreateController(
        PlatformSettingDefinition def,
        IPlatformSettingOptionProvider provider,
        Mock<IAuthorizationEvaluationService>? authorization = null,
        bool integrationExists = true)
    {
        var (principalContext, _, auth) = PlatformSettingOptionProviderTestHarness.CreateDefaultAuthMocks();
        if (authorization is not null)
            auth = authorization;

        var integrations = CreateIntegrationRepository(integrationExists);
        var coordinator = PlatformSettingOptionProviderTestHarness.CreateCoordinator(
            provider,
            principalContext,
            auth,
            integrations);

        return new ConfigPlatformSettingsController(
            new Mock<IPlatformSettingsService>().Object,
            principalContext.Object,
            auth.Object,
            new PlatformSettingOptionProviderTestHarness.SingleDefinitionRegistry(def),
            coordinator);
    }

    private static PlatformSettingsService CreateService(
        PlatformSettingDefinition def,
        IPlatformSettingOptionProvider provider,
        bool integrationExists)
    {
        var integrations = CreateIntegrationRepository(integrationExists);
        var coordinator = PlatformSettingOptionProviderTestHarness.CreateCoordinator(
            provider,
            integrations: integrations);

        var resolver = new Mock<IPlatformSettingsResolver>();
        resolver.Setup(x => x.ListResolvedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlatformSettingResolvedValue>());
        resolver.Setup(x => x.GetResolvedAsync(def.Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Resolved(def, $"{IntegrationId}:alpha"));

        var repo = new Mock<IPlatformSettingValueRepository>();
        repo.Setup(x => x.GetByKeyAsync(def.Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformSettingValue?)null);
        repo.Setup(x => x.UpsertAsync(It.IsAny<PlatformSettingValue>(), null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var (principalContext, _, _) = PlatformSettingOptionProviderTestHarness.CreateDefaultAuthMocks();
        var audit = new Mock<IAuditService>();

        return new PlatformSettingsService(
            new PlatformSettingOptionProviderTestHarness.SingleDefinitionRegistry(def),
            repo.Object,
            resolver.Object,
            coordinator,
            new PlatformSettingSecretMaterialProvisioner(Mock.Of<ISecretStore>()),
            new AuditRecording());
    }

    private static PlatformSettingOptionProviderCoordinator CreateCoordinator(
        IPlatformSettingOptionProvider provider,
        bool integrationExists)
    {
        var integrations = CreateIntegrationRepository(integrationExists);
        return PlatformSettingOptionProviderTestHarness.CreateCoordinator(
            provider,
            integrations: integrations);
    }

    private static Mock<IIntegrationRepository> CreateIntegrationRepository(bool integrationExists)
    {
        var integrations = new Mock<IIntegrationRepository>();
        integrations.Setup(x => x.GetByIdAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>()))
            .Returns((IntegrationId id, CancellationToken _) =>
            {
                if (!integrationExists || !string.Equals(id.Value, IntegrationId, StringComparison.Ordinal))
                    return ValueTask.FromResult<DomainIntegration?>(null);

                return ValueTask.FromResult<DomainIntegration?>(
                    new DomainIntegration(
                        id,
                        "Test Integration",
                        "mod-1",
                        null,
                        IntegrationState.Active,
                        null,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow));
            });

        return integrations;
    }

    private static IPlatformSettingOptionProvider CreateInstanceProvider()
    {
        var provider = new Mock<IPlatformSettingOptionProvider>();
        provider.SetupGet(p => p.ProviderKey).Returns(PlatformSettingTestDefinitions.ProviderKey);
        provider.Setup(p => p.GetOptionsAsync(
                It.IsAny<PlatformSettingDefinition>(),
                It.IsAny<PlatformSettingOptionProviderContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlatformSettingAllowedValue { Value = "alpha", Label = "Alpha" }]);
        return provider.Object;
    }

    private static PlatformSettingResolvedValue Resolved(PlatformSettingDefinition def, object currentValue) => new()
    {
        Key = def.Key,
        DisplayName = def.DisplayName,
        Description = def.Description,
        Category = def.Category,
        ValueType = def.ValueType,
        InputKind = def.InputKind,
        CurrentValue = currentValue,
        Source = PlatformSettingValueSource.Database
    };
}
