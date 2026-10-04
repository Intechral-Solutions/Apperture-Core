namespace Apperture.Core.Application.Services;

/// <summary>Audit action prefixes that never carry a ResourceContext by design (design gate §5.5).</summary>
public static class AuditSystemActionPrefixes
{
    public static readonly IReadOnlyList<string> All =
    [
        "auth.", "user.", "credential.", "identity_link.", "auth_admin.", "account.",
        "governance.", "audit.read.", "module.lifecycle.",
        "platform.settings.", "emergency.", "entra.", "identity."
    ];

    public static bool Matches(string? action) =>
        action is not null && All.Any(p => action.StartsWith(p, StringComparison.Ordinal));
}

/// <summary>
/// Inventory's Context filter, translated to an audit-log query predicate (design gate §5.5): classify
/// by <c>ResourceContextId</c> first, and only fall back to <see cref="AuditSystemActionPrefixes"/> when
/// it is null.
/// </summary>
public enum AuditContextClassificationFilter { None, System, NotRecorded }
