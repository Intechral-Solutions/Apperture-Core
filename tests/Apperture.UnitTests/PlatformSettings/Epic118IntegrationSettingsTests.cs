using Apperture.ControlPlane.Security;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
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

public sealed class Epic118IntegrationSettingsTests
{
    private const string IntegrationId = "int-test-001";

    [Fact]
    public async Task Resolver_uses_integration_db_value_with_database_source()
    {
        var def = PlatformSettingTestDefinitions.IntegrationScopedSelectSample;
        var repo = new PlatformSettingRepositoryFake(new PlatformSettingValue
        {
            Key = def.Key,
            ScopeKind = PlatformSettingScopeConstants.IntegrationKind,
            ScopeId = IntegrationId,
            ValueJson = $"\"{IntegrationId}:beta\""
        });

        var resolver = new IntegrationSettingsResolver(new TestIntegrationSettingDefinitionRegistry(), repo);
        var resolved = await resolver.GetResolvedAsync(IntegrationId, def.Key);

        resolved!.Source.Should().Be(PlatformSettingValueSource.Database);
        resolved.CurrentValue.Should().Be($"{IntegrationId}:beta");
        resolved.ValueScopeKind.Should().Be(PlatformSettingScopeConstants.IntegrationKind);
        resolved.ValueScopeId.Should().Be(IntegrationId);
    }

    [Fact]
    public async Task Resolver_falls_back_to_definition_default_when_db_missing()
    {
        var def = PlatformSettingTestDefinitions.IntegrationScopedSelectSample;
        var resolver = new IntegrationSettingsResolver(
            new TestIntegrationSettingDefinitionRegistry(),
            new PlatformSettingRepositoryFake());

        var resolved = await resolver.GetResolvedAsync(IntegrationId, def.Key);

        resolved!.Source.Should().Be(PlatformSettingValueSource.DefinitionDefault);
        resolved.CurrentValue.Should().Be("int-test:alpha");
    }

    [Fact]
    public async Task Reset_deletes_only_integration_scoped_row()
    {
        var def = PlatformSettingTestDefinitions.IntegrationScopedSelectSample;
        var repo = new PlatformSettingRepositoryFake(
            new PlatformSettingValue
            {
                Key = def.Key,
                ScopeKind = PlatformSettingScopeConstants.IntegrationKind,
                ScopeId = IntegrationId,
                ValueJson = $"\"{IntegrationId}:beta\""
            },
            new PlatformSettingValue
            {
                Key = PlatformSettingKeys.PlatformEventsEnabled,
                ScopeKind = PlatformSettingScopeConstants.InstanceKind,
                ScopeId = PlatformSettingScopeConstants.InstanceId,
                ValueJson = "true"
            });

        var service = CreateIntegrationSettingsService(def, repo, new TestIntegrationOptionsOptionProvider());
        var reset = await service.ResetAsync(IntegrationId, def.Key);

        reset!.Source.Should().Be(PlatformSettingValueSource.DefinitionDefault);
        (await repo.GetScopedAsync(
            def.Key,
            PlatformSettingScopeConstants.IntegrationKind,
            IntegrationId)).Should().BeNull();
        (await repo.GetByKeyAsync(PlatformSettingKeys.PlatformEventsEnabled)).Should().NotBeNull();
    }

    [Fact]
    public async Task Set_persists_integration_scoped_value()
    {
        var def = PlatformSettingTestDefinitions.IntegrationScopedSelectSample;
        var repo = new PlatformSettingRepositoryFake();
        var provider = new TestIntegrationOptionsOptionProvider();
        var service = CreateIntegrationSettingsService(def, repo, provider);

        var updated = await service.SetAsync(
            IntegrationId,
            def.Key,
            new PlatformSettingUpdatePayload { ScalarValue = $"{IntegrationId}:beta" },
            null);

        updated.Source.Should().Be(PlatformSettingValueSource.Database);
        var stored = await repo.GetScopedAsync(
            def.Key,
            PlatformSettingScopeConstants.IntegrationKind,
            IntegrationId);
        stored!.ValueJson.Should().Be($"\"{IntegrationId}:beta\"");
    }

    [Fact]
    public async Task Set_rejects_invalid_provider_value()
    {
        var def = PlatformSettingTestDefinitions.IntegrationScopedSelectSample;
        var service = CreateIntegrationSettingsService(
            def,
            new PlatformSettingRepositoryFake(),
            new TestIntegrationOptionsOptionProvider());

        var act = () => service.SetAsync(
            IntegrationId,
            def.Key,
            new PlatformSettingUpdatePayload { ScalarValue = "not-allowed" },
            null);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Platform_service_rejects_integration_only_definition()
    {
        var def = PlatformSettingTestDefinitions.IntegrationScopedSelectSample;
        var platformService = new PlatformSettingsService(
            new PlatformSettingOptionProviderTestHarness.SingleDefinitionRegistry(def),
            new PlatformSettingRepositoryFake(),
            new Mock<IPlatformSettingsResolver>().Object,
            PlatformSettingOptionProviderTestHarness.CreateCoordinator(new TestIntegrationOptionsOptionProvider()),
            new PlatformSettingSecretMaterialProvisioner(Mock.Of<ISecretStore>()),
            new Mock<IPrincipalContext>().Object,
            new Mock<IAuditService>().Object);

        var act = () => platformService.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload { ScalarValue = "int-test:alpha" },
            null);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*instance value scope*");
    }

    [Fact]
    public async Task Options_endpoint_uses_route_integration_id_without_query()
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

        var def = PlatformSettingTestDefinitions.IntegrationScopedSelectSample;
        var controller = CreateIntegrationSettingsController(def, provider.Object);

        var result = await controller.Options(IntegrationId, def.Key, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        captured.Should().NotBeNull();
        captured!.IntegrationId.Should().Be(IntegrationId);
    }

    [Fact]
    public async Task Get_returns_not_found_when_integration_missing()
    {
        var def = PlatformSettingTestDefinitions.IntegrationScopedSelectSample;
        var controller = CreateIntegrationSettingsController(
            def,
            new TestIntegrationOptionsOptionProvider(),
            integrationExists: false);

        var result = await controller.Get(IntegrationId, def.Key, CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Get_returns_forbidden_when_integration_read_denied()
    {
        var def = PlatformSettingTestDefinitions.IntegrationScopedSelectSample;
        var (_, _, auth) = PlatformSettingOptionProviderTestHarness.CreateDefaultAuthMocks(allowAuthorization: false);
        var controller = CreateIntegrationSettingsController(
            def,
            new TestIntegrationOptionsOptionProvider(),
            authorization: auth);

        var result = await controller.Get(IntegrationId, def.Key, CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Get_returns_not_found_for_instance_only_key()
    {
        var def = PlatformSettingTestDefinitions.IntegrationProviderBackedSelectSample;
        var controller = CreateIntegrationSettingsController(def, new TestIntegrationOptionsOptionProvider());

        var result = await controller.Get(IntegrationId, def.Key, CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Set_writes_integration_settings_audit()
    {
        var def = PlatformSettingTestDefinitions.IntegrationScopedSelectSample;
        var audit = new Mock<IAuditService>();
        AuditRecord? captured = null;
        audit.Setup(x => x.AppendAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AuditRecord, CancellationToken>((record, _) => captured = record)
            .Returns(ValueTask.CompletedTask);

        var service = CreateIntegrationSettingsService(
            def,
            new PlatformSettingRepositoryFake(),
            new TestIntegrationOptionsOptionProvider(),
            audit: audit);

        await service.SetAsync(
            IntegrationId,
            def.Key,
            new PlatformSettingUpdatePayload { ScalarValue = $"{IntegrationId}:beta" },
            null);

        captured.Should().NotBeNull();
        captured!.Action.Should().Be(IntegrationSettingsAuditActionNames.Set);
        captured.Resource.ResourceType.Should().Be(ResourceTypeConstants.Integration);
        captured.Resource.ScopeKey.Should().Be(IntegrationId);
        captured.Details.Should().Contain(PlatformSettingScopeConstants.IntegrationKind);
    }

    [Fact]
    public async Task SetAsync_AuditsWithTheIntegrationsCurrentResourceContextId()
    {
        var def = PlatformSettingTestDefinitions.IntegrationScopedSelectSample;
        var audit = new Mock<IAuditService>();
        string? capturedContext = null;
        audit.Setup(x => x.AppendAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AuditRecord, CancellationToken>((record, _) => capturedContext = record.ResourceContextId)
            .Returns(ValueTask.CompletedTask);

        var integrations = new Mock<IIntegrationRepository>();
        integrations.Setup(x => x.GetByIdAsync(new IntegrationId(IntegrationId), It.IsAny<CancellationToken>()))
            .Returns((IntegrationId id, CancellationToken _) => ValueTask.FromResult<DomainIntegration?>(
                new DomainIntegration(
                    id,
                    "Test Integration",
                    "mod-1",
                    null,
                    IntegrationState.Active,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow)
                {
                    ResourceContextId = new ResourceContextId("app-a")
                }));

        var service = CreateIntegrationSettingsService(
            def,
            new PlatformSettingRepositoryFake(),
            new TestIntegrationOptionsOptionProvider(),
            audit: audit,
            integrations: integrations);

        await service.SetAsync(
            IntegrationId,
            def.Key,
            new PlatformSettingUpdatePayload { ScalarValue = $"{IntegrationId}:beta" },
            null);

        capturedContext.Should().Be("app-a");
    }

    private static IntegrationSettingsService CreateIntegrationSettingsService(
        PlatformSettingDefinition def,
        IPlatformSettingValueRepository repo,
        IPlatformSettingOptionProvider provider,
        Mock<IAuditService>? audit = null,
        Mock<IIntegrationRepository>? integrations = null)
    {
        integrations ??= CreateIntegrationRepository(integrationExists: true);
        var coordinator = PlatformSettingOptionProviderTestHarness.CreateCoordinator(provider, integrations: integrations);
        var resolver = new IntegrationSettingsResolver(new TestIntegrationSettingDefinitionRegistry(), repo);
        var (principalContext, _, _) = PlatformSettingOptionProviderTestHarness.CreateDefaultAuthMocks();
        var auditMock = audit ?? new Mock<IAuditService>();

        return new IntegrationSettingsService(
            new TestIntegrationSettingDefinitionRegistry(),
            repo,
            resolver,
            coordinator,
            new PlatformSettingSecretMaterialProvisioner(Mock.Of<ISecretStore>()),
            principalContext.Object,
            auditMock.Object,
            integrations.Object);
    }

    private static IntegrationSettingsController CreateIntegrationSettingsController(
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

        var repo = new PlatformSettingRepositoryFake();
        var resolver = new IntegrationSettingsResolver(new TestIntegrationSettingDefinitionRegistry(), repo);
        var (principal, _, _) = PlatformSettingOptionProviderTestHarness.CreateDefaultAuthMocks();
        var service = new IntegrationSettingsService(
            new TestIntegrationSettingDefinitionRegistry(),
            repo,
            resolver,
            coordinator,
            new PlatformSettingSecretMaterialProvisioner(Mock.Of<ISecretStore>()),
            principal.Object,
            new Mock<IAuditService>().Object,
            integrations.Object);

        var integrationAuth = new IntegrationSettingsAuthorization(
            principalContext.Object,
            auth.Object,
            integrations.Object);

        return new IntegrationSettingsController(
            service,
            new TestIntegrationSettingDefinitionRegistry(),
            integrations.Object,
            integrationAuth,
            coordinator);
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
}
