using System.Text.RegularExpressions;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Proof.CampusNexus.StudentReview;
using Sample.ApiPolling;
using Sample.FileImport;
using Sample.HelloWorld;
using Unisa.CampusNexus.PaymentPlans;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// The permission catalog carries product metadata for every entry: a name, a description, a category and a
/// sensitivity (IAM-10.1, acceptance A10.1.1, A10.1.3 and A10.1.5).
/// </summary>
public sealed class PermissionDefinitionCatalogTests
{
    private const string Standard = nameof(PermissionSensitivity.Standard);
    private const string Elevated = nameof(PermissionSensitivity.Elevated);
    private const string Critical = nameof(PermissionSensitivity.Critical);

    /// <summary>
    /// The approved classification of every platform permission, pinned by identity (<c>action@resourceType:scopeKey</c>).
    /// Adding a permission to the catalog fails the pin test until it is classified here.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> PinnedPlatformClassification = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Critical: changes who holds authority, replaces credentials or identity trust, reads or writes secrets, or introduces executable code.
        ["role.manage@Role:instance"] = Critical,
        ["permission.assign@Role:instance"] = Critical,
        ["role.assign@Role:instance"] = Critical,
        ["credential.manage@User:instance"] = Critical,
        ["auth.entra.manage@InstanceConfiguration:instance"] = Critical,
        ["secret.manage@SecretDescriptor:instance"] = Critical,
        ["module.install@Module:instance"] = Critical,
        ["relationship.manage@TrustRelationship:instance"] = Critical,

        // Elevated: changes configuration, runtime or alerting behaviour, deletes data, makes an outbound call with a stored credential, or reads audit.
        ["user.manage@User:instance"] = Elevated,
        ["auth.ui_policy.manage@InstanceConfiguration:instance"] = Elevated,
        ["auth.directory.search@InstanceConfiguration:instance"] = Elevated,
        ["platform.settings.manage@InstanceConfiguration:instance"] = Elevated,
        ["connection.create@Connection:instance"] = Elevated,
        ["connection.update@Connection:instance"] = Elevated,
        ["connection.delete@Connection:instance"] = Elevated,
        ["connection.browse@Connection:instance"] = Elevated,
        ["connection.verify@Connection:instance"] = Elevated,
        ["connector.manage@ConnectorDefinition:instance"] = Elevated,
        ["integration.create@Integration:instance"] = Elevated,
        ["integration.update@Integration:instance"] = Elevated,
        ["integration.delete@Integration:instance"] = Elevated,
        ["binding.create@Binding:instance"] = Elevated,
        ["binding.delete@Binding:instance"] = Elevated,
        ["binding.assign@Binding:instance"] = Elevated,
        ["binding.revoke@Binding:instance"] = Elevated,
        ["capability.register@CapabilityRegistration:instance"] = Elevated,
        ["mapping.manage@MappingDefinition:instance"] = Elevated,
        ["module.configure@Module:instance"] = Elevated,
        ["schedule.manage@Schedule:instance"] = Elevated,
        ["recovery.manage@Recovery:instance"] = Elevated,
        ["runtime.control.shutdown.request@RuntimeInstance:environment:local"] = Elevated,
        ["runtime.enroll.invitation.create@RuntimeInstance:environment:local"] = Elevated,
        ["resource_context.manage@ResourceContext:instance"] = Elevated,
        ["operational_team.manage@OperationalTeam:instance"] = Elevated,
        ["notification.definition.manage@NotificationDefinition:instance"] = Elevated,
        ["managed_contact.manage@ManagedContact:instance"] = Elevated,
        ["managed_contact.import@ManagedContact:instance"] = Elevated,
        ["audit.read@AuditPolicy:instance"] = Elevated,
        ["operation.execute@OperationDefinition:instance"] = Elevated,
        ["workflow.start@WorkflowInstance:instance"] = Elevated,

        // Standard: reads of operational data and routine authoring.
        ["user.read@User:instance"] = Standard,
        ["auth.configuration.read@InstanceConfiguration:instance"] = Standard,
        ["platform.settings.read@InstanceConfiguration:instance"] = Standard,
        ["connection.read@Connection:instance"] = Standard,
        ["connector.definition.read@ConnectorDefinition:instance"] = Standard,
        ["integration.read@Integration:instance"] = Standard,
        ["binding.read@Binding:instance"] = Standard,
        ["capability.consume@CapabilitySlot:instance"] = Standard,
        ["environment.read@Environment:environment:local"] = Standard,
        ["runtime.read@RuntimeInstance:environment:local"] = Standard,
        ["runtime.health.read@RuntimeHealth:instance"] = Standard,
        ["recovery.read@Recovery:instance"] = Standard,
        ["operation.read@OperationDefinition:instance"] = Standard,
        ["managed_contact.read@ManagedContact:instance"] = Standard,
        ["notification.definition.read@NotificationDefinition:instance"] = Standard,
    };

    private static readonly Regex PlanningLabel = new(
        @"\b(?:IAM|SEC|AUD|CTX|SDK|SWM|BDS|RR|PFB|LST|LFC|OPS|RCV|UIX|HSK|POL)-\d|\bEpic\s*\d|\b(?:OQ|ADR|[DPC])-\d+\b|\bmigration\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static InMemoryResourceDescriptorRegistry SeedPlatform()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        new PlatformPermissionSeederService(new FixedEnvironment()).Seed(registry);
        return registry;
    }

    private static IReadOnlyList<(string ModuleId, ModuleManifest Manifest)> InTreeManifests()
    {
        var manifests = new ModuleManifest[]
        {
            new HelloWorldModule().GetManifest(),
            new FileImportModule().GetManifest(),
            new ApiPollingModule().GetManifest(),
            new UnisaCampusNexusPaymentPlansModule().GetManifest(),
            new ProofCampusNexusStudentReviewModule().GetManifest(),
        };
        return manifests.Select(m => (m.Id, m)).ToList();
    }

    private static InMemoryResourceDescriptorRegistry SeedPlatformAndInTreeModules()
    {
        var registry = SeedPlatform();
        ModulePermissionRegistrar.Register(registry, InTreeManifests());
        return registry;
    }

    private static string Key(PermissionDefinition definition) => PermissionSetChange.Identity(definition.Identity);

    [Fact]
    public void Every_registered_permission_has_a_name_description_category_and_sensitivity()
    {
        var definitions = SeedPlatformAndInTreeModules().GetPermissionDefinitions();

        definitions.Should().NotBeEmpty();
        foreach (var d in definitions)
        {
            var key = Key(d);
            d.DisplayName.Should().NotBeNullOrWhiteSpace(key);
            d.DisplayName.Length.Should().BeLessThanOrEqualTo(80, key);
            d.Description.Should().NotBeNullOrWhiteSpace(key);
            d.Description.Length.Should().BeLessThanOrEqualTo(300, key);
            d.Description.Should().EndWith(".", $"{key}: a description is one sentence");
            Enum.IsDefined(d.Category).Should().BeTrue(key);
            Enum.IsDefined(d.Sensitivity).Should().BeTrue(key);
        }
    }

    [Fact]
    public void Display_names_are_unique_across_the_catalog()
    {
        var definitions = SeedPlatformAndInTreeModules().GetPermissionDefinitions();

        var duplicates = definitions
            .GroupBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(Key))}")
            .ToList();

        duplicates.Should().BeEmpty("an operator must be able to tell two permissions apart by name");
    }

    [Fact]
    public void Platform_classification_is_pinned_and_covers_the_whole_platform_catalog()
    {
        var platform = SeedPlatform().GetPermissionDefinitions();

        var actual = platform.ToDictionary(Key, d => d.Sensitivity.ToString(), StringComparer.Ordinal);

        actual.Keys.Except(PinnedPlatformClassification.Keys).Should().BeEmpty("every platform permission must be classified in the pin");
        PinnedPlatformClassification.Keys.Except(actual.Keys).Should().BeEmpty("the pin must not name a permission the catalog does not register");
        foreach (var (key, sensitivity) in PinnedPlatformClassification)
            actual[key].Should().Be(sensitivity, key);
    }

    [Fact]
    public void Critical_permissions_are_exactly_the_approved_set()
    {
        var critical = SeedPlatform().GetPermissionDefinitions()
            .Where(d => d.Sensitivity == PermissionSensitivity.Critical)
            .Select(d => d.Identity.Action)
            .OrderBy(a => a, StringComparer.Ordinal);

        critical.Should().Equal(
            "auth.entra.manage",
            "credential.manage",
            "module.install",
            "permission.assign",
            "relationship.manage",
            "role.assign",
            "role.manage",
            "secret.manage");
    }

    [Fact]
    public void Every_platform_permission_is_platform_origin_without_a_module_id()
    {
        foreach (var d in SeedPlatform().GetPermissionDefinitions())
        {
            d.Origin.Should().Be(PermissionOrigin.Platform, Key(d));
            d.ModuleId.Should().BeNull(Key(d));
        }
    }

    [Fact]
    public void Every_in_tree_module_permission_is_module_origin_with_its_module_id_and_is_elevated()
    {
        var registry = new InMemoryResourceDescriptorRegistry();
        var manifests = InTreeManifests();
        ModulePermissionRegistrar.Register(registry, manifests);

        var definitions = registry.GetPermissionDefinitions();

        definitions.Should().NotBeEmpty();
        foreach (var d in definitions)
        {
            d.Origin.Should().Be(PermissionOrigin.Module, Key(d));
            d.ModuleId.Should().NotBeNullOrWhiteSpace(Key(d));
            d.Identity.Resource!.ScopeKey.Should().Be(ScopeKeyConstants.Module(d.ModuleId!), Key(d));
            d.GrantedToPlatformAdministratorByDefault.Should().BeTrue("a module cannot decline the administrator default");
            d.Sensitivity.Should().Be(PermissionSensitivity.Elevated, $"{Key(d)}: running workflows and operations is Elevated");
        }

        foreach (var (moduleId, manifest) in manifests)
        {
            definitions.Where(d => d.ModuleId == moduleId).Should().NotBeEmpty($"{moduleId} declares permissions");
            manifest.Permissions.Should().NotBeNullOrEmpty(moduleId);
        }
    }

    [Fact]
    public void The_three_new_catalog_entries_have_the_approved_identities()
    {
        var byKey = SeedPlatform().GetPermissionDefinitions().ToDictionary(Key, StringComparer.Ordinal);

        byKey.Should().ContainKey("operational_team.manage@OperationalTeam:instance");
        byKey.Should().ContainKey("auth.directory.search@InstanceConfiguration:instance");
        byKey.Should().ContainKey("connection.verify@Connection:instance");
        foreach (var key in new[]
                 {
                     "operational_team.manage@OperationalTeam:instance",
                     "auth.directory.search@InstanceConfiguration:instance",
                     "connection.verify@Connection:instance",
                 })
        {
            byKey[key].Sensitivity.Should().Be(PermissionSensitivity.Elevated, key);
            byKey[key].RequiresStepUp.Should().BeFalse(key);
        }

        ActionConstants.OperationalTeamManage.Should().Be("operational_team.manage");
        ActionConstants.AuthDirectorySearch.Should().Be("auth.directory.search");
        ActionConstants.ConnectionVerify.Should().Be("connection.verify");
        ResourceTypeConstants.OperationalTeam.Should().Be("OperationalTeam");
    }

    [Fact]
    public void No_permission_requires_step_up_in_this_release()
    {
        // Only the later credential-recovery permission is flagged; the flag is declared metadata now and enforced later.
        SeedPlatformAndInTreeModules().GetPermissionDefinitions()
            .Where(d => d.RequiresStepUp)
            .Select(Key)
            .Should().BeEmpty();
    }

    [Fact]
    public void Every_permission_the_administrator_role_is_forced_to_hold_is_flagged_as_an_administrator_default()
    {
        var registry = SeedPlatformAndInTreeModules();
        var flagged = registry.GetPermissionDefinitions()
            .Where(d => d.GrantedToPlatformAdministratorByDefault)
            .Select(d => d.Identity)
            .ToHashSet();

        foreach (var grant in ApprovedNonCatalogStoredGrants.AllRepairGrants())
        {
            if (ApprovedNonCatalogStoredGrants.Exceptions.Contains(grant))
                continue;
            flagged.Should().Contain(grant, PermissionSetChange.Identity(grant));
        }
    }

    [Fact]
    public void Only_the_dormant_trust_relationship_permission_is_not_an_administrator_default()
    {
        // Nothing consumes the flag yet. The one platform permission with no consumer (federation was withdrawn) must not
        // be handed to the administrator by anything that later derives grants from the flag.
        SeedPlatform().GetPermissionDefinitions()
            .Where(d => !d.GrantedToPlatformAdministratorByDefault)
            .Select(Key)
            .Should().Equal("relationship.manage@TrustRelationship:instance");
    }

    [Fact]
    public void No_name_or_description_contains_a_planning_label_epic_or_migration_name()
    {
        var offenders = new List<string>();
        foreach (var d in SeedPlatformAndInTreeModules().GetPermissionDefinitions())
        {
            if (PlanningLabel.IsMatch(d.DisplayName))
                offenders.Add($"{Key(d)} name: {d.DisplayName}");
            if (PlanningLabel.IsMatch(d.Description))
                offenders.Add($"{Key(d)} description: {d.Description}");
        }

        offenders.Should().BeEmpty();
    }

    [Theory]
    [InlineData("IAM-10.1 role editor")]
    [InlineData("Created by SEC-2.3")]
    [InlineData("Epic 77 notifications")]
    [InlineData("see D-08")]
    [InlineData("added in a migration")]
    public void The_planning_label_guard_detects_what_it_is_meant_to_detect(string text)
    {
        PlanningLabel.IsMatch(text).Should().BeTrue();
    }

    [Theory]
    [InlineData("Manage roles")]
    [InlineData("Create, rename and delete custom roles.")]
    [InlineData("Search the Entra directory for people.")]
    public void The_planning_label_guard_accepts_product_text(string text)
    {
        PlanningLabel.IsMatch(text).Should().BeFalse();
    }

    [Fact]
    public void The_dead_governance_scaffolding_no_longer_exists()
    {
        var domain = typeof(PermissionDefinition).Assembly;
        domain.GetType("Apperture.Core.Domain.Governance.GovernedActionDescriptor").Should().BeNull();
        domain.GetType("Apperture.Core.Domain.Governance.ActionCategory").Should().BeNull();
        typeof(ModuleGovernedActionDeclaration).GetProperty("IsPrivileged").Should().BeNull();
        typeof(ModuleGovernedActionDeclaration).GetConstructors().SelectMany(c => c.GetParameters())
            .Should().NotContain(p => p.Name == "IsPrivileged");
    }

    [Fact]
    public void Every_permission_the_repair_forces_is_in_the_catalog_except_the_one_approved_exception()
    {
        var registry = SeedPlatformAndInTreeModules();
        var catalog = registry.GetPermissionDescriptors().ToHashSet();

        var outsideCatalog = ApprovedNonCatalogStoredGrants.AllRepairGrants()
            .Where(g => !catalog.Contains(g))
            .ToHashSet();

        outsideCatalog.Should().BeEquivalentTo(ApprovedNonCatalogStoredGrants.Exceptions);
        ApprovedNonCatalogStoredGrants.Exceptions.Should().HaveCount(1);
    }

    private sealed class FixedEnvironment : IEnvironmentContext
    {
        public string EnvironmentId => "local";
    }
}
