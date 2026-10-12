using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// AUD-1.4 slice 6 (gate §13.4): the notification definition, subscription, managed contact and diagnostics writers record only
/// through the audited unit of work or the event writer. None of them reaches the append bridge again, and none can run a mutation
/// without the unit.
/// </summary>
public sealed class Aud14Slice6ConvertedWritersTests
{
    private static readonly string[] ConvertedFiles =
    [
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/NotificationDefinitionService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/NotificationSubscriptionService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/ManagedContactManagementService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/ManagedContactRecipientProvisioner.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/NotificationPipelineTestService.cs",
        "Apperture.ControlPlane/Apperture.Management.Api/Controllers/NotificationDefinitionsController.cs",
        "Apperture.ControlPlane/Apperture.Management.Api/Controllers/NotificationSubscriptionsController.cs",
        "Apperture.ControlPlane/Apperture.Management.Api/Controllers/ManagedContactsController.cs",
        "Apperture.ControlPlane/Apperture.Management.Api/Controllers/NotificationDiagnosticsController.cs",
    ];

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
    public void Controllers_present_an_audit_invariant_failure_as_a_server_error_never_as_a_refusal()
    {
        var src = Path.Combine(RepoRoot(), "src");
        foreach (var file in ConvertedFiles.Where(f => f.Contains("/Controllers/", StringComparison.Ordinal)))
        {
            var lines = File.ReadAllLines(Path.Combine(src, file));
            var unguarded = lines
                .Where(l => l.Contains("catch (InvalidOperationException", StringComparison.Ordinal))
                .Where(l => !l.Contains("when (ex is not AuditInvariantException)", StringComparison.Ordinal))
                .ToList();
            unguarded.Should().BeEmpty($"{file} maps a genuine refusal only");
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
