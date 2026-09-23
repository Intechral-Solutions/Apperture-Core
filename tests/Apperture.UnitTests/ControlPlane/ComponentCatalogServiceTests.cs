using Apperture.ControlPlane.Security;
using Apperture.Core.Application.ComponentCatalog;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;
using Apperture.ModuleSdk.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Aggregation semantics of <see cref="ComponentCatalogService"/> over fake providers: filtering,
/// identity, authorization omission and the performance guards.
/// </summary>
public sealed class ComponentCatalogServiceTests
{
    private static ComponentSummary Conn(string id, string name, string? module = null, string? description = null) =>
        new(new ComponentKey(ComponentKinds.Connector, id), name, description,
            ComponentOrigins.FromSupplyingModuleId(module), module);

    private static ComponentSummary Ndef(string id, string name, string? module = null, string? description = null) =>
        new(new ComponentKey(ComponentKinds.NotificationDefinition, id), name, description,
            ComponentOrigins.FromSupplyingModuleId(module), module);

    private static (ComponentCatalogService Sut, FakeProvider Connectors, FakeProvider Definitions, Mock<IModuleWorkflowListingService> Modules) Build(
        bool connectorsDenied = false,
        bool definitionsDenied = false)
    {
        var connectors = new FakeProvider(ComponentKinds.Connector, connectorsDenied,
            Conn("Apperture.Platform.Sql", "Platform SQL", description: "Relational database"),
            Conn("Apperture.Platform.Sftp", "SFTP"),
            Conn("Fake.Module.Connector", "Fake Module Connector", module: "Fake.Module"));
        var definitions = new FakeProvider(ComponentKinds.NotificationDefinition, definitionsDenied,
            Ndef("platform.schedule.expected_run_missing", "Expected run missing"),
            Ndef("module.sample.helloworld.demo.completed", "Hello World demo completed", module: "Sample.HelloWorld"),
            Ndef("module.sample.fileimport.import.completed_with_errors", "Import completed with errors", module: "Sample.FileImport"));
        var modules = new Mock<IModuleWorkflowListingService>(MockBehavior.Strict);
        return (new ComponentCatalogService([connectors, definitions], modules.Object), connectors, definitions, modules);
    }

    // ---- composition ----------------------------------------------------------------------------

    [Fact]
    public async Task Query_returns_items_from_every_authorized_provider()
    {
        var (sut, _, _, _) = Build();

        var result = await sut.QueryAsync(new ComponentCatalogQuery());

        result.Items.Should().HaveCount(6);
        result.TotalMatching.Should().Be(6);
        result.Items.Should().Contain(i => i.Key.NativeId == "Apperture.Platform.Sql" && i.Origin == ComponentOrigins.System);
        result.Items.Should().Contain(i => i.Key.NativeId == "Fake.Module.Connector" && i.Origin == ComponentOrigins.Module && i.SupplyingModuleId == "Fake.Module");
    }

    [Fact]
    public async Task Items_are_sorted_by_display_name_then_kind_then_native_id()
    {
        var a = new FakeProvider(ComponentKinds.Connector, false,
            Conn("z-id", "Same name"), Conn("a-id", "Same name"));
        var b = new FakeProvider(ComponentKinds.NotificationDefinition, false,
            Ndef("m-id", "Same name"), Ndef("a-first", "alpha"));
        var sut = new ComponentCatalogService([a, b], new Mock<IModuleWorkflowListingService>().Object);

        var result = await sut.QueryAsync(new ComponentCatalogQuery());

        result.Items.Select(i => i.Key.ToString()).Should().Equal(
            "notification-definition:a-first",
            "connector:a-id",
            "connector:z-id",
            "notification-definition:m-id");
    }

    // ---- identity -------------------------------------------------------------------------------

    [Fact]
    public async Task Same_native_id_in_different_kinds_does_not_collide()
    {
        var a = new FakeProvider(ComponentKinds.Connector, false, Conn("shared.id", "Connector One"));
        var b = new FakeProvider(ComponentKinds.NotificationDefinition, false, Ndef("shared.id", "Definition One"));
        var sut = new ComponentCatalogService([a, b], new Mock<IModuleWorkflowListingService>().Object);

        (await sut.QueryAsync(new ComponentCatalogQuery())).Items.Should().HaveCount(2);
        (await sut.GetAsync(new ComponentKey(ComponentKinds.Connector, "shared.id")))!.Summary.DisplayName.Should().Be("Connector One");
        (await sut.GetAsync(new ComponentKey(ComponentKinds.NotificationDefinition, "shared.id")))!.Summary.DisplayName.Should().Be("Definition One");
    }

    [Fact]
    public async Task Native_id_is_preserved_verbatim_through_lookup()
    {
        var provider = new FakeProvider(ComponentKinds.Connector, false, Conn("Apperture.Platform.SQL", "Mixed case"));
        var sut = new ComponentCatalogService([provider], new Mock<IModuleWorkflowListingService>().Object);

        var detail = await sut.GetAsync(new ComponentKey(ComponentKinds.Connector, "Apperture.Platform.SQL"));

        detail!.Summary.Key.Should().Be(new ComponentKey(ComponentKinds.Connector, "Apperture.Platform.SQL"));
        provider.LastGetNativeId.Should().Be("Apperture.Platform.SQL");
    }

    [Fact]
    public async Task Get_unknown_kind_returns_null_without_consulting_any_provider()
    {
        var (sut, connectors, definitions, _) = Build();

        var detail = await sut.GetAsync(new ComponentKey("workflow", "x"));

        detail.Should().BeNull();
        connectors.GetCalls.Should().Be(0);
        definitions.GetCalls.Should().Be(0);
    }

    [Fact]
    public async Task Get_unknown_native_id_returns_null()
    {
        var (sut, _, _, _) = Build();

        (await sut.GetAsync(new ComponentKey(ComponentKinds.Connector, "nope"))).Should().BeNull();
    }

    // ---- filtering ------------------------------------------------------------------------------

    [Fact]
    public async Task Kind_filter_returns_only_that_kind_and_never_calls_the_other_provider()
    {
        var (sut, connectors, definitions, _) = Build();

        var result = await sut.QueryAsync(new ComponentCatalogQuery(Kind: ComponentKinds.Connector));

        result.Items.Should().OnlyContain(i => i.Key.Kind == ComponentKinds.Connector);
        connectors.ListCalls.Should().Be(1);
        definitions.ListCalls.Should().Be(0);
    }

    [Fact]
    public async Task Origin_filters_partition_the_set_exactly()
    {
        var (sut, _, _, _) = Build();

        var system = await sut.QueryAsync(new ComponentCatalogQuery(Origin: ComponentOrigins.System));
        var module = await sut.QueryAsync(new ComponentCatalogQuery(Origin: ComponentOrigins.Module));

        system.Items.Should().OnlyContain(i => i.Origin == ComponentOrigins.System);
        module.Items.Should().OnlyContain(i => i.Origin == ComponentOrigins.Module);
        (system.Items.Count + module.Items.Count).Should().Be(6);
        system.Items.Select(i => i.Key).Intersect(module.Items.Select(i => i.Key)).Should().BeEmpty();
    }

    [Fact]
    public async Task ModuleId_filter_returns_that_modules_components_only()
    {
        var (sut, _, _, _) = Build();

        var result = await sut.QueryAsync(new ComponentCatalogQuery(ModuleId: "Sample.HelloWorld"));

        result.Items.Should().ContainSingle().Which.Key.NativeId.Should().Be("module.sample.helloworld.demo.completed");
    }

    [Fact]
    public async Task Search_matches_display_name_native_id_and_description_case_insensitively()
    {
        var (sut, _, _, _) = Build();

        (await sut.QueryAsync(new ComponentCatalogQuery(Q: "platform sql"))).Items.Should().ContainSingle();
        (await sut.QueryAsync(new ComponentCatalogQuery(Q: "APPERTURE.PLATFORM.SFTP"))).Items.Should().ContainSingle(i => i.DisplayName == "SFTP");
        (await sut.QueryAsync(new ComponentCatalogQuery(Q: "RELATIONAL"))).Items.Should().ContainSingle(i => i.DisplayName == "Platform SQL");
        (await sut.QueryAsync(new ComponentCatalogQuery(Q: "  "))).Items.Should().HaveCount(6);
    }

    [Fact]
    public async Task Filters_compose_with_and_semantics()
    {
        var (sut, _, _, _) = Build();

        (await sut.QueryAsync(new ComponentCatalogQuery(Kind: ComponentKinds.Connector, Origin: ComponentOrigins.Module)))
            .Items.Should().ContainSingle().Which.Key.NativeId.Should().Be("Fake.Module.Connector");
        (await sut.QueryAsync(new ComponentCatalogQuery(Origin: ComponentOrigins.Module, ModuleId: "Sample.FileImport")))
            .Items.Should().ContainSingle();
        (await sut.QueryAsync(new ComponentCatalogQuery(ModuleId: "Sample.FileImport", Q: "hello")))
            .Items.Should().BeEmpty();
    }

    [Fact]
    public async Task System_origin_with_a_module_id_is_empty_not_an_error()
    {
        var (sut, _, _, _) = Build();

        var result = await sut.QueryAsync(new ComponentCatalogQuery(Origin: ComponentOrigins.System, ModuleId: "Sample.HelloWorld"));

        result.Items.Should().BeEmpty();
        result.TotalMatching.Should().Be(0);
    }

    [Fact]
    public async Task Take_truncates_items_but_not_total_matching()
    {
        var (sut, _, _, _) = Build();

        var result = await sut.QueryAsync(new ComponentCatalogQuery(Take: 2));

        result.Items.Should().HaveCount(2);
        result.TotalMatching.Should().Be(6);
    }

    [Theory]
    [InlineData(0, 6)]
    [InlineData(-5, 6)]
    [InlineData(2, 2)]
    [InlineData(100000, 6)]
    public async Task Take_is_bounded(int take, int expectedCount)
    {
        var (sut, _, _, _) = Build();

        var result = await sut.QueryAsync(new ComponentCatalogQuery(Take: take));

        result.Items.Should().HaveCount(expectedCount);
    }

    [Fact]
    public async Task Take_is_capped_at_the_documented_maximum()
    {
        var many = Enumerable.Range(0, 250).Select(i => Conn($"c{i:000}", $"Connector {i:000}")).ToArray();
        var sut = new ComponentCatalogService(
            [new FakeProvider(ComponentKinds.Connector, false, many)],
            new Mock<IModuleWorkflowListingService>().Object);

        var result = await sut.QueryAsync(new ComponentCatalogQuery(Take: 10_000));

        result.Items.Should().HaveCount(ComponentCatalogQuery.MaxTake);
        result.TotalMatching.Should().Be(250);
    }

    [Fact]
    public async Task Unknown_kind_or_origin_matches_nothing_without_error()
    {
        var (sut, connectors, definitions, _) = Build();

        var byKind = await sut.QueryAsync(new ComponentCatalogQuery(Kind: "workflow"));

        byKind.Items.Should().BeEmpty();
        connectors.ListCalls.Should().Be(0);
        definitions.ListCalls.Should().Be(0);

        var byOrigin = await sut.QueryAsync(new ComponentCatalogQuery(Origin: "user"));
        byOrigin.Items.Should().BeEmpty();
    }

    // ---- available kinds / modules --------------------------------------------------------------

    [Fact]
    public async Task AvailableModules_contains_only_real_supplying_module_ids()
    {
        var (sut, _, _, _) = Build();

        var result = await sut.QueryAsync(new ComponentCatalogQuery());

        result.AvailableModules.Select(m => m.ModuleId).Should().Equal(
            "Fake.Module", "Sample.FileImport", "Sample.HelloWorld");
    }

    [Fact]
    public async Task AvailableKinds_and_modules_are_not_narrowed_by_origin_module_or_search_filters()
    {
        var (sut, _, _, _) = Build();

        var result = await sut.QueryAsync(new ComponentCatalogQuery(Origin: ComponentOrigins.System, Q: "sql"));

        result.AvailableKinds.Should().Equal(ComponentKinds.Connector, ComponentKinds.NotificationDefinition);
        result.AvailableModules.Should().HaveCount(3);
    }

    [Fact]
    public async Task System_components_need_no_module_registry_and_no_module_appears_for_them()
    {
        var provider = new FakeProvider(ComponentKinds.Connector, false, Conn("Apperture.Platform.Sql", "Platform SQL"));
        var modules = new Mock<IModuleWorkflowListingService>(MockBehavior.Strict);
        var sut = new ComponentCatalogService([provider], modules.Object);

        var list = await sut.QueryAsync(new ComponentCatalogQuery());
        var detail = await sut.GetAsync(new ComponentKey(ComponentKinds.Connector, "Apperture.Platform.Sql"));

        list.AvailableModules.Should().BeEmpty();
        detail!.SupplyingModule.Should().BeNull();
        modules.VerifyNoOtherCalls();
    }

    // ---- authorization --------------------------------------------------------------------------

    [Fact]
    public async Task Denied_connector_family_is_omitted_from_items_kinds_and_total()
    {
        var (sut, _, _, _) = Build(connectorsDenied: true);

        var result = await sut.QueryAsync(new ComponentCatalogQuery());

        result.Items.Should().OnlyContain(i => i.Key.Kind == ComponentKinds.NotificationDefinition);
        result.AvailableKinds.Should().Equal(ComponentKinds.NotificationDefinition);
        result.TotalMatching.Should().Be(3);
        result.AvailableModules.Select(m => m.ModuleId).Should().NotContain("Fake.Module");
    }

    [Fact]
    public async Task Denied_notification_family_is_omitted_from_items_kinds_and_total()
    {
        var (sut, _, _, _) = Build(definitionsDenied: true);

        var result = await sut.QueryAsync(new ComponentCatalogQuery());

        result.Items.Should().OnlyContain(i => i.Key.Kind == ComponentKinds.Connector);
        result.AvailableKinds.Should().Equal(ComponentKinds.Connector);
        result.TotalMatching.Should().Be(3);
        result.AvailableModules.Select(m => m.ModuleId).Should().Equal("Fake.Module");
    }

    [Fact]
    public async Task Both_families_denied_throws_unauthorized()
    {
        var (sut, _, _, _) = Build(connectorsDenied: true, definitionsDenied: true);

        var act = async () => await sut.QueryAsync(new ComponentCatalogQuery());

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Authorized_but_empty_catalog_is_not_a_denial()
    {
        var sut = new ComponentCatalogService(
            [new FakeProvider(ComponentKinds.Connector, false)],
            new Mock<IModuleWorkflowListingService>().Object);

        var result = await sut.QueryAsync(new ComponentCatalogQuery());

        result.Items.Should().BeEmpty();
        result.AvailableKinds.Should().BeEmpty();
    }

    [Fact]
    public async Task Kind_filter_naming_a_denied_family_throws_unauthorized()
    {
        var (sut, _, _, _) = Build(connectorsDenied: true);

        var act = async () => await sut.QueryAsync(new ComponentCatalogQuery(Kind: ComponentKinds.Connector));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Detail_for_a_denied_family_returns_null_not_a_denial()
    {
        var (sut, _, _, _) = Build(connectorsDenied: true);

        var detail = await sut.GetAsync(new ComponentKey(ComponentKinds.Connector, "Apperture.Platform.Sql"));

        detail.Should().BeNull();
    }

    // ---- module metadata gating -----------------------------------------------------------------

    [Fact]
    public async Task Detail_without_module_configure_keeps_the_module_id_but_no_module_metadata()
    {
        var (sut, _, _, modules) = Build();
        modules.Setup(m => m.GetModuleAsync("Sample.HelloWorld", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException());

        var detail = await sut.GetAsync(new ComponentKey(ComponentKinds.NotificationDefinition, "module.sample.helloworld.demo.completed"));

        detail!.Summary.SupplyingModuleId.Should().Be("Sample.HelloWorld");
        detail.SupplyingModule.Should().BeNull();
    }

    [Fact]
    public async Task Detail_is_gated_by_the_real_module_configure_check_and_never_reaches_the_registry_when_denied()
    {
        PermissionDescriptor? evaluated = null;
        var actor = new Mock<IActor>();
        actor.SetupGet(a => a.Id).Returns(new ActorId("user:test"));
        var principal = new Mock<IPrincipalContext>();
        principal.Setup(p => p.GetCurrentActorAsync(It.IsAny<CancellationToken>())).ReturnsAsync(actor.Object);
        var authorization = new Mock<IAuthorizationEvaluationService>();
        authorization.Setup(a => a.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>()))
            .Callback<AuthorizationEvaluationContext, CancellationToken>((c, _) => evaluated = c.Permission)
            .ReturnsAsync(false);
        var registry = new Mock<IModuleRegistryService>(MockBehavior.Strict);
        var modules = new ModuleWorkflowListingService(principal.Object, authorization.Object, [], registry.Object);
        var provider = new FakeProvider(ComponentKinds.NotificationDefinition, false,
            Ndef("module.x", "Module definition", module: "Sample.HelloWorld"));
        var sut = new ComponentCatalogService([provider], modules);

        var detail = await sut.GetAsync(new ComponentKey(ComponentKinds.NotificationDefinition, "module.x"));

        detail!.Summary.SupplyingModuleId.Should().Be("Sample.HelloWorld");
        detail.SupplyingModule.Should().BeNull();
        evaluated!.Action.Should().Be(ActionConstants.ModuleConfigure);
        evaluated.Resource!.ResourceType.Should().Be(ResourceTypeConstants.Module);
        registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Detail_with_module_configure_resolves_module_attribution()
    {
        var (sut, _, _, modules) = Build();
        modules.Setup(m => m.GetModuleAsync("Sample.HelloWorld", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModuleEntry("Sample.HelloWorld", "Hello World", "1.2.3", ModuleLifecycleState.InstalledDisabled));

        var detail = await sut.GetAsync(new ComponentKey(ComponentKinds.NotificationDefinition, "module.sample.helloworld.demo.completed"));

        detail!.SupplyingModule.Should().Be(new ComponentModuleAttribution(
            "Sample.HelloWorld", "Hello World", "1.2.3", nameof(ModuleLifecycleState.InstalledDisabled)));
    }

    [Fact]
    public async Task Detail_with_an_unresolvable_module_id_keeps_the_raw_id_and_claims_no_module()
    {
        var (sut, _, _, modules) = Build();
        modules.Setup(m => m.GetModuleAsync("Sample.HelloWorld", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModuleCatalogEntry?)null);

        var detail = await sut.GetAsync(new ComponentKey(ComponentKinds.NotificationDefinition, "module.sample.helloworld.demo.completed"));

        detail.Should().NotBeNull();
        detail!.Summary.SupplyingModuleId.Should().Be("Sample.HelloWorld");
        detail.SupplyingModule.Should().BeNull();
    }

    // ---- performance guards ---------------------------------------------------------------------

    [Fact]
    public async Task List_never_touches_the_module_listing_service()
    {
        var (sut, _, _, modules) = Build();

        await sut.QueryAsync(new ComponentCatalogQuery());
        await sut.QueryAsync(new ComponentCatalogQuery(ModuleId: "Sample.HelloWorld"));

        modules.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task List_never_invokes_the_module_registry_through_the_real_listing_service()
    {
        var registry = new Mock<IModuleRegistryService>(MockBehavior.Strict);
        var modules = new ModuleWorkflowListingService(
            new Mock<IPrincipalContext>(MockBehavior.Strict).Object,
            new Mock<IAuthorizationEvaluationService>(MockBehavior.Strict).Object,
            [],
            registry.Object);
        var connectors = new FakeProvider(ComponentKinds.Connector, false, Conn("a", "A", module: "Some.Module"));
        var sut = new ComponentCatalogService([connectors], modules);

        var result = await sut.QueryAsync(new ComponentCatalogQuery(ModuleId: "Some.Module"));

        result.Items.Should().ContainSingle();
        registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Each_wanted_provider_is_listed_exactly_once_per_query()
    {
        var (sut, connectors, definitions, _) = Build();

        await sut.QueryAsync(new ComponentCatalogQuery(Q: "a", Origin: ComponentOrigins.Module, ModuleId: "Sample.HelloWorld"));

        connectors.ListCalls.Should().Be(1);
        definitions.ListCalls.Should().Be(1);
        connectors.GetCalls.Should().Be(0);
        definitions.GetCalls.Should().Be(0);
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static ModuleCatalogEntry ModuleEntry(string id, string name, string version, ModuleLifecycleState state) =>
        new(
            new ModuleDescriptor(id, name, version, null, null, null, []),
            state,
            Installed: true,
            Enabled: false,
            EffectivelyActive: false,
            ValidationIssues: []);

    private sealed class FakeProvider : IComponentCatalogProvider
    {
        private readonly bool _denied;
        private readonly IReadOnlyList<ComponentSummary> _items;

        public FakeProvider(string kind, bool denied, params ComponentSummary[] items)
        {
            Kind = kind;
            _denied = denied;
            _items = items;
        }

        public string Kind { get; }
        public int ListCalls { get; private set; }
        public int GetCalls { get; private set; }
        public string? LastGetNativeId { get; private set; }

        public ValueTask<IReadOnlyList<ComponentSummary>> ListAsync(CancellationToken cancellationToken = default)
        {
            ListCalls++;
            if (_denied)
                throw new UnauthorizedAccessException("denied");
            return ValueTask.FromResult(_items);
        }

        public ValueTask<ComponentDetail?> GetAsync(string nativeId, CancellationToken cancellationToken = default)
        {
            GetCalls++;
            LastGetNativeId = nativeId;
            if (_denied)
                throw new UnauthorizedAccessException("denied");
            var item = _items.FirstOrDefault(i => string.Equals(i.Key.NativeId, nativeId, StringComparison.Ordinal));
            return ValueTask.FromResult(item is null ? null : new ComponentDetail(item));
        }
    }
}
