using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.UnitTests.Audit;
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

    // ---- the integration row lock (AUD-1.4 slice 3) ----

    [Fact]
    public async Task Set_for_an_integration_that_does_not_exist_is_a_not_found_that_stores_and_records_nothing()
    {
        var def = PlatformSettingTestDefinitions.IntegrationScopedSelectSample;
        var repo = new PlatformSettingRepositoryFake();
        var recorded = new AuditRecording();
        var service = CreateIntegrationSettingsService(
            def,
            repo,
            new TestIntegrationOptionsOptionProvider(),
            audit: recorded,
            integrations: CreateIntegrationRepository(integrationExists: false));

        var act = () => service.SetAsync(
            IntegrationId,
            def.Key,
            new PlatformSettingUpdatePayload { ScalarValue = $"{IntegrationId}:beta" },
            null);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        (await repo.GetScopedAsync(def.Key, PlatformSettingScopeConstants.IntegrationKind, IntegrationId)).Should().BeNull();
        recorded.Actions.Should().BeEmpty();
        recorded.Unit.Staged.Should().BeEmpty();
    }

    [Fact]
    public async Task Reset_for_an_integration_that_does_not_exist_is_a_not_found_even_when_an_override_remains()
    {
        var def = PlatformSettingTestDefinitions.IntegrationScopedSelectSample;
        var repo = new PlatformSettingRepositoryFake(new PlatformSettingValue
        {
            Key = def.Key,
            ScopeKind = PlatformSettingScopeConstants.IntegrationKind,
            ScopeId = IntegrationId,
            ValueJson = $"\"{IntegrationId}:beta\"",
            RowVersion = [1],
        });
        var recorded = new AuditRecording();
        var service = CreateIntegrationSettingsService(
            def,
            repo,
            new TestIntegrationOptionsOptionProvider(),
            audit: recorded,
            integrations: CreateIntegrationRepository(integrationExists: false));

        var act = () => service.ResetAsync(IntegrationId, def.Key);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        (await repo.GetScopedAsync(def.Key, PlatformSettingScopeConstants.IntegrationKind, IntegrationId)).Should().NotBeNull("nothing was removed");
        recorded.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task The_integration_is_locked_before_the_provider_is_asked_for_options_and_before_the_setting_is_read()
    {
        var def = PlatformSettingTestDefinitions.IntegrationScopedSelectSample;
        var steps = new List<string>();
        var integrations = CreateIntegrationRepository(integrationExists: true);
        integrations.Setup(x => x.GetByIdForUpdateAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>()))
            .Returns((IntegrationId id, CancellationToken _) =>
            {
                steps.Add("lock");
                return ValueTask.FromResult<DomainIntegration?>(new DomainIntegration(
                    id, "Test Integration", "mod-1", null, IntegrationState.Active, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            });
        var service = CreateIntegrationSettingsService(
            def,
            new OrderRecordingRepository(steps),
            new OrderRecordingOptionProvider(new TestIntegrationOptionsOptionProvider(), steps),
            integrations: integrations);

        await service.SetAsync(
            IntegrationId,
            def.Key,
            new PlatformSettingUpdatePayload { ScalarValue = $"{IntegrationId}:beta" },
            null);

        // The last read is the response, taken after the commit.
        steps.Should().Equal("lock", "options", "read", "write", "read");
    }

    private sealed class OrderRecordingRepository(List<string> steps) : PlatformSettingRepositoryFake
    {
        public override Task<PlatformSettingValue?> GetScopedAsync(string key, string scopeKind, string scopeId, CancellationToken cancellationToken = default)
        {
            steps.Add("read");
            return base.GetScopedAsync(key, scopeKind, scopeId, cancellationToken);
        }

        public override Task UpsertAsync(PlatformSettingValue value, byte[]? expectedRowVersion, CancellationToken cancellationToken = default)
        {
            steps.Add("write");
            return base.UpsertAsync(value, expectedRowVersion, cancellationToken);
        }
    }

    private sealed class OrderRecordingOptionProvider(IPlatformSettingOptionProvider inner, List<string> steps) : IPlatformSettingOptionProvider
    {
        public string ProviderKey => inner.ProviderKey;

        public Task<IReadOnlyList<PlatformSettingAllowedValue>> GetOptionsAsync(
            PlatformSettingDefinition definition,
            PlatformSettingOptionProviderContext context,
            CancellationToken cancellationToken = default)
        {
            steps.Add("options");
            return inner.GetOptionsAsync(definition, context, cancellationToken);
        }
    }

    [Fact]
    public async Task The_callers_authorization_runs_again_under_the_lock_and_a_refusal_changes_and_records_nothing()
    {
        var def = PlatformSettingTestDefinitions.IntegrationScopedSelectSample;
        var repo = new PlatformSettingRepositoryFake();
        var recorded = new AuditRecording();
        var service = CreateIntegrationSettingsService(def, repo, new TestIntegrationOptionsOptionProvider(), audit: recorded);
        var reauthorized = 0;

        var act = () => service.SetAsync(
            IntegrationId,
            def.Key,
            new PlatformSettingUpdatePayload { ScalarValue = $"{IntegrationId}:beta" },
            null,
            reauthorize: _ =>
            {
                reauthorized++;
                throw new UnauthorizedAccessException("moved");
            });

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        reauthorized.Should().Be(1);
        (await repo.GetScopedAsync(def.Key, PlatformSettingScopeConstants.IntegrationKind, IntegrationId)).Should().BeNull();
        recorded.Actions.Should().BeEmpty();
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
            new AuditRecording());

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
        var recorded = new AuditRecording();

        var service = CreateIntegrationSettingsService(
            def,
            new PlatformSettingRepositoryFake(),
            new TestIntegrationOptionsOptionProvider(),
            audit: recorded);

        await service.SetAsync(
            IntegrationId,
            def.Key,
            new PlatformSettingUpdatePayload { ScalarValue = $"{IntegrationId}:beta" },
            null);

        var set = recorded.Single(AuditActionNames.IntegrationSettingSet);
        set.Target!.Type.Should().Be(AuditTargetTypes.IntegrationSetting);
        set.Target.Id.Should().Be(AuditEvents.IntegrationSettingTargetId(IntegrationId, def.Key));
        set.Fields["integrationId"].Should().Be(IntegrationId);
        set.Fields["key"].Should().Be(def.Key);
        set.Fields["operation"].Should().Be("set");
        set.Fields["newValue"].Should().Be($"{IntegrationId}:beta");
    }

    [Fact]
    public async Task SetAsync_AuditsWithTheIntegrationsCurrentResourceContextId()
    {
        var def = PlatformSettingTestDefinitions.IntegrationScopedSelectSample;
        var recorded = new AuditRecording();

        var integrations = new Mock<IIntegrationRepository>();
        ValueTask<DomainIntegration?> Owned(IntegrationId id) => ValueTask.FromResult<DomainIntegration?>(
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
            });
        integrations.Setup(x => x.GetByIdAsync(new IntegrationId(IntegrationId), It.IsAny<CancellationToken>()))
            .Returns((IntegrationId id, CancellationToken _) => Owned(id));
        // The change takes the integration row lock and requires the row (AUD-1.4): the same row, read for update.
        integrations.Setup(x => x.GetByIdForUpdateAsync(new IntegrationId(IntegrationId), It.IsAny<CancellationToken>()))
            .Returns((IntegrationId id, CancellationToken _) => Owned(id));

        var service = CreateIntegrationSettingsService(
            def,
            new PlatformSettingRepositoryFake(),
            new TestIntegrationOptionsOptionProvider(),
            audit: recorded,
            integrations: integrations);

        await service.SetAsync(
            IntegrationId,
            def.Key,
            new PlatformSettingUpdatePayload { ScalarValue = $"{IntegrationId}:beta" },
            null);

        recorded.Single(AuditActionNames.IntegrationSettingSet).Related.ResourceContextId.Should().Be("app-a");
    }

    private static IntegrationSettingsService CreateIntegrationSettingsService(
        PlatformSettingDefinition def,
        IPlatformSettingValueRepository repo,
        IPlatformSettingOptionProvider provider,
        AuditRecording? audit = null,
        Mock<IIntegrationRepository>? integrations = null)
    {
        integrations ??= CreateIntegrationRepository(integrationExists: true);
        var coordinator = PlatformSettingOptionProviderTestHarness.CreateCoordinator(provider, integrations: integrations);
        var resolver = new IntegrationSettingsResolver(new TestIntegrationSettingDefinitionRegistry(), repo);
        var (principalContext, _, _) = PlatformSettingOptionProviderTestHarness.CreateDefaultAuthMocks();

        return new IntegrationSettingsService(
            new TestIntegrationSettingDefinitionRegistry(),
            repo,
            resolver,
            coordinator,
            new PlatformSettingSecretMaterialProvisioner(Mock.Of<ISecretStore>()),
            integrations.Object,
            audit ?? new AuditRecording());
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
            integrations.Object,
            new AuditRecording());

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
        ValueTask<DomainIntegration?> Find(IntegrationId id)
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
        }

        integrations.Setup(x => x.GetByIdAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>()))
            .Returns((IntegrationId id, CancellationToken _) => Find(id));
        // The change takes the integration row lock and requires the row (AUD-1.4): the same row, read for update.
        integrations.Setup(x => x.GetByIdForUpdateAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>()))
            .Returns((IntegrationId id, CancellationToken _) => Find(id));

        return integrations;
    }
}
