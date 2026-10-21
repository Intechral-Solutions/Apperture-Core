using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
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
    private readonly IInstanceAuthUiPolicyRepository _policyRepository;
    private readonly IEntraOpenIdConnectConfigurationValidator _openIdValidator;
    private readonly IEntraDirectoryDiscoveryService _directoryDiscovery;
    private readonly EntraClientSecretMaterialProvisioner _clientSecretProvisioner;
    private readonly ISecretResolver _secretResolver;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;
    private readonly IAuditEventWriter? _auditEvents;
    private readonly IAdministratorViabilityCoordinator? _viability;

    public ConfigEntraTenantsController(
        IEntraInstanceRegistrationRepository repository,
        IPrincipalContext principalContext,
        IAuthorizationEvaluationService authorizationEvaluation,
        IConfiguration configuration,
        IInstanceAuthUiPolicyRepository policyRepository,
        IEntraOpenIdConnectConfigurationValidator openIdValidator,
        IEntraDirectoryDiscoveryService directoryDiscovery,
        EntraClientSecretMaterialProvisioner clientSecretProvisioner,
        ISecretResolver secretResolver,
        IAuditedUnitOfWork? auditedUnitOfWork = null,
        IAuditEventWriter? auditEvents = null,
        IAdministratorViabilityCoordinator? viability = null,
        ILogger<ConfigEntraTenantsController>? logger = null)
    {
        _ = logger;
        _repository = repository;
        _principalContext = principalContext;
        _authorizationEvaluation = authorizationEvaluation;
        _configuration = configuration;
        _policyRepository = policyRepository;
        _openIdValidator = openIdValidator;
        _directoryDiscovery = directoryDiscovery;
        _clientSecretProvisioner = clientSecretProvisioner;
        _secretResolver = secretResolver;
        _auditedUnitOfWork = auditedUnitOfWork;
        _auditEvents = auditEvents;
        _viability = viability;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new InvalidOperationException("Entra registration changes require the audited unit of work.");

    // A registration change that cannot ask whether it leaves anybody able to sign in is not made: no coordinator is a defect of the
    // composition, never a reason to skip the check.
    private IAdministratorViabilityCoordinator Viability =>
        _viability ?? throw new InvalidOperationException("Entra registration changes require the administrator viability coordinator.");

    /// <summary>A change that the instance-level rule or administrator viability refused: the W3 row to write after the rollback, and the response.</summary>
    private sealed record RegistrationRefusal(Apperture.Core.Application.Audit.AuditEvent Event, ActionResult Result);

    /// <summary>
    /// The instance-level sign-in rules over one reading of the persisted configuration. A reading whose Local or Entra input is not
    /// established asserts nothing: administrator viability, which fails closed on it, is what decides then.
    /// </summary>
    private static string? InstanceRuleViolation(AdministratorViabilityState state, InstanceAuthUiPolicy policy)
    {
        if (!state.Configuration.Trust.LocalKnown || !state.Configuration.Trust.EntraKnown)
            return null;
        var restart = state.Configuration.Restart;
        return AuthenticationConfigurationGuard.ValidateNoLockout(restart.LocalSignInEnabled, restart.EntraSignInUsable, policy);
    }

    /// <summary>
    /// Decides a change that has been applied in the unit's transaction and is not yet staged: the instance-level rule first (400,
    /// as it has always been, now asked of the persisted state the change produced and refused only when the change newly breaks
    /// it), then administrator viability in both views (409, typed). Null allows the change.
    /// </summary>
    private async Task<RegistrationRefusal?> DecideAppliedChangeAsync(
        AdministratorViabilityScope viability,
        InstanceAuthUiPolicy policy,
        string registrationId,
        string change,
        CancellationToken ct)
    {
        var verdict = await viability.VerifyAfterAsync(ct);
        var before = viability.Captured ?? throw new InvalidOperationException("The state before the change was not captured.");
        var after = viability.After ?? throw new InvalidOperationException("The state after the change was not read.");

        var errorAfter = InstanceRuleViolation(after, policy);
        if (AuthenticationConfigurationGuard.NewlyViolates(InstanceRuleViolation(before, policy), errorAfter))
        {
            return new RegistrationRefusal(
                AuditEvents.TenantRegistrationChangeRefused(registrationId, change, "sign_in_lockout"),
                BadRequest(new { message = errorAfter }));
        }

        if (!verdict.IsAllowed)
        {
            return new RegistrationRefusal(
                AuditEvents.TenantRegistrationChangeRefused(registrationId, change, "sign_in_lockout"),
                GovernanceFailureResults.Map(ViabilityRefusal.For(verdict, ViabilityMutation.RegistrationChange)));
        }

        return null;
    }

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
    /// Inside the change's unit, retires the client secret this registration owns and is about to stop naming, and stages its
    /// Secrets-category row. The secret store joins the unit's transaction (AUD-1.4), so the retirement commits or rolls back
    /// with the change: nothing is retired for a change that does not commit.
    /// </summary>
    private async Task RetireOwnedSecretAsync(IAuditedTransaction transaction, string registrationId, string? secretRef, string reason, CancellationToken ct)
    {
        if (await _clientSecretProvisioner.TryDisableOwnedSecretAsync(registrationId, secretRef, ct).ConfigureAwait(false))
            await transaction.StageAsync(AuditEvents.TenantRegistrationClientSecretRetired(registrationId, reason), ct);
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
    [ProducesResponseType(StatusCodes.Status409Conflict)]
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

        // A supplied secret is stored inside the unit under the registration id chosen here. The secret store joins the unit's
        // transaction (AUD-1.4), so the secret, the registration naming it and their rows commit together, or none of them
        // does. Neither the secret nor its reference is recorded.
        var registrationId = Guid.NewGuid().ToString("N");
        var secretSupplied = !string.IsNullOrWhiteSpace(body.ClientSecret);
        EntraInstanceRegistration Draft(string? secretRef) => new()
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

        // The running Entra options are built before the unit opens, so that their first build can never read behind the unit's own
        // locks (R-1).
        await Viability.PrepareAsync(cancellationToken);

        // A verified commit is the attempt that ran last, so this is the secret its row names.
        string? storedSecretRef = null;
        EntraInstanceRegistration created;
        try
        {
            created = await Unit.ExecuteAsync(
                Operation(AuditActionNames.TenantRegistrationCreated),
                async (transaction, ct) =>
                {
                    // The administrator-set gate first (IAM-10.2b): the registration is a viability input, and this writer only adds to it.
                    await Viability.EnterAsync(ct);

                    // A new registration is never the active one, so it cannot change what the sign-in accepts. That is asserted of
                    // this writer's own effect (the active registration, before and after), not of the whole system: a configuration
                    // reload or a change of Recovery Access health is not this writer's doing.
                    var activeBefore = await _repository.GetActiveAsync(ct);
                    storedSecretRef = secretSupplied
                        ? await _clientSecretProvisioner.StoreNewAsync(registrationId, body.ClientSecret!, ct).ConfigureAwait(false)
                        : null;
                    var added = await _repository.AddAsync(Draft(storedSecretRef), ct);
                    RegistrationRaiseOnlyAssertion.RequireActiveRegistrationUnchanged(activeBefore, await _repository.GetActiveAsync(ct), added.Id);
                    await transaction.StageAsync(
                        AuditEvents.TenantRegistrationCreated(
                            added.Id,
                            added.DisplayName,
                            added.ClientId,
                            added.AzureTenantId,
                            added.Enabled,
                            clientAuthenticationReplaced: storedSecretRef is not null,
                            added.AllowMemberAccessRequests,
                            added.AllowGuestAccessRequests),
                        ct);
                    if (storedSecretRef is not null)
                        await transaction.StageAsync(AuditEvents.TenantRegistrationClientSecretStored(added.Id, "registration_created"), ct);
                    return added;
                },
                async (_, ct) => await _repository.GetByIdAsync(registrationId, ct) ?? Draft(storedSecretRef),
                cancellationToken);
        }
        catch (Exception ex) when (GovernanceFailureResults.IsGovernanceOutcome(ex))
        {
            return GovernanceFailureResults.Map(ex);
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

        // A supplied secret is stored as a new secret of its own, never as a version of the one the registration names, and only
        // once every guard has passed. The secret store joins the unit's transaction (AUD-1.4): the new secret, the row naming
        // it and the retirement of the secret it replaces commit together, or none of them does, so the registration resolves
        // its current secret until the change commits, and a refused, conflicting, failed or rolled-back change stores and
        // retires nothing. After an unknown outcome it is either all committed or none.
        var secretSupplied = !string.IsNullOrWhiteSpace(body.ClientSecret);

        // The running Entra options are built before the unit opens, so that their first build can never read behind the unit's own
        // locks (R-1).
        await Viability.PrepareAsync(cancellationToken);

        ActionResult? refusal;
        Apperture.Core.Application.Audit.AuditEvent? refusalEvent = null;
        try
        {
            refusal = await Unit.ExecuteAsync(
                Operation(AuditActionNames.TenantRegistrationUpdated),
                async (transaction, ct) =>
                {
                    refusalEvent = null;

                    // The administrator-set gate first (IAM-10.2b), before any registration row is read for the decision or locked.
                    var viability = await Viability.EnterAsync(ct);

                    // Every condition the change depends on is read in this transaction.
                    var current = await _repository.GetByIdAsync(id, ct);
                    if (current is null)
                    {
                        transaction.DeclareNoOp();
                        return NotFound();
                    }

                    if (current.RowVersion is null || !current.RowVersion.SequenceEqual(expected))
                        throw new DbUpdateConcurrencyException("Entra registration was modified by another request.");

                    // The guards and the change list do not depend on which secret the registration names.
                    var updatedModel = Updated(current, body, authenticationContextId, current.ClientSecretRef);
                    var changed = ChangedSettings(current, updatedModel, secretReplaced: secretSupplied);
                    if (changed.Count == 0)
                    {
                        // The same settings: nothing to write and no row (D7); the answer is the current registration.
                        transaction.DeclareNoOp();
                        return null;
                    }

                    // Apply, then verify (P-9): the change is written in this transaction, the persisted configuration it produces is
                    // read back, and a change that breaks an invariant is rolled back with everything it did, including a secret it
                    // stored. The state before is read under the gate and before the first write.
                    var policy = await _policyRepository.GetAsync(ct);
                    await viability.CaptureBeforeAsync(ct);

                    if (secretSupplied)
                    {
                        var newSecretRef = await _clientSecretProvisioner.StoreNewAsync(current.Id, body.ClientSecret!, ct).ConfigureAwait(false);
                        updatedModel = Updated(current, body, authenticationContextId, newSecretRef);
                    }

                    await _repository.UpdateAsync(updatedModel, expected, ct);

                    if (await DecideAppliedChangeAsync(viability, policy, current.Id, "update", ct) is { } refused)
                    {
                        refusalEvent = refused.Event;
                        transaction.DeclareNoOp();
                        return refused.Result;
                    }

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

                    if (secretSupplied)
                    {
                        await transaction.StageAsync(AuditEvents.TenantRegistrationClientSecretStored(current.Id, "registration_updated"), ct);
                        await RetireOwnedSecretAsync(transaction, current.Id, current.ClientSecretRef, "replaced", ct);
                    }

                    return null;
                },
                (_, _) => Task.FromResult<ActionResult?>(null),
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Registration was modified elsewhere. Refresh and try again." });
        }
        catch (Exception ex) when (GovernanceFailureResults.IsGovernanceOutcome(ex))
        {
            return GovernanceFailureResults.Map(ex);
        }

        if (refusal is not null)
        {
            await RecordRefusalAsync(refusalEvent);
            return refusal;
        }

        var refreshed = await _repository.GetByIdAsync(id, cancellationToken);
        return Ok(ToListItem(refreshed!, DeploymentSecretPresent()));
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        if (!await AuthorizeManageAsync(cancellationToken))
            return Forbid();

        // The running Entra options are built before the unit opens, so that their first build can never read behind the unit's own
        // locks (R-1).
        await Viability.PrepareAsync(cancellationToken);

        // The registration's own secret is retired in the delete's unit, so it is retired exactly when the delete commits.
        Apperture.Core.Application.Audit.AuditEvent? refusalEvent = null;
        IActionResult? refusal;
        try
        {
            refusal = await Unit.ExecuteAsync<IActionResult?>(
                Operation(AuditActionNames.TenantRegistrationDeleted),
                async (transaction, ct) =>
                {
                    refusalEvent = null;

                    // The administrator-set gate first (IAM-10.2b), before the registration row is read for the decision or locked.
                    var viability = await Viability.EnterAsync(ct);

                    var existing = await _repository.GetByIdAsync(id, ct);
                    if (existing is null)
                    {
                        transaction.DeclareNoOp();
                        return null;
                    }

                    // Apply, then verify (P-9): the persisted configuration without this registration is what is judged.
                    var policy = await _policyRepository.GetAsync(ct);
                    await viability.CaptureBeforeAsync(ct);
                    await _repository.DeleteAsync(id, ct);

                    if (await DecideAppliedChangeAsync(viability, policy, existing.Id, "delete", ct) is { } refused)
                    {
                        refusalEvent = refused.Event;
                        transaction.DeclareNoOp();
                        return refused.Result;
                    }

                    await transaction.StageAsync(AuditEvents.TenantRegistrationDeleted(existing.Id, existing.DisplayName, existing.IsActive), ct);
                    await RetireOwnedSecretAsync(transaction, existing.Id, existing.ClientSecretRef, "registration_deleted", ct);
                    return null;
                },
                (_, _) => Task.FromResult<IActionResult?>(null),
                cancellationToken);
        }
        catch (Exception ex) when (GovernanceFailureResults.IsGovernanceOutcome(ex))
        {
            return GovernanceFailureResults.Map(ex);
        }

        if (refusal is not null)
        {
            await RecordRefusalAsync(refusalEvent);
            return refusal;
        }

        return NoContent();
    }

    [HttpPost("{id}/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Activate(string id, CancellationToken cancellationToken)
    {
        if (!await AuthorizeManageAsync(cancellationToken))
            return Forbid();

        // The running Entra options are built before the unit opens, so that their first build can never read behind the unit's own
        // locks (R-1).
        await Viability.PrepareAsync(cancellationToken);

        Apperture.Core.Application.Audit.AuditEvent? refusalEvent = null;
        IActionResult? refusal;
        try
        {
            refusal = await Unit.ExecuteAsync<IActionResult?>(
                Operation(AuditActionNames.TenantRegistrationActivated),
                async (transaction, ct) =>
                {
                    refusalEvent = null;

                    // The administrator-set gate first (IAM-10.2b), before the registration rows are read for the decision or locked.
                    var viability = await Viability.EnterAsync(ct);

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

                    // Apply, then verify (P-9): the persisted configuration with this registration active is what is judged.
                    var policy = await _policyRepository.GetAsync(ct);
                    await viability.CaptureBeforeAsync(ct);
                    var previous = await _repository.GetActiveAsync(ct);
                    await _repository.SetActiveAsync(id, ct);

                    if (await DecideAppliedChangeAsync(viability, policy, target.Id, "activate", ct) is { } refused)
                    {
                        refusalEvent = refused.Event;
                        transaction.DeclareNoOp();
                        return refused.Result;
                    }

                    await transaction.StageAsync(AuditEvents.TenantRegistrationActivated(target.Id, previous?.Id), ct);
                    return null;
                },
                (_, _) => Task.FromResult<IActionResult?>(null),
                cancellationToken);
        }
        catch (Exception ex) when (GovernanceFailureResults.IsGovernanceOutcome(ex))
        {
            return GovernanceFailureResults.Map(ex);
        }

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
        if (!await AuthorizeDirectorySearchAsync(cancellationToken))
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
        if (!await AuthorizeDirectorySearchAsync(cancellationToken))
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

    /// <summary>
    /// The two directory searches call Microsoft Graph with the registered tenant's stored secret, so they have a permission of
    /// their own. Neither the read check nor the manage check reaches them, and there is no period in which either is accepted.
    /// </summary>
    private async Task<bool> AuthorizeDirectorySearchAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            return false;

        var search = new PermissionDescriptor(
            ActionConstants.AuthDirectorySearch,
            new ResourceDescriptor(ResourceTypeConstants.InstanceConfiguration, ScopeKeyConstants.Instance));

        return await _authorizationEvaluation.EvaluateAsync(
            new AuthorizationEvaluationContext(actor, search, search.Resource),
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
