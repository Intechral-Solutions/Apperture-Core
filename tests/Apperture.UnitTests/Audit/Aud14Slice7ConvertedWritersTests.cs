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
        "Apperture.ControlPlane/Apperture.Management.Api/Controllers/ModulesController.cs",
    ];

    private const string ModuleRegistryFile = "Apperture.Persistence/Apperture.Persistence.SqlServer/SqlModuleRegistryService.cs";

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

    /// <summary>
    /// The module registry keeps the append bridge for one thing: the discovery telemetry row (a later slice owns it). The four
    /// lifecycle transitions and everything they call record through the audited unit of work and the event writer only.
    /// </summary>
    [Fact]
    public void The_module_lifecycle_transitions_never_use_the_append_bridge_and_discovery_is_the_one_exception()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", ModuleRegistryFile));
        var start = text.IndexOf("public async ValueTask<ModuleLifecycleTransitionResult> InstallModuleAsync", StringComparison.Ordinal);
        var end = text.IndexOf("private async ValueTask<IReadOnlyList<ModuleDescriptor>> DiscoverDescriptorsAsync", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        end.Should().BeGreaterThan(start);
        var transitions = text[start..end];

        transitions.Should().NotContain("_auditService");
        transitions.Should().NotContain("new AuditRecord(");
        transitions.Should().NotContain("AppendDiscoveryAuditAsync");
        transitions.Should().Contain("unit.ExecuteAsync").And.Contain("TryWriteAsync");

        System.Text.RegularExpressions.Regex.Matches(text, @"_auditService\.AppendAsync").Should().ContainSingle("only discovery appends");
        System.Text.RegularExpressions.Regex.Matches(text, @"new AuditRecord\(").Should().ContainSingle("only discovery appends");
        var discover = text[text.IndexOf("DiscoverModulesAsync(", StringComparison.Ordinal)..start];
        discover.Should().Contain("AppendDiscoveryAuditAsync");
    }

    [Fact]
    public async Task The_module_lifecycle_endpoints_let_an_audit_invariant_failure_reach_the_server_error_handler()
    {
        var listing = new Mock<IModuleWorkflowListingService>();
        listing.Setup(l => l.EnableModuleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Throws(new AuditInvariantException("Module lifecycle changes require the audited unit of work."));
        var controller = new ModulesController(listing.Object);

        var act = async () => await controller.EnableModule("sample", CancellationToken.None);

        await act.Should().ThrowAsync<AuditInvariantException>("an invariant failure is a 500, not a 400");
    }

    [Fact]
    public async Task The_module_lifecycle_endpoints_still_map_a_genuine_refusal_to_a_bad_request()
    {
        var listing = new Mock<IModuleWorkflowListingService>();
        listing.Setup(l => l.InstallModuleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("Module 'sample' is not discovered."));
        var controller = new ModulesController(listing.Object);

        var result = await controller.InstallModule("sample", CancellationToken.None);

        var bad = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value.Should().BeOfType<Apperture.Management.Api.Controllers.ModuleLifecycleActionResultDto>().Which.Blockers.Should().ContainSingle(b => b.Message == "Module 'sample' is not discovered.");
    }

    [Theory]
    [InlineData("InstallModule")]
    [InlineData("EnableModule")]
    [InlineData("DisableModule")]
    [InlineData("UninstallModule")]
    public async Task The_module_lifecycle_endpoints_answer_a_busy_lifecycle_lock_with_a_retryable_conflict(string endpoint)
    {
        var busy = new ModuleLifecycleBusyException();
        var listing = new Mock<IModuleWorkflowListingService>();
        listing.Setup(l => l.InstallModuleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Throws(busy);
        listing.Setup(l => l.EnableModuleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Throws(busy);
        listing.Setup(l => l.DisableModuleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Throws(busy);
        listing.Setup(l => l.UninstallModuleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Throws(busy);
        var controller = new ModulesController(listing.Object);

        var result = endpoint switch
        {
            "InstallModule" => await controller.InstallModule("sample", CancellationToken.None),
            "EnableModule" => await controller.EnableModule("sample", CancellationToken.None),
            "DisableModule" => await controller.DisableModule("sample", CancellationToken.None),
            _ => await controller.UninstallModule("sample", CancellationToken.None),
        };

        var conflict = result.Result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value.Should().BeEquivalentTo(new { error = "Another module change is in progress. Wait a moment and try again.", code = "module_lifecycle_busy" });
    }

    [Fact]
    public void A_busy_lifecycle_lock_is_not_an_invalid_operation_so_no_refusal_mapping_can_catch_it()
    {
        var busy = new ModuleLifecycleBusyException();

        busy.Should().NotBeAssignableTo<InvalidOperationException>();
        busy.Code.Should().Be(ModuleLifecycleBusyException.BusyCode).And.Be("module_lifecycle_busy");
        busy.Message.Should().Be("Another module change is in progress. Wait a moment and try again.");
    }

    /// <summary>
    /// The lock result is decided by <c>SqlModuleRegistryService.LockOutcome</c>; the lock itself is not taken on the in-memory
    /// provider, and a test cannot shorten its bounded wait without a production knob. A timeout, a cancelled wait and a deadlock
    /// victim are busy; a granted lock is no failure; any other result is a precondition breach.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_granted_lifecycle_lock_is_no_failure(int code) =>
        Apperture.Persistence.SqlServer.SqlModuleRegistryService.LockOutcome(code).Should().BeNull();

    [Theory]
    [InlineData(-1)]
    [InlineData(-2)]
    [InlineData(-3)]
    public void A_lifecycle_lock_that_timed_out_was_cancelled_or_lost_a_deadlock_is_busy(int code) =>
        Apperture.Persistence.SqlServer.SqlModuleRegistryService.LockOutcome(code).Should().BeOfType<ModuleLifecycleBusyException>();

    [Theory]
    [InlineData(-4)]
    [InlineData(-999)]
    public void An_unexpected_lifecycle_lock_result_is_an_audit_invariant_failure_not_a_refusal(int code)
    {
        var failure = Apperture.Persistence.SqlServer.SqlModuleRegistryService.LockOutcome(code);

        failure.Should().BeOfType<AuditInvariantException>().Which.Message.Should().Contain($"code {code}");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
