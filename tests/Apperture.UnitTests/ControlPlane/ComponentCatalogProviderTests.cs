using Apperture.ControlPlane.Security;
using Apperture.Core.Application.ComponentCatalog;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Notifications;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Provider-level composition: what is eligible, how origin is derived, and that each provider reaches
/// its family only through the authorized read service.
/// </summary>
public sealed class ComponentCatalogProviderTests
{
    // ---- connector provider ---------------------------------------------------------------------

    private static ConnectorDefinition Connector(string id, string name, string? moduleId = null, ConnectorCapabilities caps = ConnectorCapabilities.None) =>
        new(new ConnectorDefinitionId(id), name, moduleId, new Dictionary<string, string> { ["Host"] = "string" })
        {
            Description = $"{name} description",
            ConnectionFields =
            [
                new ConnectionFieldDescriptor("Host", "Host name", false, "Server host"),
                new ConnectionFieldDescriptor("Password", "Password", true),
            ],
            Capabilities = caps,
        };

    private static Mock<IConnectorDefinitionCatalogReadService> ConnectorService(params ConnectorDefinition[] definitions)
    {
        var mock = new Mock<IConnectorDefinitionCatalogReadService>(MockBehavior.Strict);
        mock.Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(definitions);
        mock.Setup(s => s.GetByIdAsync(It.IsAny<ConnectorDefinitionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConnectorDefinitionId id, CancellationToken _) =>
                definitions.FirstOrDefault(d => d.Id.Value == id.Value));
        return mock;
    }

    [Fact]
    public void Connector_provider_kind_is_the_connector_token()
    {
        new ConnectorComponentCatalogProvider(ConnectorService().Object).Kind.Should().Be(ComponentKinds.Connector);
    }

    [Fact]
    public async Task System_connector_projects_with_system_origin_and_no_module()
    {
        var sut = new ConnectorComponentCatalogProvider(ConnectorService(Connector("Apperture.Platform.Sql", "Platform SQL")).Object);

        var list = await sut.ListAsync();

        var item = list.Should().ContainSingle().Subject;
        item.Key.Should().Be(new ComponentKey(ComponentKinds.Connector, "Apperture.Platform.Sql"));
        item.DisplayName.Should().Be("Platform SQL");
        item.Description.Should().Be("Platform SQL description");
        item.Origin.Should().Be(ComponentOrigins.System);
        item.SupplyingModuleId.Should().BeNull();
    }

    [Fact]
    public async Task Module_connector_projects_with_module_origin_and_module_id()
    {
        var sut = new ConnectorComponentCatalogProvider(ConnectorService(Connector("Fake.Module.Connector", "Fake", moduleId: "Fake.Module")).Object);

        var item = (await sut.ListAsync()).Should().ContainSingle().Subject;

        item.Origin.Should().Be(ComponentOrigins.Module);
        item.SupplyingModuleId.Should().Be("Fake.Module");
    }

    [Fact]
    public async Task Connector_detail_carries_fields_and_only_the_verification_capability_fact()
    {
        var sut = new ConnectorComponentCatalogProvider(ConnectorService(
            Connector("Apperture.Platform.Sql", "Platform SQL",
                caps: ConnectorCapabilities.SupportsConnectionVerification | ConnectorCapabilities.BrokeredAccessCandidate)).Object);

        var detail = await sut.GetAsync("Apperture.Platform.Sql");

        detail.Should().NotBeNull();
        detail!.NotificationDefinition.Should().BeNull();
        detail.Connector!.SupportsConnectionVerification.Should().BeTrue();
        detail.Connector.Fields.Should().Equal(
            new ConnectorComponentField("Host", "Host name", false, "Server host", IsRequired: true),
            new ConnectorComponentField("Password", "Password", true, null, IsRequired: false));
    }

    [Fact]
    public async Task Connector_without_verification_capability_reports_false()
    {
        var sut = new ConnectorComponentCatalogProvider(ConnectorService(
            Connector("a", "A", caps: ConnectorCapabilities.BrokeredAccessCandidate)).Object);

        (await sut.GetAsync("a"))!.Connector!.SupportsConnectionVerification.Should().BeFalse();
    }

    [Fact]
    public async Task Connector_get_unknown_returns_null()
    {
        var sut = new ConnectorComponentCatalogProvider(ConnectorService(Connector("a", "A")).Object);

        (await sut.GetAsync("missing")).Should().BeNull();
    }

    [Fact]
    public async Task Connector_provider_propagates_family_denial()
    {
        var service = new Mock<IConnectorDefinitionCatalogReadService>();
        service.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException());
        service.Setup(s => s.GetByIdAsync(It.IsAny<ConnectorDefinitionId>(), It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException());
        var sut = new ConnectorComponentCatalogProvider(service.Object);

        await ((Func<Task>)(async () => await sut.ListAsync())).Should().ThrowAsync<UnauthorizedAccessException>();
        await ((Func<Task>)(async () => await sut.GetAsync("x"))).Should().ThrowAsync<UnauthorizedAccessException>();
    }

    // ---- notification definition provider -------------------------------------------------------

    private static NotificationDefinition Definition(
        string id,
        string origin,
        string scope,
        string? moduleId = null,
        string? integrationId = null,
        string name = "Definition") =>
        new(
            NotificationDefinitionId.From(id),
            name,
            $"{name} description",
            NotificationSourceKinds.PlatformEvent,
            origin,
            scope,
            NotificationDefinitionMutabilities.StaticRuleMutableSubscription,
            moduleId,
            integrationId,
            "platform.event",
            IsEnabled: true,
            DefaultSeverity: "Warning",
            RuleJson: "{}",
            TemplateJson: "{}",
            CreatedAtUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAtUtc: new DateTimeOffset(2026, 2, 3, 0, 0, 0, TimeSpan.Zero),
            CreatedByActorId: null,
            UpdatedByActorId: null,
            ToastEnabledByDefault: true);

    private static Mock<INotificationDefinitionService> DefinitionService(params NotificationDefinition[] rows)
    {
        var mock = new Mock<INotificationDefinitionService>(MockBehavior.Strict);
        mock.Setup(s => s.ListAsync(It.IsAny<NotificationDefinitionListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        mock.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => rows.FirstOrDefault(r => r.Id.Value == id));
        return mock;
    }

    [Fact]
    public void Definition_provider_kind_is_the_notification_definition_token()
    {
        new NotificationDefinitionComponentCatalogProvider(DefinitionService().Object)
            .Kind.Should().Be(ComponentKinds.NotificationDefinition);
    }

    [Fact]
    public async Task Definition_list_is_one_origin_set_query_without_scope_shortcut_or_per_row_lookups()
    {
        var service = DefinitionService(
            Definition("platform.a", NotificationDefinitionOrigins.PlatformBuiltIn, NotificationDefinitionScopes.Platform),
            Definition("module.b", NotificationDefinitionOrigins.ModuleBuiltIn, NotificationDefinitionScopes.Module, moduleId: "M.B"));
        var sut = new NotificationDefinitionComponentCatalogProvider(service.Object);

        await sut.ListAsync();

        service.Verify(s => s.ListAsync(
            It.Is<NotificationDefinitionListQuery>(q =>
                q.Origins != null
                && q.Origins.Count == 2
                && q.Origins.Contains(NotificationDefinitionOrigins.PlatformBuiltIn)
                && q.Origins.Contains(NotificationDefinitionOrigins.ModuleBuiltIn)
                && !q.SystemScopedOnly
                && q.IntegrationId == null
                && q.Origin == null
                && q.Take == 200),
            It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Platform_built_in_definition_is_a_system_component()
    {
        var sut = new NotificationDefinitionComponentCatalogProvider(DefinitionService(
            Definition("platform.schedule.expected_run_missing", NotificationDefinitionOrigins.PlatformBuiltIn, NotificationDefinitionScopes.Platform, name: "Expected run missing")).Object);

        var item = (await sut.ListAsync()).Should().ContainSingle().Subject;

        item.Key.Should().Be(new ComponentKey(ComponentKinds.NotificationDefinition, "platform.schedule.expected_run_missing"));
        item.DisplayName.Should().Be("Expected run missing");
        item.Origin.Should().Be(ComponentOrigins.System);
        item.SupplyingModuleId.Should().BeNull();
    }

    [Fact]
    public async Task Module_built_in_definition_is_a_module_component_with_its_module_id()
    {
        var sut = new NotificationDefinitionComponentCatalogProvider(DefinitionService(
            Definition("module.sample.helloworld.demo.completed", NotificationDefinitionOrigins.ModuleBuiltIn, NotificationDefinitionScopes.Module, moduleId: "Sample.HelloWorld")).Object);

        var item = (await sut.ListAsync()).Should().ContainSingle().Subject;

        item.Origin.Should().Be(ComponentOrigins.Module);
        item.SupplyingModuleId.Should().Be("Sample.HelloWorld");
    }

    [Fact]
    public async Task User_created_definitions_never_appear_even_when_the_store_returns_them()
    {
        var sut = new NotificationDefinitionComponentCatalogProvider(DefinitionService(
            Definition("ndef_1", NotificationDefinitionOrigins.UserCreated, NotificationDefinitionScopes.Integration, integrationId: "int-1"),
            Definition("ndef_2", NotificationDefinitionOrigins.UserCreated, NotificationDefinitionScopes.Platform),
            Definition("platform.a", NotificationDefinitionOrigins.PlatformBuiltIn, NotificationDefinitionScopes.Platform)).Object);

        var list = await sut.ListAsync();

        list.Select(i => i.Key.NativeId).Should().Equal("platform.a");
    }

    [Fact]
    public async Task Eligibility_follows_origin_not_scope()
    {
        var sut = new NotificationDefinitionComponentCatalogProvider(DefinitionService(
            // Built-in that happens to carry Integration scope: still eligible.
            Definition("module.odd", NotificationDefinitionOrigins.ModuleBuiltIn, NotificationDefinitionScopes.Integration, moduleId: "M.Odd"),
            // User-created that happens to carry Platform scope: still excluded.
            Definition("ndef_platform_scoped", NotificationDefinitionOrigins.UserCreated, NotificationDefinitionScopes.Platform)).Object);

        var list = await sut.ListAsync();

        list.Select(i => i.Key.NativeId).Should().Equal("module.odd");
    }

    [Fact]
    public async Task Platform_built_in_carrying_a_module_id_is_inconsistent_data_and_is_not_coerced()
    {
        var sut = new NotificationDefinitionComponentCatalogProvider(DefinitionService(
            Definition("platform.bad", NotificationDefinitionOrigins.PlatformBuiltIn, NotificationDefinitionScopes.Platform, moduleId: "Some.Module")).Object);

        await ((Func<Task>)(async () => await sut.ListAsync())).Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*platform.bad*");
    }

    [Fact]
    public async Task Module_built_in_without_a_module_id_is_inconsistent_data_and_is_not_coerced()
    {
        var sut = new NotificationDefinitionComponentCatalogProvider(DefinitionService(
            Definition("module.bad", NotificationDefinitionOrigins.ModuleBuiltIn, NotificationDefinitionScopes.Module, moduleId: null)).Object);

        await ((Func<Task>)(async () => await sut.ListAsync())).Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*module.bad*");
    }

    [Fact]
    public async Task Definition_detail_carries_family_facts()
    {
        var sut = new NotificationDefinitionComponentCatalogProvider(DefinitionService(
            Definition("module.sample.helloworld.demo.completed", NotificationDefinitionOrigins.ModuleBuiltIn, NotificationDefinitionScopes.Module, moduleId: "Sample.HelloWorld")).Object);

        var detail = await sut.GetAsync("module.sample.helloworld.demo.completed");

        detail!.Connector.Should().BeNull();
        detail.NotificationDefinition.Should().Be(new NotificationDefinitionComponentFacts(
            "platform.event",
            NotificationSourceKinds.PlatformEvent,
            NotificationDefinitionScopes.Module,
            NotificationDefinitionMutabilities.StaticRuleMutableSubscription,
            "Warning",
            IsEnabled: true,
            ToastEnabledByDefault: true,
            new DateTimeOffset(2026, 2, 3, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task Definition_detail_for_a_user_created_id_is_null()
    {
        var sut = new NotificationDefinitionComponentCatalogProvider(DefinitionService(
            Definition("ndef_1", NotificationDefinitionOrigins.UserCreated, NotificationDefinitionScopes.Platform)).Object);

        (await sut.GetAsync("ndef_1")).Should().BeNull();
    }

    [Fact]
    public async Task Definition_detail_for_an_orphaned_user_definition_is_null_not_an_error()
    {
        var service = new Mock<INotificationDefinitionService>();
        service.Setup(s => s.GetAsync("ndef_orphan", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Integration int-gone not found."));
        var sut = new NotificationDefinitionComponentCatalogProvider(service.Object);

        (await sut.GetAsync("ndef_orphan")).Should().BeNull();
    }

    [Fact]
    public async Task Definition_detail_unknown_id_is_null()
    {
        var sut = new NotificationDefinitionComponentCatalogProvider(DefinitionService().Object);

        (await sut.GetAsync("missing")).Should().BeNull();
    }

    [Fact]
    public async Task Definition_provider_propagates_family_denial()
    {
        var service = new Mock<INotificationDefinitionService>();
        service.Setup(s => s.ListAsync(It.IsAny<NotificationDefinitionListQuery>(), It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException());
        service.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException());
        var sut = new NotificationDefinitionComponentCatalogProvider(service.Object);

        await ((Func<Task>)(async () => await sut.ListAsync())).Should().ThrowAsync<UnauthorizedAccessException>();
        await ((Func<Task>)(async () => await sut.GetAsync("x"))).Should().ThrowAsync<UnauthorizedAccessException>();
    }

    // ---- seeded-state composition ---------------------------------------------------------------

    [Fact]
    public async Task Seeded_state_composes_three_system_connectors_and_four_definitions()
    {
        var connectors = new ConnectorComponentCatalogProvider(ConnectorService(
            Connector(WellKnownConnectorDefinitionIds.PlatformSql.Value, "Platform SQL"),
            Connector(WellKnownConnectorDefinitionIds.PlatformSftp.Value, "SFTP"),
            Connector(WellKnownConnectorDefinitionIds.PlatformCampusNexus.Value, "Campus Nexus")).Object);
        var definitions = new NotificationDefinitionComponentCatalogProvider(DefinitionService(
            Definition(NotificationDefinitionIds.PlatformScheduleExpectedRunMissing, NotificationDefinitionOrigins.PlatformBuiltIn, NotificationDefinitionScopes.Platform),
            Definition(NotificationDefinitionIds.ModuleSampleHelloWorldDemoCompleted, NotificationDefinitionOrigins.ModuleBuiltIn, NotificationDefinitionScopes.Module, moduleId: "Sample.HelloWorld"),
            Definition(NotificationDefinitionIds.ModuleSampleFileImportCompletedWithErrors, NotificationDefinitionOrigins.ModuleBuiltIn, NotificationDefinitionScopes.Module, moduleId: "Sample.FileImport"),
            Definition(NotificationDefinitionIds.ModuleSampleApiPollingSyncCompletedWithErrors, NotificationDefinitionOrigins.ModuleBuiltIn, NotificationDefinitionScopes.Module, moduleId: "Sample.ApiPolling"),
            Definition("ndef_user", NotificationDefinitionOrigins.UserCreated, NotificationDefinitionScopes.Integration, integrationId: "int-1")).Object);
        var sut = new ComponentCatalogService([connectors, definitions], new Mock<IModuleWorkflowListingService>().Object);

        var result = await sut.QueryAsync(new ComponentCatalogQuery());

        result.TotalMatching.Should().Be(7);
        result.Items.Count(i => i.Origin == ComponentOrigins.System).Should().Be(4);
        result.Items.Count(i => i.Origin == ComponentOrigins.Module).Should().Be(3);
        result.AvailableModules.Select(m => m.ModuleId).Should().Equal("Sample.ApiPolling", "Sample.FileImport", "Sample.HelloWorld");
    }
}
