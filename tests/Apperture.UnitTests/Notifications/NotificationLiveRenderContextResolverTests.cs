using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.ControlPlane.Security;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.UnitTests.Notifications;

public sealed class NotificationLiveRenderContextResolverTests
{
    [Fact]
    public async Task ResolveAsync_loads_integration_display_name_and_management_host_url()
    {
        var integration = new DomainIntegration(
            new IntegrationId("int-42"),
            "Campus Nexus Prod",
            "module.campusnexus",
            null,
            IntegrationState.Active,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ShellUi:ManagementHostUrl"] = "https://mgmt.example.com/",
            })
            .Build();

        var sut = new NotificationLiveRenderContextResolver(new StubIntegrationRepository(integration), config);
        var hints = await sut.ResolveAsync("int-42");

        hints.IntegrationDisplayName.Should().Be("Campus Nexus Prod");
        hints.InstanceBaseUrl.Should().Be("https://mgmt.example.com");
    }

    [Fact]
    public void Render_context_builder_includes_live_hints()
    {
        var envelope = NotificationEvaluationTestHelpers.SampleMissingEvent("int-42", "inc-1");
        var builder = new NotificationRenderContextBuilder();
        var context = builder.Build(
            envelope,
            NotificationKinds.Activation,
            templateVariantKey: NotificationKinds.Activation,
            incident: null,
            integrationDisplayName: "Campus Nexus Prod",
            instanceBaseUrl: "https://mgmt.example.com");

        context["integration.displayName"].Should().Be("Campus Nexus Prod");
        context["instance.baseUrl"].Should().Be("https://mgmt.example.com");
    }

    private sealed class StubIntegrationRepository(DomainIntegration? integration) : IIntegrationRepository
    {
        public ValueTask<DomainIntegration?> GetByIdAsync(IntegrationId id, CancellationToken cancellationToken = default) =>
            new(integration);

        public ValueTask<IReadOnlyList<DomainIntegration>> GetVisibleAsync(string scopeKey, CancellationToken cancellationToken = default) =>
            new(Array.Empty<DomainIntegration>());

        public ValueTask AddAsync(DomainIntegration integration, CancellationToken cancellationToken = default) => default;

        public ValueTask UpdateAsync(DomainIntegration integration, CancellationToken cancellationToken = default) => default;

        public ValueTask<DomainIntegration?> GetByIdForUpdateAsync(IntegrationId id, CancellationToken cancellationToken = default) =>
            new(integration);

        public ValueTask SetResourceContextIdAsync(IntegrationId id, ResourceContextId resourceContextId, CancellationToken cancellationToken = default) => default;
    }
}
