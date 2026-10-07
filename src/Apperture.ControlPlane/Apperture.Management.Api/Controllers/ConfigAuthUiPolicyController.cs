using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Management.Api.Models;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Apperture.Management.Api.Controllers;

/// <summary>Instance sign-in UX policy (management UI chooser).</summary>
[ApiController]
[Route("api/config/auth-ui-policy")]
[Authorize]
public sealed class ConfigAuthUiPolicyController : ControllerBase
{
    private readonly IInstanceAuthUiPolicyRepository _policyRepository;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly LocalAuthOptions _localOptions;
    private readonly EntraAuthOptions _entraOptions;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;

    public ConfigAuthUiPolicyController(
        IInstanceAuthUiPolicyRepository policyRepository,
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IOptions<LocalAuthOptions> localOptions,
        IOptions<EntraAuthOptions> entraOptions,
        IAuditedUnitOfWork? auditedUnitOfWork = null)
    {
        _policyRepository = policyRepository;
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _localOptions = localOptions.Value;
        _entraOptions = entraOptions.Value;
        _auditedUnitOfWork = auditedUnitOfWork;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new InvalidOperationException("Sign-in policy changes require the audited unit of work.");

    [HttpGet]
    [ProducesResponseType(typeof(AuthUiPolicyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthUiPolicyResponse>> Get(CancellationToken cancellationToken)
    {
        if (!await AuthorizeReadAsync(cancellationToken))
            return Forbid();

        var p = await _policyRepository.GetAsync(cancellationToken);
        return Ok(ToDto(p));
    }

    [HttpPatch]
    [ProducesResponseType(typeof(AuthUiPolicyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthUiPolicyResponse>> Patch(
        [FromBody] UpdateAuthUiPolicyRequestBody body,
        CancellationToken cancellationToken)
    {
        if (!await AuthorizePolicyManageAsync(cancellationToken))
            return Forbid();

        var proposed = new InstanceAuthUiPolicy
        {
            ShowLocalSignInInPrimaryChooser = body.ShowLocalSignInInPrimaryChooser,
            PreferEntraInChooser = body.PreferEntraInChooser,
            AutoRedirectToEntra = body.AutoRedirectToEntra
        };

        var validationError = AuthSignInOptionsMerger.ValidatePolicyUpdate(proposed, _localOptions, _entraOptions);
        if (validationError is not null)
            return BadRequest(new { message = validationError });

        if (string.IsNullOrWhiteSpace(body.RowVersionBase64))
            return BadRequest(new { message = "rowVersionBase64 is required (GET the policy first)." });

        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(body.RowVersionBase64);
        }
        catch (FormatException)
        {
            return BadRequest(new { message = "rowVersionBase64 is not valid base64." });
        }

        // AUD-1.2: the policy and its row commit together; the same settings change nothing and write no row (D7).
        try
        {
            await Unit.ExecuteAsync(
                new AuditedOperation(AuditActionNames.SignInPolicyUpdated, AuditActor.CurrentPrincipal),
                async (transaction, ct) =>
                {
                    var current = await _policyRepository.GetAsync(ct);
                    if (current.RowVersion is null || !current.RowVersion.SequenceEqual(expected))
                        throw new DbUpdateConcurrencyException("Instance auth UI policy was modified by another request.");

                    var changed = new List<string>();
                    if (current.ShowLocalSignInInPrimaryChooser != proposed.ShowLocalSignInInPrimaryChooser)
                        changed.Add("showLocalSignInInPrimaryChooser");
                    if (current.PreferEntraInChooser != proposed.PreferEntraInChooser)
                        changed.Add("preferEntraInChooser");
                    if (current.AutoRedirectToEntra != proposed.AutoRedirectToEntra)
                        changed.Add("autoRedirectToEntra");
                    if (changed.Count == 0)
                    {
                        transaction.DeclareNoOp();
                        return false;
                    }

                    await _policyRepository.SaveAsync(proposed, expected, ct);
                    await transaction.StageAsync(
                        AuditEvents.SignInPolicyUpdated(
                            changed,
                            proposed.ShowLocalSignInInPrimaryChooser,
                            proposed.PreferEntraInChooser,
                            proposed.AutoRedirectToEntra),
                        ct);
                    return true;
                },
                (_, _) => Task.FromResult(true),
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Policy was modified elsewhere. Refresh and try again." });
        }

        var updated = await _policyRepository.GetAsync(cancellationToken);
        return Ok(ToDto(updated));
    }

    private async Task<bool> AuthorizeReadAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            return false;

        var read = new PermissionDescriptor(
            ActionConstants.AuthConfigurationRead,
            new ResourceDescriptor(ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance));

        if (await _authorizationEvaluation.EvaluateAsync(
                new AuthorizationEvaluationContext(actor, read, read.Resource),
                cancellationToken))
            return true;

        return await AuthorizePolicyManageAsync(cancellationToken);
    }

    private async Task<bool> AuthorizePolicyManageAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            return false;

        var permission = new PermissionDescriptor(
            ActionConstants.AuthUiPolicyManage,
            new ResourceDescriptor(ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance));

        return await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(actor, permission, permission.Resource),
            cancellationToken);
    }

    private static AuthUiPolicyResponse ToDto(InstanceAuthUiPolicy p) =>
        new()
        {
            ShowLocalSignInInPrimaryChooser = p.ShowLocalSignInInPrimaryChooser,
            PreferEntraInChooser = p.PreferEntraInChooser,
            AutoRedirectToEntra = p.AutoRedirectToEntra,
            RowVersionBase64 = p.RowVersion is { Length: > 0 } ? Convert.ToBase64String(p.RowVersion) : null
        };
}
