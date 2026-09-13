using Apperture.ControlPlane.Security;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Api.Models;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Apperture.UnitTests.PlatformSettings;

public sealed class Epic114PlatformSettingOptionProviderTests
{
    [Fact]
    public async Task Options_endpoint_returns_provider_options_for_provider_backed_setting()
    {
        var def = PlatformSettingTestDefinitions.ProviderBackedSelectSample;

        var providerOptions = new[]
        {
            new PlatformSettingAllowedValue { Value = "alpha", Label = "Alpha", Description = "First option" },
            new PlatformSettingAllowedValue { Value = "beta", Label = "Beta" }
        };

        var provider = new Mock<IPlatformSettingOptionProvider>();
        provider.SetupGet(p => p.ProviderKey).Returns(PlatformSettingTestDefinitions.ProviderKey);
        provider.Setup(p => p.GetOptionsAsync(
                It.IsAny<PlatformSettingDefinition>(),
                It.IsAny<PlatformSettingOptionProviderContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(providerOptions);

        var (principalContext, _, authorization) = PlatformSettingOptionProviderTestHarness.CreateDefaultAuthMocks();
        var coordinator = PlatformSettingOptionProviderTestHarness.CreateCoordinator(provider.Object);

        var controller = new ConfigPlatformSettingsController(
            new Mock<IPlatformSettingsService>().Object,
            principalContext.Object,
            authorization.Object,
            new PlatformSettingOptionProviderTestHarness.SingleDefinitionRegistry(def),
            coordinator);

        var result = await controller.Options(def.Key, null, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        ok.Value.Should().BeAssignableTo<IReadOnlyList<PlatformSettingAllowedValueDto>>();

        var dtos = (IReadOnlyList<PlatformSettingAllowedValueDto>)ok.Value!;
        dtos.Should().HaveCount(2);
        dtos[0].Value.Should().Be("alpha");
        dtos[0].Label.Should().Be("Alpha");
    }

    [Fact]
    public async Task Options_endpoint_returns_bad_request_when_setting_is_not_provider_backed()
    {
        var def = PlatformSettingTestDefinitions.MultiSelectSample with
        {
            OptionProviderKey = null
        };

        var (principalContext, _, authorization) = PlatformSettingOptionProviderTestHarness.CreateDefaultAuthMocks();
        var coordinator = PlatformSettingOptionProviderTestHarness.CreateCoordinator(
            new Mock<IPlatformSettingOptionProvider>().Object);

        var controller = new ConfigPlatformSettingsController(
            new Mock<IPlatformSettingsService>().Object,
            principalContext.Object,
            authorization.Object,
            new PlatformSettingOptionProviderTestHarness.SingleDefinitionRegistry(def),
            coordinator);

        var result = await controller.Options(def.Key, null, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SetAsync_provider_backed_select_accepts_valid_value()
    {
        var def = PlatformSettingTestDefinitions.ProviderBackedSelectSample;
        var providerOptions = new[]
        {
            new PlatformSettingAllowedValue { Value = "alpha", Label = "Alpha" }
        };

        var provider = new Mock<IPlatformSettingOptionProvider>();
        provider.SetupGet(p => p.ProviderKey).Returns(PlatformSettingTestDefinitions.ProviderKey);
        provider.Setup(p => p.GetOptionsAsync(
                It.IsAny<PlatformSettingDefinition>(),
                It.IsAny<PlatformSettingOptionProviderContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(providerOptions);

        var resolver = new Mock<IPlatformSettingsResolver>();
        resolver.Setup(x => x.ListResolvedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlatformSettingResolvedValue>());
        resolver.Setup(x => x.GetResolvedAsync(def.Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Resolved(def, "alpha"));

        var repo = new Mock<IPlatformSettingValueRepository>();
        repo.Setup(x => x.GetByKeyAsync(def.Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformSettingValue?)null);
        repo.Setup(x => x.UpsertAsync(It.IsAny<PlatformSettingValue>(), null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var (principalContext, _, _) = PlatformSettingOptionProviderTestHarness.CreateDefaultAuthMocks();
        var audit = new Mock<IAuditService>();
        var coordinator = PlatformSettingOptionProviderTestHarness.CreateCoordinator(provider.Object);

        var sut = new PlatformSettingsService(
            new PlatformSettingOptionProviderTestHarness.SingleDefinitionRegistry(def),
            repo.Object,
            resolver.Object,
            coordinator,
            new PlatformSettingSecretMaterialProvisioner(Mock.Of<ISecretStore>()),
            principalContext.Object,
            audit.Object);

        await sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload { ScalarValue = "alpha" },
            null,
            CancellationToken.None);
    }

    [Fact]
    public async Task SetAsync_provider_backed_select_rejects_invalid_value()
    {
        var def = PlatformSettingTestDefinitions.ProviderBackedSelectSample;
        var providerOptions = new[]
        {
            new PlatformSettingAllowedValue { Value = "alpha", Label = "Alpha" }
        };

        var provider = new Mock<IPlatformSettingOptionProvider>();
        provider.SetupGet(p => p.ProviderKey).Returns(PlatformSettingTestDefinitions.ProviderKey);
        provider.Setup(p => p.GetOptionsAsync(
                It.IsAny<PlatformSettingDefinition>(),
                It.IsAny<PlatformSettingOptionProviderContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(providerOptions);

        var resolver = new Mock<IPlatformSettingsResolver>();
        resolver.Setup(x => x.ListResolvedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlatformSettingResolvedValue>());

        var repo = new Mock<IPlatformSettingValueRepository>();
        repo.Setup(x => x.GetByKeyAsync(def.Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformSettingValue?)null);

        var (principalContext, _, _) = PlatformSettingOptionProviderTestHarness.CreateDefaultAuthMocks();
        var audit = new Mock<IAuditService>();
        var coordinator = PlatformSettingOptionProviderTestHarness.CreateCoordinator(provider.Object);

        var sut = new PlatformSettingsService(
            new PlatformSettingOptionProviderTestHarness.SingleDefinitionRegistry(def),
            repo.Object,
            resolver.Object,
            coordinator,
            new PlatformSettingSecretMaterialProvisioner(Mock.Of<ISecretStore>()),
            principalContext.Object,
            audit.Object);

        var act = () => sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload { ScalarValue = "delta" },
            null,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*not an allowed option*");
    }

    [Fact]
    public async Task SetAsync_provider_backed_multi_select_rejects_invalid_option()
    {
        var def = PlatformSettingTestDefinitions.ProviderBackedMultiSelectSample;
        var providerOptions = new[]
        {
            new PlatformSettingAllowedValue { Value = "alpha", Label = "Alpha" },
            new PlatformSettingAllowedValue { Value = "beta", Label = "Beta" }
        };

        var provider = new Mock<IPlatformSettingOptionProvider>();
        provider.SetupGet(p => p.ProviderKey).Returns(PlatformSettingTestDefinitions.ProviderKey);
        provider.Setup(p => p.GetOptionsAsync(
                It.IsAny<PlatformSettingDefinition>(),
                It.IsAny<PlatformSettingOptionProviderContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(providerOptions);

        var resolver = new Mock<IPlatformSettingsResolver>();
        resolver.Setup(x => x.ListResolvedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlatformSettingResolvedValue>());

        var repo = new Mock<IPlatformSettingValueRepository>();
        repo.Setup(x => x.GetByKeyAsync(def.Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformSettingValue?)null);

        var (principalContext, _, _) = PlatformSettingOptionProviderTestHarness.CreateDefaultAuthMocks();
        var audit = new Mock<IAuditService>();
        var coordinator = PlatformSettingOptionProviderTestHarness.CreateCoordinator(provider.Object);

        var sut = new PlatformSettingsService(
            new PlatformSettingOptionProviderTestHarness.SingleDefinitionRegistry(def),
            repo.Object,
            resolver.Object,
            coordinator,
            new PlatformSettingSecretMaterialProvisioner(Mock.Of<ISecretStore>()),
            principalContext.Object,
            audit.Object);

        var act = () => sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload { ArrayValues = ["alpha", "delta"] },
            null,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*not allowed options*");
    }

    [Fact]
    public async Task SetAsync_provider_backed_multi_select_enforces_max_items_and_rejects_duplicates()
    {
        var def = PlatformSettingTestDefinitions.ProviderBackedMultiSelectSample;
        var providerOptions = new[]
        {
            new PlatformSettingAllowedValue { Value = "alpha", Label = "Alpha" },
            new PlatformSettingAllowedValue { Value = "beta", Label = "Beta" },
            new PlatformSettingAllowedValue { Value = "gamma", Label = "Gamma" }
        };

        var provider = new Mock<IPlatformSettingOptionProvider>();
        provider.SetupGet(p => p.ProviderKey).Returns(PlatformSettingTestDefinitions.ProviderKey);
        provider.Setup(p => p.GetOptionsAsync(
                It.IsAny<PlatformSettingDefinition>(),
                It.IsAny<PlatformSettingOptionProviderContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(providerOptions);

        var resolver = new Mock<IPlatformSettingsResolver>();
        resolver.Setup(x => x.ListResolvedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlatformSettingResolvedValue>());

        var repo = new Mock<IPlatformSettingValueRepository>();
        repo.Setup(x => x.GetByKeyAsync(def.Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformSettingValue?)null);

        var (principalContext, _, _) = PlatformSettingOptionProviderTestHarness.CreateDefaultAuthMocks();
        var audit = new Mock<IAuditService>();
        var coordinator = PlatformSettingOptionProviderTestHarness.CreateCoordinator(provider.Object);

        var sut = new PlatformSettingsService(
            new PlatformSettingOptionProviderTestHarness.SingleDefinitionRegistry(def),
            repo.Object,
            resolver.Object,
            coordinator,
            new PlatformSettingSecretMaterialProvisioner(Mock.Of<ISecretStore>()),
            principalContext.Object,
            audit.Object);

        var actTooMany = () => sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload { ArrayValues = ["alpha", "beta", "gamma"] },
            null,
            CancellationToken.None);

        await actTooMany.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*At most 2 value(s) are allowed.*");

        var actDuplicates = () => sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload { ArrayValues = ["alpha", "alpha"] },
            null,
            CancellationToken.None);

        await actDuplicates.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Duplicate values are not allowed.*");
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
