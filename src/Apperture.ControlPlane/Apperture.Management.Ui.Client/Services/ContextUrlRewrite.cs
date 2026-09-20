namespace Apperture.Management.Ui.Client.Services;

/// <summary>
/// The one safety rule for rewriting the address bar's <c>?context=</c> after asynchronous work: a
/// continuation may only rewrite the URL it was started for. If the operator has navigated elsewhere in
/// the meantime, rewriting from a captured URL would replace the new page's history entry with a stale
/// one, so the rewrite is dropped.
/// </summary>
public static class ContextUrlRewrite
{
    /// <summary>
    /// The URL to <c>replace</c> the current entry with -- <paramref name="requestUri"/> with its
    /// <c>context</c> parameter set to <paramref name="contextToken"/>, every other parameter preserved --
    /// or null when <paramref name="currentUri"/> is no longer the URL the work was started for.
    /// </summary>
    public static string? TryBuild(string requestUri, string currentUri, string contextToken) =>
        string.Equals(requestUri, currentUri, StringComparison.Ordinal)
            ? ResourceContextViewState.BuildUriWithContext(new Uri(requestUri), contextToken)
            : null;
}
