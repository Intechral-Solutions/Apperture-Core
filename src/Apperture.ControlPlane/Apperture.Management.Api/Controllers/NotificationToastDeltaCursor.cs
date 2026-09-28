using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// Resolves the toast-delta composite cursor from the HTTP query string.
/// Presence of the pair is taken from the raw query, not from the bound
/// <see cref="DateTimeOffset"/>, so a missing parameter cannot be treated as a continuation.
/// </summary>
internal static class NotificationToastDeltaCursor
{
    internal const string MismatchedPairDetail =
        "afterCreatedAtUtc and afterId must both be provided or both omitted.";

    internal const string InvalidTimestampDetail =
        "afterCreatedAtUtc is not a valid timestamp.";

    internal static bool TryResolve(
        IQueryCollection query,
        DateTimeOffset? boundCreatedAtUtc,
        string? boundAfterId,
        out DateTimeOffset? createdAtUtc,
        out string? afterId,
        out string? error)
    {
        createdAtUtc = null;
        afterId = null;
        error = null;

        var rawTime = query["afterCreatedAtUtc"].ToString();
        var rawId = query["afterId"].ToString();
        var hasTime = !string.IsNullOrWhiteSpace(rawTime);
        var hasId = !string.IsNullOrWhiteSpace(rawId);

        if (hasTime != hasId)
        {
            error = MismatchedPairDetail;
            return false;
        }

        if (!hasTime)
            return true;

        if (boundCreatedAtUtc is not null)
        {
            createdAtUtc = boundCreatedAtUtc;
        }
        else if (TryParseTimestamp(rawTime, out var parsed))
        {
            createdAtUtc = parsed;
        }
        else
        {
            error = InvalidTimestampDetail;
            return false;
        }

        afterId = boundAfterId is not null && !string.IsNullOrWhiteSpace(boundAfterId)
            ? boundAfterId.Trim()
            : rawId.Trim();
        return true;
    }

    private static bool TryParseTimestamp(string raw, out DateTimeOffset parsed) =>
        DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces,
            out parsed)
        || DateTimeOffset.TryParse(
            raw,
            CultureInfo.CurrentCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces,
            out parsed);
}
