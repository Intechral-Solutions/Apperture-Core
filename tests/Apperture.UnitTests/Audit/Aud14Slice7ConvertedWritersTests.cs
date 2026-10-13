using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Client;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// AUD-1.4 slice 7 (gate §13.4): the attention routing writers and the runtime shutdown request record only through the audited unit
/// of work. None of them reaches the append bridge again, and an audit invariant failure reaches the caller as the server error it
/// is and never as a refusal.
/// </summary>
public sealed class Aud14Slice7ConvertedWritersTests
{
    private static readonly string[] ConvertedFiles =
    [
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/AttentionRoutingManagementService.cs",
        "Apperture.ControlPlane/Apperture.ControlPlane.Security/RuntimeControlService.cs",
        "Apperture.ControlPlane/Apperture.Management.Api/Controllers/AttentionRoutingDefaultsController.cs",
        "Apperture.ControlPlane/Apperture.Management.Api/Controllers/RuntimeControlsController.cs",
    ];

    private const string IntegrationsControllerFile = "Apperture.ControlPlane/Apperture.Management.Api/Controllers/IntegrationsController.cs";

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
            var unguarded = File.ReadAllLines(Path.Combine(src, file))
                .Where(l => l.Contains("catch (InvalidOperationException", StringComparison.Ordinal))
                .Where(l => !l.Contains("when (ex is not AuditInvariantException)", StringComparison.Ordinal))
                .ToList();
            unguarded.Should().BeEmpty($"{file} maps a genuine refusal only");
        }
    }

    [Fact]
    public void The_integration_routing_routes_present_an_audit_invariant_failure_as_a_server_error()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", IntegrationsControllerFile));
        foreach (var route in new[] { "PutAttentionRouting(", "DeleteAttentionRouting(" })
        {
            var start = text.IndexOf(route, StringComparison.Ordinal);
            start.Should().BeGreaterThan(0, route);
            var end = text.IndexOf("\n    /// <summary>", start, StringComparison.Ordinal);
            var body = text[start..end];
            body.Should().Contain("catch (InvalidOperationException ex) when (ex is not AuditInvariantException)", route);
            System.Text.RegularExpressions.Regex.IsMatch(body, @"catch \(InvalidOperationException ex\)\s*\{")
                .Should().BeFalse($"{route} has no unguarded catch");
        }
    }

    [Fact]
    public async Task The_default_routing_endpoint_lets_an_audit_invariant_failure_reach_the_server_error_handler()
    {
        var governance = new Mock<IScheduleGovernanceService>();
        governance.Setup(g => g.CanManageAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var routing = new Mock<IAttentionRoutingManagementService>();
        routing
            .Setup(r => r.UpsertInstanceDefaultRoutingAsync(It.IsAny<string>(), It.IsAny<AttentionRoutingUpsertCommand>(), It.IsAny<CancellationToken>()))
            .Throws(new AuditInvariantException("The audited operation expected a row and staged none."));
        var controller = new AttentionRoutingDefaultsController(governance.Object, Mock.Of<IAttentionRecipientResolver>(), routing.Object);

        var act = () => controller.PutDefaults(
            new UpsertAttentionRoutingRequest(true, true, true, true, null, null, null, null), null, CancellationToken.None);

        await act.Should().ThrowAsync<AuditInvariantException>("an invariant failure is a 500, not a 404");
    }

    [Fact]
    public async Task The_default_routing_endpoint_still_maps_a_genuine_refusal_to_not_found()
    {
        var governance = new Mock<IScheduleGovernanceService>();
        governance.Setup(g => g.CanManageAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var routing = new Mock<IAttentionRoutingManagementService>();
        routing
            .Setup(r => r.UpsertInstanceDefaultRoutingAsync(It.IsAny<string>(), It.IsAny<AttentionRoutingUpsertCommand>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("Instance attention routing for condition 'x' was not found."));
        var controller = new AttentionRoutingDefaultsController(governance.Object, Mock.Of<IAttentionRecipientResolver>(), routing.Object);

        var result = await controller.PutDefaults(
            new UpsertAttentionRoutingRequest(true, true, true, true, null, null, null, null), "x", CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
