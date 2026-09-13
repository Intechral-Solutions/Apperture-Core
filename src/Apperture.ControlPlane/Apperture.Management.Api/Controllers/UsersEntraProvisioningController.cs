using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UsersEntraProvisioningController : ControllerBase
{
    public const int MaxBatchSize = IEntraProvisioningDirectoryService.MaxBatchSize;

    private readonly IEntraProvisioningDirectoryService _directory;
    private readonly IUserProvisioningService _provisioning;
    private readonly IEntraAccessRequestService _accessRequests;
    private readonly IEntraInstanceRegistrationRepository _registrations;

    public UsersEntraProvisioningController(
        IEntraProvisioningDirectoryService directory,
        IUserProvisioningService provisioning,
        IEntraAccessRequestService accessRequests,
        IEntraInstanceRegistrationRepository registrations)
    {
        _directory = directory;
        _provisioning = provisioning;
        _accessRequests = accessRequests;
        _registrations = registrations;
    }

    [HttpGet("entra/registrations")]
    public async Task<ActionResult<IReadOnlyList<EntraProvisioningRegistrationDto>>> ListRegistrations(
        CancellationToken cancellationToken)
    {
        try
        {
            await _accessRequests.ListPendingForReviewAsync(cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        var registrations = await _registrations.ListAsync(cancellationToken);
        return Ok(registrations
            .Where(r => r.Enabled)
            .Select(r => new EntraProvisioningRegistrationDto(r.Id, r.DisplayName, r.IsActive, r.AllowMemberAccessRequests, r.AllowGuestAccessRequests))
            .ToList());
    }

    [HttpGet("entra/directory")]
    public async Task<ActionResult<EntraProvisioningDirectoryPageDto>> SearchDirectory(
        [FromQuery] string registrationId,
        [FromQuery] string? search,
        [FromQuery] string? continuation,
        [FromQuery] int top = 25,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await _directory.SearchAsync(registrationId, search, continuation, top, cancellationToken);
            return Ok(ToDto(page));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("entra")]
    public async Task<ActionResult<EntraProvisioningBatchResultDto>> Provision(
        [FromBody] EntraDirectoryProvisionRequestBody body,
        CancellationToken cancellationToken)
    {
        var objectIds = (body.ObjectIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (string.IsNullOrWhiteSpace(body.RegistrationId) || objectIds.Count == 0)
            return BadRequest(new { message = "Choose at least one Microsoft directory user." });
        if (objectIds.Count > MaxBatchSize)
            return BadRequest(new { message = $"Choose at most {MaxBatchSize} people at a time." });

        var results = new List<EntraProvisioningPersonResultDto>();
        foreach (var objectId in objectIds)
        {
            try
            {
                var result = await _provisioning.ProvisionFromEntraDirectoryAsync(
                    new EntraDirectoryProvisioningRequest(
                        body.RegistrationId.Trim(),
                        objectId,
                        body.InitialRoleId,
                        body.SendWelcomeEmail),
                    cancellationToken);
                results.Add(new EntraProvisioningPersonResultDto(
                    true,
                    objectId,
                    result.UserId.Value,
                    result.DisplayName,
                    null,
                    null,
                    result.WelcomeMailAttempted,
                    result.WelcomeMailDelivered,
                    result.WelcomeMailFailureSummary));
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (UserProvisioningException ex)
            {
                results.Add(new EntraProvisioningPersonResultDto(
                    false, objectId, null, null, ex.Code, ex.Message, false, false, null));
            }
            catch (InvalidOperationException ex)
            {
                results.Add(new EntraProvisioningPersonResultDto(
                    false, objectId, null, null, "user.provisioning.failed", ex.Message, false, false, null));
            }
        }

        return Ok(new EntraProvisioningBatchResultDto(results));
    }

    [HttpGet("entra/access-requests")]
    public async Task<ActionResult<EntraAccessRequestListDto>> ListAccessRequests(CancellationToken cancellationToken)
    {
        try
        {
            var (items, count) = await _accessRequests.ListPendingForReviewAsync(cancellationToken);
            var registrations = (await _registrations.ListAsync(cancellationToken))
                .ToDictionary(r => r.Id, r => r.DisplayName, StringComparer.Ordinal);
            return Ok(new EntraAccessRequestListDto(
                count,
                items.Select(r => ToDto(r, registrations)).ToList()));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("entra/access-requests/{id}/approve")]
    public async Task<ActionResult<EntraProvisioningPersonResultDto>> Approve(
        string id,
        [FromBody] EntraAccessRequestDecisionBody body,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _accessRequests.ApproveAsync(id, body.InitialRoleId, body.SendWelcomeEmail, cancellationToken);
            return Ok(new EntraProvisioningPersonResultDto(
                true,
                result.ObjectId,
                result.UserId.Value,
                result.DisplayName,
                null,
                null,
                result.WelcomeMailAttempted,
                result.WelcomeMailDelivered,
                result.WelcomeMailFailureSummary));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (UserProvisioningException ex)
        {
            return Conflict(new { message = ex.Message, code = ex.Code });
        }
        catch (EntraAccessRequestException ex)
        {
            return Conflict(new { message = ex.Message, code = ex.Code });
        }
    }

    [HttpPost("entra/access-requests/{id}/reject")]
    public async Task<IActionResult> Reject(string id, CancellationToken cancellationToken)
    {
        try
        {
            await _accessRequests.RejectAsync(id, cancellationToken);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (EntraAccessRequestException ex)
        {
            return Conflict(new { message = ex.Message, code = ex.Code });
        }
    }

    private static EntraProvisioningDirectoryPageDto ToDto(EntraProvisioningDirectoryPage page) =>
        new(
            page.Success,
            page.Summary,
            page.TechnicalDetail,
            page.RegistrationId,
            page.RegistrationDisplayName,
            page.ContinuationToken,
            page.Items.Select(u => new EntraProvisioningDirectoryUserDto(
                u.ObjectId,
                u.DisplayName,
                u.UserPrincipalName,
                u.Mail,
                u.UserType,
                u.AccountEnabled,
                u.AppertureStatus.ToString(),
                u.ProvisionedUserId)).ToList());

    private static EntraAccessRequestItemDto ToDto(EntraAccessRequest request, IReadOnlyDictionary<string, string> names) =>
        new(
            request.Id,
            request.DisplayNameSnapshot,
            FirstNonEmpty(request.MailSnapshot, request.UserPrincipalNameSnapshot),
            names.TryGetValue(request.RegistrationId, out var name) ? name : "Microsoft Entra",
            request.UserTypeSnapshot,
            request.RequestedAtUtc,
            request.RegistrationId,
            request.TenantId,
            request.ProviderObjectId);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}

public sealed record EntraProvisioningRegistrationDto(
    string Id,
    string DisplayName,
    bool IsActive,
    bool AllowMemberAccessRequests,
    bool AllowGuestAccessRequests);

public sealed record EntraProvisioningDirectoryPageDto(
    bool Success,
    string Summary,
    string? TechnicalDetail,
    string RegistrationId,
    string? RegistrationDisplayName,
    string? ContinuationToken,
    IReadOnlyList<EntraProvisioningDirectoryUserDto> Items);

public sealed record EntraProvisioningDirectoryUserDto(
    string ObjectId,
    string DisplayName,
    string? UserPrincipalName,
    string? Mail,
    string? UserType,
    bool AccountEnabled,
    string AppertureStatus,
    string? ProvisionedUserId);

public sealed record EntraDirectoryProvisionRequestBody(
    string RegistrationId,
    IReadOnlyList<string>? ObjectIds,
    string? InitialRoleId,
    bool SendWelcomeEmail);

public sealed record EntraProvisioningBatchResultDto(IReadOnlyList<EntraProvisioningPersonResultDto> Results);

public sealed record EntraProvisioningPersonResultDto(
    bool Succeeded,
    string ObjectId,
    string? UserId,
    string? DisplayName,
    string? ErrorCode,
    string? ErrorMessage,
    bool WelcomeMailAttempted,
    bool WelcomeMailDelivered,
    string? WelcomeMailFailureSummary);

public sealed record EntraAccessRequestListDto(int PendingCount, IReadOnlyList<EntraAccessRequestItemDto> Items);

public sealed record EntraAccessRequestItemDto(
    string Id,
    string? DisplayName,
    string? DirectoryAddress,
    string RegistrationDisplayName,
    string? UserType,
    DateTimeOffset RequestedAtUtc,
    string RegistrationId,
    string TenantId,
    string ProviderObjectId);

public sealed record EntraAccessRequestDecisionBody(string? InitialRoleId, bool SendWelcomeEmail);
