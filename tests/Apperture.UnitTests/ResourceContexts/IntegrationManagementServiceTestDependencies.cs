using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Moq;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// CTX-3B Plan 6a Task 5: <see cref="IntegrationManagementService"/> gained two constructor dependencies
/// (<see cref="IAuditService"/> for the <c>integration.deleted</c> record and
/// <see cref="IIntegrationSettingsService"/> for secret-safe Integration-scoped setting cleanup). The
/// harnesses that merely create Integrations to exercise ownership/inventory/gate behavior need both
/// supplied without each file inventing its own wiring.
/// </summary>
/// <remarks>
/// The settings service handed out here is the <b>real</b> <see cref="IntegrationSettingsService"/> over
/// the caller's own InMemory <see cref="AppertureDbContext"/>, deliberately not a mock: a no-op double
/// would make a future orphaned-secret regression invisible in every one of these suites. These harnesses
/// never write Integration-scoped setting values, so cleanup finds nothing and does nothing -- but if one
/// ever does, it runs the same code production runs.
/// </remarks>
internal static class IntegrationManagementServiceTestDependencies
{
    /// <summary>Records appended audit records; the harnesses using it assert on nothing but may inspect it.</summary>
    internal sealed class RecordingAuditService : IAuditService
    {
        private readonly List<AuditRecord> _records = new();

        public IReadOnlyList<AuditRecord> Records => _records;

        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            _records.Add(record);
            return ValueTask.CompletedTask;
        }

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by these harnesses.");

        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by these harnesses.");

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by these harnesses.");
    }

    /// <summary>
    /// A real <see cref="IntegrationSettingsService"/> over <paramref name="db"/>, using the production
    /// <see cref="IntegrationSettingDefinitionRegistry"/> (currently empty), the real SQL value repository,
    /// the real resolver, and a real <see cref="PlatformSettingSecretMaterialProvisioner"/> over
    /// <paramref name="secretStore"/>.
    /// </summary>
    public static IIntegrationSettingsService CreateSettingsService(
        AppertureDbContext db,
        IIntegrationRepository integrations,
        IAuditService? audit = null,
        ISecretStore? secretStore = null)
    {
        var registry = new IntegrationSettingDefinitionRegistry();
        var repository = new SqlPlatformSettingValueRepository(db);
        var resolver = new IntegrationSettingsResolver(registry, repository);

        var principalContext = CreatePrincipalContext();
        var authorization = new Mock<IAuthorizationEvaluationService>();
        authorization
            .Setup(x => x.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var coordinator = new PlatformSettingOptionProviderCoordinator(
            new PlatformSettingOptionProviderRegistry([]),
            new PlatformSettingOptionProviderAuthorization(principalContext, authorization.Object, integrations),
            new IntegrationSettingOptionProviderAuthorization(principalContext, authorization.Object, integrations));

        return new IntegrationSettingsService(
            registry,
            repository,
            resolver,
            coordinator,
            new PlatformSettingSecretMaterialProvisioner(secretStore ?? Mock.Of<ISecretStore>()),
            principalContext,
            audit ?? new RecordingAuditService(),
            integrations);
    }

    private static IPrincipalContext CreatePrincipalContext()
    {
        var actor = new Mock<IActor>();
        actor.SetupGet(x => x.Id).Returns(new ActorId("test-actor"));
        actor.SetupGet(x => x.Kind).Returns(ActorKind.Human);

        var principalContext = new Mock<IPrincipalContext>();
        principalContext
            .Setup(x => x.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(actor.Object);
        return principalContext.Object;
    }
}
