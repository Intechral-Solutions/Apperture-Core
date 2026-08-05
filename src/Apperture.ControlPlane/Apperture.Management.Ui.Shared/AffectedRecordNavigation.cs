using System.Text;
using Apperture.Management.Ui.Shared.Models;

namespace Apperture.Management.Ui.Shared;

/// <summary>
/// UIX-3 — centralized affected-record URL contract (group/item/q/from/fromKey).
/// Return destinations are application-owned; query strings never carry arbitrary routes.
/// </summary>
public static class AffectedRecordNavigation
{
    public const string GroupParam = "group";
    public const string ItemParam = "item";
    public const string SearchParam = "q";
    public const string FromParam = "from";
    public const string FromKeyParam = "fromKey";

    public static string BuildAffectedRunUrl(
        string runId,
        string? operationGroupId = null,
        string? operationItemId = null,
        string? searchQuery = null,
        ManagementNavigationReturnContext? returnContext = null)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return "/runs";

        var path = ProcedureLaunchOperatorUiHelpers.GetRunDetailHref(runId.Trim());
        var pairs = new List<(string Key, string Value)>();

        if (!string.IsNullOrWhiteSpace(operationGroupId))
            pairs.Add((GroupParam, operationGroupId.Trim()));
        if (!string.IsNullOrWhiteSpace(operationItemId))
            pairs.Add((ItemParam, operationItemId.Trim()));
        if (!string.IsNullOrWhiteSpace(searchQuery))
            pairs.Add((SearchParam, searchQuery.Trim()));

        if (returnContext is not null
            && IsSupportedReturnContext(returnContext.Type, returnContext.Key))
        {
            pairs.Add((FromParam, returnContext.Type.Trim()));
            pairs.Add((FromKeyParam, returnContext.Key.Trim()));
        }

        return AppendQuery(path, pairs);
    }

    public static bool TryParseAffectedRecordNavigation(
        string? navigationUri,
        out AffectedRecordAnchor anchor,
        out ManagementNavigationReturnContext? returnContext)
    {
        anchor = AffectedRecordAnchor.Empty;
        returnContext = null;

        if (string.IsNullOrWhiteSpace(navigationUri))
            return false;

        if (!TryGetQuery(navigationUri, out var query))
            return false;

        var group = GetQueryValue(query, GroupParam);
        var item = GetQueryValue(query, ItemParam);
        var q = GetQueryValue(query, SearchParam);
        var from = GetQueryValue(query, FromParam);
        var fromKey = GetQueryValue(query, FromKeyParam);

        anchor = new AffectedRecordAnchor(
            OperationGroupId: NullIfWhiteSpace(group),
            OperationItemId: NullIfWhiteSpace(item),
            SearchQuery: NullIfWhiteSpace(q));

        if (IsSupportedReturnContext(from, fromKey))
            returnContext = new ManagementNavigationReturnContext(from!.Trim(), fromKey!.Trim());

        return !anchor.IsEmpty || returnContext is not null;
    }

    public static bool TryResolveReturnAffordance(
        ManagementNavigationReturnContext? context,
        out ManagementNavigationReturnAffordance? affordance)
    {
        affordance = null;
        if (context is null || !IsSupportedReturnContext(context.Type, context.Key))
            return false;

        if (string.Equals(context.Type, ManagementNavigationReturnContext.DemoCustomerType, StringComparison.Ordinal))
        {
            var key = context.Key.Trim();
            var href = DemoCustomerUiRoutes.Detail(key);
            var label = $"Back to demo customer {key}";
            affordance = new ManagementNavigationReturnAffordance(label, href, label);
            return true;
        }

        return false;
    }

    public static bool IsSupportedReturnContext(string? type, string? key)
    {
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(key))
            return false;

        if (string.Equals(type.Trim(), ManagementNavigationReturnContext.DemoCustomerType, StringComparison.Ordinal))
            return IsSafeBusinessKey(key);

        return false;
    }

    /// <summary>
    /// Operator-facing link label for a Demo Customer source based on available identifiers.
    /// </summary>
    public static string ResolveDemoCustomerSourceLinkLabel(
        string? runId,
        string? operationGroupId,
        string? operationItemId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return "Open run";

        if (!string.IsNullOrWhiteSpace(operationGroupId) && !string.IsNullOrWhiteSpace(operationItemId))
            return "Open affected record";

        if (!string.IsNullOrWhiteSpace(operationGroupId))
            return "Open affected group";

        return "Open run";
    }

    public static string? BuildDemoCustomerSourceHref(
        string businessKey,
        string? runId,
        string? operationGroupId,
        string? operationItemId)
    {
        if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(businessKey))
            return null;

        var ctx = new ManagementNavigationReturnContext(
            ManagementNavigationReturnContext.DemoCustomerType,
            businessKey.Trim());

        return BuildAffectedRunUrl(
            runId.Trim(),
            operationGroupId: NullIfWhiteSpace(operationGroupId),
            operationItemId: NullIfWhiteSpace(operationItemId),
            searchQuery: null,
            returnContext: ctx);
    }

    /// <summary>
    /// When continuing deeper within the same run investigation, re-apply validated return context.
    /// </summary>
    public static string PreserveReturnContextOnRunHref(
        string runHref,
        ManagementNavigationReturnContext? returnContext)
    {
        if (returnContext is null || !IsSupportedReturnContext(returnContext.Type, returnContext.Key))
            return runHref;

        if (!TryGetPathAndQuery(runHref, out var path, out var query))
            return runHref;

        // Only preserve on run-detail paths.
        if (!path.StartsWith("/runs/", StringComparison.OrdinalIgnoreCase))
            return runHref;

        query.Remove(FromParam);
        query.Remove(FromKeyParam);
        query[FromParam] = returnContext.Type.Trim();
        query[FromKeyParam] = returnContext.Key.Trim();
        return AppendQuery(path, query.Select(kv => (kv.Key, kv.Value)));
    }

    public static string FormatOperationItemElementId(string operationItemId) =>
        "operation-item-" + Uri.EscapeDataString(operationItemId.Trim());

    private static bool IsSafeBusinessKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        var value = key.Trim();
        if (value.Length is < 1 or > 128)
            return false;

        // Reject path injection / absolute URLs.
        if (value.Contains('/') || value.Contains('\\') || value.Contains('?') || value.Contains('#')
            || value.Contains(':') || value.Contains("..", StringComparison.Ordinal))
            return false;

        return true;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TryGetQuery(string navigationUri, out Dictionary<string, string> query)
    {
        query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetPathAndQuery(navigationUri, out _, out query))
            return false;
        return query.Count > 0;
    }

    private static bool TryGetPathAndQuery(
        string navigationUri,
        out string path,
        out Dictionary<string, string> query)
    {
        path = "/";
        query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var raw = navigationUri.Trim();
        if (Uri.TryCreate(raw, UriKind.Absolute, out var absolute))
        {
            path = string.IsNullOrEmpty(absolute.AbsolutePath) ? "/" : absolute.AbsolutePath;
            ParseQueryInto(absolute.Query, query);
            return true;
        }

        var fragment = raw.IndexOf('#', StringComparison.Ordinal);
        if (fragment >= 0)
            raw = raw[..fragment];

        var qIndex = raw.IndexOf('?', StringComparison.Ordinal);
        if (qIndex >= 0)
        {
            path = qIndex == 0 ? "/" : raw[..qIndex];
            ParseQueryInto(raw[qIndex..], query);
        }
        else
        {
            path = raw.Length == 0 ? "/" : raw;
        }

        if (!path.StartsWith('/'))
            path = "/" + path;

        return true;
    }

    private static void ParseQueryInto(string? queryString, Dictionary<string, string> target)
    {
        if (string.IsNullOrWhiteSpace(queryString))
            return;

        var q = queryString.StartsWith('?') ? queryString[1..] : queryString;
        foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            string key;
            string value;
            if (eq < 0)
            {
                key = Uri.UnescapeDataString(part);
                value = "";
            }
            else
            {
                key = Uri.UnescapeDataString(part[..eq]);
                value = Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
            }

            if (string.IsNullOrWhiteSpace(key))
                continue;

            // First key wins — avoid duplicate-key ambiguity.
            if (!target.ContainsKey(key))
                target[key] = value;
        }
    }

    private static string? GetQueryValue(IReadOnlyDictionary<string, string> query, string key) =>
        query.TryGetValue(key, out var value) ? value : null;

    private static string AppendQuery(string path, IEnumerable<(string Key, string Value)> pairs)
    {
        var sb = new StringBuilder(path);
        var first = true;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in pairs)
        {
            if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
                continue;

            sb.Append(first ? '?' : '&');
            first = false;
            sb.Append(Uri.EscapeDataString(key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(value));
        }

        return sb.ToString();
    }
}
