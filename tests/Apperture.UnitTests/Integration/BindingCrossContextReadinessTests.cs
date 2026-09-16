using Apperture.Core.Application.Integration;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.ModuleSdk.Abstractions;
using Apperture.ModuleSdk.Integration.Configuration;
using Apperture.Persistence.SqlServer;
using Apperture.UnitTests.ResourceContexts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Integration;

/// <summary>
/// Design gate §8.7 "Defense in depth": a cross-context-invalid binding should be unreachable through
/// the normal APIs after CTX-3B Plans 1-2, but if one exists anyway (old data, a direct DB edit, a bug),
/// the Integration must be reported not runnable. Seeds the invalid row directly, bypassing every
/// service, exactly as the design gate specifies.
/// </summary>
public sealed class BindingCrossContextReadinessTests
{
    [Fact]
    public async Task Seeded_cross_context_binding_makes_integration_not_runnable()
    {
        using var db = new AppertureDbContext(
            new DbContextOptionsBuilder<AppertureDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        db.ResourceContexts.Add(new ResourceContextEntity
        {
            Id = ResourceContextIds.SharedValue, Name = "Shared", Slug = "shared",
            Kind = ResourceContextKinds.Shared, State = ResourceContextStates.Active, IsReserved = true,
            CreatedAtUtc = DateTimeOffset.UnixEpoch, UpdatedAtUtc = DateTimeOffset.UnixEpoch
        });
        var resourceContexts = new ResourceContextService(new SqlResourceContextRepository(db), TimeProvider.System);
        var appA = await resourceContexts.CreateApplicationContextAsync("App A");
        var appB = await resourceContexts.CreateApplicationContextAsync("App B");

        db.Integrations.Add(new IntegrationEntity
        {
            Id = "xctx-int", DisplayName = "Cross Context Integration", ModuleId = "Sample.Mod",
            ResourceContextId = appA.Id.Value, State = 0, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        db.Connections.Add(new ConnectionEntity
        {
            Id = "xctx-conn", ConnectorDefinitionId = "test.connector", Name = "Cross Context Connection",
            ConfigJson = "{}", SecretRefsJson = "{}", ResourceContextId = appB.Id.Value, State = 0
        });
        db.Bindings.Add(new BindingEntity
        {
            Id = "xctx-binding", IntegrationId = "xctx-int", ModuleId = "Sample.Mod", SlotName = "primary",
            ContractType = "test.contract", ConnectionId = "xctx-conn", State = 0
        });
        await db.SaveChangesAsync();

        var integrationRepository = new SqlIntegrationRepository(db);
        var connectionRepository = new SqlConnectionRepository(db);
        var bindingRepository = new SqlBindingRepository(db);
        var evaluator = new IntegrationDispatchReadinessEvaluator(
            integrationRepository,
            bindingRepository,
            new SqlScheduleRepository(db),
            EmptySlotRegistry(),
            connectionRepository,
            new BindingEligibilityService(connectionRepository, new PermissiveRegistry()),
            new AdvisoryConnectionVerificationReadinessPolicy(),
            new IntegrationConfigurationRegistry(Array.Empty<ModuleIntegrationConfigurationBinding>()),
            NoOpConfigurationEvidenceBuilder(connectionRepository),
            TimeProvider.System);

        var projection = await evaluator.EvaluateForDispatchAsync(new IntegrationId("xctx-int"), procedureId: null);

        projection!.IsRunnable.Should().BeFalse();
        var issue = projection.Issues.Should().ContainSingle(i => i.Code == IntegrationReadinessIssueCodes.BindingCrossContext).Subject;
        issue.Message.Should().Contain("Remove this binding",
            "the message must tell the operator what to do, because this plan can land before Plan 5's move UI exists");
    }

    [Fact]
    public async Task Seeded_cross_context_binding_makes_integration_not_runnable_via_SDK6C_configuration_path()
    {
        // Global constraint: the check must apply to every binding with a ConnectionId "in both the SDK-6C
        // configuration-evidence path and the legacy slot-binding path -- do not scope it to only one
        // branch." The two tests above both use an empty IntegrationConfigurationRegistry, which always
        // takes the legacy branch. This test registers a real ModuleIntegrationConfiguration so
        // TryGetModuleConfiguration returns true and EvaluateAsync takes the configuration-evidence branch
        // instead, proving the backstop isn't accidentally scoped to the legacy branch only.
        using var db = new AppertureDbContext(
            new DbContextOptionsBuilder<AppertureDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        db.ResourceContexts.Add(new ResourceContextEntity
        {
            Id = ResourceContextIds.SharedValue, Name = "Shared", Slug = "shared",
            Kind = ResourceContextKinds.Shared, State = ResourceContextStates.Active, IsReserved = true,
            CreatedAtUtc = DateTimeOffset.UnixEpoch, UpdatedAtUtc = DateTimeOffset.UnixEpoch
        });
        var resourceContexts = new ResourceContextService(new SqlResourceContextRepository(db), TimeProvider.System);
        var appA = await resourceContexts.CreateApplicationContextAsync("Configured App A");
        var appB = await resourceContexts.CreateApplicationContextAsync("Configured App B");

        const string moduleId = "Sample.Mod.Configured";
        db.Integrations.Add(new IntegrationEntity
        {
            Id = "xctx-int-cfg", DisplayName = "Cross Context Integration (SDK-6C)", ModuleId = moduleId,
            ResourceContextId = appA.Id.Value, State = 0, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        db.Connections.Add(new ConnectionEntity
        {
            Id = "xctx-conn-cfg", ConnectorDefinitionId = "test.connector", Name = "Cross Context Connection (SDK-6C)",
            ConfigJson = "{}", SecretRefsJson = "{}", ResourceContextId = appB.Id.Value, State = 0
        });
        db.Bindings.Add(new BindingEntity
        {
            Id = "xctx-binding-cfg", IntegrationId = "xctx-int-cfg", ModuleId = moduleId, SlotName = "primary",
            ContractType = "test.contract", ConnectionId = "xctx-conn-cfg", State = 0
        });
        await db.SaveChangesAsync();

        var integrationRepository = new SqlIntegrationRepository(db);
        var connectionRepository = new SqlConnectionRepository(db);
        var bindingRepository = new SqlBindingRepository(db);

        var moduleConfiguration = new ModuleIntegrationConfiguration(
            moduleId,
            Array.Empty<IntegrationSettingDefinition>(),
            new[]
            {
                new ConnectionRequirementDefinition
                {
                    Key = "primary",
                    DisplayName = "Primary",
                    Description = "Primary connection requirement.",
                    CapabilityContractType = "test.contract",
                }
            },
            Array.Empty<ProcedureRequirementSet>());

        var evaluator = new IntegrationDispatchReadinessEvaluator(
            integrationRepository,
            bindingRepository,
            new SqlScheduleRepository(db),
            EmptySlotRegistry(),
            connectionRepository,
            new BindingEligibilityService(connectionRepository, new PermissiveRegistry()),
            new AdvisoryConnectionVerificationReadinessPolicy(),
            new FixedModuleConfigurationRegistry(moduleConfiguration),
            NoOpConfigurationEvidenceBuilder(connectionRepository),
            TimeProvider.System);

        var projection = await evaluator.EvaluateForDispatchAsync(new IntegrationId("xctx-int-cfg"), procedureId: null);

        projection!.IsRunnable.Should().BeFalse();
        var issue = projection.Issues.Should().ContainSingle(i => i.Code == IntegrationReadinessIssueCodes.BindingCrossContext).Subject;
        issue.Message.Should().Contain("Remove this binding",
            "the message must tell the operator what to do, because this plan can land before Plan 5's move UI exists");
    }

    [Fact]
    public async Task A_successful_move_leaves_readiness_clean()
    {
        // The complement of the test above: the backstop must fire on genuinely invalid data and must NOT
        // fire on the state a legitimate CTX-3B move produces. Without this, a too-eager check would make
        // every moved Integration unrunnable and nothing would catch it.
        await using var h = new ResourceOwnershipMoveTestHarness();
        var source = await h.CreateApplicationAsync("CampusNexus");
        var target = await h.CreateApplicationAsync("Canvas");
        var integration = await h.CreateIntegrationAsync(source.Id);
        var sharedConnection = await h.CreateConnectionAsync(ResourceContextIds.Shared);
        await h.AddDirectBindingAsync(integration.Id, integration.ModuleId, sharedConnection.Id);

        var moved = await h.Service.MoveIntegrationAsync(integration.Id, target.Id);
        moved.CanMove.Should().BeTrue();

        var projection = await h.BuildReadinessEvaluator().EvaluateForDispatchAsync(integration.Id, procedureId: null);

        projection!.Issues.Should().NotContain(i => i.Code == IntegrationReadinessIssueCodes.BindingCrossContext,
            "a Shared connection stays reachable from every context, so a legitimate move must not trip the backstop");
    }

    /// <summary>
    /// Real <see cref="IModuleSlotRegistry"/> stand-in declaring no slots for any module: this plan's check
    /// walks <c>bindings</c> directly rather than through declared slots, so an empty registry keeps the
    /// pre-existing legacy-slot-binding branch inert and isolates the assertions to the new check.
    /// </summary>
    internal static IModuleSlotRegistry EmptySlotRegistry()
    {
        var mock = new Mock<IModuleSlotRegistry>();
        mock.Setup(x => x.GetSlotsForModule(It.IsAny<string>())).Returns(Array.Empty<ICapabilitySlot>());
        return mock.Object;
    }

    /// <summary>
    /// Real <see cref="IntegrationConfigurationEvidenceBuilder"/> wired with minimal, mostly-inert
    /// collaborators. Reachable only when the SDK-6C configuration registry passed alongside it has a
    /// declaration for the integration's module -- the legacy-path tests use an empty
    /// <c>IntegrationConfigurationRegistry</c>, so for them this builder's dependencies are constructed but
    /// never actually invoked; <see cref="Seeded_cross_context_binding_makes_integration_not_runnable_via_SDK6C_configuration_path"/>
    /// does register a module declaration, so for that test <c>BuildAsync</c> genuinely runs against these
    /// stand-ins (empty resolved settings, empty module catalog). Either way, the evaluator's constructor
    /// requires a real, non-null instance.
    /// </summary>
    internal static IntegrationConfigurationEvidenceBuilder NoOpConfigurationEvidenceBuilder(IConnectionRepository connectionRepository)
    {
        var settingsResolver = new Mock<IIntegrationSettingsResolver>();
        settingsResolver.Setup(x => x.ListResolvedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlatformSettingResolvedValue>());
        var moduleRegistry = new Mock<IModuleRegistryService>();
        moduleRegistry.Setup(x => x.ListModulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ModuleCatalogEntry>());
        return new IntegrationConfigurationEvidenceBuilder(
            settingsResolver.Object,
            connectionRepository,
            new BindingEligibilityService(connectionRepository, new PermissiveRegistry()),
            moduleRegistry.Object);
    }

    /// <summary>
    /// Minimal <see cref="IIntegrationConfigurationRegistry"/> stand-in that always resolves to one fixed
    /// <see cref="ModuleIntegrationConfiguration"/> for a matching module id. Used instead of the real
    /// <c>IntegrationConfigurationRegistry</c> production class here because that class requires a full
    /// <c>IIntegrationConfigurationContributor</c> (with its own validation pass) just to build a
    /// declaration -- this test only needs one minimal connection requirement to route
    /// <c>EvaluateAsync</c> down the SDK-6C configuration-evidence branch.
    /// </summary>
    private sealed class FixedModuleConfigurationRegistry : IIntegrationConfigurationRegistry
    {
        private readonly ModuleIntegrationConfiguration _configuration;

        public FixedModuleConfigurationRegistry(ModuleIntegrationConfiguration configuration) =>
            _configuration = configuration;

        public IReadOnlyList<string> GetRegisteredModuleIds() => new[] { _configuration.ModuleId };

        public bool TryGetModuleConfiguration(string moduleId, out ModuleIntegrationConfiguration configuration)
        {
            if (string.Equals(moduleId, _configuration.ModuleId, StringComparison.Ordinal))
            {
                configuration = _configuration;
                return true;
            }

            configuration = null!;
            return false;
        }
    }

    private sealed class PermissiveRegistry : IConnectorDefinitionRegistry
    {
        private readonly Dictionary<string, ConnectorDefinition> _definitions = new(StringComparer.Ordinal);
        public void Register(ConnectorDefinition definition) => _definitions[definition.Id.Value] = definition;
        public IReadOnlyList<ConnectorDefinition> GetAll(string? moduleId = null) => _definitions.Values.ToList();
        public ConnectorDefinition? Get(ConnectorDefinitionId id)
        {
            if (!_definitions.TryGetValue(id.Value, out var definition))
            {
                definition = new ConnectorDefinition(id, id.Value, null, new Dictionary<string, string>());
                _definitions[id.Value] = definition;
            }
            return definition;
        }
    }
}
