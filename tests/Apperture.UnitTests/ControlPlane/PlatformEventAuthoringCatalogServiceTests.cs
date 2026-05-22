using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.ModuleSdk.Abstractions;
using Apperture.ModuleSdk.Abstractions.Signals;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Sample.HelloWorld;
using Xunit;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.UnitTests.ControlPlane;

public sealed class PlatformEventAuthoringCatalogServiceTests
{
    [Fact]
    public async Task List_includes_platform_and_module_types_with_display_names()
    {
        IModule[] modules = [new HelloWorldModule()];
        ModuleSignalManifestValidator.ThrowIfInvalid(modules);
        var registry = ModuleSignalRegistry.FromModules(modules);
        var sut = CreateService(modules, registry);

        var rows = await sut.ListAsync();

        rows.Should().Contain(e => e.Type == PlatformEventTypes.ScheduleExpectedRunMissing);
        rows.Should().Contain(e => e.Type == PlatformEventTypes.ExecutionRunCompleted);
        rows.Should().Contain(e => e.Type == PlatformEventTypes.ExecutionRunFailed);
        rows.First(e => e.Type == PlatformEventTypes.ScheduleExpectedRunMissing).DisplayName
            .Should().Be("Expected run missing");
        rows.First(e => e.Type == PlatformEventTypes.ExecutionRunCompleted).DisplayName
            .Should().Be("Run completed");
        rows.Should().Contain(e =>
            e.Type == ModulePlatformEventTypes.Format("Sample.HelloWorld", "demo.completed"));
    }

    private static PlatformEventAuthoringCatalogService CreateService(IModule[] modules, IModuleSignalRegistry registry)
    {
        var catalog = new CompositePlatformEventCatalog(registry);
        var variables = new NotificationTemplateVariableCatalog(registry);
        var auth = new NotificationDefinitionAuthorization(
            new FixedPrincipalContext(new StubActor("user:1", "Op")),
            new AllowAuth(),
            new AllowAuthEval(),
            new StubIntegrationRepo());
        return new PlatformEventAuthoringCatalogService(catalog, variables, modules, auth);
    }

    private sealed class AllowAuth : IAuthorizationService
    {
        public ValueTask<bool> EvaluateAsync(
            IActor actor,
            PermissionDescriptor permission,
            ResourceDescriptor? resource,
            IGovernedResourceIdentity? governedResource,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class AllowAuthEval : IAuthorizationEvaluationService
    {
        public ValueTask<bool> EvaluateAsync(
            AuthorizationEvaluationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class StubIntegrationRepo : IIntegrationRepository
    {
        public ValueTask<DomainIntegration?> GetByIdAsync(IntegrationId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<DomainIntegration?>(null);

        public ValueTask<IReadOnlyList<DomainIntegration>> GetVisibleAsync(
            string scopeKey,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<DomainIntegration>>([]);

        public ValueTask AddAsync(DomainIntegration integration, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask UpdateAsync(DomainIntegration integration, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class FixedPrincipalContext : IPrincipalContext
    {
        private readonly IActor _actor;
        public FixedPrincipalContext(IActor actor) => _actor = actor;

        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(_actor);
    }

    private sealed class StubActor : IActor
    {
        public StubActor(string id, string? name)
        {
            Id = new ActorId(id);
            DisplayName = name;
        }

        public ActorId Id { get; }
        public ActorKind Kind => ActorKind.Human;
        public string? DisplayName { get; }
    }
}
