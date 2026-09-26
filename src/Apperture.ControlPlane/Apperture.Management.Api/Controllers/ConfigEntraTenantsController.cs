using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Management.Api.Models;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/config/auth/entra-tenants")]
[Authorize]
public sealed class ConfigEntraTenantsController : ControllerBase
{
    private readonly IEntraInstanceRegistrationRepository _repository;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationEvaluationService _authorizationEvaluation;
    private readonly IConfiguration _configuration;
    private readonly IOptions<LocalAuthOptions> _localOptions;
    private readonly IInstanceAuthUiPolicyRepository _policyRepository;
    private readonly IEntraOpenIdConnectConfigurationValidator _openIdValidator;
    private readonly IEntraDirectoryDiscoveryService _directoryDiscovery;
    private readonly EntraClientSecretMaterialProvisioner _clientSecretProvisioner;
    private readonly ISecretResolver _secretResolver;
    private readonly IAuditService? _audit;

    public ConfigEntraTenantsController(
        IEntraInstanceRegistrationRepository repository,
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IConfiguration configuration,
        IOptions<LocalAuthOptions> localOptions,
        IInstanceAuthUiPolicyRepository policyRepository,
        IEntraOpenIdConnectConfigurationValidator openIdValidator,
        IEntraDirectoryDiscoveryService directoryDiscovery,
        EntraClientSecretMaterialProvisioner clientSecretProvisioner,
        ISecretResolver secretResolver,
        IAuditService? audit = null)
    {
        _repository = repository;
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _configuration = configuration;
        _localOptions = localOptions;
        _policyRepository = policyRepository;
        _openIdValidator = openIdValidator;
        _directoryDiscovery = directoryDiscovery;
        _clientSecretProvisioner = clientSecretProvisioner;
        _secretResolver = secretResolver;
        _audit = audit;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<EntraTenantListItemResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<EntraTenantListItemResponse>>> List(CancellationToken cancellationToken)
    {
        if (!await AuthorizeReadAsync(cancellationToken))
            return Forbid();

        var list = await _repository.ListAsync(cancellationToken);
        var deploymentSecretConfigured = DeploymentSecretPresent();
        return Ok(list.Select(e => ToListItem(e, deploymentSecretConfigured)).ToList());
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(EntraTenantListItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EntraTenantListItemResponse>> GetById(string id, CancellationToken cancellationToken)
    {
        if (!await AuthorizeReadAsync(cancellationToken))
            return Forbid();

        var e = await _repository.GetByIdAsync(id, cancellationToken);
        if (e is null)
            return NotFound();

        return Ok(ToListItem(e, DeploymentSecretPresent()));
    }

    [HttpPost]
    [ProducesResponseType(typeof(EntraTenantListItemResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EntraTenantListItemResponse>> Create(
        [FromBody] CreateEntraTenantRequestBody body,
        CancellationToken cancellationToken)
    {
        if (!await AuthorizeManageAsync(cancellationToken))
            return Forbid();

        var err = AuthenticationConfigurationGuard.ValidateEntraRegistrationFields(
            body.DisplayName,
            body.Authority,
            body.ClientId,
            body.UsesDeploymentSecret);
        if (err is not null)
            return BadRequest(new { message = err });

        if (!TryParseAuthenticationContextId(body.AuthenticationContextId, out var authenticationContextId, out var contextError))
            return BadRequest(new { message = contextError });

        var draft = new EntraInstanceRegistration
        {
            DisplayName = body.DisplayName.Trim(),
            Authority = body.Authority.Trim(),
            ClientId = body.ClientId.Trim(),
            UsesDeploymentSecret = body.UsesDeploymentSecret,
            CallbackPath = string.IsNullOrWhiteSpace(body.CallbackPath) ? null : body.CallbackPath.Trim(),
            Enabled = body.Enabled,
            IsActive = false,
            AzureTenantId = string.IsNullOrWhiteSpace(body.AzureTenantId) ? null : body.AzureTenantId.Trim(),
            AuthenticationContextId = authenticationContextId,
            AllowMemberAccessRequests = body.AllowMemberAccessRequests,
            AllowGuestAccessRequests = body.AllowGuestAccessRequests
        };

        var created = await _repository.AddAsync(draft, cancellationToken);
        created = await PersistClientSecretIfProvidedAsync(created, body.ClientSecret, cancellationToken);

        var dto = ToListItem(created, DeploymentSecretPresent());
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, dto);
    }

    [HttpPatch("{id}")]
    [ProducesResponseType(typeof(EntraTenantListItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EntraTenantListItemResponse>> Patch(
        string id,
        [FromBody] UpdateEntraTenantRequestBody body,
        CancellationToken cancellationToken)
    {
        if (!await AuthorizeManageAsync(cancellationToken))
            return Forbid();

        var existing = await _repository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
            return NotFound();

        var err = AuthenticationConfigurationGuard.ValidateEntraRegistrationFields(
            body.DisplayName,
            body.Authority,
            body.ClientId,
            body.UsesDeploymentSecret);
        if (err is not null)
            return BadRequest(new { message = err });

        if (!TryParseAuthenticationContextId(body.AuthenticationContextId, out var authenticationContextId, out var contextError))
            return BadRequest(new { message = contextError });

        if (string.IsNullOrWhiteSpace(body.RowVersionBase64))
            return BadRequest(new { message = "rowVersionBase64 is required." });

        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(body.RowVersionBase64);
        }
        catch (FormatException)
        {
            return BadRequest(new { message = "rowVersionBase64 is not valid base64." });
        }

        var policy = await _policyRepository.GetAsync(cancellationToken);
        var legacy = AuthenticationConfigurationGuard.ReadLegacyEntraFromConfiguration(_configuration);

        var updatedModel = new EntraInstanceRegistration
        {
            Id = existing.Id,
            DisplayName = body.DisplayName.Trim(),
            Authority = body.Authority.Trim(),
            ClientId = body.ClientId.Trim(),
            UsesDeploymentSecret = body.UsesDeploymentSecret,
            CallbackPath = string.IsNullOrWhiteSpace(body.CallbackPath) ? null : body.CallbackPath.Trim(),
            AzureTenantId = string.IsNullOrWhiteSpace(body.AzureTenantId) ? null : body.AzureTenantId.Trim(),
            AuthenticationContextId = authenticationContextId,
            Enabled = body.Enabled,
            IsActive = existing.IsActive,
            RowVersion = existing.RowVersion,
            CreatedUtc = existing.CreatedUtc,
            UpdatedUtc = existing.UpdatedUtc,
            OpenIdConfigurationStatus = existing.OpenIdConfigurationStatus,
            LastOpenIdValidationUtc = existing.LastOpenIdValidationUtc,
            LastOpenIdValidationSummary = existing.LastOpenIdValidationSummary,
            LastOpenIdValidationDetail = existing.LastOpenIdValidationDetail,
            DirectoryDiscoveryStatus = existing.DirectoryDiscoveryStatus,
            LastDirectoryDiscoveryUtc = existing.LastDirectoryDiscoveryUtc,
            LastDirectoryDiscoverySummary = existing.LastDirectoryDiscoverySummary,
            LastDirectoryDiscoveryDetail = existing.LastDirectoryDiscoveryDetail,
            AllowMemberAccessRequests = body.AllowMemberAccessRequests,
            AllowGuestAccessRequests = body.AllowGuestAccessRequests,
            ClientSecretRef = existing.ClientSecretRef
        };

        if (!string.IsNullOrWhiteSpace(body.ClientSecret))
        {
            var (secretRef, _) = await _clientSecretProvisioner
                .ReplaceAsync(existing.Id, body.ClientSecret, existing.ClientSecretRef, cancellationToken)
                .ConfigureAwait(false);
            updatedModel = WithClientSecretRef(updatedModel, secretRef);
        }

        // If disabling or breaking the active registration, ensure we do not lock the instance out.
        var simulatedEffective = SimulateEffectiveEntraAfterUpdate(legacy, await _repository.ListAsync(cancellationToken), updatedModel);
        var lockout = AuthenticationConfigurationGuard.ValidateNoLockout(_localOptions.Value, policy, simulatedEffective);
        if (lockout is not null)
            return BadRequest(new { message = lockout });

        try
        {
            await _repository.UpdateAsync(updatedModel, expected, cancellationToken);
            if (_audit is not null
                && (existing.AllowMemberAccessRequests != body.AllowMemberAccessRequests
                    || existing.AllowGuestAccessRequests != body.AllowGuestAccessRequests))
            {
                var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
                if (actor is not null)
                {
                    await _audit.AppendAsync(
                        new AuditRecord(
                            new AuditRecordId(Guid.NewGuid().ToString("N")),
                            new ActorId(actor.Id.Value),
                            EntraAccessRequestAuditActionNames.PolicyChanged,
                            new ResourceDescriptor(ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance),
                            EntraAccessRequestAuditActionNames.Outcome,
                            DateTimeOffset.UtcNow,
                            new CorrelationId(null),
                            $"registrationId={existing.Id} members={body.AllowMemberAccessRequests} guests={body.AllowGuestAccessRequests}"),
                        cancellationToken);
                }
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Registration was modified elsewhere. Refresh and try again." });
        }

        var refreshed = await _repository.GetByIdAsync(id, cancellationToken);
        return Ok(ToListItem(refreshed!, DeploymentSecretPresent()));
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        if (!await AuthorizeManageAsync(cancellationToken))
            return Forbid();

        var existing = await _repository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
            return NoContent();

        var policy = await _policyRepository.GetAsync(cancellationToken);
        var legacy = AuthenticationConfigurationGuard.ReadLegacyEntraFromConfiguration(_configuration);
        var err = AuthenticationConfigurationGuard.ValidateDeleteActiveRegistration(
            _localOptions.Value,
            policy,
            legacy,
            existing.IsActive);
        if (err is not null)
            return BadRequest(new { message = err });

        await _clientSecretProvisioner
            .TryDisableOwnedSecretAsync(existing.Id, existing.ClientSecretRef, cancellationToken)
            .ConfigureAwait(false);

        await _repository.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id}/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Activate(string id, CancellationToken cancellationToken)
    {
        if (!await AuthorizeManageAsync(cancellationToken))
            return Forbid();

        var target = await _repository.GetByIdAsync(id, cancellationToken);
        if (target is null)
            return NotFound();

        if (!target.Enabled)
            return BadRequest(new { message = "Cannot activate a disabled registration." });

        var policy = await _policyRepository.GetAsync(cancellationToken);
        var legacy = AuthenticationConfigurationGuard.ReadLegacyEntraFromConfiguration(_configuration);
        var simulated = CloneEntra(legacy);
        simulated.Enabled = true;
        simulated.Authority = target.Authority;
        simulated.ClientId = target.ClientId;
        if (!string.IsNullOrWhiteSpace(target.CallbackPath))
            simulated.CallbackPath = target.CallbackPath!;

        var lockout = AuthenticationConfigurationGuard.ValidateNoLockout(_localOptions.Value, policy, simulated);
        if (lockout is not null)
            return BadRequest(new { message = lockout });

        try
        {
            await _repository.SetActiveAsync(id, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        return NoContent();
    }

    [HttpPost("{id}/validate-openid")]
    [ProducesResponseType(typeof(ValidateOpenIdResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ValidateOpenIdResponse>> ValidateOpenId(
        string id,
        CancellationToken cancellationToken)
    {
        if (!await AuthorizeManageAsync(cancellationToken))
            return Forbid();

        var reg = await _repository.GetByIdAsync(id, cancellationToken);
        if (reg is null)
            return NotFound();

        var secretConfigured = EntraClientSecretAccess.IsConfigured(
            reg.ClientSecretRef,
            reg.UsesDeploymentSecret,
            DeploymentSecretPresent());

        var outcome = await _openIdValidator.ValidateAsync(
            reg.Authority,
            reg.ClientId,
            reg.UsesDeploymentSecret,
            secretConfigured,
            reg.AzureTenantId,
            cancellationToken);

        var status = outcome.Status;

        await _repository.UpdateOpenIdValidationOutcomeAsync(
            id,
            status,
            DateTimeOffset.UtcNow,
            outcome.Summary,
            outcome.TechnicalDetail,
            cancellationToken);

        var refreshed = await _repository.GetByIdAsync(id, cancellationToken);
        if (refreshed is null)
            return NotFound();

        return Ok(new ValidateOpenIdResponse
        {
            Validation = new ValidationResultResponse
            {
                Success = outcome.Success,
                OpenIdConfigurationStatus = status.ToString(),
                Code = outcome.Code,
                Summary = outcome.Summary,
                TechnicalDetail = outcome.TechnicalDetail
            },
            Tenant = ToListItem(refreshed, DeploymentSecretPresent())
        });
    }

    // POST, not GET: each search calls Microsoft Graph with the stored secret and records the discovery outcome.
    [HttpPost("{id}/directory/users")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(EntraDirectoryUsersApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<EntraDirectoryUsersApiResponse>> SearchDirectoryUsers(
        string id,
        [FromBody] EntraDirectorySearchRequestBody request,
        CancellationToken cancellationToken = default)
    {
        if (!await AuthorizeReadAsync(cancellationToken))
            return Forbid();

        return await SearchDirectoryUsersCoreAsync(id, request.Search, request.Top, cancellationToken);
    }

    [HttpPost("{id}/directory/groups")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(EntraDirectoryGroupsApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<EntraDirectoryGroupsApiResponse>> SearchDirectoryGroups(
        string id,
        [FromBody] EntraDirectorySearchRequestBody request,
        CancellationToken cancellationToken = default)
    {
        if (!await AuthorizeReadAsync(cancellationToken))
            return Forbid();

        return await SearchDirectoryGroupsCoreAsync(id, request.Search, request.Top, cancellationToken);
    }

    private async Task<ActionResult<EntraDirectoryUsersApiResponse>> SearchDirectoryUsersCoreAsync(
        string id,
        string? search,
        int top,
        CancellationToken cancellationToken)
    {
        var reg = await _repository.GetByIdAsync(id, cancellationToken);
        if (reg is null)
            return NotFound();

        var secret = await ResolveClientSecretAsync(reg, cancellationToken);
        var tenantId = EntraDirectoryTenantResolver.TryResolveGraphTenantId(reg) ?? "";
        var ctx = new EntraDirectoryDiscoveryContext(tenantId, reg.ClientId, secret, reg.Id);

        var result = await _directoryDiscovery.SearchUsersAsync(ctx, search, top, cancellationToken);
        var discStatus = ToDirectoryDiscoveryStatus(result);
        await _repository.UpdateDirectoryDiscoveryOutcomeAsync(
            id,
            discStatus,
            DateTimeOffset.UtcNow,
            result.Summary,
            result.TechnicalDetail,
            cancellationToken);

        var items = (result.Data ?? Array.Empty<EntraDirectoryUser>())
            .Select(u => new EntraDirectoryUserResponse
            {
                ObjectId = u.ObjectId,
                DisplayName = u.DisplayName,
                UserPrincipalName = u.UserPrincipalName,
                Mail = u.Mail
            })
            .ToList();

        return Ok(new EntraDirectoryUsersApiResponse
        {
            Success = result.Success,
            FailureCategory = result.Success ? null : result.FailureCategory.ToString(),
            Summary = result.Summary,
            TechnicalDetail = result.TechnicalDetail,
            Items = items
        });
    }

    private async Task<ActionResult<EntraDirectoryGroupsApiResponse>> SearchDirectoryGroupsCoreAsync(
        string id,
        string? search,
        int top,
        CancellationToken cancellationToken)
    {
        var reg = await _repository.GetByIdAsync(id, cancellationToken);
        if (reg is null)
            return NotFound();

        var secret = await ResolveClientSecretAsync(reg, cancellationToken);
        var tenantId = EntraDirectoryTenantResolver.TryResolveGraphTenantId(reg) ?? "";
        var ctx = new EntraDirectoryDiscoveryContext(tenantId, reg.ClientId, secret, reg.Id);

        var result = await _directoryDiscovery.SearchGroupsAsync(ctx, search, top, cancellationToken);
        var discStatus = ToDirectoryDiscoveryStatus(result);
        await _repository.UpdateDirectoryDiscoveryOutcomeAsync(
            id,
            discStatus,
            DateTimeOffset.UtcNow,
            result.Summary,
            result.TechnicalDetail,
            cancellationToken);

        var items = (result.Data ?? Array.Empty<EntraDirectoryGroup>())
            .Select(g => new EntraDirectoryGroupResponse
            {
                ObjectId = g.ObjectId,
                DisplayName = g.DisplayName,
                MailNickname = g.MailNickname,
                Description = g.Description
            })
            .ToList();

        return Ok(new EntraDirectoryGroupsApiResponse
        {
            Success = result.Success,
            FailureCategory = result.Success ? null : result.FailureCategory.ToString(),
            Summary = result.Summary,
            TechnicalDetail = result.TechnicalDetail,
            Items = items
        });
    }

    private static DirectoryDiscoveryStatus ToDirectoryDiscoveryStatus<T>(DirectoryDiscoveryResult<T> result)
    {
        if (result.Success)
            return DirectoryDiscoveryStatus.Success;
        return result.FailureCategory switch
        {
            DiscoveryFailureCategory.Configuration => DirectoryDiscoveryStatus.FailedConfiguration,
            DiscoveryFailureCategory.Permissions => DirectoryDiscoveryStatus.FailedPermissions,
            DiscoveryFailureCategory.Transient => DirectoryDiscoveryStatus.TransientFailure,
            DiscoveryFailureCategory.Throttled => DirectoryDiscoveryStatus.Throttled,
            _ => DirectoryDiscoveryStatus.Unknown
        };
    }

    private static EntraAuthOptions SimulateEffectiveEntraAfterUpdate(
        EntraAuthOptions legacy,
        IReadOnlyList<EntraInstanceRegistration> all,
        EntraInstanceRegistration updated)
    {
        var mergedList = all.Select(r => r.Id == updated.Id ? updated : r).ToList();
        var active = mergedList.FirstOrDefault(r => r.IsActive && r.Enabled);
        var o = CloneEntra(legacy);
        if (active is null || !active.Enabled)
            return o;

        o.Authority = active.Authority;
        o.ClientId = active.ClientId;
        o.Enabled = true;
        o.AuthenticationContextId = active.AuthenticationContextId;
        if (!string.IsNullOrWhiteSpace(active.CallbackPath))
            o.CallbackPath = active.CallbackPath!;
        return o;
    }

    private static EntraAuthOptions CloneEntra(EntraAuthOptions source) =>
        new()
        {
            Enabled = source.Enabled,
            Authority = source.Authority,
            ClientId = source.ClientId,
            ClientSecret = source.ClientSecret,
            AzureTenantId = source.AzureTenantId,
            AuthenticationContextId = source.AuthenticationContextId,
            CallbackPath = source.CallbackPath,
            Scopes = source.Scopes?.ToArray() ?? ["openid", "profile"]
        };

    private static EntraTenantListItemResponse ToListItem(EntraInstanceRegistration e, bool deploymentSecretConfigured)
    {
        var clientSecretConfigured = EntraClientSecretAccess.IsConfigured(
            e.ClientSecretRef,
            e.UsesDeploymentSecret,
            deploymentSecretConfigured);
        return new()
        {
            Id = e.Id,
            DisplayName = e.DisplayName,
            Authority = e.Authority,
            ClientId = e.ClientId,
            UsesDeploymentSecret = e.UsesDeploymentSecret,
            CallbackPath = e.CallbackPath,
            Enabled = e.Enabled,
            IsActive = e.IsActive,
            DeploymentSecretConfigured = e.UsesDeploymentSecret && deploymentSecretConfigured,
            ClientSecretConfigured = clientSecretConfigured,
            AzureTenantId = e.AzureTenantId,
            AuthenticationContextId = e.AuthenticationContextId,
            OpenIdConfigurationStatus = e.OpenIdConfigurationStatus.ToString(),
            LastOpenIdValidationUtc = e.LastOpenIdValidationUtc,
            LastOpenIdValidationSummary = e.LastOpenIdValidationSummary,
            LastOpenIdValidationDetail = e.LastOpenIdValidationDetail,
            DirectoryDiscoveryStatus = e.DirectoryDiscoveryStatus.ToString(),
            LastDirectoryDiscoveryUtc = e.LastDirectoryDiscoveryUtc,
            LastDirectoryDiscoverySummary = e.LastDirectoryDiscoverySummary,
            LastDirectoryDiscoveryDetail = e.LastDirectoryDiscoveryDetail,
            RowVersionBase64 = e.RowVersion is { Length: > 0 } ? Convert.ToBase64String(e.RowVersion) : null,
            AllowMemberAccessRequests = e.AllowMemberAccessRequests,
            AllowGuestAccessRequests = e.AllowGuestAccessRequests
        };
    }

    private static bool TryParseAuthenticationContextId(string? raw, out string? normalized, out string? error)
    {
        normalized = null;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        normalized = EntraAuthenticationContextId.TryNormalize(raw);
        if (normalized is not null)
            return true;

        error = "Authentication context identifier must be a Microsoft identifier of the form c1–c25.";
        return false;
    }

    private bool DeploymentSecretPresent() =>
        !string.IsNullOrWhiteSpace(
            _configuration[EntraAuthOptions.SectionName + ":" + nameof(EntraAuthOptions.ClientSecret)]);

    private async Task<string> ResolveClientSecretAsync(
        EntraInstanceRegistration registration,
        CancellationToken cancellationToken)
    {
        var resolved = await EntraClientSecretAccess
            .TryResolveAsync(
                registration,
                _secretResolver,
                _configuration[EntraAuthOptions.SectionName + ":" + nameof(EntraAuthOptions.ClientSecret)],
                cancellationToken)
            .ConfigureAwait(false);
        return resolved ?? "";
    }

    private async Task<EntraInstanceRegistration> PersistClientSecretIfProvidedAsync(
        EntraInstanceRegistration created,
        string? clientSecret,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientSecret))
            return created;

        var (secretRef, _) = await _clientSecretProvisioner
            .ReplaceAsync(created.Id, clientSecret, created.ClientSecretRef, cancellationToken)
            .ConfigureAwait(false);
        await _repository.UpdateAsync(WithClientSecretRef(created, secretRef), created.RowVersion, cancellationToken)
            .ConfigureAwait(false);
        return await _repository.GetByIdAsync(created.Id, cancellationToken).ConfigureAwait(false) ?? created;
    }

    private static EntraInstanceRegistration WithClientSecretRef(EntraInstanceRegistration source, string? clientSecretRef) =>
        new()
        {
            Id = source.Id,
            DisplayName = source.DisplayName,
            Authority = source.Authority,
            ClientId = source.ClientId,
            UsesDeploymentSecret = source.UsesDeploymentSecret,
            ClientSecretRef = clientSecretRef,
            CallbackPath = source.CallbackPath,
            Enabled = source.Enabled,
            IsActive = source.IsActive,
            RowVersion = source.RowVersion,
            CreatedUtc = source.CreatedUtc,
            UpdatedUtc = source.UpdatedUtc,
            AzureTenantId = source.AzureTenantId,
            OpenIdConfigurationStatus = source.OpenIdConfigurationStatus,
            LastOpenIdValidationUtc = source.LastOpenIdValidationUtc,
            LastOpenIdValidationSummary = source.LastOpenIdValidationSummary,
            LastOpenIdValidationDetail = source.LastOpenIdValidationDetail,
            DirectoryDiscoveryStatus = source.DirectoryDiscoveryStatus,
            LastDirectoryDiscoveryUtc = source.LastDirectoryDiscoveryUtc,
            LastDirectoryDiscoverySummary = source.LastDirectoryDiscoverySummary,
            LastDirectoryDiscoveryDetail = source.LastDirectoryDiscoveryDetail
        };

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

        var manage = new PermissionDescriptor(
            ActionConstants.AuthEntraManage,
            new ResourceDescriptor(ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance));

        return await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(actor, manage, manage.Resource),
            cancellationToken);
    }

    private async Task<bool> AuthorizeManageAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            return false;

        var manage = new PermissionDescriptor(
            ActionConstants.AuthEntraManage,
            new ResourceDescriptor(ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance));

        return await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(actor, manage, manage.Resource),
            cancellationToken);
    }
}
