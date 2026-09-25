namespace Apperture.Management.Host;

internal enum BrowserRequestTrustMode
{
    Enforce = 0,
    ReportOnly = 1,
}

/// <summary>
/// Fixed at startup from <c>ShellUi:ManagementUiUrl</c>. There is no second origin setting.
/// </summary>
internal sealed class BrowserRequestTrustOptions
{
    public const string SectionName = "BrowserRequestTrust";

    public const string ModeKey = SectionName + ":Mode";

    /// <summary>The code written on every enforced rejection. It is not a 401, so session recovery ignores it.</summary>
    public const string RejectionCode = "request_origin_rejected";

    public const string RejectionTitle = "Request blocked";

    public IReadOnlyList<BrowserOrigin> TrustedOrigins { get; init; } = [];

    /// <summary>IdP callback paths taken from the live OpenID Connect options. They are not under <c>/api</c>.</summary>
    public IReadOnlyList<string> OidcCallbackPaths { get; init; } = [];

    /// <summary>True only when the operator asked for report-only and the host is Development. Otherwise enforcement stays on.</summary>
    public bool ReportOnlyActive { get; init; }

    public bool ReportOnlyConfiguredButIgnored { get; init; }

    public static bool HonorReportOnly(BrowserRequestTrustMode mode, bool isDevelopment) =>
        mode == BrowserRequestTrustMode.ReportOnly && isDevelopment;
}
