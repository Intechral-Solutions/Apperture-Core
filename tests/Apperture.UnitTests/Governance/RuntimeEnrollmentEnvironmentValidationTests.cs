using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.UnitTests.Audit;
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
using Apperture.Core.Domain.Runtime;

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
            environment: new StubEnvironmentContext("local"),
            authorizationEvaluation: new StubAuthorizationEvaluationService(allowed: true));

        var result = await controller.CreateInvitation(new CreateInvitationRequest("rt-1"), CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task CreateInvitation_WhenNotAuthorized_Returns403_AndCreatesNothing()
    {
        var enrollment = new StubRuntimeEnrollmentService(token: "t-1");
        var controller = new EnrollController(
            enrollment: enrollment,
            principalContext: new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)),
            principalToUserResolver: new StubPrincipalToUserResolver(),
            roleProvider: new StubRoleProvider(),
            environment: new StubEnvironmentContext("local"),
            authorizationEvaluation: new StubAuthorizationEvaluationService(allowed: false));

        var result = await controller.CreateInvitation(
            new CreateInvitationRequest("rt-1", TtlHours: 1, Scopes: new HashSet<string> { RuntimeCapabilityCatalog.InspectionCapabilities }),
            CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
        enrollment.InvitationCalls.Should().Be(0);
    }

    [Fact]
    public async Task CreateInvitation_records_the_administrators_invitation_with_its_row_id_and_never_the_token()
    {
        var db = NewDb();
        var audit = new AuditRecording();
        var controller = new EnrollController(
            enrollment: new SqlRuntimeEnrollmentService(db, new StubEnvironmentContext("local"), audit),
            principalContext: new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)),
            principalToUserResolver: new StubPrincipalToUserResolver(),
            roleProvider: new StubRoleProvider(),
            environment: new StubEnvironmentContext("local"),
            authorizationEvaluation: new StubAuthorizationEvaluationService(allowed: true));

        var result = await controller.CreateInvitation(
            new CreateInvitationRequest("rt-1", TtlHours: 1, Scopes: new HashSet<string>
            {
                RuntimeCapabilityCatalog.InspectionCapabilities,
                RuntimeCapabilityCatalog.ControlShutdown
            }),
            CancellationToken.None);

        var invitation = result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<EnrollmentInvitation>().Which;
        var row = await db.EnrollmentTokens.SingleAsync();
        var (created, actor) = audit.Events.Should().ContainSingle().Subject;
        created.Action.Should().Be(GovernanceAuditActionNames.RuntimeEnrollmentInvitationCreated);
        actor.Should().Be(AuditActor.CurrentPrincipal);
        created.Target.Should().Be(new AuditTarget(AuditTargetTypes.RuntimeInstance, ScopeKeyConstants.RuntimeInstance("local", "rt-1")));
        created.Fields["runtimeId"].Should().Be("rt-1");
        created.Fields["invitationId"].Should().Be(row.Id.ToString("N"));
        created.Fields["capabilityCount"].Should().Be(2);
        created.Fields.Values.Should().NotContain(invitation.Token, "the enrollment token is a capability and is never recorded");
    }

    [Fact]
    public async Task CreateInvitation_WhenAuthorized_WithUnknownScopes_Returns400_AndCreatesNothing()
    {
        var enrollment = new StubRuntimeEnrollmentService(token: "t-1");
        var controller = new EnrollController(
            enrollment: enrollment,
            principalContext: new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)),
            principalToUserResolver: new StubPrincipalToUserResolver(),
            roleProvider: new StubRoleProvider(),
            environment: new StubEnvironmentContext("local"),
            authorizationEvaluation: new StubAuthorizationEvaluationService(allowed: true));

        var result = await controller.CreateInvitation(
            new CreateInvitationRequest(
                "rt-1",
                TtlHours: 1,
                Scopes: new HashSet<string> { "unknown.runtime.capability" }),
            CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        enrollment.InvitationCalls.Should().Be(0);
    }

    [Fact]
    public async Task EnrollAsync_records_the_completed_enrollment_as_anonymous_against_the_runtime()
    {
        var db = NewDb();
        var invitationId = Guid.NewGuid();
        db.EnrollmentTokens.Add(new EnrollmentTokenEntity
        {
            Id = invitationId,
            Token = "token-a",
            RuntimeId = "rt-a",
            EnvironmentId = "local",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Used = false,
            CreatedAt = DateTimeOffset.UtcNow,
            ScopesJson = "[]"
        });
        await db.SaveChangesAsync();
        var audit = new AuditRecording();
        var service = new SqlRuntimeEnrollmentService(db, new StubEnvironmentContext("local"), audit);

        var result = await service.EnrollAsync("token-a", new RuntimeIdentityRegistration("rt-name", "PublicKey", "pk", null));

        result.Success.Should().BeTrue();
        var (completed, actor) = audit.Events.Should().ContainSingle().Subject;
        completed.Action.Should().Be(AuditActionNames.RuntimeEnrollmentCompleted);
        actor.Should().Be(AuditActor.Anonymous, "the token is a capability, not an identity (D10)");
        completed.Target.Should().Be(new AuditTarget(AuditTargetTypes.RuntimeInstance, ScopeKeyConstants.RuntimeInstance("local", "rt-a")));
        completed.Fields["invitationId"].Should().Be(invitationId.ToString("N"));
        completed.Fields.Values.Should().NotContain("token-a");
    }

    [Fact]
    public async Task EnrollAsync_refused_writes_no_row()
    {
        var db = NewDb();
        var audit = new AuditRecording();
        var service = new SqlRuntimeEnrollmentService(db, new StubEnvironmentContext("local"), audit);

        var result = await service.EnrollAsync("no-such-token", new RuntimeIdentityRegistration("rt-name", "PublicKey", "pk", null));

        result.Success.Should().BeFalse();
        audit.Events.Should().BeEmpty();
        audit.Unit.Operations.Should().ContainSingle().Which.NoOp.Should().BeTrue();
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
            ScopesJson = JsonSerializer.Serialize(new[] { RuntimeCapabilityCatalog.InspectionCapabilities })
        });
        await db.SaveChangesAsync();

        var service = new SqlRuntimeEnrollmentService(db, new StubEnvironmentContext("local"), new AuditRecording());
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

        var scopes = new[]
        {
            RuntimeCapabilityCatalog.InspectionCapabilities,
            RuntimeCapabilityCatalog.ControlShutdown
        };
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

        var service = new SqlRuntimeEnrollmentService(db, new StubEnvironmentContext("local"), new AuditRecording());
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

        var enrollment = new SqlRuntimeEnrollmentService(db, environment, new AuditRecording());
        var identity = new SqlRuntimeIdentityService(db);

        var createInvitationAuthz = new StubAuthorizationEvaluationService(allowed: true);

        var enrollController = new EnrollController(
            enrollment: enrollment,
            principalContext: new StubPrincipalContext(new HumanActor(new ActorId("u1"), "u1", isBreakGlassSession: false)),
            principalToUserResolver: new StubPrincipalToUserResolver(),
            roleProvider: new StubRoleProvider(),
            environment: environment,
            authorizationEvaluation: createInvitationAuthz);

        var invitationResult = await enrollController.CreateInvitation(
            new CreateInvitationRequest("rt-3", TtlHours: 1, Scopes: new HashSet<string> { RuntimeCapabilityCatalog.InspectionCapabilities }),
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
        dto.Capabilities.Should().ContainSingle(c =>
            c.CapabilityId == RuntimeCapabilityCatalog.InspectionCapabilities &&
            c.Kind == RuntimeCapabilityKind.Inspection);
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

        public int InvitationCalls { get; private set; }

        public ValueTask<EnrollmentInvitation> CreateInvitationAsync(
            string runtimeId,
            TimeSpan? ttl = null,
            IReadOnlySet<string>? scopes = null,
            CancellationToken cancellationToken = default)
        {
            InvitationCalls++;
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
        public IReadOnlyList<(ResourceDescriptor Descriptor, ResourceFamily? Family)> GetResourceDescriptorsWithFamily(ResourceFamily? family = null) => Array.Empty<(ResourceDescriptor, ResourceFamily?)>();
        public IReadOnlyList<(PermissionDescriptor Descriptor, ResourceFamily? Family)> GetPermissionDescriptorsWithFamily(ResourceFamily? family = null) => Array.Empty<(PermissionDescriptor, ResourceFamily?)>();
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

        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AuditRecord?>(null);
    }
}

