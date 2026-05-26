using Apperture.Connectors.CampusNexus;
using Apperture.Connectors.CampusNexus.Models;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Api.Models;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Proof.CampusNexus.StudentReview;
using Xunit;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.UnitTests.PlatformSettings;

public sealed class Epic116CampusNexusCampusesOptionProviderTests
{
    private const string IntegrationId = "int-cns-001";

    [Fact]
    public async Task Provider_returns_campus_options_for_active_cns_integration()
    {
        var def = PlatformSettingTestDefinitions.CampusNexusProviderBackedSelectSample;
        var provider = CreateProviderWithCampuses(
            out var slotResolver,
            campuses:
            [
                new CampusODataRow { Id = 10, Name = "Main", Code = "MAIN" },
                new CampusODataRow { Id = 20, Name = "West", Code = "WEST" }
            ]);

        var context = CreateIntegrationContext(def.Key, IntegrationId);
        var options = await provider.GetOptionsAsync(def, context, CancellationToken.None);

        options.Should().HaveCount(2);
        options[0].Value.Should().Be("10");
        options[0].Label.Should().Be("Main");
        options[1].Value.Should().Be("20");

        slotResolver.Verify(s => s.ResolveSlotAsync<ICampusNexusApiClient>(
            ProofCampusNexusStudentReviewConstants.ModuleId,
            CampusNexusCampusesOptionProvider.CampusNexusApiSlotName,
            IntegrationId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Provider_throws_when_cns_slot_unbound()
    {
        var def = PlatformSettingTestDefinitions.CampusNexusProviderBackedSelectSample;
        var integrations = CreateIntegrationRepository(integrationExists: true);
        var slotResolver = new Mock<IRuntimeSlotResolver>();
        slotResolver.Setup(s => s.ResolveSlotAsync<ICampusNexusApiClient>(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ICampusNexusApiClient?)null);

        var provider = new CampusNexusCampusesOptionProvider(integrations.Object, slotResolver.Object);
        var context = CreateIntegrationContext(def.Key, IntegrationId);

        var act = () => provider.GetOptionsAsync(def, context, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*active Campus Nexus API*");
    }

    [Fact]
    public async Task Provider_throws_when_integration_not_found()
    {
        var def = PlatformSettingTestDefinitions.CampusNexusProviderBackedSelectSample;
        var integrations = CreateIntegrationRepository(integrationExists: false);
        var slotResolver = new Mock<IRuntimeSlotResolver>();

        var provider = new CampusNexusCampusesOptionProvider(integrations.Object, slotResolver.Object);
        var context = CreateIntegrationContext(def.Key, IntegrationId);

        var act = () => provider.GetOptionsAsync(def, context, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Options_endpoint_returns_campus_options_for_valid_integration()
    {
        var def = PlatformSettingTestDefinitions.CampusNexusProviderBackedSelectSample;
        var provider = CreateProviderWithCampuses(
            out _,
            [new CampusODataRow { Id = 5, Name = "South", Code = "S" }]);

        var controller = CreateController(def, provider);

        var result = await controller.Options(def.Key, IntegrationId, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var dtos = (IReadOnlyList<PlatformSettingAllowedValueDto>)((OkObjectResult)result.Result!).Value!;
        dtos.Should().ContainSingle();
        dtos[0].Value.Should().Be("5");
        dtos[0].Label.Should().Be("South");
    }

    [Fact]
    public async Task Options_endpoint_returns_bad_request_when_integration_id_missing()
    {
        var def = PlatformSettingTestDefinitions.CampusNexusProviderBackedSelectSample;
        var provider = CreateProviderWithCampuses(out _, []);
        var controller = CreateController(def, provider);

        var result = await controller.Options(def.Key, null, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SetAsync_accepts_valid_campus_value()
    {
        var def = PlatformSettingTestDefinitions.CampusNexusProviderBackedSelectSample;
        var provider = CreateProviderWithCampuses(
            out _,
            [new CampusODataRow { Id = 10, Name = "Main" }]);

        var sut = CreateService(def, provider);

        await sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload
            {
                ScalarValue = "10",
                IntegrationId = IntegrationId
            },
            null,
            CancellationToken.None);
    }

    [Fact]
    public async Task SetAsync_rejects_unknown_campus_value()
    {
        var def = PlatformSettingTestDefinitions.CampusNexusProviderBackedSelectSample;
        var provider = CreateProviderWithCampuses(
            out _,
            [new CampusODataRow { Id = 10, Name = "Main" }]);

        var sut = CreateService(def, provider);

        var act = () => sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload
            {
                ScalarValue = "99",
                IntegrationId = IntegrationId
            },
            null,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*not an allowed option*");
    }

    [Fact]
    public async Task SetAsync_fails_when_integration_id_missing()
    {
        var def = PlatformSettingTestDefinitions.CampusNexusProviderBackedSelectSample;
        var provider = CreateProviderWithCampuses(out _, []);
        var sut = CreateService(def, provider);

        var act = () => sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload { ScalarValue = "10" },
            null,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*IntegrationId is required*");
    }

    private static CampusNexusCampusesOptionProvider CreateProviderWithCampuses(
        out Mock<IRuntimeSlotResolver> slotResolver,
        IReadOnlyList<CampusODataRow> campuses)
    {
        var integrations = CreateIntegrationRepository(integrationExists: true);
        slotResolver = new Mock<IRuntimeSlotResolver>();

        var client = new Mock<ICampusNexusApiClient>();
        client.Setup(c => c.QueryAsync<CampusODataRow>(
                It.IsAny<ApiQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(campuses);
        client.Setup(c => c.Dispose());

        slotResolver.Setup(s => s.ResolveSlotAsync<ICampusNexusApiClient>(
                ProofCampusNexusStudentReviewConstants.ModuleId,
                CampusNexusCampusesOptionProvider.CampusNexusApiSlotName,
                IntegrationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        return new CampusNexusCampusesOptionProvider(integrations.Object, slotResolver.Object);
    }

    private static ConfigPlatformSettingsController CreateController(
        PlatformSettingDefinition def,
        CampusNexusCampusesOptionProvider provider)
    {
        var (principalContext, _, authorization) = PlatformSettingOptionProviderTestHarness.CreateDefaultAuthMocks();
        var integrations = CreateIntegrationRepository(integrationExists: true);
        var coordinator = PlatformSettingOptionProviderTestHarness.CreateCoordinator(
            provider,
            integrations: integrations);

        return new ConfigPlatformSettingsController(
            new Mock<IPlatformSettingsService>().Object,
            principalContext.Object,
            authorization.Object,
            new PlatformSettingOptionProviderTestHarness.SingleDefinitionRegistry(def),
            coordinator);
    }

    private static PlatformSettingsService CreateService(
        PlatformSettingDefinition def,
        CampusNexusCampusesOptionProvider provider)
    {
        var integrations = CreateIntegrationRepository(integrationExists: true);
        var coordinator = PlatformSettingOptionProviderTestHarness.CreateCoordinator(
            provider,
            integrations: integrations);

        var resolver = new Mock<IPlatformSettingsResolver>();
        resolver.Setup(x => x.ListResolvedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlatformSettingResolvedValue>());
        resolver.Setup(x => x.GetResolvedAsync(def.Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Resolved(def, "10"));

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
            principalContext.Object,
            audit.Object);
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
                        "Proof CNS",
                        ProofCampusNexusStudentReviewConstants.ModuleId,
                        null,
                        IntegrationState.Active,
                        null,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow));
            });

        return integrations;
    }

    private static PlatformSettingOptionProviderContext CreateIntegrationContext(string settingKey, string integrationId)
    {
        var actor = new Mock<IActor>();
        actor.Setup(x => x.Id).Returns(new ActorId("actor-1"));
        actor.Setup(x => x.Kind).Returns(ActorKind.Human);

        return new PlatformSettingOptionProviderContext
        {
            ContextKind = PlatformSettingOptionProviderContextKind.Integration,
            SettingKey = settingKey,
            IntegrationId = integrationId,
            Actor = actor.Object
        };
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
