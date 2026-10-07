using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// AUD-1.3 (gate §22, §13.4, §10.5): the operator-action writers it converted write only through the recorder, never name a
/// placeholder actor, and every start request records who requested it from the audited unit or from a named subsystem.
/// </summary>
public sealed partial class Aud13ConvertedWritersTests
{
    private static readonly string[] ConvertedFiles =
    [
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/AuditedExecutionStartService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/WorkflowCancelService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/OperationCancelService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/WorkflowExternalControlService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/WorkflowRetryService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/OperationItemRetryService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/BulkOperationItemRetryService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/OperationItemAttentionService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/IntegrationProcedureService.cs",
        "Apperture.ControlPlane/Apperture.Management.Api/Controllers/WorkflowsController.cs",
        "Apperture.ControlPlane/Apperture.Management.Api/Controllers/OperationsController.cs",
        "Apperture.ControlPlane/Apperture.Management.Api/Controllers/RecoveryController.cs",
        "Apperture.ControlPlane/Apperture.Management.Api/Controllers/SchedulerOperationsController.cs",
    ];

    /// <summary>
    /// Every place a start request is built, and who builds it. A new site fails this test until it is reviewed and added here
    /// with its reason. The first three go through <c>AuditedExecutionStartService</c>, which replaces the requester with the
    /// unit's own actor; the next two run inside an audited unit and set it from that unit; the scheduler is a system subsystem.
    /// </summary>
    private static readonly Dictionary<string, int> StartRequestSites = new(StringComparer.Ordinal)
    {
        ["Apperture.ControlPlane/Apperture.ControlPlane.Security/IntegrationProcedureService.cs"] = 2,
        ["Apperture.ControlPlane/Apperture.Management.Api/Controllers/WorkflowsController.cs"] = 1,
        ["Apperture.ControlPlane/Apperture.Management.Api/Controllers/OperationsController.cs"] = 1,
        ["Apperture.ControlPlane/Apperture.ControlPlane.Security/WorkflowRetryService.cs"] = 1,
        ["Apperture.ControlPlane/Apperture.ControlPlane.Security/OperationItemRetryService.cs"] = 1,
        ["Apperture.RuntimePlane/Apperture.RuntimePlane.Dispatch.SqlServer/ScheduleDispatchRunner.cs"] = 2,
    };

    [Fact]
    public void Converted_writers_never_use_the_append_bridge()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var offenders = ConvertedFiles
            .Select(f => (File: f, Text: File.ReadAllText(Path.Combine(src, f))))
            .Where(f => f.Text.Contains("IAuditService", StringComparison.Ordinal)
                || f.Text.Contains("new AuditRecord(", StringComparison.Ordinal))
            .Select(f => f.File);

        offenders.Should().BeEmpty("a converted writer records through the audited unit of work or the event writer only");
    }

    [Fact]
    public void No_converted_writer_names_a_placeholder_or_the_platform_in_place_of_the_person_who_acted()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var offenders = ConvertedFiles
            .Select(f => (File: f, Text: File.ReadAllText(Path.Combine(src, f))))
            .Where(f => PlaceholderActor().IsMatch(f.Text))
            .Select(f => f.File);

        offenders.Should().BeEmpty(
            "a request is attributed to its authenticated principal, or recorded as not signed in; there is no 'system', 'system:unknown' or 'unknown' fallback");
    }

    [Fact]
    public void Every_start_request_is_built_at_a_reviewed_site()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var separator = Path.DirectorySeparatorChar;
        var sites = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (new[] { "obj", "bin" }.Any(skip => file.Contains($"{separator}{skip}{separator}", StringComparison.Ordinal)))
                continue;
            var count = StartRequestConstruction().Matches(File.ReadAllText(file)).Count;
            if (count > 0)
                sites[Path.GetRelativePath(src, file).Replace('\\', '/')] = count;
        }

        sites.Should().BeEquivalentTo(StartRequestSites);
    }

    [Fact]
    public void The_scheduler_records_itself_as_the_requester_of_the_starts_it_dispatches()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Apperture.RuntimePlane/Apperture.RuntimePlane.Dispatch.SqlServer/ScheduleDispatchRunner.cs"));

        Regex.Matches(text, @"RequestedBy:\s*ActorLiterals\.SystemScheduler").Count.Should().Be(2, "one workflow start and one operation start");
    }

    [Fact]
    public void The_retry_and_item_retry_requests_take_their_requester_from_the_audited_unit()
    {
        foreach (var file in new[] { "WorkflowRetryService.cs", "OperationItemRetryService.cs" })
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Apperture.ControlPlane/Apperture.ControlPlane.Security", file));
            text.Should().Contain("transaction.Actor.RecordedValue", $"{file} names the requester from the unit that records the row");
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>A literal actor of the platform or an unresolved person where a principal belongs.</summary>
    [GeneratedRegex(@"new\s+ActorId\(\s*""(system|system:unknown|unknown)""\s*\)|\?\?\s*""unknown""", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderActor();

    [GeneratedRegex(@"new\s+Start(Workflow|Operation)Request\(", RegexOptions.CultureInvariant)]
    private static partial Regex StartRequestConstruction();
}
