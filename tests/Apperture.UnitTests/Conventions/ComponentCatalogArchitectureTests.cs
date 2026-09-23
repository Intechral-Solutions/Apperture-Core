using System.Reflection;
using System.Text.RegularExpressions;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.ComponentCatalog;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Conventions;

/// <summary>
/// Pins the Component catalog's structural invariants: it is a projection over the authorized family
/// read services, never over the raw registries/stores, and it introduces no persistence or permission.
/// </summary>
public sealed class ComponentCatalogArchitectureTests
{
    private static readonly string[] ProviderAndCatalogSources =
    [
        "src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ComponentCatalogService.cs",
        "src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ConnectorComponentCatalogProvider.cs",
        "src/Apperture.ControlPlane/Apperture.ControlPlane.Security/NotificationDefinitionComponentCatalogProvider.cs",
    ];

    private static readonly string[] BypassIdentifiers =
    [
        "IConnectorDefinitionRegistry",
        "INotificationDefinitionStore",
        "IModuleRegistryService",
        "AppertureDbContext",
        "IConnectorDefinitionContributor",
    ];

    [Fact]
    public void Connector_provider_depends_only_on_authorized_services()
    {
        ConstructorParameterTypes(typeof(ConnectorComponentCatalogProvider))
            .Should().Equal(typeof(IConnectorDefinitionCatalogReadService), typeof(IConnectionManagementService));
    }

    [Fact]
    public void Notification_definition_provider_depends_only_on_authorized_services_and_never_on_notification_instances()
    {
        ConstructorParameterTypes(typeof(NotificationDefinitionComponentCatalogProvider))
            .Should().Equal(typeof(INotificationDefinitionService), typeof(Apperture.Core.Application.Notifications.Subscriptions.INotificationSubscriptionService));
    }

    [Fact]
    public void Catalog_service_depends_on_providers_and_the_authorized_module_listing_service_only()
    {
        ConstructorParameterTypes(typeof(ComponentCatalogService))
            .Should().Equal(typeof(IEnumerable<IComponentCatalogProvider>), typeof(IModuleWorkflowListingService));
    }

    [Fact]
    public void Governance_registration_wires_exactly_two_providers_and_the_catalog()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddAppertureGovernance();

        services.Where(d => d.ServiceType == typeof(IComponentCatalogProvider))
            .Select(d => d.ImplementationType)
            .Should().BeEquivalentTo(
            [
                typeof(ConnectorComponentCatalogProvider),
                typeof(NotificationDefinitionComponentCatalogProvider),
            ]);
        services.Single(d => d.ServiceType == typeof(IComponentCatalog)).ImplementationType
            .Should().Be(typeof(ComponentCatalogService));
    }

    [Theory]
    [MemberData(nameof(ProviderAndCatalogSourceFiles))]
    public void Catalog_sources_never_name_a_raw_registry_store_or_db_context(string relativePath)
    {
        var source = StripComments(File.ReadAllText(RepoPath(relativePath)));

        foreach (var identifier in BypassIdentifiers)
            source.Should().NotContain(identifier, "{0} must reach its family only through the authorized read service", relativePath);
    }

    public static TheoryData<string> ProviderAndCatalogSourceFiles()
    {
        var data = new TheoryData<string>();
        foreach (var path in ProviderAndCatalogSources)
            data.Add(path);
        return data;
    }

    [Fact]
    public void Contracts_assembly_contains_no_type_named_exactly_Component()
    {
        // A bare `Component` collides with Blazor's ComponentBase / "Razor component" vocabulary.
        var offenders = Directory
            .EnumerateFiles(RepoPath("src"), "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
            .Where(f =>
                Path.GetFileNameWithoutExtension(f) == "Component"
                || Regex.IsMatch(
                    File.ReadAllText(f),
                    @"\b(class|record|struct|interface|enum)\s+Component\b(?!\w)"))
            .Select(f => Path.GetRelativePath(RepoPath("src"), f))
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void No_component_permission_or_action_exists()
    {
        var actionConstants = typeof(ActionConstants);
        var values = actionConstants
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string))
            .Select(f => (string?)f.GetValue(null))
            .ToList();

        values.Should().NotContain(v => v != null && v.StartsWith("component.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Persistence_has_no_component_entity_or_table()
    {
        var persistenceRoot = RepoPath("src/Apperture.Persistence");
        var offenders = Directory
            .EnumerateFiles(persistenceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => Regex.IsMatch(
                File.ReadAllText(f),
                @"DbSet<Component\w*>|class\s+Component\w*Entity\b|CreateTable\(\s*name:\s*""Components?""",
                RegexOptions.CultureInvariant))
            .Select(f => Path.GetRelativePath(persistenceRoot, f))
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void No_resource_carries_a_component_foreign_key_or_universal_component_interface()
    {
        var offenders = Directory
            .EnumerateFiles(RepoPath("src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\bComponentId\b|\bIComponentResource\b"))
            .Select(f => Path.GetRelativePath(RepoPath("src"), f))
            .ToList();

        offenders.Should().BeEmpty();
    }

    private static Type[] ConstructorParameterTypes(Type type) =>
        type.GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToArray();

    private static string StripComments(string content)
    {
        content = Regex.Replace(content, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(content, @"//.*$", " ", RegexOptions.Multiline);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return dir!.FullName;
    }

    private static string RepoPath(string relative) =>
        Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));
}
