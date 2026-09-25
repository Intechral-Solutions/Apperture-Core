using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Domain.Runtime;
using Apperture.ControlPlane.Security;
using Apperture.Core.Security.Trust;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Runtime enrollment endpoints. Short-lived tokens; operational auth uses registered identity.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class EnrollController : ControllerBase
{
    private readonly IRuntimeEnrollmentService _enrollment;
    private readonly IPrincipalContext _principalContext;
    private readonly IPrincipalToUserResolver _principalToUserResolver;
    private readonly IRoleProvider _roleProvider;
    private readonly IEnvironmentContext _environment;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly IAuditService _audit;

    public EnrollController(
        IRuntimeEnrollmentService enrollment,
        IPrincipalContext principalContext,
        IPrincipalToUserResolver principalToUserResolver,
        IRoleProvider roleProvider,
        IEnvironmentContext environment,
        IAuthorizationEvaluationService authorizationEvaluation,
        IAuditService audit)
    {
        _enrollment = enrollment;
        _principalContext = principalContext;
        _principalToUserResolver = principalToUserResolver;
        _roleProvider = roleProvider;
        _environment = environment;
        _authorizationEvaluation = authorizationEvaluation;
        _audit = audit;
    }

    /// <summary>
    /// Creates an enrollment invitation for a runtime. Requires an authenticated platform administrator.
    /// </summary>
    [HttpPost("invitation")]
    public async ValueTask<ActionResult<EnrollmentInvitation>> CreateInvitation(
        [FromBody] CreateInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            return Unauthorized();

        var environmentId = _environment.EnvironmentId;
        var runtimeScopeKey = ScopeKeyConstants.RuntimeInstance(environmentId, request.RuntimeId);
        var permission = new PermissionDescriptor(
            ActionConstants.RuntimeEnrollInvitationCreate,
            new ResourceDescriptor(ResourceTypeConstants.RuntimeInstance, runtimeScopeKey));

        var allowed = await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(actor, permission, permission.Resource),
            cancellationToken);
        if (!allowed)
            return Forbid();

        var ttl = request.TtlHours.HasValue ? TimeSpan.FromHours(request.TtlHours.Value) : (TimeSpan?)null;

        // Canonical runtime capability validation:
        // Invitation Scopes represent requested runtime capability ids and must be validated before persistence.
        IReadOnlySet<string>? validatedScopes = null;
        if (request.Scopes is not null)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var invalid = new List<string>();

            foreach (var scope in request.Scopes)
            {
                if (RuntimeCapabilityCatalog.TryResolve(scope, out var def))
                    set.Add(def.CapabilityId);
                else
                    invalid.Add(scope);
            }

            if (invalid.Count > 0)
                return BadRequest(new
                {
                    error = "UnknownRuntimeCapability",
                    invalidScopes = invalid.ToArray()
                });

            validatedScopes = set;
        }

        var invitation = await _enrollment.CreateInvitationAsync(
            request.RuntimeId,
            ttl,
            validatedScopes,
            cancellationToken).ConfigureAwait(false);

        var runtimeResource = new ResourceDescriptor(ResourceTypeConstants.RuntimeInstance, runtimeScopeKey);
        var record = new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            actor.Id,
            GovernanceAuditActionNames.RuntimeEnrollmentInvitationCreated,
            runtimeResource,
            "auth-admin",
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            $"runtimeId={request.RuntimeId} scopesCount={validatedScopes?.Count ?? 0}");
        await _audit.AppendAsync(record, cancellationToken);

        return Ok(invitation);
    }

    /// <summary>
    /// Enrolls a runtime using an enrollment token. Registers runtime identity. Anonymous so runtimes can enroll with a token.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [BrowserRequestTrustExempt("runtime enrollment: machine client, token-authenticated")]
    public async ValueTask<ActionResult<EnrollmentResult>> Enroll(
        [FromBody] EnrollRequest request,
        CancellationToken cancellationToken)
    {
        var registration = new RuntimeIdentityRegistration(
            request.RuntimeName,
            request.CredentialType ?? "PublicKey",
            request.PublicKeyOrThumbprint,
            request.ManagedIdentityPrincipalId);

        var result = await _enrollment.EnrollAsync(
            request.Token,
            registration,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }
}

/// <summary>
/// Request to create an enrollment invitation.
/// </summary>
public sealed record CreateInvitationRequest(
    string RuntimeId,
    int? TtlHours = null,
    IReadOnlySet<string>? Scopes = null);

/// <summary>
/// Request to enroll a runtime.
/// </summary>
public sealed record EnrollRequest(
    string Token,
    string RuntimeName,
    string PublicKeyOrThumbprint,
    string? CredentialType = null,
    string? ManagedIdentityPrincipalId = null);
