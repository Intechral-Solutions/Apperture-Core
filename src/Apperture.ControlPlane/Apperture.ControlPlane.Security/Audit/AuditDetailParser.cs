using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Apperture.Core.Application.Services;

namespace Apperture.ControlPlane.Security.Audit;

/// <summary>
/// Best-effort parser for extracting outcome and related references from audit Details.
/// Formats may vary across writers; extraction is not guaranteed. Epic 32.
/// </summary>
public static class AuditDetailParser
{
    private static readonly Regex RunIdRegex = new(@"runId=([^;]+)", RegexOptions.Compiled);
    private static readonly Regex DefinitionIdRegex = new(@"definitionId=([^;]+)", RegexOptions.Compiled);
    private static readonly Regex WorkflowIdRegex = new(@"workflowId=([^;]+)", RegexOptions.Compiled);
    private static readonly Regex OperationDefinitionIdRegex = new(@"operationDefinitionId=([^;]+)", RegexOptions.Compiled);
    private static readonly Regex SchedulePrefixRegex = new(@"schedule:([^;]+)", RegexOptions.Compiled);
    private static readonly Regex RunIdValueRegex = new(@"RunId=([^;]+)", RegexOptions.Compiled);
    private static readonly Regex ModuleRegex = new(@"module=([^;]+)", RegexOptions.Compiled);
    private static readonly Regex RuntimePlaneIdRegex = new(@"runtimePlaneId=([^;]+)", RegexOptions.Compiled);
    private static readonly Regex OriginalRunIdRegex = new(@"originalRunId=([^;]+)", RegexOptions.Compiled);
    private static readonly Regex NewRunIdRegex = new(@"newRunId=([^;]+)", RegexOptions.Compiled);

    /// <summary>
    /// Extracts the outcome from Details. When Outcome was merged on write, it is typically the first segment before ";".
    /// </summary>
    public static string ParseOutcome(string? details)
    {
        if (string.IsNullOrEmpty(details))
            return "";

        var firstSemicolon = details.IndexOf(';');
        if (firstSemicolon < 0)
        {
            var trimmed = details.Trim();
            return IsLikelyOutcome(trimmed) ? trimmed : "";
        }

        var first = details[..firstSemicolon].Trim();
        return IsLikelyOutcome(first) ? first : "";
    }

    private static bool IsLikelyOutcome(string segment)
    {
        return segment.Length > 0
            && segment.Length < 128
            && !segment.StartsWith("Resource=", StringComparison.Ordinal)
            && !segment.StartsWith("runId=", StringComparison.Ordinal)
            && !segment.StartsWith("schedule:", StringComparison.Ordinal)
            && !segment.StartsWith("definitionId=", StringComparison.Ordinal)
            && !segment.StartsWith("workflowId=", StringComparison.Ordinal)
            && !segment.StartsWith("operationDefinitionId=", StringComparison.Ordinal)
            && !segment.StartsWith("module=", StringComparison.Ordinal)
            && !segment.StartsWith("runtimePlaneId=", StringComparison.Ordinal);
    }

    /// <summary>
    /// Extracts related entity references from Details.
    /// </summary>
    public static IReadOnlyList<AuditRelatedReference> ParseRelatedReferences(string? details)
    {
        if (string.IsNullOrEmpty(details))
            return [];

        var refs = new List<AuditRelatedReference>();

        AddMatch(refs, RunIdRegex, details, "run");
        AddMatch(refs, OriginalRunIdRegex, details, "run");
        AddMatch(refs, NewRunIdRegex, details, "run");
        AddMatch(refs, RunIdValueRegex, details, "run");
        AddMatch(refs, DefinitionIdRegex, details, "workflow_definition"); // may be workflow or operation
        AddMatch(refs, WorkflowIdRegex, details, "workflow_definition");
        AddMatch(refs, OperationDefinitionIdRegex, details, "operation_definition");
        AddMatch(refs, SchedulePrefixRegex, details, "schedule");
        AddMatch(refs, ModuleRegex, details, "module");
        AddMatch(refs, RuntimePlaneIdRegex, details, "runtime_plane");

        return refs.DistinctBy(r => (r.Kind, r.Id)).ToImmutableList();
    }

    private static void AddMatch(List<AuditRelatedReference> refs, Regex regex, string details, string kind)
    {
        var m = regex.Match(details);
        if (m.Success && m.Groups.Count >= 2)
        {
            var id = m.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(id))
                refs.Add(new AuditRelatedReference(kind, id));
        }
    }

    /// <summary>
    /// Builds a short display summary from Action and parsed outcome.
    /// </summary>
    public static string BuildSummary(string action, string outcome)
    {
        if (!string.IsNullOrEmpty(outcome))
            return $"{action}: {outcome}";
        return action;
    }
}
