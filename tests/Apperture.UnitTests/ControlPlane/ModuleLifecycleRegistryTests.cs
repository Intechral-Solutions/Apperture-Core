using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Domain.Operations;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using AuditEvent = Apperture.Core.Application.Audit.AuditEvent;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// The module catalog and the module lifecycle transitions over EF InMemory, with the audited unit of work and the event writer
/// recorded. Every transition keeps the answer the API always gave; what changed is that a transition is one audited unit (its row
/// and its audit row), no-ops record nothing, a refused transition changes nothing and records a blocked attempt, and the legacy
/// append is left to discovery. The transactional guarantees themselves are proven against SQL Server.
/// </summary>
public sealed class ModuleLifecycleRegistryTests
{
    private const string Environment = "test";

    // ---- the catalog (unchanged behavior) ----

    [Fact]
    public async Task ListModules_marks_discovered_modules_as_enabled_installations_for_compatibility()
    {
        var rig = new Rig([new TestModule("M1")]);

        var modules = await rig.Service.ListModulesAsync();

        modules.Should().ContainSingle();
        modules[0].Descriptor.ModuleId.Should().Be("M1");
        modules[0].Enabled.Should().BeFalse();
        modules[0].State.Should().Be(ModuleLifecycleState.Discovered);
    }

    [Fact]
    public async Task DisableModule_blocks_when_persisted_references_exist()
    {
        var rig = new Rig([new TestModule("M1")]);
        await rig.SeedWorkflowInstanceAsync("M1.Workflow");

        await rig.Service.InstallModuleAsync("M1");
        await rig.Service.EnableModuleAsync("M1");

        var result = await rig.Service.DisableModuleAsync("M1");

        result.Succeeded.Should().BeFalse();
        result.Blockers.Should().Contain(x => x.Code == "active_workflow_references");
    }

    [Fact]
    public async Task EnableModule_blocks_on_workflow_conflict_with_enabled_module()
    {
        var rig = new Rig([new TestModule("M1"), new TestModule("M2", workflowId: "M1.Workflow")]);

        await rig.Service.InstallModuleAsync("M1");
        await rig.Service.EnableModuleAsync("M1");
        await rig.Service.InstallModuleAsync("M2");
        var result = await rig.Service.EnableModuleAsync("M2");

        result.Succeeded.Should().BeFalse();
        result.Blockers.Should().Contain(x => x.Code == "workflow_id_conflict");
    }

    [Fact]
    public async Task EnableModule_blocks_on_operation_conflict_with_enabled_module()
    {
        var rig = new Rig([new TestModule("M1"), new TestModule("M2", operationId: "M1.Operation")]);

        await rig.Service.InstallModuleAsync("M1");
        await rig.Service.EnableModuleAsync("M1");
        await rig.Service.InstallModuleAsync("M2");
        var result = await rig.Service.EnableModuleAsync("M2");

        result.Succeeded.Should().BeFalse();
        result.Blockers.Should().Contain(x => x.Code == "operation_id_conflict");
    }

    [Fact]
    public async Task EnableModule_blocks_on_navigation_conflict_with_enabled_module()
    {
        var rig = new Rig([new TestModule("M1", navRoute: "/common"), new TestModule("M2", navRoute: "/common")]);

        await rig.Service.InstallModuleAsync("M1");
        await rig.Service.EnableModuleAsync("M1");
        await rig.Service.InstallModuleAsync("M2");
        var result = await rig.Service.EnableModuleAsync("M2");

        result.Succeeded.Should().BeFalse();
        result.Blockers.Should().Contain(x => x.Code == "navigation_route_conflict");
    }

    /// <summary>
    /// Module permissions are scoped per module at registration time; overlapping (action, resourceType)
    /// declarations must not block install when workflow/operation/nav ids differ.
    /// </summary>
    [Fact]
    public async Task InstallModule_succeeds_when_only_generic_permission_shape_overlaps_enabled_module()
    {
        var rig = new Rig([new TestModuleWithGenericPermissions("M1"), new TestModuleWithGenericPermissions("M2")]);

        (await rig.Service.InstallModuleAsync("M1")).Succeeded.Should().BeTrue();
        (await rig.Service.EnableModuleAsync("M1")).Succeeded.Should().BeTrue();

        var install2 = await rig.Service.InstallModuleAsync("M2");
        install2.Succeeded.Should().BeTrue();
        install2.Blockers.Should().BeEmpty();
    }

    [Fact]
    public async Task ListModules_surfaces_missing_descriptor_for_persisted_enabled_module()
    {
        var rig = new Rig([]);
        await rig.SeedAsync("Missing.Module", enabled: true);

        var modules = await rig.Service.ListModulesAsync();

        modules.Should().ContainSingle();
        modules[0].EffectivelyActive.Should().BeFalse();
        modules[0].ValidationIssues.Should().Contain(x => x.Code == "descriptor_missing");
    }

    [Fact]
    public async Task Enable_disable_uninstall_report_effect_semantics_and_record_through_the_unit_of_work()
    {
        var rig = new Rig([new TestModule("M1")]);

        var install = await rig.Service.InstallModuleAsync("M1");
        var enable = await rig.Service.EnableModuleAsync("M1");
        var disable = await rig.Service.DisableModuleAsync("M1");
        var uninstall = await rig.Service.UninstallModuleAsync("M1");

        install.Effect.Should().Be(ModuleLifecycleEffect.Immediate);
        enable.Effect.Should().Be(ModuleLifecycleEffect.PartialUntilRestart);
        disable.Effect.Should().Be(ModuleLifecycleEffect.PartialUntilRestart);
        uninstall.Effect.Should().Be(ModuleLifecycleEffect.RequiresRestart);
        rig.Recording.Actions.Should().Equal(
            AuditActionNames.ModuleInstalled, AuditActionNames.ModuleEnabled, AuditActionNames.ModuleDisabled, AuditActionNames.ModuleUninstalled);
        rig.Audit.Records.Should().BeEmpty("the transitions no longer use the legacy append");
    }

    [Fact]
    public async Task ListModules_reports_real_installation_state_timestamp_for_installed_modules()
    {
        var rig = new Rig([new TestModule("M1")]);

        await rig.Service.InstallModuleAsync("M1");
        var enable = await rig.Service.EnableModuleAsync("M1");
        enable.Succeeded.Should().BeTrue();

        var state = (await rig.RowsAsync()).Single(x => x.ModuleId == "M1");

        var modules = await rig.Service.ListModulesAsync();

        var entry = modules.Should().ContainSingle().Subject;
        entry.UpdatedAtUtc.Should().Be(state.UpdatedAt);
    }

    [Fact]
    public async Task ListModules_reports_no_timestamp_for_discovered_but_never_installed_modules()
    {
        var rig = new Rig([new TestModule("M1")]);

        var modules = await rig.Service.ListModulesAsync();

        var entry = modules.Should().ContainSingle().Subject;
        entry.Installed.Should().BeFalse();
        entry.UpdatedAtUtc.Should().BeNull("no installation row exists to source a real timestamp from, and none should be fabricated");
    }

    // ---- install ----

    [Fact]
    public async Task Install_inserts_a_disabled_installation_and_records_one_install_row()
    {
        var rig = new Rig([new TestModule("M1", version: "1.4.0")]);

        var result = await rig.Service.InstallModuleAsync("M1");

        result.Should().Be(Result(true, "M1", "install", ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.Immediate));
        var row = (await rig.RowsAsync()).Should().ContainSingle().Subject;
        row.Enabled.Should().BeFalse();
        row.Version.Should().Be("1.4.0");
        row.EnvironmentId.Should().Be(Environment);
        var evt = rig.Recording.Single(AuditActionNames.ModuleInstalled);
        Fields(evt).Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["version"] = "1.4.0",
            ["previousState"] = "discovered",
            ["state"] = "installed_disabled",
            ["restartEffect"] = "immediate",
        });
        evt.Target!.Id.Should().Be("M1");
        rig.Recording.Actions.Should().ContainSingle();
        rig.Recording.Writer.Written.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, ModuleLifecycleState.InstalledDisabled)]
    [InlineData(true, ModuleLifecycleState.InstalledEnabled)]
    public async Task Installing_an_installed_module_is_a_no_op_that_reports_the_row(bool enabled, ModuleLifecycleState expected)
    {
        var rig = new Rig([new TestModule("M1")]);
        var stamp = await rig.SeedAsync("M1", enabled);

        var result = await rig.Service.InstallModuleAsync("M1");

        result.Should().Be(Result(true, "M1", "install", expected, ModuleLifecycleEffect.Immediate));
        rig.Recording.Unit.Operations.Should().ContainSingle().Which.NoOp.Should().BeTrue();
        rig.Recording.Unit.Actions.Should().BeEmpty();
        rig.Recording.Writer.Written.Should().BeEmpty("an already-satisfied request is not an attempt");
        (await rig.RowsAsync()).Single().UpdatedAt.Should().Be(stamp);
    }

    [Fact]
    public async Task Install_blocked_by_conflicts_changes_nothing_and_records_one_blocked_attempt_with_codes_only()
    {
        var rig = new Rig([new TestModule("M1"), new TestModule("M2", workflowId: "M1.Workflow")]);
        await rig.SeedAsync("M1", enabled: true);
        var before = await rig.SnapshotAsync();

        var result = await rig.Service.InstallModuleAsync("M2");

        result.Succeeded.Should().BeFalse();
        result.ModuleId.Should().Be("M2");
        result.Action.Should().Be("install");
        result.State.Should().Be(ModuleLifecycleState.Discovered);
        result.Effect.Should().Be(ModuleLifecycleEffect.Immediate);
        result.Blockers.Should().ContainSingle(b => b.Code == "workflow_id_conflict" && b.Message.Contains("M1.Workflow", StringComparison.Ordinal));
        (await rig.SnapshotAsync()).Should().Be(before);
        rig.Recording.Unit.Actions.Should().BeEmpty();
        var attempt = rig.Recording.Writer.Written.Should().ContainSingle().Subject;
        attempt.BestEffort.Should().BeTrue();
        attempt.Event.Action.Should().Be(AuditActionNames.ModuleInstallBlocked);
        attempt.Event.Target!.Id.Should().Be("M2");
        Fields(attempt.Event).Should().ContainKey("state").WhoseValue.Should().Be("discovered");
        BlockerCodes(attempt.Event).Should().Equal("workflow_id_conflict");
        AllText(attempt.Event).Should().NotContain("Conflicting", "a message is never recorded");
    }

    // ---- enable ----

    [Fact]
    public async Task Enable_of_an_installed_disabled_module_updates_the_row_and_records_one_enable_row()
    {
        var rig = new Rig([new TestModule("M1", version: "1.0.0")]);
        var stamp = await rig.SeedAsync("M1", enabled: false, version: "1.0.0");

        var result = await rig.Service.EnableModuleAsync("M1");

        result.Should().Be(Result(true, "M1", "enable", ModuleLifecycleState.InstalledEnabled, ModuleLifecycleEffect.PartialUntilRestart));
        var row = (await rig.RowsAsync()).Should().ContainSingle().Subject;
        row.Enabled.Should().BeTrue();
        row.UpdatedAt.Should().BeAfter(stamp);
        var evt = rig.Recording.Single(AuditActionNames.ModuleEnabled);
        Fields(evt).Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["version"] = "1.0.0",
            ["previousState"] = "installed_disabled",
            ["state"] = "installed_enabled",
            ["restartEffect"] = "partial_until_restart",
        });
        rig.Recording.Actions.Should().ContainSingle();
    }

    [Fact]
    public async Task Enable_at_a_newer_discovered_version_moves_the_installation_and_records_the_previous_version()
    {
        var rig = new Rig([new TestModule("M1", version: "2.0.0")]);
        await rig.SeedAsync("M1", enabled: true, version: "1.0.0");

        var result = await rig.Service.EnableModuleAsync("M1");

        result.Succeeded.Should().BeTrue();
        (await rig.RowsAsync()).Single().Version.Should().Be("2.0.0");
        var evt = rig.Recording.Single(AuditActionNames.ModuleEnabled);
        Fields(evt).Should().Contain("version", "2.0.0").And.Contain("previousVersion", "1.0.0").And.Contain("previousState", "installed_enabled");
    }

    [Fact]
    public async Task Enabling_an_enabled_module_at_the_discovered_version_is_a_no_op()
    {
        var rig = new Rig([new TestModule("M1", version: "1.0.0")]);
        var stamp = await rig.SeedAsync("M1", enabled: true, version: "1.0.0");

        var result = await rig.Service.EnableModuleAsync("M1");

        result.Should().Be(Result(true, "M1", "enable", ModuleLifecycleState.InstalledEnabled, ModuleLifecycleEffect.PartialUntilRestart));
        rig.Recording.Unit.Operations.Should().ContainSingle().Which.NoOp.Should().BeTrue();
        rig.Recording.Unit.Actions.Should().BeEmpty();
        rig.Recording.Writer.Written.Should().BeEmpty();
        (await rig.RowsAsync()).Single().UpdatedAt.Should().Be(stamp);
    }

    [Fact]
    public async Task Enable_of_a_module_that_is_not_installed_installs_it_enabled_and_records_the_install_before_the_enable()
    {
        var rig = new Rig([new TestModule("M1", version: "3.1.0")]);

        var result = await rig.Service.EnableModuleAsync("M1");

        result.Should().Be(Result(true, "M1", "enable", ModuleLifecycleState.InstalledEnabled, ModuleLifecycleEffect.PartialUntilRestart));
        var row = (await rig.RowsAsync()).Should().ContainSingle().Subject;
        row.Enabled.Should().BeTrue();
        row.Version.Should().Be("3.1.0");
        rig.Recording.Actions.Should().Equal(AuditActionNames.ModuleInstalled, AuditActionNames.ModuleEnabled);
        var operation = rig.Recording.Unit.Operations.Should().ContainSingle().Subject;
        operation.Operation.PrimaryAction.Should().Be(AuditActionNames.ModuleEnabled, "the request is an enable; the installation is its secondary row");
        Fields(rig.Recording.Single(AuditActionNames.ModuleInstalled)).Should().Contain("implicitInstall", true)
            .And.Contain("previousState", "discovered").And.Contain("state", "installed_enabled");
        var enable = Fields(rig.Recording.Single(AuditActionNames.ModuleEnabled));
        enable.Should().Contain("previousState", "discovered").And.Contain("state", "installed_enabled").And.Contain("restartEffect", "partial_until_restart");
        enable.Should().NotContainKey("previousVersion");
        enable.Should().Contain("implicitInstall", true, "the primary enable row carries the flag beside the install row");
    }

    /// <summary>
    /// The first of the two defects this conversion fixes: a blocked enable of an installed, disabled module used to leave
    /// <c>Enabled = true</c> tracked and a later save persisted it.
    /// </summary>
    [Fact]
    public async Task A_blocked_enable_of_an_installed_disabled_module_leaves_it_disabled()
    {
        var rig = new Rig([new TestModule("M1"), new TestModule("M2", workflowId: "M1.Workflow")]);
        await rig.SeedAsync("M1", enabled: true);
        var stamp = await rig.SeedAsync("M2", enabled: false);
        var before = await rig.SnapshotAsync();

        var result = await rig.Service.EnableModuleAsync("M2");

        result.Should().BeEquivalentTo(new ModuleLifecycleTransitionResult(
            false, "M2", "enable", ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.Immediate, result.Blockers));
        result.Blockers.Should().Contain(b => b.Code == "workflow_id_conflict");
        (await rig.SnapshotAsync()).Should().Be(before, "a blocked enable changes no row");
        (await rig.RowsAsync()).Single(x => x.ModuleId == "M2").Enabled.Should().BeFalse();
        (await rig.RowsAsync()).Single(x => x.ModuleId == "M2").UpdatedAt.Should().Be(stamp);
        rig.Db.ChangeTracker.Entries().Should().OnlyContain(e => e.State == EntityState.Unchanged, "nothing evaluated is left to save");

        // Whatever saves next on the same context (the legacy append used to) cannot persist the evaluated candidate.
        await rig.Db.SaveChangesAsync();
        (await rig.SnapshotAsync()).Should().Be(before);

        rig.Recording.Unit.Actions.Should().BeEmpty();
        var attempt = rig.Recording.Writer.Written.Should().ContainSingle().Subject.Event;
        attempt.Action.Should().Be(AuditActionNames.ModuleEnableBlocked);
        Fields(attempt).Should().Contain("state", "installed_disabled");
        BlockerCodes(attempt).Should().Equal("workflow_id_conflict");
    }

    /// <summary>
    /// The second defect: a blocked enable of a module that was not installed used to leave the installation it had added to the
    /// tracker, and a later save persisted it.
    /// </summary>
    [Fact]
    public async Task A_blocked_enable_of_a_module_that_is_not_installed_leaves_no_installation()
    {
        var rig = new Rig([new TestModule("M1"), new TestModule("M2", workflowId: "M1.Workflow")]);
        await rig.SeedAsync("M1", enabled: true);
        var before = await rig.SnapshotAsync();

        var result = await rig.Service.EnableModuleAsync("M2");

        result.Succeeded.Should().BeFalse();
        result.State.Should().Be(ModuleLifecycleState.InstalledDisabled);
        result.Effect.Should().Be(ModuleLifecycleEffect.Immediate);
        result.Blockers.Should().Contain(b => b.Code == "workflow_id_conflict");
        rig.Db.ChangeTracker.Entries().Should().OnlyContain(e => e.State == EntityState.Unchanged, "no installation was added to the tracker");

        await rig.Db.SaveChangesAsync();
        (await rig.SnapshotAsync()).Should().Be(before, "the implicit installation was never persisted");
        (await rig.RowsAsync()).Should().NotContain(x => x.ModuleId == "M2");

        rig.Recording.Unit.Actions.Should().BeEmpty();
        var attempt = rig.Recording.Writer.Written.Should().ContainSingle().Subject.Event;
        attempt.Action.Should().Be(AuditActionNames.ModuleEnableBlocked);
        Fields(attempt).Should().Contain("state", "discovered");
    }

    // ---- disable ----

    [Fact]
    public async Task Disable_of_an_enabled_module_updates_the_row_and_records_one_disable_row()
    {
        var rig = new Rig([new TestModule("M1")]);
        var stamp = await rig.SeedAsync("M1", enabled: true, version: "0.9.0");

        var result = await rig.Service.DisableModuleAsync("M1");

        result.Should().Be(Result(true, "M1", "disable", ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.PartialUntilRestart));
        var row = (await rig.RowsAsync()).Should().ContainSingle().Subject;
        row.Enabled.Should().BeFalse();
        row.UpdatedAt.Should().BeAfter(stamp);
        Fields(rig.Recording.Single(AuditActionNames.ModuleDisabled)).Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["version"] = "0.9.0",
            ["previousState"] = "installed_enabled",
            ["state"] = "installed_disabled",
            ["restartEffect"] = "partial_until_restart",
        });
        rig.Recording.Actions.Should().ContainSingle();
    }

    [Fact]
    public async Task Disabling_a_disabled_module_is_a_no_op_with_the_same_success()
    {
        var rig = new Rig([new TestModule("M1")]);
        var stamp = await rig.SeedAsync("M1", enabled: false);

        var result = await rig.Service.DisableModuleAsync("M1");

        result.Should().Be(Result(true, "M1", "disable", ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.PartialUntilRestart));
        rig.Recording.Unit.Operations.Should().ContainSingle().Which.NoOp.Should().BeTrue();
        rig.Recording.Unit.Actions.Should().BeEmpty();
        rig.Recording.Writer.Written.Should().BeEmpty();
        (await rig.RowsAsync()).Single().UpdatedAt.Should().Be(stamp);
    }

    [Fact]
    public async Task Disabling_a_module_that_is_not_installed_is_refused_and_recorded_as_a_blocked_attempt()
    {
        var rig = new Rig([new TestModule("M1")]);

        var result = await rig.Service.DisableModuleAsync("M1");

        result.Succeeded.Should().BeFalse();
        result.Action.Should().Be("disable");
        result.State.Should().Be(ModuleLifecycleState.Discovered);
        result.Effect.Should().Be(ModuleLifecycleEffect.Immediate);
        result.Blockers.Should().Equal(new ModuleLifecycleBlocker("not_installed", "Module is not installed."));
        (await rig.RowsAsync()).Should().BeEmpty();
        rig.Recording.Unit.Actions.Should().BeEmpty();
        var attempt = rig.Recording.Writer.Written.Should().ContainSingle().Subject.Event;
        attempt.Action.Should().Be(AuditActionNames.ModuleDisableBlocked);
        Fields(attempt).Should().Contain("state", "discovered");
        BlockerCodes(attempt).Should().Equal("not_installed");
    }

    [Fact]
    public async Task Disable_blocked_by_persisted_references_changes_nothing_and_records_one_blocked_attempt()
    {
        var rig = new Rig([new TestModule("M1")]);
        await rig.SeedAsync("M1", enabled: true);
        await rig.SeedWorkflowInstanceAsync("M1.Workflow");
        await rig.SeedOperationInstanceAsync("M1.Operation");
        var before = await rig.SnapshotAsync();

        var result = await rig.Service.DisableModuleAsync("M1");

        result.Succeeded.Should().BeFalse();
        result.State.Should().Be(ModuleLifecycleState.InstalledEnabled);
        result.Effect.Should().Be(ModuleLifecycleEffect.PartialUntilRestart);
        result.Blockers.Select(b => b.Code).Should().Equal("active_workflow_references", "active_operation_references");
        (await rig.SnapshotAsync()).Should().Be(before);
        rig.Recording.Unit.Actions.Should().BeEmpty();
        var attempt = rig.Recording.Writer.Written.Should().ContainSingle().Subject.Event;
        attempt.Action.Should().Be(AuditActionNames.ModuleDisableBlocked);
        Fields(attempt).Should().Contain("state", "installed_enabled");
        BlockerCodes(attempt).Should().Equal("active_operation_references", "active_workflow_references");
        AllText(attempt).Should().NotContain("persisted", "a message is never recorded");
    }

    [Fact]
    public async Task Disable_of_a_module_that_is_not_installed_but_referenced_keeps_the_references_answer()
    {
        var rig = new Rig([new TestModule("M1")]);
        await rig.SeedWorkflowInstanceAsync("M1.Workflow");

        var result = await rig.Service.DisableModuleAsync("M1");

        result.Succeeded.Should().BeFalse();
        result.State.Should().Be(ModuleLifecycleState.InstalledEnabled, "the references are decided first, as before");
        result.Blockers.Should().ContainSingle(b => b.Code == "active_workflow_references");
        Fields(rig.Recording.Writer.Written.Single().Event).Should().Contain("state", "discovered", "the attempt row records the persisted state");
    }

    // ---- uninstall ----

    [Fact]
    public async Task Uninstall_deletes_the_row_and_records_one_uninstall_row()
    {
        var rig = new Rig([new TestModule("M1")]);
        await rig.SeedAsync("M1", enabled: true, version: "0.9.0");

        var result = await rig.Service.UninstallModuleAsync("M1");

        result.Should().Be(Result(true, "M1", "uninstall", ModuleLifecycleState.Discovered, ModuleLifecycleEffect.RequiresRestart));
        (await rig.RowsAsync()).Should().BeEmpty();
        Fields(rig.Recording.Single(AuditActionNames.ModuleUninstalled)).Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["version"] = "0.9.0",
            ["previousState"] = "installed_enabled",
            ["state"] = "discovered",
            ["restartEffect"] = "requires_restart",
        });
        rig.Recording.Actions.Should().ContainSingle();
    }

    [Fact]
    public async Task Uninstalling_a_module_that_is_not_installed_is_refused_and_recorded_as_a_blocked_attempt()
    {
        var rig = new Rig([new TestModule("M1")]);

        var result = await rig.Service.UninstallModuleAsync("M1");

        result.Succeeded.Should().BeFalse();
        result.Action.Should().Be("uninstall");
        result.State.Should().Be(ModuleLifecycleState.Discovered);
        result.Effect.Should().Be(ModuleLifecycleEffect.Immediate);
        result.Blockers.Should().Equal(new ModuleLifecycleBlocker("not_installed", "Module is not installed."));
        rig.Recording.Unit.Actions.Should().BeEmpty();
        var attempt = rig.Recording.Writer.Written.Should().ContainSingle().Subject.Event;
        attempt.Action.Should().Be(AuditActionNames.ModuleUninstallBlocked);
        BlockerCodes(attempt).Should().Equal("not_installed");
    }

    [Fact]
    public async Task Uninstall_blocked_by_persisted_references_keeps_the_installation_and_records_a_blocked_attempt()
    {
        var rig = new Rig([new TestModule("M1")]);
        await rig.SeedAsync("M1", enabled: false);
        await rig.SeedWorkflowInstanceAsync("M1.Workflow");
        var before = await rig.SnapshotAsync();

        var result = await rig.Service.UninstallModuleAsync("M1");

        result.Succeeded.Should().BeFalse();
        result.State.Should().Be(ModuleLifecycleState.InstalledDisabled);
        result.Effect.Should().Be(ModuleLifecycleEffect.RequiresRestart);
        result.Blockers.Should().ContainSingle(b => b.Code == "active_workflow_references");
        (await rig.SnapshotAsync()).Should().Be(before);
        rig.Recording.Unit.Actions.Should().BeEmpty();
        var attempt = rig.Recording.Writer.Written.Should().ContainSingle().Subject.Event;
        attempt.Action.Should().Be(AuditActionNames.ModuleUninstallBlocked);
        Fields(attempt).Should().Contain("state", "installed_disabled");
        BlockerCodes(attempt).Should().Equal("active_workflow_references");
    }

    // ---- the blocked attempt is best effort ----

    [Fact]
    public async Task A_failing_blocked_attempt_write_never_changes_the_answer()
    {
        var rig = new Rig([new TestModule("M1")]);
        rig.Recording.FailWrites = true;

        var result = await rig.Service.UninstallModuleAsync("M1");

        result.Succeeded.Should().BeFalse();
        result.Blockers.Should().Equal(new ModuleLifecycleBlocker("not_installed", "Module is not installed."));
        rig.Recording.Writer.Written.Should().BeEmpty();
    }

    // ---- actor ----

    [Fact]
    public async Task With_an_authenticated_principal_the_unit_and_the_blocked_attempt_use_the_current_principal()
    {
        var rig = new Rig([new TestModule("M1")], principal: new FixedPrincipal(Human()));

        await rig.Service.InstallModuleAsync("M1");
        await rig.Service.DisableModuleAsync("M1");
        await rig.Service.UninstallModuleAsync("M1");
        await rig.Service.UninstallModuleAsync("M1");

        rig.Recording.Unit.Operations.Should().OnlyContain(o => o.Operation.Actor.IsCurrentPrincipal);
        rig.Recording.Writer.Written.Should().ContainSingle().Which.Actor.IsCurrentPrincipal.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Without_a_principal_the_named_module_lifecycle_actor_is_recorded(bool principalContextPresent)
    {
        var rig = new Rig([new TestModule("M1")], principal: principalContextPresent ? new FixedPrincipal(null) : null);

        await rig.Service.EnableModuleAsync("M1");
        await rig.Service.UninstallModuleAsync("M1");
        await rig.Service.UninstallModuleAsync("M1");

        var systemActor = AuditActor.System("module-lifecycle");
        systemActor.Literal.Should().Be(ActorLiterals.SystemModuleLifecycle);
        rig.Recording.Unit.Operations.Should().OnlyContain(o => o.Operation.Actor == systemActor);
        rig.Recording.Writer.Written.Should().ContainSingle().Which.Actor.Should().Be(systemActor, "the attempt row is the unit's own actor");
    }

    // ---- missing collaborators, undiscovered modules ----

    public static IEnumerable<object[]> Transitions() =>
        [["install"], ["enable"], ["disable"], ["uninstall"]];

    private static ValueTask<ModuleLifecycleTransitionResult> Run(SqlModuleRegistryService service, string transition) => transition switch
    {
        "install" => service.InstallModuleAsync("M1"),
        "enable" => service.EnableModuleAsync("M1"),
        "disable" => service.DisableModuleAsync("M1"),
        "uninstall" => service.UninstallModuleAsync("M1"),
        _ => throw new ArgumentOutOfRangeException(nameof(transition)),
    };

    [Theory]
    [MemberData(nameof(Transitions))]
    public async Task A_lifecycle_transition_without_the_audited_unit_is_an_invariant_failure_that_changes_nothing(string transition)
    {
        var rig = new Rig([new TestModule("M1")], withUnit: false);
        await rig.SeedAsync("M1", enabled: true);
        var before = await rig.SnapshotAsync();

        var act = async () => await Run(rig.Service, transition);

        await act.Should().ThrowAsync<AuditInvariantException>();
        (await rig.SnapshotAsync()).Should().Be(before);
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public async Task A_lifecycle_transition_without_the_event_writer_is_an_invariant_failure_that_changes_nothing(string transition)
    {
        var rig = new Rig([new TestModule("M1")], withWriter: false);
        await rig.SeedAsync("M1", enabled: true);
        var before = await rig.SnapshotAsync();

        var act = async () => await Run(rig.Service, transition);

        await act.Should().ThrowAsync<AuditInvariantException>();
        (await rig.SnapshotAsync()).Should().Be(before);
        rig.Recording.Unit.Operations.Should().BeEmpty();
    }

    [Fact]
    public async Task A_registry_without_the_recorder_still_reads_and_discovers()
    {
        var rig = new Rig([new TestModule("M1")], withUnit: false, withWriter: false);
        await rig.SeedAsync("M1", enabled: true);

        (await rig.Service.ListModulesAsync()).Should().ContainSingle().Which.EffectivelyActive.Should().BeTrue();
        (await rig.Service.GetEnabledDescriptorsAsync()).Should().ContainSingle();
        (await rig.Service.DiscoverModulesAsync()).Succeeded.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public async Task A_module_the_host_does_not_discover_is_the_existing_refusal_with_no_unit_and_no_row(string transition)
    {
        var rig = new Rig([]);

        var act = async () => await Run(rig.Service, transition);

        var thrown = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        thrown.Should().NotBeOfType<AuditInvariantException>();
        thrown.Message.Should().Be("Module 'M1' is not discovered.");
        rig.Recording.Unit.Operations.Should().BeEmpty();
        rig.Recording.Writer.Written.Should().BeEmpty();
        rig.Recording.Unit.Actions.Should().BeEmpty();
    }

    // ---- reconstruct ----

    [Theory]
    [InlineData("install", ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.Immediate)]
    [InlineData("enable", ModuleLifecycleState.InstalledEnabled, ModuleLifecycleEffect.PartialUntilRestart)]
    [InlineData("disable", ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.PartialUntilRestart)]
    [InlineData("uninstall", ModuleLifecycleState.Discovered, ModuleLifecycleEffect.RequiresRestart)]
    public async Task A_verified_commit_after_a_lost_acknowledgement_returns_the_transition_from_the_committed_row(
        string transition, ModuleLifecycleState expectedState, ModuleLifecycleEffect expectedEffect)
    {
        var rig = new Rig([new TestModule("M1")]);
        if (transition is "disable" or "uninstall")
            await rig.SeedAsync("M1", enabled: true);
        rig.Recording.Unit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;

        var result = await Run(rig.Service, transition);

        result.Should().Be(Result(true, "M1", transition, expectedState, expectedEffect));
        rig.Recording.Writer.Written.Should().BeEmpty("a committed transition is never a refusal");
    }

    [Fact]
    public async Task A_lost_acknowledgement_of_an_implicit_install_returns_the_enable()
    {
        var rig = new Rig([new TestModule("M1")]);
        rig.Recording.Unit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;

        var result = await rig.Service.EnableModuleAsync("M1");

        result.Should().Be(Result(true, "M1", "enable", ModuleLifecycleState.InstalledEnabled, ModuleLifecycleEffect.PartialUntilRestart));
    }

    [Fact]
    public async Task A_committed_row_that_cannot_be_interpreted_is_a_committed_result_unavailable()
    {
        var rig = new Rig([new TestModule("M1")]);
        rig.Recording.Unit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;
        var field = typeof(SqlModuleRegistryService).GetMethod(
            "Reconstruct", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var act = () => field.Invoke(null, [new AuditCommittedOperation("e", AuditActionNames.ModuleEnabled, "module", "M1", "{}"), "M1"]);

        act.Should().Throw<System.Reflection.TargetInvocationException>().WithInnerException<InvalidOperationException>();
        await Task.CompletedTask;
    }

    // ---- discovery ----

    [Fact]
    public async Task Discovery_keeps_its_one_legacy_row_and_does_not_use_the_unit()
    {
        var rig = new Rig([new TestModule("M1")]);

        var result = await rig.Service.DiscoverModulesAsync();

        result.Should().Be(new ModuleLifecycleTransitionResult(true, "*", "discover", ModuleLifecycleState.Discovered, ModuleLifecycleEffect.Immediate, []));
        var record = rig.Audit.Records.Should().ContainSingle().Subject;
        record.Action.Should().Be("module.lifecycle.discover");
        record.Outcome.Should().Be("succeeded");
        record.Details.Should().Be("Discover modules; module=*; state=Discovered; effect=Immediate; blockers=none");
        rig.Recording.Unit.Operations.Should().BeEmpty();
        rig.Recording.Writer.Written.Should().BeEmpty();
    }

    // ---- the harness ----

    private static ModuleLifecycleTransitionResult Result(
        bool succeeded, string moduleId, string action, ModuleLifecycleState state, ModuleLifecycleEffect effect) =>
        new(succeeded, moduleId, action, state, effect, []);

    private static IReadOnlyDictionary<string, object?> Fields(AuditEvent evt) => evt.Fields;

    private static IReadOnlyList<string> BlockerCodes(AuditEvent evt) =>
        evt.Fields["blockers"] switch
        {
            IEnumerable<string> codes => codes.ToList(),
            _ => throw new InvalidOperationException("The attempt row names no blocker codes."),
        };

    private static string AllText(AuditEvent evt) =>
        string.Join("|", evt.Fields.Select(f => $"{f.Key}={(f.Value is IEnumerable<string> list ? string.Join(",", list) : f.Value)}"));

    private static HumanActor Human() => new(new ActorId("usr_module_admin"), "Module Admin", IdentitySource.Local);

    private sealed class Rig
    {
        private readonly DbContextOptions<AppertureDbContext> _options;

        public Rig(
            IReadOnlyList<IModule> modules,
            IPrincipalContext? principal = null,
            bool withUnit = true,
            bool withWriter = true)
        {
            _options = new DbContextOptionsBuilder<AppertureDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
            Db = new AppertureDbContext(_options);
            Service = new SqlModuleRegistryService(
                modules,
                Db,
                new TestEnvironmentContext(),
                Audit,
                principal,
                withUnit ? Recording : null,
                withWriter ? Recording : null);
        }

        public AppertureDbContext Db { get; }
        public InMemoryAuditService Audit { get; } = new();
        public AuditRecording Recording { get; } = new();
        public SqlModuleRegistryService Service { get; }

        /// <summary>A second context on the same store: what is persisted, not what the service context tracks.</summary>
        public async Task<List<ModuleInstallationStateEntity>> RowsAsync()
        {
            await using var db = new AppertureDbContext(_options);
            return await db.ModuleInstallationStates.AsNoTracking().OrderBy(x => x.ModuleId).ToListAsync();
        }

        /// <summary>Every installation row as one comparable text.</summary>
        public async Task<string> SnapshotAsync() =>
            string.Join("|", (await RowsAsync()).Select(r => $"{r.EnvironmentId},{r.ModuleId},{r.Version},{r.Enabled},{r.InstalledAt:O},{r.UpdatedAt:O}"));

        /// <summary>Seeds an installation through a separate context (so the service context tracks nothing). Returns its UpdatedAt.</summary>
        public async Task<DateTimeOffset> SeedAsync(string moduleId, bool enabled, string version = "1.0.0")
        {
            var stamp = DateTimeOffset.UtcNow.AddHours(-1);
            await using var db = new AppertureDbContext(_options);
            db.ModuleInstallationStates.Add(new ModuleInstallationStateEntity
            {
                EnvironmentId = Environment,
                ModuleId = moduleId,
                Version = version,
                Enabled = enabled,
                InstalledAt = stamp,
                UpdatedAt = stamp
            });
            await db.SaveChangesAsync();
            return stamp;
        }

        public async Task SeedWorkflowInstanceAsync(string definitionId)
        {
            await using var db = new AppertureDbContext(_options);
            db.WorkflowInstances.Add(new WorkflowInstanceEntity
            {
                Id = "wf-" + Guid.NewGuid().ToString("N")[..8],
                DefinitionId = definitionId,
                Status = "running",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        public async Task SeedOperationInstanceAsync(string definitionId)
        {
            await using var db = new AppertureDbContext(_options);
            db.OperationInstances.Add(new OperationInstanceEntity
            {
                Id = "op-" + Guid.NewGuid().ToString("N")[..8],
                DefinitionId = definitionId,
                Status = "running",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }
    }

    private sealed class FixedPrincipal(IActor? actor) : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(actor);
    }

    private sealed class TestEnvironmentContext : IEnvironmentContext
    {
        public string EnvironmentId => Environment;
    }

    private sealed class TestModuleWithGenericPermissions : IModule, IOperationDefinitionProvider
    {
        private readonly string _moduleId;

        public TestModuleWithGenericPermissions(string moduleId) => _moduleId = moduleId;

        public ModuleManifest GetManifest() => new(_moduleId, _moduleId, "1.0.0", "test")
        {
            Permissions =
            [
                new ModulePermissionDeclaration("workflow.execute", "WorkflowInstance"),
                new ModulePermissionDeclaration(ActionConstants.OperationExecute, ResourceTypeConstants.OperationDefinition)
            ]
        };

        public async IAsyncEnumerable<WorkflowDefinition> GetDefinitionsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new WorkflowDefinition(
                new WorkflowDefinitionId($"{_moduleId}.Workflow"),
                "Test",
                _moduleId,
                ["Step1"]);
            await Task.CompletedTask;
        }

        public async IAsyncEnumerable<OperationDefinition> GetOperationsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new OperationDefinition(
                new OperationId($"{_moduleId}.Operation"),
                "TestOp",
                _moduleId,
                OperationCategory.Command,
                null,
                []);
            await Task.CompletedTask;
        }
    }

    private sealed class TestModule : IModule, IOperationDefinitionProvider
    {
        private readonly string _moduleId;
        private readonly string _workflowId;
        private readonly string _operationId;
        private readonly string _navRoute;
        private readonly string _version;

        public TestModule(string moduleId, string? workflowId = null, string? operationId = null, string? navRoute = null, string version = "1.0.0")
        {
            _moduleId = moduleId;
            _workflowId = workflowId ?? $"{moduleId}.Workflow";
            _operationId = operationId ?? $"{moduleId}.Operation";
            _navRoute = navRoute ?? $"/{moduleId.ToLowerInvariant()}";
            _version = version;
        }

        public ModuleManifest GetManifest() => new(_moduleId, _moduleId, _version, "test");

        public ValueTask<ModuleDescriptor> BuildDescriptorAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new ModuleDescriptor(
                _moduleId,
                _moduleId,
                _version,
                "test",
                null,
                null,
                [
                    new WorkflowContributionDescriptor(_workflowId, "TestWorkflow"),
                    new OperationContributionDescriptor(_operationId, "TestOp"),
                    new NavigationContributionDescriptor("Automation", "Test", _navRoute),
                ]));

        public async IAsyncEnumerable<WorkflowDefinition> GetDefinitionsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new WorkflowDefinition(new WorkflowDefinitionId(_workflowId), "Test", _moduleId, ["Step1"]);
            await Task.CompletedTask;
        }

        public async IAsyncEnumerable<OperationDefinition> GetOperationsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new OperationDefinition(new OperationId(_operationId), "TestOp", _moduleId, OperationCategory.Command, null, []);
            await Task.CompletedTask;
        }
    }

    internal sealed class InMemoryAuditService : IAuditService
    {
        public List<AuditRecord> Records { get; } = [];

        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return ValueTask.CompletedTask;
        }

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AuditPage(Records, null));

        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default)
        {
            var q = Records.AsEnumerable();
            if (query.FromUtc.HasValue)
                q = q.Where(r => r.Timestamp >= query.FromUtc.Value);
            if (query.ToUtc.HasValue)
                q = q.Where(r => r.Timestamp <= query.ToUtc.Value);
            return ValueTask.FromResult(q.Count());
        }

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<AuditRecord?>(Records.FirstOrDefault(r => r.Id.Value == id));
    }
}
