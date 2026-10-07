using Apperture.Core.Application;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// First-admin bootstrap endpoints. Succeed only when no platform users exist. No auth required for v1 bootstrap APIs.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public sealed class BootstrapController : ControllerBase
{
    private readonly IFirstAdminBootstrapService _bootstrap;
    private readonly IAuditEventWriter? _auditEvents;

    public BootstrapController(IFirstAdminBootstrapService bootstrap, IAuditEventWriter? auditEvents = null)
    {
        _bootstrap = bootstrap;
        _auditEvents = auditEvents;
    }

    /// <summary>
    /// Returns whether the platform has completed first-admin setup. v1 gate: at least one platform user exists.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(BootstrapStatusResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<BootstrapStatusResponse>> GetStatus(CancellationToken cancellationToken)
    {
        var status = await _bootstrap.GetSetupStatusAsync(cancellationToken);
        return Ok(ToStatusResponse(status));
    }

    /// <summary>
    /// Creates the first platform administrator. After setup, returns 409 with a stable error code.
    /// </summary>
    [HttpPost("first-admin")]
    public async Task<ActionResult<FirstAdminResponse>> CreateFirstAdmin(
        [FromBody] FirstAdminRequest request,
        CancellationToken cancellationToken)
    {
        var setupStatus = await _bootstrap.GetSetupStatusAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.ExternalId))
        {
            await RecordRefusalAsync(AuditEvents.FirstAdminRejectedInvalidRequest("missing_fields"));
            return BadRequest(new BootstrapErrorResponse(
                "DisplayName and ExternalId are required.",
                BootstrapErrorCodes.InvalidRequest,
                setupStatus.Initialized));
        }

        if (!Enum.TryParse<IdentitySource>(request.IdentitySource, ignoreCase: true, out var source) ||
            (source != IdentitySource.Entra && source != IdentitySource.Local))
        {
            await RecordRefusalAsync(AuditEvents.FirstAdminRejectedInvalidRequest("invalid_identity_source"));
            return BadRequest(new BootstrapErrorResponse(
                "IdentitySource must be 'Entra' or 'Local'.",
                BootstrapErrorCodes.InvalidRequest,
                setupStatus.Initialized));
        }

        FirstAdminBootstrapResult result;
        try
        {
            result = await _bootstrap.TryCreateFirstAdminAsync(
                request.DisplayName.Trim(),
                source,
                request.ExternalId.Trim(),
                request.Password,
                cancellationToken);
        }
        catch (PasswordPolicyException ex)
        {
            await RecordRefusalAsync(AuditEvents.FirstAdminRejectedInvalidRequest("password_policy"));
            return BadRequest(new BootstrapErrorResponse(
                ex.Message,
                BootstrapErrorCodes.InvalidRequest,
                setupStatus.Initialized));
        }
        catch (UsernameException ex)
        {
            await RecordRefusalAsync(AuditEvents.FirstAdminRejectedInvalidRequest("username_policy"));
            return BadRequest(new BootstrapErrorResponse(
                ex.Message,
                BootstrapErrorCodes.InvalidRequest,
                setupStatus.Initialized));
        }

        return result.Kind switch
        {
            // The created row committed with the user in the bootstrap service (AUD-1.2).
            FirstAdminBootstrapResultKind.Success => Created("/api/bootstrap/first-admin", new FirstAdminResponse(result.UserId!.Value)),
            FirstAdminBootstrapResultKind.AlreadyExists => await RejectAlreadyInitializedAsync(),
            FirstAdminBootstrapResultKind.RoleNotFound => StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new BootstrapErrorResponse(
                    "Platform administrator role not found; ensure migrations have been applied.",
                    BootstrapErrorCodes.RoleNotFound,
                    Initialized: true)),
            FirstAdminBootstrapResultKind.DuplicateIdentityLink => Conflict(new BootstrapErrorResponse(
                "An identity link with this source and external id already exists.",
                BootstrapErrorCodes.DuplicateIdentityLink,
                setupStatus.Initialized)),
            _ => StatusCode(
                StatusCodes.Status500InternalServerError,
                new BootstrapErrorResponse(
                    "Unexpected bootstrap result.",
                    BootstrapErrorCodes.Unexpected,
                    setupStatus.Initialized))
        };
    }

    private async Task<ActionResult<FirstAdminResponse>> RejectAlreadyInitializedAsync()
    {
        await RecordRefusalAsync(AuditEvents.FirstAdminRejectedAlreadyInitialized());
        return Conflict(new BootstrapErrorResponse(
            "Platform is already initialized. First-admin bootstrap is not available.",
            BootstrapErrorCodes.AlreadyInitialized,
            Initialized: true));
    }

    /// <summary>A refused request changed nothing: a W3 row of the anonymous caller that never changes the response.</summary>
    private async Task RecordRefusalAsync(Apperture.Core.Application.Audit.AuditEvent refusal)
    {
        if (_auditEvents is not null)
            await _auditEvents.TryWriteAsync(refusal, AuditActor.Anonymous, CancellationToken.None);
    }

    private static BootstrapStatusResponse ToStatusResponse(BootstrapSetupStatus status) =>
        new(status.Initialized, status.AllowsFirstAdmin);
}

/// <summary>
/// Stable bootstrap error codes for UI and automation.
/// </summary>
public static class BootstrapErrorCodes
{
    public const string AlreadyInitialized = "bootstrap.already_initialized";
    public const string InvalidRequest = "bootstrap.invalid_request";
    public const string DuplicateIdentityLink = "bootstrap.duplicate_identity_link";
    public const string RoleNotFound = "bootstrap.role_not_found";
    public const string Unexpected = "bootstrap.unexpected";
}

/// <summary>
/// Request to create the first platform administrator. When IdentitySource is Local, optional Password creates the local credential.
/// </summary>
public sealed record FirstAdminRequest(string DisplayName, string IdentitySource, string ExternalId, string? Password = null);

/// <summary>
/// Response when first admin is created (userId only; no secrets).
/// </summary>
public sealed record FirstAdminResponse(string UserId);

/// <summary>
/// Anonymous-safe bootstrap setup status.
/// </summary>
public sealed record BootstrapStatusResponse(bool Initialized, bool AllowsFirstAdmin);

/// <summary>
/// Structured bootstrap error payload for scripts and UI.
/// </summary>
public sealed record BootstrapErrorResponse(string Message, string Code, bool Initialized);
