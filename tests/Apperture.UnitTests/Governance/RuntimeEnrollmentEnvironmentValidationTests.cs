using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Security.Trust;
using Apperture.Persistence.SqlServer;
using Apperture.Management.Api.Controllers;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class RuntimeEnrollmentEnvironmentValidationTests
{
    [Fact]
    public async Task CreateInvitation_WhenNoActor_Returns401()
    {
        var controller = new EnrollController(
            enrollment: new StubRuntimeEnrollmentService(),
            principalContext: new StubPrincipalContext(actor: null),
            principalToUserResolver: new StubPrincipalToUserResolver(),
            roleProvider: new StubRoleProvider(),
            descriptorRegistry: new StubResourceDescriptorRegistry(),
            environment: new StubEnvironmentContext("local"),
            authorizationEvaluation: new StubAuthorizationEvaluationService(allowed: true),
            audit: new StubAuditService());

        var result = await controller.CreateInvitation(new CreateInvitationRequest("rt-1"), CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task CreateInvitation_WhenNotAuthorized_Returns403_AndDoesNotAudit()
    {
        var audit = new StubAuditService();
        var controller = new EnrollController(
            enrollment: new StubRuntimeEnrollmentService(token: "t-1"),
            principalContext: new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)),
            principalToUserResolver: new StubPrincipalToUserResolver(),
            roleProvider: new StubRoleProvider(),
            descriptorRegistry: new StubResourceDescriptorRegistry(),
            environment: new StubEnvironmentContext("local"),
            authorizationEvaluation: new StubAuthorizationEvaluationService(allowed: false),
            audit: audit);

        var result = await controller.CreateInvitation(
            new CreateInvitationRequest("rt-1", TtlHours: 1, Scopes: new HashSet<string> { "cap.a" }),
            CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
        audit.AppendCalls.Should().Be(0);
    }

    [Fact]
    public async Task CreateInvitation_WhenAuthorized_AppendsAuditAndReturnsInvitation()
    {
        var audit = new StubAuditService();
        var controller = new EnrollController(
            enrollment: new StubRuntimeEnrollmentService(token: "t-1"),
            principalContext: new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)),
            principalToUserResolver: new StubPrincipalToUserResolver(),
            roleProvider: new StubRoleProvider(),
            descriptorRegistry: new StubResourceDescriptorRegistry(),
            environment: new StubEnvironmentContext("local"),
            authorizationEvaluation: new StubAuthorizationEvaluationService(allowed: true),
            audit: audit);

        var result = await controller.CreateInvitation(
            new CreateInvitationRequest("rt-1", TtlHours: 1, Scopes: new HashSet<string> { "cap.a", "cap.b" }),
            CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        audit.AppendCalls.Should().Be(1);
        audit.LastRecord!.Action.Should().Be(GovernanceAuditActionNames.RuntimeEnrollmentInvitationCreated);
        audit.LastRecord.Resource.Should().NotBeNull();
        audit.LastRecord.Resource!.ResourceType.Should().Be(ResourceTypeConstants.RuntimeInstance);
        audit.LastRecord.Outcome.Should().Be("auth-admin");

        audit.LastRecord.Details.Should().Contain("runtimeId=rt-1");
        audit.LastRecord.Details.Should().Contain("scopesCount=2");
    }

    [Fact]
    public async Task EnrollAsync_WhenTokenEnvironmentMismatch_ReturnsFailure()
    {
        var db = NewDb();

        db.EnrollmentTokens.Add(new EnrollmentTokenEntity
        {
            Id = Guid.NewGuid(),
            Token = "token-1",
            RuntimeId = "rt-1",
            EnvironmentId = "other",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Used = false,
            CreatedAt = DateTimeOffset.UtcNow,
            ScopesJson = JsonSerializer.Serialize(new[] { "cap.a" })
        });
        await db.SaveChangesAsync();

        var service = new SqlRuntimeEnrollmentService(db, new StubEnvironmentContext("local"));
        var result = await service.EnrollAsync(
            "token-1",
            new RuntimeIdentityRegistration("rt-name", "PublicKey", "pk", null),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.RuntimeId.Should().Be("rt-1");
        result.Message.Should().Contain("environment mismatch");
    }

    [Fact]
    public async Task EnrollAsync_OnSuccess_TransfersDeclaredScopesToRuntimeIdentity()
    {
        var db = NewDb();

        var scopes = new[] { "cap.a", "cap.b" };
        db.EnrollmentTokens.Add(new EnrollmentTokenEntity
        {
            Id = Guid.NewGuid(),
            Token = "token-2",
            RuntimeId = "rt-2",
            EnvironmentId = "local",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Used = false,
            CreatedAt = DateTimeOffset.UtcNow,
            ScopesJson = JsonSerializer.Serialize(scopes)
        });
        await db.SaveChangesAsync();

        var service = new SqlRuntimeEnrollmentService(db, new StubEnvironmentContext("local"));
        var result = await service.EnrollAsync(
            "token-2",
            new RuntimeIdentityRegistration("rt-name", "PublicKey", "pk", null),
            CancellationToken.None);

        result.Success.Should().BeTrue();

        var entity = await db.RuntimeIdentities.FirstAsync(x => x.Id == "rt-2");
        entity.DeclaredScopesJson.Should().Be(JsonSerializer.Serialize(scopes));
    }

    [Fact]
    public async Task Operator_CreateInvitation_ThenRuntimeEnroll_ThenInspectionShowsScopes()
    {
        var db = NewDb();
        var environment = new StubEnvironmentContext("local");

        var enrollment = new SqlRuntimeEnrollmentService(db, environment);
        var identity = new SqlRuntimeIdentityService(db);

        var audit = new StubAuditService();
        var createInvitationAuthz = new StubAuthorizationEvaluationService(allowed: true);

        var enrollController = new EnrollController(
            enrollment: enrollment,
            principalContext: new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)),
            principalToUserResolver: new StubPrincipalToUserResolver(),
            roleProvider: new StubRoleProvider(),
            descriptorRegistry: new StubResourceDescriptorRegistry(),
            environment: environment,
            authorizationEvaluation: createInvitationAuthz,
            audit: audit);

        var invitationResult = await enrollController.CreateInvitation(
            new CreateInvitationRequest("rt-3", TtlHours: 1, Scopes: new HashSet<string> { "cap.a" }),
            CancellationToken.None);

        var ok = invitationResult.Result.Should().BeOfType<OkObjectResult>().Which;
        var invitation = ok.Value.Should().BeOfType<EnrollmentInvitation>().Which;

        var enrollResult = await enrollController.Enroll(
            new EnrollRequest(
                Token: invitation.Token,
                RuntimeName: "rt-3-name",
                PublicKeyOrThumbprint: "pk",
                CredentialType: "PublicKey",
                ManagedIdentityPrincipalId: null),
            CancellationToken.None);

        enrollResult.Result.Should().BeOfType<OkObjectResult>();

        var inspectionAuthz = new StubAuthorizationEvaluationService(allowed: true);
        var inspection = new RuntimeInstancesController(
            runtimeIdentity: identity,
            authorizationEvaluation: inspectionAuthz,
            principalContext: new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)),
            environment: environment);

        var inspectionResult = await inspection.GetCapabilities("rt-3", CancellationToken.None);
        inspectionResult.Result.Should().BeOfType<OkObjectResult>();
        var dto = ((OkObjectResult)inspectionResult.Result!).Value.Should().BeOfType<RuntimeCapabilitiesDto>().Which;
        dto.DeclaredScopes.Should().ContainSingle(s => s == "cap.a");
    }

    private static AppertureDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(opts);
    }

    private sealed class StubEnvironmentContext : IEnvironmentContext
    {
        public string EnvironmentId { get; }

        public StubEnvironmentContext(string environmentId) => EnvironmentId = environmentId;
    }

    private sealed class StubPrincipalContext : IPrincipalContext
    {
        private readonly IActor? _actor;

        public StubPrincipalContext(IActor? actor) => _actor = actor;

        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_actor);
    }

    private sealed class StubAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        private readonly bool _allowed;

        public PermissionDescriptor? LastPermission { get; private set; }
        public ResourceDescriptor? LastResource { get; private set; }

        public StubAuthorizationEvaluationService(bool allowed) => _allowed = allowed;

        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default)
        {
            LastPermission = context.Permission;
            LastResource = context.Resource;
            return ValueTask.FromResult(_allowed);
        }
    }

    private sealed class StubRuntimeEnrollmentService : IRuntimeEnrollmentService
    {
        private readonly string? _token;

        public StubRuntimeEnrollmentService(string? token = null) => _token = token;

        public ValueTask<EnrollmentInvitation> CreateInvitationAsync(
            string runtimeId,
            TimeSpan? ttl = null,
            IReadOnlySet<string>? scopes = null,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new EnrollmentInvitation(
                _token ?? "t-default",
                "https://localhost/api/enroll?token=t-default",
                DateTimeOffset.UtcNow.AddHours(1)));
        }

        public ValueTask<EnrollmentTokenValidationResult> ValidateTokenAsync(
            string enrollmentToken,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new EnrollmentTokenValidationResult(true, null, null));

        public ValueTask<EnrollmentResult> EnrollAsync(
            string enrollmentToken,
            RuntimeIdentityRegistration registration,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new EnrollmentResult(registration.RuntimeName, true));
    }

    private sealed class StubPrincipalToUserResolver : IPrincipalToUserResolver
    {
        public ValueTask<UserId?> ResolveUserIdAsync(IActor actor, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<UserId?>(null);
    }

    private sealed class StubRoleProvider : IRoleProvider
    {
        public ValueTask<IReadOnlyList<Role>> GetRolesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Role>>(Array.Empty<Role>());

        public ValueTask<Role?> GetRoleAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Role?>(null);

        public ValueTask<IReadOnlyList<Role>> GetRolesForUserAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Role>>(Array.Empty<Role>());

        public ValueTask AssignRoleToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask RemoveRoleFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class StubResourceDescriptorRegistry : IResourceDescriptorRegistry
    {
        public void RegisterPermission(PermissionDescriptor descriptor, ResourceFamily? family = null) { }
        public void RegisterResource(ResourceDescriptor descriptor, ResourceFamily? family = null) { }
        public IReadOnlyList<ResourceDescriptor> GetResourceDescriptors(ResourceFamily? family = null) => Array.Empty<ResourceDescriptor>();
        public IReadOnlyList<PermissionDescriptor> GetPermissionDescriptors(ResourceFamily? family = null) => Array.Empty<PermissionDescriptor>();
        public bool IsPermissionRegistered(PermissionDescriptor descriptor) => true;
    }

    private sealed class StubAuditService : IAuditService
    {
        public int AppendCalls { get; private set; }
        public AuditRecord? LastRecord { get; private set; }

        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            AppendCalls++;
            LastRecord = record;
            return ValueTask.CompletedTask;
        }

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AuditPage(Array.Empty<AuditRecord>(), null));
    }
}

