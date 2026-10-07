using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
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
using Microsoft.Extensions.Logging;
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
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;
    private readonly IAuditEventWriter? _auditEvents;
    private readonly ILogger<ConfigEntraTenantsController>? _logger;

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
        IAuditedUnitOfWork? auditedUnitOfWork = null,
        IAuditEventWriter? auditEvents = null,
        ILogger<ConfigEntraTenantsController>? logger = null)
    {
        _logger = logger;
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
        _auditedUnitOfWork = auditedUnitOfWork;
        _auditEvents = auditEvents;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new InvalidOperationException("Entra registration changes require the audited unit of work.");

    private static AuditedOperation Operation(string primaryAction) => new(primaryAction, AuditActor.CurrentPrincipal);

    /// <summary>
    /// A guard refusal decided in the change's transaction: a W3 row of the administrator, written after the transaction
    /// rolled back. A failure to write it is logged by the writer and never changes the refusal.
    /// </summary>
    private async Task RecordRefusalAsync(Apperture.Core.Application.Audit.AuditEvent? refusal)
    {
        if (refusal is not null && _auditEvents is not null)
            await _auditEvents.TryWriteAsync(refusal, AuditActor.CurrentPrincipal, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Retires a client secret this registration owns and no longer names, outside any unit. Called only once it is known that
    /// no committed registration row names it. Best effort: a failure leaves an unused secret and is logged, and never changes
    /// the outcome of the registration change.
    /// </summary>
    private async Task RetireSecretAsync(string registrationId, string? secretRef)
    {
        if (string.IsNullOrWhiteSpace(secretRef))
            return;
        try
        {
            await _clientSecretProvisioner.TryDisableOwnedSecretAsync(registrationId, secretRef, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "An unused client secret of Entra registration {RegistrationId} could not be retired.", registrationId);
        }
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

        // AUD-1.2: the secret store runs its own transaction, so the secret is stored first, outside the unit, under the
        // registration id chosen here; the registration and its row then commit together. If nothing commits, the stored
        // secret is retired. The secret itself is never recorded.
        var registrationId = Guid.NewGuid().ToString("N");
        string? secretRef = null;
        if (!string.IsNullOrWhiteSpace(body.ClientSecret))
        {
            secretRef = await _clientSecretProvisioner
                .StoreNewAsync(registrationId, body.ClientSecret, cancellationToken)
                .ConfigureAwait(false);
        }

        var draft = new EntraInstanceRegistration
        {
            Id = registrationId,
            DisplayName = body.DisplayName.Trim(),
            Authority = body.Authority.Trim(),
            ClientId = body.ClientId.Trim(),
            UsesDeploymentSecret = body.UsesDeploymentSecret,
            ClientSecretRef = secretRef,
            CallbackPath = string.IsNullOrWhiteSpace(body.CallbackPath) ? null : body.CallbackPath.Trim(),
            Enabled = body.Enabled,
            IsActive = false,
            AzureTenantId = string.IsNullOrWhiteSpace(body.AzureTenantId) ? null : body.AzureTenantId.Trim(),
            AuthenticationContextId = authenticationContextId,
            AllowMemberAccessRequests = body.AllowMemberAccessRequests,
            AllowGuestAccessRequests = body.AllowGuestAccessRequests
        };

        EntraInstanceRegistration created;
        var verifiedCommit = false;
        try
        {
            created = await Unit.ExecuteAsync(
                Operation(AuditActionNames.TenantRegistrationCreated),
                async (transaction, ct) =>
                {
                    var added = await _repository.AddAsync(draft, ct);
                    await transaction.StageAsync(
                        AuditEvents.TenantRegistrationCreated(
                            added.Id,
                            added.DisplayName,
                            added.ClientId,
                            added.AzureTenantId,
                            added.Enabled,
                            clientAuthenticationReplaced: secretRef is not null,
                            added.AllowMemberAccessRequests,
                            added.AllowGuestAccessRequests),
                        ct);
                    return added;
                },
                async (_, ct) =>
                {
                    // Verified as committed: from here on the stored secret belongs to a committed registration.
                    verifiedCommit = true;
                    return await _repository.GetByIdAsync(registrationId, ct) ?? draft;
                },
                cancellationToken);
        }
        catch (Exception ex) when (secretRef is not null && !verifiedCommit && ex is not AuditOutcomeUnknownException)
        {
            // Nothing committed: retire the secret stored for it. An unknown outcome, or a failure after a verified commit,
            // may name it, so it stays.
            await RetireSecretAsync(registrationId, secretRef);
            throw;
        }

        var refreshed = await _repository.GetByIdAsync(created.Id, cancellationToken) ?? created;
        var dto = ToListItem(refreshed, DeploymentSecretPresent());
        return CreatedAtAction(nameof(GetById), new { id = refreshed.Id }, dto);
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

        // The secret store runs its own transaction and cannot join the unit. A supplied secret is therefore stored as a new
        // secret of its own, before the unit; the registration keeps resolving its current secret until the row naming the new
        // one commits. Afterwards exactly one of them is unused and is retired: the previous one after a confirmed commit, the
        // new one when nothing committed. After an unknown outcome either may be in use, so neither is retired.
        string? newSecretRef = null;
        if (!string.IsNullOrWhiteSpace(body.ClientSecret))
        {
            newSecretRef = await _clientSecretProvisioner
                .StoreNewAsync(existing.Id, body.ClientSecret, cancellationToken)
                .ConfigureAwait(false);
        }

        var legacy = AuthenticationConfigurationGuard.ReadLegacyEntraFromConfiguration(_configuration);
        ActionResult? refusal;
        Apperture.Core.Application.Audit.AuditEvent? refusalEvent = null;
        string? previousSecretRef = null;
        try
        {
            refusal = await Unit.ExecuteAsync(
                Operation(AuditActionNames.TenantRegistrationUpdated),
                async (transaction, ct) =>
                {
                    refusalEvent = null;
                    previousSecretRef = null;

                    // Every condition the change depends on is read in this transaction.
                    var current = await _repository.GetByIdAsync(id, ct);
                    if (current is null)
                    {
                        transaction.DeclareNoOp();
                        return NotFound();
                    }

                    if (current.RowVersion is null || !current.RowVersion.SequenceEqual(expected))
                        throw new DbUpdateConcurrencyException("Entra registration was modified by another request.");

                    var updatedModel = Updated(current, body, authenticationContextId, newSecretRef ?? current.ClientSecretRef);
                    var changed = ChangedSettings(current, updatedModel, secretReplaced: newSecretRef is not null);
                    if (changed.Count == 0)
                    {
                        // The same settings: nothing to write and no row (D7); the answer is the current registration.
                        transaction.DeclareNoOp();
                        return null;
                    }

                    var policy = await _policyRepository.GetAsync(ct);
                    var simulatedEffective = SimulateEffectiveEntraAfterUpdate(legacy, await _repository.ListAsync(ct), updatedModel);
                    var lockout = AuthenticationConfigurationGuard.ValidateNoLockout(_localOptions.Value, policy, simulatedEffective);
                    if (lockout is not null)
                    {
                        refusalEvent = AuditEvents.TenantRegistrationChangeRefused(current.Id, "update", "sign_in_lockout");
                        transaction.DeclareNoOp();
                        return BadRequest(new { message = lockout });
                    }

                    await _repository.UpdateAsync(updatedModel, expected, ct);
                    if (newSecretRef is not null)
                        previousSecretRef = current.ClientSecretRef;
                    var stillActive = current.IsActive && updatedModel.Enabled;
                    await transaction.StageAsync(
                        AuditEvents.TenantRegistrationUpdated(current.Id, updatedModel.DisplayName, changed, updatedModel.Enabled, stillActive),
                        ct);
                    if (current.AllowMemberAccessRequests != updatedModel.AllowMemberAccessRequests
                        || current.AllowGuestAccessRequests != updatedModel.AllowGuestAccessRequests)
                    {
                        await transaction.StageAsync(
                            AuditEvents.AccessRequestPolicyChanged(current.Id, updatedModel.AllowMemberAccessRequests, updatedModel.AllowGuestAccessRequests),
                            ct);
                    }

                    return null;
                },
                // A verified commit is the attempt that ran last, so previousSecretRef is the one its row replaced.
                (_, _) => Task.FromResult<ActionResult?>(null),
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await RetireSecretAsync(existing.Id, newSecretRef);
            return Conflict(new { message = "Registration was modified elsewhere. Refresh and try again." });
        }
        catch (Exception ex) when (ex is not AuditOutcomeUnknownException)
        {
            // Every other failure here is a confirmed rollback (a commit that could not be verified is outcome_unknown).
            await RetireSecretAsync(existing.Id, newSecretRef);
            throw;
        }

        if (refusal is not null)
        {
            await RetireSecretAsync(existing.Id, newSecretRef);
            await RecordRefusalAsync(refusalEvent);
            return refusal;
        }

        // Committed (or nothing to change, which a supplied secret never is): the replaced secret is no longer named.
        await RetireSecretAsync(existing.Id, previousSecretRef);

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

        var legacy = AuthenticationConfigurationGuard.ReadLegacyEntraFromConfiguration(_configuration);
        EntraInstanceRegistration? deleted = null;
        Apperture.Core.Application.Audit.AuditEvent? refusalEvent = null;
        var refusal = await Unit.ExecuteAsync<IActionResult?>(
            Operation(AuditActionNames.TenantRegistrationDeleted),
            async (transaction, ct) =>
            {
                deleted = null;
                refusalEvent = null;
                var existing = await _repository.GetByIdAsync(id, ct);
                if (existing is null)
                {
                    transaction.DeclareNoOp();
                    return null;
                }

                var policy = await _policyRepository.GetAsync(ct);
                var err = AuthenticationConfigurationGuard.ValidateDeleteActiveRegistration(
                    _localOptions.Value,
                    policy,
                    legacy,
                    existing.IsActive);
                if (err is not null)
                {
                    refusalEvent = AuditEvents.TenantRegistrationChangeRefused(existing.Id, "delete", "sign_in_lockout");
                    transaction.DeclareNoOp();
                    return BadRequest(new { message = err });
                }

                await _repository.DeleteAsync(id, ct);
                await transaction.StageAsync(AuditEvents.TenantRegistrationDeleted(existing.Id, existing.DisplayName, existing.IsActive), ct);
                deleted = existing;
                return null;
            },
            (_, _) => Task.FromResult<IActionResult?>(null),
            cancellationToken);

        if (refusal is not null)
        {
            await RecordRefusalAsync(refusalEvent);
            return refusal;
        }

        // After the delete committed, its stored secret is retired (best effort, outside the unit).
        if (deleted is not null)
            await RetireSecretAsync(deleted.Id, deleted.ClientSecretRef);

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

        var legacy = AuthenticationConfigurationGuard.ReadLegacyEntraFromConfiguration(_configuration);
        Apperture.Core.Application.Audit.AuditEvent? refusalEvent = null;
        var refusal = await Unit.ExecuteAsync<IActionResult?>(
            Operation(AuditActionNames.TenantRegistrationActivated),
            async (transaction, ct) =>
            {
                refusalEvent = null;
                var target = await _repository.GetByIdAsync(id, ct);
                if (target is null)
                {
                    transaction.DeclareNoOp();
                    return NotFound();
                }

                if (!target.Enabled)
                {
                    refusalEvent = AuditEvents.TenantRegistrationChangeRefused(target.Id, "activate", "registration_disabled");
                    transaction.DeclareNoOp();
                    return BadRequest(new { message = "Cannot activate a disabled registration." });
                }

                if (target.IsActive)
                {
                    // Already the active registration: nothing changes and no row (D7).
                    transaction.DeclareNoOp();
                    return null;
                }

                var policy = await _policyRepository.GetAsync(ct);
                var simulated = CloneEntra(legacy);
                simulated.Enabled = true;
                simulated.Authority = target.Authority;
                simulated.ClientId = target.ClientId;
                if (!string.IsNullOrWhiteSpace(target.CallbackPath))
                    simulated.CallbackPath = target.CallbackPath!;

                var lockout = AuthenticationConfigurationGuard.ValidateNoLockout(_localOptions.Value, policy, simulated);
                if (lockout is not null)
                {
                    refusalEvent = AuditEvents.TenantRegistrationChangeRefused(target.Id, "activate", "sign_in_lockout");
                    transaction.DeclareNoOp();
                    return BadRequest(new { message = lockout });
                }

                var previous = await _repository.GetActiveAsync(ct);
                await _repository.SetActiveAsync(id, ct);
                await transaction.StageAsync(AuditEvents.TenantRegistrationActivated(target.Id, previous?.Id), ct);
                return null;
            },
            (_, _) => Task.FromResult<IActionResult?>(null),
            cancellationToken);

        if (refusal is not null)
            await RecordRefusalAsync(refusalEvent);
        return refusal ?? NoContent();
    }

    /// <summary>The registration <paramref name="body"/> asks for, keeping everything the request does not set.</summary>
    private static EntraInstanceRegistration Updated(
        EntraInstanceRegistration existing,
        UpdateEntraTenantRequestBody body,
        string? authenticationContextId,
        string? clientSecretRef) =>
        new()
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
            ClientSecretRef = clientSecretRef
        };

    /// <summary>The names of the settings that differ between <paramref name="before"/> and <paramref name="after"/>.</summary>
    private static IReadOnlyList<string> ChangedSettings(EntraInstanceRegistration before, EntraInstanceRegistration after, bool secretReplaced)
    {
        var changed = new List<string>();
        void Compare(string name, string? a, string? b)
        {
            if (!string.Equals(a, b, StringComparison.Ordinal))
                changed.Add(name);
        }

        Compare("displayName", before.DisplayName, after.DisplayName);
        Compare("authority", before.Authority, after.Authority);
        Compare("clientId", before.ClientId, after.ClientId);
        if (secretReplaced || !string.Equals(before.ClientSecretRef, after.ClientSecretRef, StringComparison.Ordinal))
            changed.Add("clientAuthentication");
        if (before.UsesDeploymentSecret != after.UsesDeploymentSecret)
            changed.Add("usesDeploymentSecret");
        Compare("callbackPath", before.CallbackPath, after.CallbackPath);
        Compare("azureTenantId", before.AzureTenantId, after.AzureTenantId);
        Compare("authenticationContextId", before.AuthenticationContextId, after.AuthenticationContextId);
        if (before.Enabled != after.Enabled)
            changed.Add("enabled");
        if (before.AllowMemberAccessRequests != after.AllowMemberAccessRequests)
            changed.Add("allowMemberAccessRequests");
        if (before.AllowGuestAccessRequests != after.AllowGuestAccessRequests)
            changed.Add("allowGuestAccessRequests");
        return changed;
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
