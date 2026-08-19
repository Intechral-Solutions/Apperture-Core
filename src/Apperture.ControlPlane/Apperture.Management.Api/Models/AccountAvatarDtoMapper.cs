using Apperture.Core.Domain.Identity;
using Apperture.Management.Client;

namespace Apperture.Management.Api.Models;

/// <summary>
/// Single place that turns a domain avatar into its wire form, so the session response and the
/// account endpoints can never disagree about a user's avatar or its cache-busting token.
/// </summary>
public static class AccountAvatarDtoMapper
{
    public static AccountAvatarDto ToDto(UserAvatar avatar) => new()
    {
        Kind = avatar.StorageKind,
        BuiltInOptionId = avatar.BuiltInOptionId,
        Version = avatar.VersionToken,
        // A relative API path, never a storage location. Built-in artwork is drawn by the client, so
        // only a custom photo needs a fetchable URL.
        ImageUrl = avatar.Kind == UserAvatarKind.Custom
            ? BuildImageUrl(AccountAvatarDto.DefaultImageSize, avatar.VersionToken)
            : null
    };

    public static string BuildImageUrl(int size, string version) =>
        $"api/account/avatar/image?size={size}&v={Uri.EscapeDataString(version)}";
}
