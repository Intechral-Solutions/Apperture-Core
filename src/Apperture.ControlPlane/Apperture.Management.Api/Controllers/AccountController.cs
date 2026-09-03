using System.Security.Claims;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Models;
using Apperture.Management.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Self-service account surface for the signed-in operator: profile, personal preferences, avatar,
/// and personal notification preferences.
/// </summary>
/// <remarks>
/// Every action resolves the acting user from the authentication cookie via
/// <see cref="IPrincipalToUserResolver"/>. No route, query string, or body on this controller
/// accepts a user id, so there is no shape of request that lets one operator read or mutate another
/// operator's account.
/// </remarks>
[ApiController]
[Route("api/account")]
[Authorize]
public sealed class AccountController : ControllerBase
{
    private readonly IPrincipalContext _principalContext;
    private readonly IPrincipalToUserResolver _principalToUserResolver;
    private readonly IAccountProfileService _account;
    private readonly IRoleProvider _roleProvider;

    public AccountController(
        IPrincipalContext principalContext,
        IPrincipalToUserResolver principalToUserResolver,
        IAccountProfileService account,
        IRoleProvider roleProvider)
    {
        _principalContext = principalContext;
        _principalToUserResolver = principalToUserResolver;
        _account = account;
        _roleProvider = roleProvider;
    }

    [HttpGet("profile")]
    [ProducesResponseType(typeof(AccountProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AccountProfileDto>> GetProfile(CancellationToken cancellationToken)
    {
        var userId = await ResolveUserIdAsync(cancellationToken);
        if (userId is null)
            return Forbid();

        var snapshot = await _account.GetProfileAsync(userId, cancellationToken);
        if (snapshot is null)
            return Forbid();

        return Ok(await ComposeProfileAsync(userId, snapshot, cancellationToken));
    }

    [HttpPut("profile")]
    [ProducesResponseType(typeof(AccountProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AccountProfileDto>> UpdateProfile(
        [FromBody] UpdateAccountProfileRequest request,
        CancellationToken cancellationToken)
    {
        var userId = await ResolveUserIdAsync(cancellationToken);
        if (userId is null)
            return Forbid();

        AccountProfileSnapshot snapshot;
        try
        {
            snapshot = await _account.UpdateDisplayNameAsync(userId, request.DisplayName, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return ValidationProblem(nameof(request.DisplayName), ex.Message);
        }
        catch (EmergencyAccessException ex)
        {
            return Conflict(new { message = ex.Message, code = ex.Code });
        }

        return Ok(await ComposeProfileAsync(userId, snapshot, cancellationToken));
    }

    [HttpGet("preferences")]
    [ProducesResponseType(typeof(UserPreferencesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UserPreferencesDto>> GetPreferences(CancellationToken cancellationToken)
    {
        var userId = await ResolveUserIdAsync(cancellationToken);
        if (userId is null)
            return Forbid();

        var preferences = await _account.GetPreferencesAsync(userId, cancellationToken);
        return Ok(ToDto(preferences));
    }

    [HttpPut("preferences")]
    [ProducesResponseType(typeof(UserPreferencesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UserPreferencesDto>> UpdatePreferences(
        [FromBody] UpdateUserPreferencesRequest request,
        CancellationToken cancellationToken)
    {
        var userId = await ResolveUserIdAsync(cancellationToken);
        if (userId is null)
            return Forbid();

        try
        {
            var updated = await _account.UpdatePreferencesAsync(
                userId,
                new AccountPreferencesUpdate(request.Appearance, request.TimeFormat, request.TimeZoneId),
                cancellationToken);
            return Ok(ToDto(updated));
        }
        catch (ArgumentException ex)
        {
            return ValidationProblem("preferences", ex.Message);
        }
    }

    /// <summary>The fixed set of curated avatar illustrations. Static content; no user data.</summary>
    [HttpGet("avatar/options")]
    [ProducesResponseType(typeof(IReadOnlyList<AvatarOptionDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<AvatarOptionDto>> GetAvatarOptions() =>
        Ok(BuiltInAvatarCatalog.Options
            .Select(o => new AvatarOptionDto
            {
                Id = o.Id,
                DisplayName = o.DisplayName,
                Description = o.Description,
                IsDefault = string.Equals(o.Id, BuiltInAvatarCatalog.DefaultOptionId, StringComparison.Ordinal)
            })
            .ToList());

    [HttpPut("avatar/selection")]
    [ProducesResponseType(typeof(AccountAvatarDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AccountAvatarDto>> SelectAvatar(
        [FromBody] SelectAvatarRequest request,
        CancellationToken cancellationToken)
    {
        var userId = await ResolveUserIdAsync(cancellationToken);
        if (userId is null)
            return Forbid();

        try
        {
            var avatar = await _account.SelectBuiltInAvatarAsync(userId, request.OptionId, cancellationToken);
            return Ok(ToDto(avatar));
        }
        catch (ArgumentException ex)
        {
            return ValidationProblem(nameof(request.OptionId), ex.Message);
        }
        catch (EmergencyAccessException ex)
        {
            return Conflict(new { message = ex.Message, code = ex.Code });
        }
    }

    /// <summary>
    /// Accepts an untrusted image, normalizes it, and stores only the derived square renditions.
    /// The original bytes are never persisted.
    /// </summary>
    [HttpPost("avatar")]
    [RequestSizeLimit(ProfileImageLimits.MaxUploadBytes + 8192)]
    [ProducesResponseType(typeof(AccountAvatarDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<AccountAvatarDto>> UploadAvatar(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        var userId = await ResolveUserIdAsync(cancellationToken);
        if (userId is null)
            return Forbid();

        if (file is null || file.Length == 0)
            return ValidationProblem("file", "Choose an image to upload.");

        if (file.Length > ProfileImageLimits.MaxUploadBytes)
            return ValidationProblem(
                "file",
                $"The image must be {ProfileImageLimits.MaxUploadBytes / (1024 * 1024)} MB or smaller.");

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);

        AccountAvatarUploadResult result;
        try
        {
            result = await _account.UploadAvatarAsync(
                userId,
                new AccountAvatarUpload(file.FileName, file.ContentType, buffer.ToArray()),
                cancellationToken);
        }
        catch (EmergencyAccessException ex)
        {
            return Conflict(new { message = ex.Message, code = ex.Code });
        }

        if (!result.Succeeded)
            return ValidationProblem("file", result.Message ?? "That image could not be used.");

        return Ok(ToDto(result.Avatar));
    }

    [HttpDelete("avatar")]
    [ProducesResponseType(typeof(AccountAvatarDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AccountAvatarDto>> ResetAvatar(CancellationToken cancellationToken)
    {
        var userId = await ResolveUserIdAsync(cancellationToken);
        if (userId is null)
            return Forbid();

        try
        {
            var avatar = await _account.ResetAvatarAsync(userId, cancellationToken);
            return Ok(ToDto(avatar));
        }
        catch (EmergencyAccessException ex)
        {
            return Conflict(new { message = ex.Message, code = ex.Code });
        }
    }

    /// <summary>
    /// Serves one rendition of the caller's own normalized avatar.
    /// </summary>
    /// <remarks>
    /// There is no image key in the route. The image is located from the authenticated user's own
    /// record, which removes both key enumeration and any path-shaped input from the surface. The
    /// <c>v</c> parameter is a cache-busting token only: a new avatar produces a new token, which is
    /// why a matching token can be cached as immutable.
    /// </remarks>
    [HttpGet("avatar/image")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAvatarImage(
        [FromQuery] int size,
        [FromQuery] string? v,
        CancellationToken cancellationToken)
    {
        var userId = await ResolveUserIdAsync(cancellationToken);
        if (userId is null)
            return Forbid();

        if (!ProfileImageLimits.IsSupportedRenditionSize(size))
            return NotFound();

        var image = await _account.GetOwnAvatarImageAsync(userId, size, cancellationToken);
        if (image is null)
            return NotFound();

        Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        Response.Headers[HeaderNames.CacheControl] = string.IsNullOrWhiteSpace(v)
            ? "private, no-cache"
            : "private, max-age=31536000, immutable";

        return File(image.Content, image.ContentType);
    }

    [HttpGet("notification-preferences")]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationPreferenceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<NotificationPreferenceDto>>> GetNotificationPreferences(
        CancellationToken cancellationToken)
    {
        var userId = await ResolveUserIdAsync(cancellationToken);
        if (userId is null)
            return Forbid();

        var rows = await _account.GetNotificationPreferencesAsync(userId, cancellationToken);
        return Ok(rows.Select(ToDto).ToList());
    }

    [HttpPut("notification-preferences/{definitionId}")]
    [ProducesResponseType(typeof(NotificationPreferenceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationPreferenceDto>> SetNotificationPreference(
        string definitionId,
        [FromBody] SetNotificationPreferenceRequest request,
        CancellationToken cancellationToken)
    {
        var userId = await ResolveUserIdAsync(cancellationToken);
        if (userId is null)
            return Forbid();

        try
        {
            var updated = await _account.SetNotificationEmailPreferenceAsync(
                userId,
                definitionId,
                request.EmailEnabled,
                cancellationToken);
            return Ok(ToDto(updated));
        }
        catch (ArgumentException ex)
        {
            return ValidationProblem(nameof(definitionId), ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Notification not configurable",
                Detail = ex.Message
            });
        }
    }

    private async Task<AccountProfileDto> ComposeProfileAsync(
        UserId userId,
        AccountProfileSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var roles = await _roleProvider.GetRolesForUserAsync(userId, cancellationToken);
        return new AccountProfileDto
        {
            UserId = snapshot.UserId,
            DisplayName = snapshot.DisplayName,
            DisplayNameEditable = snapshot.DisplayNameEditable,
            // Email and principal come from the identity provider's claims, so they are presented
            // read-only rather than as fields the platform owns.
            EmailAddress = ResolveEmailClaim(User),
            UserPrincipal = ResolveUserPrincipal(User),
            SignInSource = AccountMeSnapshotBuilder.ResolveSignInSource(User),
            Avatar = ToDto(snapshot.Avatar),
            TimeZoneId = snapshot.DisplayTimeZoneId,
            IdentityLinks = snapshot.IdentityLinks
                .Select(l => new AccountIdentityLinkDetailDto
                {
                    Source = l.Source,
                    DisplayLabel = l.DisplayLabel,
                    ExternalId = l.ExternalId,
                    LastAuthenticatedAtUtc = l.LastAuthenticatedAtUtc
                })
                .ToList(),
            RoleNames = roles.Select(r => r.Name).ToList()
        };
    }

    private static AccountAvatarDto ToDto(UserAvatar avatar) => AccountAvatarDtoMapper.ToDto(avatar);

    private static UserPreferencesDto ToDto(AccountPreferencesSnapshot preferences) => new()
    {
        Appearance = preferences.Appearance.ToString(),
        TimeFormat = preferences.TimeFormat.ToString(),
        TimeZoneId = preferences.DisplayTimeZoneId
    };

    private static NotificationPreferenceDto ToDto(AccountNotificationPreference preference) => new()
    {
        DefinitionId = preference.DefinitionId,
        DisplayName = preference.DisplayName,
        Description = preference.Description,
        PlatformEnabled = preference.Platform.Enabled,
        PlatformEditable = preference.Platform.Editable,
        PlatformNotEditableReason = preference.Platform.NotEditableReason,
        EmailEnabled = preference.Email.Enabled,
        EmailEditable = preference.Email.Editable,
        EmailNotEditableReason = preference.Email.NotEditableReason
    };

    private ActionResult ValidationProblem(string field, string message)
    {
        ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }

    private async Task<UserId?> ResolveUserIdAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            return null;

        return await _principalToUserResolver.ResolveUserIdAsync(actor, cancellationToken);
    }

    private static string? ResolveEmailClaim(ClaimsPrincipal principal) =>
        principal.FindFirst("email")?.Value
        ?? principal.FindFirst(ClaimTypes.Email)?.Value;

    private static string? ResolveUserPrincipal(ClaimsPrincipal principal) =>
        principal.FindFirst("preferred_username")?.Value
        ?? principal.FindFirst("upn")?.Value
        ?? principal.FindFirst(ClaimTypes.Upn)?.Value
        ?? principal.FindFirst("email")?.Value
        ?? principal.FindFirst(ClaimTypes.Email)?.Value
        ?? principal.FindFirst("unique_name")?.Value
        ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
}

public sealed record UpdateAccountProfileRequest(string? DisplayName);

public sealed record UpdateUserPreferencesRequest(string? Appearance, string? TimeFormat, string? TimeZoneId);

public sealed record SelectAvatarRequest(string? OptionId);

public sealed record SetNotificationPreferenceRequest(bool EmailEnabled);
