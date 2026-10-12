using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.Management.Api.Controllers;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// CTX-3A Plan 4, Task 1 (A3, fact F2): the Integrations fleet query path (both the readiness service
/// and the controller's <c>contextId</c> validation) scopes by ResourceContext exactly like the plain
/// <c>List</c> action already does.
/// </summary>
public sealed class IntegrationFleetContextFilterTests
{
    [Fact]
    public async Task ListFleetAsync_WithResourceContextId_ReturnsOnlyThatContextsIntegrations()
    {
        await using var harness = await IntegrationFleetTestHarness.CreateAsync();
        var contextA = await harness.CreateApplicationContextAsync("A");
        var contextB = await harness.CreateApplicationContextAsync("B");
        await harness.CreateIntegrationAsync(contextA.Id, "Integration A");
        await harness.CreateIntegrationAsync(contextB.Id, "Integration B");

        var query = new IntegrationFleetQuery(ResourceContextId: contextA.Id.Value);
        var result = await harness.ReadinessService.ListFleetAsync(query, CancellationToken.None);

        result.Should().ContainSingle(r => r.DisplayName == "Integration A");
        result.Single().ResourceContextId.Should().Be(contextA.Id.Value);
    }

    [Fact]
    public async Task ListFleetAsync_WithoutResourceContextId_ReturnsEverything()
    {
        await using var harness = await IntegrationFleetTestHarness.CreateAsync();
        var contextA = await harness.CreateApplicationContextAsync("A");
        await harness.CreateIntegrationAsync(contextA.Id, "Integration A");

        var result = await harness.ReadinessService.ListFleetAsync(new IntegrationFleetQuery(), CancellationToken.None);

        result.Should().Contain(r => r.DisplayName == "Integration A");
    }

    // -------------------------------------------------------------------------------------------
    // Controller-level 400, mirroring ResourceContextApiCompatibilityTests'
    // IntegrationsList_WithUnknownContextId_IsRejectedRatherThanSilentlyEmpty pattern for List.
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Fleet_WithUnknownContextId_Returns400()
    {
        var resourceContexts = new Mock<IResourceContextService>();
        resourceContexts
            .Setup(r => r.GetAsync(It.Is<ResourceContextId>(c => c.Value == "does-not-exist"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResourceContext?)null);

        var controller = new IntegrationsController(
            Mock.Of<IIntegrationManagementService>(),
            Mock.Of<IIntegrationReadinessService>(),
            Mock.Of<IIntegrationConfigurationRegistry>(),
            Mock.Of<IIntegrationScheduleDiagnosticsService>(),
            Mock.Of<IAttentionIncidentTracker>(),
            Mock.Of<IAttentionRoutingManagementService>(),
            Mock.Of<IPrincipalContext>(),
            TimeProvider.System,
            resourceContexts.Object,
            Mock.Of<IResourceOwnershipMoveService>(),
            UnusedIntegrationAuthorization.Create());

        var result = await controller.Fleet(
            contextId: "does-not-exist",
            state: null,
            runnable: null,
            hasBlocking: null,
            hasAdvisory: null,
            verificationConcern: null,
            verification: null,
            hasOperationalFailure: null,
            hasUnexpectedSilence: null,
            attention: null,
            sort: null,
            cancellationToken: CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>
    /// Wires the real <see cref="IntegrationReadinessService"/> and <see cref="IntegrationManagementService"/>
    /// over an in-memory store so fleet-context-filter tests exercise the production filter path
    /// (<c>IIntegrationRepository.GetVisibleAsync</c>) rather than a mock of it. Slot requirements are
    /// deliberately empty: this suite is about ResourceContext scoping, not readiness evaluation, which
    /// already has its own coverage in <c>IntegrationReadinessServiceTests</c>.
    /// </summary>
    private sealed class IntegrationFleetTestHarness : IAsyncDisposable
    {
        private readonly AppertureDbContext _db;

        private IntegrationFleetTestHarness(
            AppertureDbContext db,
            IResourceContextService resourceContexts,
            IIntegrationManagementService integrations,
            IIntegrationReadinessService readinessService)
        {
            _db = db;
            ResourceContexts = resourceContexts;
            Integrations = integrations;
            ReadinessService = readinessService;
        }

        public IResourceContextService ResourceContexts { get; }
        public IIntegrationManagementService Integrations { get; }
        public IIntegrationReadinessService ReadinessService { get; }

        public static Task<IntegrationFleetTestHarness> CreateAsync()
        {
            var databaseName = Guid.NewGuid().ToString("N");
            var databaseRoot = new InMemoryDatabaseRoot();
            var db = new AppertureDbContext(
                new DbContextOptionsBuilder<AppertureDbContext>()
                    .UseInMemoryDatabase(databaseName, databaseRoot)
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                    .Options);

            // The migration seeds Shared before anything can own it; mirror that here (same convention
            // as ResourceContextOwnershipTestHarness).
            db.ResourceContexts.Add(new ResourceContextEntity
            {
                Id = ResourceContextIds.SharedValue,
                Name = "Shared",
                Slug = "shared",
                Kind = ResourceContextKinds.Shared,
                State = ResourceContextStates.Active,
                IsReserved = true,
                CreatedAtUtc = DateTimeOffset.UnixEpoch,
                UpdatedAtUtc = DateTimeOffset.UnixEpoch
            });
            db.SaveChanges();

            var resourceContextRepository = new SqlResourceContextRepository(db);
            var resourceContexts = new ResourceContextService(resourceContextRepository, TimeProvider.System);
            var integrationRepository = new SqlIntegrationRepository(db);

            var integrations = new IntegrationManagementService(
                new AlwaysActor(),
                new AlwaysAllow(),
                integrationRepository,
                resourceContexts,
                db,
                new SqlResourceOwnershipMutationGate(db),
                auditedUnitOfWork: new Apperture.UnitTests.Audit.RecordingAuditedUnitOfWork());

            var slots = new Mock<IModuleSlotRegistry>();
            slots.Setup(x => x.GetSlotsForModule(It.IsAny<string>())).Returns(new List<ICapabilitySlot>());

            var bindRepo = new Mock<IBindingRepository>();
            bindRepo.Setup(x => x.GetByIntegrationIdAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Binding>());

            var connRepo = new Mock<IConnectionRepository>();
            var elig = new Mock<IBindingEligibilityService>();

            var schedRepo = new Mock<IScheduleRepository>();
            schedRepo.Setup(x => x.GetByIntegrationIdAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<ScheduleDefinition>());

            var runQuery = new Mock<IRunQueryService>();
            runQuery.Setup(x => x.GetFleetOperationalRunSignalsAsync(
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<string, FleetOperationalRunSignals>(StringComparer.Ordinal));
            runQuery.Setup(x => x.GetLatestAuthorizedScheduleOriginRunsAsync(
                    It.IsAny<IReadOnlyList<string>>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<string, FleetScheduleOriginRunEvidence>(StringComparer.Ordinal));

            var attentionIncidentTracker = new Mock<IAttentionIncidentTracker>();
            attentionIncidentTracker
                .Setup(x => x.GetIntegrationIdsWithActiveConditionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HashSet<string>(StringComparer.Ordinal));

            var configurationRegistry = new IntegrationConfigurationRegistry(Array.Empty<ModuleIntegrationConfigurationBinding>());

            var settingsResolver = new Mock<IIntegrationSettingsResolver>();
            settingsResolver.Setup(x => x.ListResolvedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<PlatformSettingResolvedValue>());
            var moduleRegistry = new Mock<IModuleRegistryService>();
            moduleRegistry.Setup(x => x.ListModulesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<ModuleCatalogEntry>());
            var configurationEvidence = new IntegrationConfigurationEvidenceBuilder(
                settingsResolver.Object,
                connRepo.Object,
                elig.Object,
                moduleRegistry.Object);

            var dispatchEvaluator = new IntegrationDispatchReadinessEvaluator(
                integrationRepository,
                bindRepo.Object,
                schedRepo.Object,
                slots.Object,
                connRepo.Object,
                elig.Object,
                new AdvisoryConnectionVerificationReadinessPolicy(),
                configurationRegistry,
                configurationEvidence,
                TimeProvider.System);

            var readinessService = new IntegrationReadinessService(
                integrations,
                integrationRepository,
                slots.Object,
                bindRepo.Object,
                connRepo.Object,
                schedRepo.Object,
                runQuery.Object,
                attentionIncidentTracker.Object,
                new ScheduleExpectationEvaluator(),
                TimeProvider.System,
                dispatchEvaluator);

            return Task.FromResult(new IntegrationFleetTestHarness(db, resourceContexts, integrations, readinessService));
        }

        public Task<ResourceContext> CreateApplicationContextAsync(string name) =>
            ResourceContexts.CreateApplicationContextAsync(name).AsTask();

        public Task<DomainIntegration> CreateIntegrationAsync(ResourceContextId contextId, string displayName) =>
            Integrations.CreateIntegrationAsync(
                new CreateIntegrationRequest(displayName, "Sample.Mod", ResourceContextId: contextId),
                CancellationToken.None).AsTask();

        public ValueTask DisposeAsync() => _db.DisposeAsync();

        private sealed class AlwaysActor : IPrincipalContext
        {
            public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
                ValueTask.FromResult<IActor?>(new Actor());

            private sealed class Actor : IActor
            {
                public ActorId Id => new("test-actor");
                public ActorKind Kind => ActorKind.Human;
                public string? DisplayName => "Test Actor";
            }
        }

        private sealed class AlwaysAllow : IAuthorizationEvaluationService
        {
            public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default) =>
                ValueTask.FromResult(true);
        }
    }
}
