using System.Reflection;
using Apperture.Core.Application.ComponentCatalog;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Client;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class ComponentsControllerTests
{
    private static ComponentSummary Summary(string kind, string id, string? module = null) =>
        new(new ComponentKey(kind, id), $"Name {id}", "desc", ComponentOrigins.FromSupplyingModuleId(module), module);

    private static ComponentCatalogResult Result(params ComponentSummary[] items) =>
        new(items, items.Select(i => i.Key.Kind).Distinct().ToList(),
            items.Where(i => i.SupplyingModuleId is not null).Select(i => new ComponentModuleRef(i.SupplyingModuleId!)).Distinct().ToList(),
            items.Length);

    private static (ComponentsController Controller, Mock<IComponentCatalog> Catalog) Build()
    {
        var catalog = new Mock<IComponentCatalog>(MockBehavior.Strict);
        return (new ComponentsController(catalog.Object), catalog);
    }

    // ---- list -----------------------------------------------------------------------------------

    [Fact]
    public async Task List_maps_the_catalog_result_to_wire_dtos()
    {
        var (controller, catalog) = Build();
        catalog.Setup(c => c.QueryAsync(It.IsAny<ComponentCatalogQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result(
                Summary(ComponentKinds.Connector, "Apperture.Platform.Sql"),
                Summary(ComponentKinds.NotificationDefinition, "module.x", "Sample.HelloWorld")));

        var action = await controller.List(null, null, null, null, 200, CancellationToken.None);

        var dto = action.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ComponentCatalogResultDto>().Subject;
        dto.TotalMatching.Should().Be(2);
        dto.AvailableKinds.Should().Equal(ComponentKinds.Connector, ComponentKinds.NotificationDefinition);
        dto.AvailableModules.Should().Equal(new ComponentModuleRefDto("Sample.HelloWorld"));
        dto.Items.Should().Contain(new ComponentSummaryDto(
            ComponentKinds.Connector, "Apperture.Platform.Sql", "Name Apperture.Platform.Sql", "desc", ComponentOrigins.System, null));
    }

    [Fact]
    public async Task List_passes_filters_through_to_the_catalog()
    {
        var (controller, catalog) = Build();
        ComponentCatalogQuery? captured = null;
        catalog.Setup(c => c.QueryAsync(It.IsAny<ComponentCatalogQuery>(), It.IsAny<CancellationToken>()))
            .Callback<ComponentCatalogQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(Result());

        await controller.List(ComponentKinds.Connector, ComponentOrigins.Module, "Fake.Module", "sql", 25, CancellationToken.None);

        captured.Should().Be(new ComponentCatalogQuery(ComponentKinds.Connector, ComponentOrigins.Module, "Fake.Module", "sql", 25));
    }

    [Theory]
    [InlineData("workflow", null)]
    [InlineData("Connector", null)]
    [InlineData(null, "user")]
    [InlineData(null, "System")]
    public async Task List_rejects_unknown_kind_or_origin_with_400(string? kind, string? origin)
    {
        var (controller, _) = Build();

        var action = await controller.List(kind, origin, null, null, 200, CancellationToken.None);

        action.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task List_when_every_consulted_family_is_denied_returns_403()
    {
        var (controller, catalog) = Build();
        catalog.Setup(c => c.QueryAsync(It.IsAny<ComponentCatalogQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException());

        var action = await controller.List(null, null, null, null, 200, CancellationToken.None);

        action.Result.Should().BeOfType<ForbidResult>();
    }

    // ---- detail ---------------------------------------------------------------------------------

    [Fact]
    public async Task Get_maps_a_connector_detail_with_only_the_connector_block()
    {
        var (controller, catalog) = Build();
        var key = new ComponentKey(ComponentKinds.Connector, "Apperture.Platform.Sql");
        catalog.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>())).ReturnsAsync(new ComponentDetail(
            Summary(ComponentKinds.Connector, "Apperture.Platform.Sql"),
            Connector: new ConnectorComponentFacts([new ConnectorComponentField("Host", "Host name", false, null, true)], true)));

        var action = await controller.Get(ComponentKinds.Connector, "Apperture.Platform.Sql", CancellationToken.None);

        var dto = action.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ComponentDetailDto>().Subject;
        dto.Kind.Should().Be(ComponentKinds.Connector);
        dto.NativeId.Should().Be("Apperture.Platform.Sql");
        dto.NotificationDefinition.Should().BeNull();
        dto.SupplyingModule.Should().BeNull();
        dto.Connector!.SupportsConnectionVerification.Should().BeTrue();
        dto.Connector.Fields.Should().ContainSingle(f => f.Key == "Host" && f.IsRequired);
    }

    [Fact]
    public async Task Get_maps_a_notification_definition_detail_with_module_attribution()
    {
        var (controller, catalog) = Build();
        var key = new ComponentKey(ComponentKinds.NotificationDefinition, "module.x");
        var updated = new DateTimeOffset(2026, 2, 3, 0, 0, 0, TimeSpan.Zero);
        catalog.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>())).ReturnsAsync(new ComponentDetail(
            Summary(ComponentKinds.NotificationDefinition, "module.x", "Sample.HelloWorld"),
            SupplyingModule: new ComponentModuleAttribution("Sample.HelloWorld", "Hello World", "1.0.0", "InstalledEnabled"),
            NotificationDefinition: new NotificationDefinitionComponentFacts("ev", "platformEvent", "module", "staticRuleMutableSubscription", "Warning", true, true, updated)));

        var action = await controller.Get(ComponentKinds.NotificationDefinition, "module.x", CancellationToken.None);

        var dto = action.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<ComponentDetailDto>().Subject;
        dto.Connector.Should().BeNull();
        dto.SupplyingModuleId.Should().Be("Sample.HelloWorld");
        dto.SupplyingModule.Should().Be(new ModuleAttributionDto("Sample.HelloWorld", "Hello World", "1.0.0", "InstalledEnabled"));
        dto.NotificationDefinition.Should().Be(new NotificationDefinitionComponentDetailDto(
            "ev", "platformEvent", "module", "staticRuleMutableSubscription", "Warning", true, true, updated));
    }

    [Fact]
    public async Task Get_unknown_kind_is_404_without_consulting_the_catalog()
    {
        var (controller, catalog) = Build();

        var action = await controller.Get("workflow", "x", CancellationToken.None);

        action.Result.Should().BeOfType<NotFoundResult>();
        catalog.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Get_missing_or_denied_component_is_404_never_403()
    {
        var (controller, catalog) = Build();
        catalog.Setup(c => c.GetAsync(It.IsAny<ComponentKey>(), It.IsAny<CancellationToken>())).ReturnsAsync((ComponentDetail?)null);

        var action = await controller.Get(ComponentKinds.Connector, "hidden", CancellationToken.None);

        action.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Get_a_dotted_native_id_reaches_the_catalog_verbatim()
    {
        var (controller, catalog) = Build();
        ComponentKey? captured = null;
        catalog.Setup(c => c.GetAsync(It.IsAny<ComponentKey>(), It.IsAny<CancellationToken>()))
            .Callback<ComponentKey, CancellationToken>((k, _) => captured = k)
            .ReturnsAsync((ComponentDetail?)null);

        await controller.Get(ComponentKinds.Connector, "Apperture.Platform.Sql", CancellationToken.None);

        captured.Should().Be(new ComponentKey(ComponentKinds.Connector, "Apperture.Platform.Sql"));
    }

    // ---- read-only surface ----------------------------------------------------------------------

    [Fact]
    public void Controller_exposes_only_GET_actions()
    {
        var actions = typeof(ComponentsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes().OfType<HttpMethodAttribute>().Any())
            .ToList();

        actions.Should().NotBeEmpty();
        actions.SelectMany(m => m.GetCustomAttributes().OfType<HttpMethodAttribute>())
            .SelectMany(a => a.HttpMethods)
            .Should().OnlyContain(verb => verb == "GET");
        typeof(ComponentsController).GetCustomAttributes().OfType<HttpMethodAttribute>().Should().BeEmpty();
    }

    [Fact]
    public void Controller_has_no_action_that_is_reachable_without_an_explicit_GET_verb()
    {
        var unverbed = typeof(ComponentsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.GetCustomAttributes().OfType<HttpMethodAttribute>().Any())
            .Where(m => !m.IsSpecialName);

        unverbed.Should().BeEmpty();
    }

    [Fact]
    public void Controller_is_authorized_and_routed_under_api_components()
    {
        typeof(ComponentsController).GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        typeof(ComponentsController).GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/components");
        typeof(ComponentsController).GetMethod(nameof(ComponentsController.Get))!
            .GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("{kind}/{nativeId}");
    }
}
