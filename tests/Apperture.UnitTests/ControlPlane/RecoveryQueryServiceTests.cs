using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Recovery;
using Apperture.Core.Application.RuntimeHealth;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Unit tests for recovery eligibility projection. Epic 31 Work Area 10.
/// </summary>
public sealed class RecoveryQueryServiceTests
{
    [Fact]
    public async Task GetBlockedWorkRemediationAsync_WhenModuleExecutable_RetryEligibleNowTrue()
    {
        var health = new StubHealthService(
            blocked: [new BlockedDeferredWorkEntry("r1", "wf-1", "workflow", "m1", DateTimeOffset.UtcNow.AddHours(-1), "blocked")],
            executableModuleIds: ["m1"]);
        var recovery = new RecoveryQueryService(health);

        var list = await recovery.GetBlockedWorkRemediationAsync("local", 24);

        var entry = list.Should().ContainSingle().Subject;
        entry.RetryEligibleNow.Should().BeTrue();
        entry.RetryEligibleReason.Should().Contain("executable");
    }

    [Fact]
    public async Task GetBlockedWorkRemediationAsync_WhenModuleNotExecutable_RetryEligibleNowFalse()
    {
        var health = new StubHealthService(
            blocked: [new BlockedDeferredWorkEntry("r1", "wf-1", "workflow", "m1", DateTimeOffset.UtcNow.AddHours(-1), "blocked")],
            executableModuleIds: []);
        var recovery = new RecoveryQueryService(health);

        var list = await recovery.GetBlockedWorkRemediationAsync("local", 24);

        var entry = list.Should().ContainSingle().Subject;
        entry.RetryEligibleNow.Should().BeFalse();
        entry.RetryEligibleReason.Should().Contain("m1");
    }

    [Fact]
    public async Task GetBlockedWorkRemediationAsync_WhenOperation_RetryEligibleNowFalse()
    {
        var health = new StubHealthService(
            blocked: [new BlockedDeferredWorkEntry("r1", "op-1", "operation", "m1", DateTimeOffset.UtcNow.AddHours(-1), "blocked")],
            executableModuleIds: ["m1"]);
        var recovery = new RecoveryQueryService(health);

        var list = await recovery.GetBlockedWorkRemediationAsync("local", 24);

        var entry = list.Should().ContainSingle().Subject;
        entry.RetryEligibleNow.Should().BeFalse();
        entry.RetryEligibleReason.Should().Contain("Operation-level retry is not supported");
    }

    [Fact]
    public async Task GetScheduleRemediationAsync_WhenEnabledAndModuleExecutable_RemediationAvailableTrue()
    {
        var health = new StubHealthService(schedules: [
            new ScheduleDiagnosticEntry("s1", "MySchedule", true, 0, null, DateTimeOffset.UtcNow.AddHours(1),
                null, "BlockedByLifecycle", "reason", null, "Blocked", null, true)
        ]);
        var recovery = new RecoveryQueryService(health);

        var entry = await recovery.GetScheduleRemediationAsync("s1", "local");

        entry.Should().NotBeNull();
        entry!.RemediationAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task GetScheduleRemediationAsync_WhenModuleNotExecutable_RemediationAvailableFalse()
    {
        var health = new StubHealthService(schedules: [
            new ScheduleDiagnosticEntry("s1", "MySchedule", true, 0, null, DateTimeOffset.UtcNow.AddHours(1),
                null, "BlockedByLifecycle", "reason", null, "Blocked", null, false)
        ]);
        var recovery = new RecoveryQueryService(health);

        var entry = await recovery.GetScheduleRemediationAsync("s1", "local");

        entry.Should().NotBeNull();
        entry!.RemediationAvailable.Should().BeFalse();
        entry.TargetModuleExecutable.Should().BeFalse();
    }

    [Fact]
    public async Task GetModuleRemediationAsync_WhenDegraded_PrerequisiteHintSet()
    {
        var health = new StubHealthService(modules: [
            new ModuleDiagnosticEntry("m1", "InstalledDisabled", true, false, false, false,
                ["V001"], "invalid_persisted_state", 2, 1)
        ]);
        var recovery = new RecoveryQueryService(health);

        var entry = await recovery.GetModuleRemediationAsync("m1");

        entry.Should().NotBeNull();
        entry!.PrerequisiteHint.Should().NotBeNullOrEmpty();
        entry.BlockedDeferredWorkCount.Should().Be(2);
        entry.BlockedScheduleCount.Should().Be(1);
    }

    private sealed class StubHealthService : IRuntimeHealthQueryService
    {
        private readonly IReadOnlyList<BlockedDeferredWorkEntry> _blocked;
        private readonly HashSet<string> _executableModuleIds;
        private readonly IReadOnlyList<ScheduleDiagnosticEntry> _schedules;
        private readonly IReadOnlyList<ModuleDiagnosticEntry> _modules;

        public StubHealthService(
            IReadOnlyList<BlockedDeferredWorkEntry>? blocked = null,
            IReadOnlyList<string>? executableModuleIds = null,
            IReadOnlyList<ScheduleDiagnosticEntry>? schedules = null,
            IReadOnlyList<ModuleDiagnosticEntry>? modules = null)
        {
            _blocked = blocked ?? [];
            _executableModuleIds = (executableModuleIds ?? []).ToHashSet(StringComparer.Ordinal);
            _schedules = schedules ?? [];
            _modules = modules ?? [];
        }

        public ValueTask<RuntimeHealthSnapshot> GetSnapshotAsync(
            string? runtimePlaneId = "local",
            int blockedDeferredWindowHours = 24,
            int scheduleFailureWindowHours = 24,
            CancellationToken cancellationToken = default)
        {
            var moduleDiagnostics = _modules.Count > 0
                ? _modules
                : BuildModuleDiagnosticsFromBlockedAndExecutable();
            var snapshot = new RuntimeHealthSnapshot(
                DateTimeOffset.UtcNow,
                RuntimeHealthStatus.Healthy,
                [],
                moduleDiagnostics,
                _schedules,
                _blocked,
                [],
                [],
                []);
            return ValueTask.FromResult(snapshot);
        }

        private IReadOnlyList<ModuleDiagnosticEntry> BuildModuleDiagnosticsFromBlockedAndExecutable()
        {
            var moduleIds = _blocked.Select(b => b.ModuleId).Where(id => id != null).Cast<string>()
                .Concat(_executableModuleIds).Distinct().ToList();
            if (moduleIds.Count == 0)
                return [new ModuleDiagnosticEntry("m1", "InstalledEnabled", true, false, true, false, [], null, 0, 0)];
            return moduleIds.Select(id => new ModuleDiagnosticEntry(
                id, "InstalledEnabled", true, _executableModuleIds.Contains(id), true, false,
                [], null, 0, 0)).ToList();
        }
    }
}
