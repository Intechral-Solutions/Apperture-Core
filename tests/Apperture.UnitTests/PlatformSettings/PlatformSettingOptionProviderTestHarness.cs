using Apperture.ControlPlane.Security;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;
using Moq;

namespace Apperture.UnitTests.PlatformSettings;

internal static class PlatformSettingOptionProviderTestHarness
{
    public static (Mock<IPrincipalContext> Principal, Mock<IActor> Actor, Mock<IAuthorizationEvaluationService> Authorization)
        CreateDefaultAuthMocks(bool allowAuthorization = true)
    {
        var principalContext = new Mock<IPrincipalContext>();
        var actor = new Mock<IActor>();
        actor.Setup(x => x.Id).Returns(new ActorId("actor-1"));
        actor.Setup(x => x.Kind).Returns(ActorKind.Human);
        principalContext.Setup(x => x.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(actor.Object);

        var authorization = new Mock<IAuthorizationEvaluationService>();
        authorization.Setup(x => x.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(allowAuthorization);

        return (principalContext, actor, authorization);
    }

    public static PlatformSettingOptionProviderCoordinator CreateCoordinator(
        IPlatformSettingOptionProvider provider,
        Mock<IPrincipalContext>? principalContext = null,
        Mock<IAuthorizationEvaluationService>? authorization = null,
        Mock<IIntegrationRepository>? integrations = null,
        bool allowAuthorization = true)
    {
        var (defaultPrincipal, _, defaultAuthorization) = CreateDefaultAuthMocks(allowAuthorization);
        var principal = principalContext ?? defaultPrincipal;
        var auth = authorization ?? defaultAuthorization;
        var integrationRepo = integrations ?? new Mock<IIntegrationRepository>();

        return CreateCoordinator(
            new SingleOptionProviderRegistry(provider),
            principal,
            auth,
            integrationRepo);
    }

    public static PlatformSettingOptionProviderCoordinator CreateCoordinator(
        IPlatformSettingOptionProviderRegistry registry,
        Mock<IPrincipalContext>? principalContext = null,
        Mock<IAuthorizationEvaluationService>? authorization = null,
        Mock<IIntegrationRepository>? integrations = null,
        bool allowAuthorization = true)
    {
        var (defaultPrincipal, _, defaultAuthorization) = CreateDefaultAuthMocks(allowAuthorization);
        var principal = principalContext ?? defaultPrincipal;
        var auth = authorization ?? defaultAuthorization;
        var integrationRepo = integrations ?? new Mock<IIntegrationRepository>();

        return CreateCoordinator(registry, principal, auth, integrationRepo);
    }

    private static PlatformSettingOptionProviderCoordinator CreateCoordinator(
        IPlatformSettingOptionProviderRegistry registry,
        Mock<IPrincipalContext> principal,
        Mock<IAuthorizationEvaluationService> auth,
        Mock<IIntegrationRepository> integrationRepo)
    {
        var platformAuth = new PlatformSettingOptionProviderAuthorization(
            principal.Object,
            auth.Object,
            integrationRepo.Object);

        var integrationAuth = new IntegrationSettingOptionProviderAuthorization(
            principal.Object,
            auth.Object,
            integrationRepo.Object);

        return new PlatformSettingOptionProviderCoordinator(registry, platformAuth, integrationAuth);
    }

    internal sealed class SingleOptionProviderRegistry(IPlatformSettingOptionProvider provider) : IPlatformSettingOptionProviderRegistry
    {
        public bool TryGet(string providerKey, out IPlatformSettingOptionProvider providerOut)
        {
            if (string.Equals(providerKey, provider.ProviderKey, StringComparison.Ordinal))
            {
                providerOut = provider;
                return true;
            }

            providerOut = null!;
            return false;
        }
    }

    internal sealed class SingleDefinitionRegistry(PlatformSettingDefinition definition) : IPlatformSettingDefinitionRegistry
    {
        public IReadOnlyList<PlatformSettingDefinition> GetAll() => [definition];

        public PlatformSettingDefinition? TryGet(string key) =>
            string.Equals(key, definition.Key, StringComparison.Ordinal) ? definition : null;
    }
}
