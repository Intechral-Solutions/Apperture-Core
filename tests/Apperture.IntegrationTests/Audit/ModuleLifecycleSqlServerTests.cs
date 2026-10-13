using System.Text.Json;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Operations;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.4 slice 7: the module lifecycle transitions (install, enable, disable, uninstall) as audited units against SQL Server on
/// locking READ COMMITTED and READ_COMMITTED_SNAPSHOT catalogs. M7: the installation row and the audit row of every transition
/// (actor, target, related module, outcome, category, severity, basis, details with the persisted states, the version and the restart
/// effect), including the implicit installation an enable performs (two rows, one operation). A refused transition (contribution
/// conflicts, persisted references, a module that is not installed) leaves the whole table as it was, writes no change row and
/// writes exactly one blocked attempt of the same actor with blocker codes only; two defects of the earlier code (a blocked enable
/// persisting <c>Enabled = true</c> or the implicit installation) are pinned against the real database. A request that changes
/// nothing writes and records nothing. Races, deterministic through a third session holding the module lifecycle lock: two enables
/// of one uninstalled module, an enable against a disable, and two modules whose contributions conflict enabled together (never both
/// enabled), each in both orders; and an installation committed by a writer that bypassed the lock. M6 for every transition: an
/// audit insert failure leaves nothing, a lost acknowledgement is verified and reconstructed, a confirmed rollback reruns once, an
/// unknown outcome is wholly committed or wholly absent, an operation lock that is not granted changes nothing. Fails, never skips,
/// without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ModuleLifecycleSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private const string Environment = "local";
    private const string Version = "2.5.0";

    private readonly AuditIsolationCatalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public ModuleLifecycleSqlServerTests(AuditIsolationCatalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    /// <summary>A catalog of this case's own, for the cases that need an empty audit table (the M6 matrix). The audit table is append-only.</summary>
    private static Task<AuditSqlScratch> NewScratchAsync(bool rcsi) => AuditIsolation.CreateAsync("Apperture_Aud14Mod", rcsi);

    public enum Transition
    {
        Install,
        Enable,
        EnableImplicit,
        Disable,
        Uninstall,
    }

    public enum Refusal
    {
        InstallConflict,
        EnableConflictNotInstalled,
        EnableConflictInstalledDisabled,
        DisableReferenced,
        DisableNotInstalled,
        UninstallReferenced,
        UninstallNotInstalled,
    }

    public static IEnumerable<object[]> Isolation() => [[false], [true]];

    public static IEnumerable<object[]> Cases() =>
        from transition in Enum.GetValues<Transition>()
        from rcsi in new[] { false, true }
        select new object[] { transition, rcsi };

    public static IEnumerable<object[]> Refusals() =>
        from refusal in Enum.GetValues<Refusal>()
        from rcsi in new[] { false, true }
        select new object[] { refusal, rcsi };

    public static IEnumerable<object[]> Transitions() =>
        new[] { Transition.Install, Transition.Enable, Transition.Disable, Transition.Uninstall }.Select(t => new object[] { t });

    /// <summary>The M6 matrix: install, enable with the implicit installation (nothing is installed), disable and uninstall.</summary>
    public static IEnumerable<object[]> M6Transitions() =>
        new[] { Transition.Install, Transition.EnableImplicit, Transition.Disable, Transition.Uninstall }.Select(t => new object[] { t });

    public static IEnumerable<object[]> OutcomeCases() =>
        from transition in new[] { Transition.Install, Transition.EnableImplicit, Transition.Disable, Transition.Uninstall }
        from committed in new[] { true, false }
        select new object[] { transition, committed };

    public static IEnumerable<object[]> Races() =>
        from firstIsA in new[] { true, false }
        from rcsi in new[] { false, true }
        select new object[] { firstIsA, rcsi };

    public static IEnumerable<object[]> BackstopCases() =>
        from enable in new[] { false, true }
        from rcsi in new[] { false, true }
        select new object[] { enable, rcsi };

    // ---- the service over a real catalog ----

    private sealed class Env : IEnvironmentContext
    {
        public string EnvironmentId => Environment;
    }

    /// <summary>The legacy append must not be reached by a lifecycle transition at all; reaching it fails the test.</summary>
    private sealed class ForbiddenAuditService : IAuditService
    {
        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A lifecycle transition reached the legacy append.");

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static SqlModuleRegistryService Registry(
        AuditSqlScratch scratch,
        AppertureDbContext db,
        IReadOnlyList<IModule> modules,
        IPrincipalContext? principal,
        IAuditedUnitOfWork unit) =>
        new(
            modules,
            db,
            new Env(),
            new ForbiddenAuditService(),
            principal,
            unit,
            new SqlAuditEventWriter(scratch.Options(), principal, HarnessCatalog, null, null, null));

    private static string NewModuleId() => "mod-" + Guid.NewGuid().ToString("N")[..10];

    private static ValueTask<ModuleLifecycleTransitionResult> Run(SqlModuleRegistryService registry, Transition transition, string moduleId) =>
        transition switch
        {
            Transition.Install => registry.InstallModuleAsync(moduleId),
            Transition.Enable or Transition.EnableImplicit => registry.EnableModuleAsync(moduleId),
            Transition.Disable => registry.DisableModuleAsync(moduleId),
            Transition.Uninstall => registry.UninstallModuleAsync(moduleId),
            _ => throw new ArgumentOutOfRangeException(nameof(transition)),
        };

    private static string ActionOf(Transition transition) => transition switch
    {
        Transition.Install => AuditActionNames.ModuleInstalled,
        Transition.Enable or Transition.EnableImplicit => AuditActionNames.ModuleEnabled,
        Transition.Disable => AuditActionNames.ModuleDisabled,
        _ => AuditActionNames.ModuleUninstalled,
    };

    /// <summary>A module the host discovers. Its workflow id is what two modules can conflict on.</summary>
    private sealed class SqlTestModule(string moduleId, string? workflowId = null, string version = Version) : IModule
    {
        public string WorkflowId { get; } = workflowId ?? $"{moduleId}.Workflow";

        public ModuleManifest GetManifest() => new(moduleId, moduleId, version, "test");

        public ValueTask<ModuleDescriptor> BuildDescriptorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ModuleDescriptor(
                moduleId,
                moduleId,
                version,
                "test",
                null,
                null,
                [
                    new WorkflowContributionDescriptor(WorkflowId, "TestWorkflow"),
                    new OperationContributionDescriptor($"{moduleId}.Operation", "TestOp"),
                    new NavigationContributionDescriptor("Automation", "Test", $"/{moduleId}"),
                ]));

        public async IAsyncEnumerable<WorkflowDefinition> GetDefinitionsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new WorkflowDefinition(new WorkflowDefinitionId(WorkflowId), "Test", moduleId, ["Step1"]);
            await Task.CompletedTask;
        }
    }

    // ---- fixtures and observation ----

    /// <summary>An installation straight into the table (the audit table is append-only, so a fixture writes no audit row).</summary>
    private static Task SeedInstallationAsync(AuditSqlScratch scratch, string moduleId, bool enabled, string version = Version) =>
        scratch.ExecuteAsync(
            $"""
            INSERT INTO ModuleInstallationStates (EnvironmentId, ModuleId, Version, Enabled, InstalledAt, UpdatedAt)
            VALUES (N'{Environment}', N'{moduleId}', N'{version}', {(enabled ? 1 : 0)}, DATEADD(HOUR, -2, SYSDATETIMEOFFSET()), DATEADD(HOUR, -1, SYSDATETIMEOFFSET()));
            """);

    private static async Task SeedWorkflowReferenceAsync(AuditSqlScratch scratch, string definitionId)
    {
        await using var db = scratch.NewDb();
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = "wf-" + Guid.NewGuid().ToString("N")[..12],
            DefinitionId = definitionId,
            Status = "running",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Every installation row of every module, as one comparable text (the snapshot a refused or failed transition must not change).</summary>
    private static async Task<string> SnapshotAsync(AuditSqlScratch scratch) =>
        JsonSerializer.Serialize((await scratch.RowsAsync("SELECT * FROM ModuleInstallationStates ORDER BY EnvironmentId, ModuleId;"))
            .Select(r => r.Values.Select(v => v?.ToString())));

    private static Task<List<Dictionary<string, object?>>> InstallationRowsAsync(AuditSqlScratch scratch, string moduleId) =>
        scratch.RowsAsync($"SELECT * FROM ModuleInstallationStates WHERE ModuleId = N'{moduleId}';");

    private static Task<List<Dictionary<string, object?>>> AuditRowsAsync(AuditSqlScratch scratch, string moduleId) =>
        scratch.RowsAsync($"SELECT * FROM AuditLogs WHERE ModuleId = N'{moduleId}' ORDER BY Id;");

    private static Task<List<Dictionary<string, object?>>> AllAuditRowsAsync(AuditSqlScratch scratch) =>
        scratch.RowsAsync("SELECT * FROM AuditLogs ORDER BY Id;");

    private static string Detail(Dictionary<string, object?> row, string name) =>
        ConnectionSqlHarness.Detail(row, name) ?? string.Empty;

    private static IReadOnlyList<string> DetailList(Dictionary<string, object?> row, string name)
    {
        using var details = JsonDocument.Parse((string)row["Details"]!);
        return details.RootElement.GetProperty(name).EnumerateArray().Select(e => e.GetString()!).ToList();
    }

    private static IReadOnlyList<string> DetailNames(Dictionary<string, object?> row)
    {
        using var details = JsonDocument.Parse((string)row["Details"]!);
        return details.RootElement.EnumerateObject().Select(p => p.Name).ToList();
    }

    private static Task<(string Admin, IPrincipalContext Principal)> AdminAsync(AuditSqlScratch scratch) =>
        ConnectionSqlHarness.AdminAsync(scratch);

    /// <summary>Records the SQL Server error numbers of the saves that failed, to show which path a race took.</summary>
    private sealed class FailedSaveNumbers : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public List<int> Numbers { get; } = [];

        public override Task SaveChangesFailedAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            for (var inner = eventData.Exception; inner is not null; inner = inner.InnerException)
            {
                if (inner is Microsoft.Data.SqlClient.SqlException sql)
                {
                    lock (Numbers)
                        Numbers.Add(sql.Number);
                    break;
                }
            }

            return Task.CompletedTask;
        }
    }

    private sealed record Outcome<T>(T? Value, Exception? Error);

    private static async Task<Outcome<T>> RunAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return new Outcome<T>(await Task.Run(operation), null);
        }
        catch (Exception ex)
        {
            return new Outcome<T>(default, ex);
        }
    }

    /// <summary>One transition on a context and unit of its own, as one request would run it.</summary>
    private static Task<Outcome<ModuleLifecycleTransitionResult>> RequestAsync(
        AuditSqlScratch scratch, IReadOnlyList<IModule> modules, IPrincipalContext principal, Transition transition, string moduleId, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors) =>
        RunAsync(async () =>
        {
            await using var db = scratch.NewDb(interceptors);
            return await Run(Registry(scratch, db, modules, principal, NewUnit(db, principal)), transition, moduleId);
        });

    // ---- M7: the committed transition ----

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_committed_transition_changes_the_row_and_records_the_administrators_row_with_the_persisted_states_and_the_restart_effect(
        Transition transition, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (admin, principal) = await AdminAsync(scratch);
        var moduleId = NewModuleId();
        IReadOnlyList<IModule> modules = [new SqlTestModule(moduleId)];
        switch (transition)
        {
            case Transition.Enable:
                await SeedInstallationAsync(scratch, moduleId, enabled: false, version: "2.0.0");
                break;
            case Transition.Disable or Transition.Uninstall:
                await SeedInstallationAsync(scratch, moduleId, enabled: true);
                break;
        }

        ModuleLifecycleTransitionResult result;
        await using (var db = scratch.NewDb())
            result = await Run(Registry(scratch, db, modules, principal, NewUnit(db, principal)), transition, moduleId);

        var (expectedState, expectedEffect, expectedPrevious, expectedRowCount) = transition switch
        {
            Transition.Install => ("InstalledDisabled", "Immediate", "discovered", 1),
            Transition.Enable or Transition.EnableImplicit => ("InstalledEnabled", "PartialUntilRestart", transition == Transition.Enable ? "installed_disabled" : "discovered", transition == Transition.Enable ? 1 : 2),
            Transition.Disable => ("InstalledDisabled", "PartialUntilRestart", "installed_enabled", 1),
            _ => ("Discovered", "RequiresRestart", "installed_enabled", 1),
        };
        result.Succeeded.Should().BeTrue();
        result.ModuleId.Should().Be(moduleId);
        result.State.ToString().Should().Be(expectedState);
        result.Effect.ToString().Should().Be(expectedEffect);
        result.Blockers.Should().BeEmpty();

        var installation = await InstallationRowsAsync(scratch, moduleId);
        switch (transition)
        {
            case Transition.Install:
                installation.Should().ContainSingle().Which["Enabled"].Should().Be(false);
                break;
            case Transition.Enable or Transition.EnableImplicit or Transition.Disable:
                var row = installation.Should().ContainSingle().Subject;
                row["Enabled"].Should().Be(transition != Transition.Disable);
                row["Version"].Should().Be(Version, "the installation is at the discovered version");
                row["EnvironmentId"].Should().Be(Environment);
                break;
            default:
                installation.Should().BeEmpty();
                break;
        }

        var rows = await AuditRowsAsync(scratch, moduleId);
        rows.Should().HaveCount(expectedRowCount, "one row per change, and the implicit installation is the one extra");
        var primary = rows.Single(r => (string)r["Action"]! == ActionOf(transition));
        primary["OperationEventId"].Should().Be(primary["EventId"], "the row is the primary row of its own operation");
        primary["Actor"].Should().Be(admin);
        primary["TargetType"].Should().Be(AuditTargetTypes.Module);
        primary["TargetId"].Should().Be(moduleId);
        primary["ModuleId"].Should().Be(moduleId);
        primary["OutcomeCode"].Should().Be(AuditOutcomeCodes.Succeeded);
        primary["Category"].Should().Be(AuditCategories.Configuration);
        primary["Severity"].Should().Be(AuditSeverities.Standard);
        primary["Basis"].Should().Be(AuditBases.Change);
        primary["Visibility"].Should().Be("default");
        Detail(primary, "previousState").Should().Be(expectedPrevious);
        Detail(primary, "state").Should().Be(JsonNamingState(expectedState));
        Detail(primary, "restartEffect").Should().Be(JsonNamingEffect(expectedEffect));
        Detail(primary, "version").Should().Be(Version);
        if (transition == Transition.Enable)
            Detail(primary, "previousVersion").Should().Be("2.0.0", "the enable moved the installation to the discovered version");
        else
            DetailNames(primary).Should().NotContain("previousVersion");

        if (transition == Transition.EnableImplicit)
        {
            var install = rows.Single(r => (string)r["Action"]! == AuditActionNames.ModuleInstalled);
            install["OperationEventId"].Should().Be(primary["EventId"], "the installation belongs to the enable's operation");
            install["EventId"].Should().NotBe(primary["EventId"]);
            install["Actor"].Should().Be(admin);
            Detail(install, "implicitInstall").Should().Be("true");
            Detail(install, "previousState").Should().Be("discovered");
            Detail(install, "state").Should().Be("installed_enabled");
            Detail(primary, "implicitInstall").Should().Be("true", "the enable that installed the module carries the flag beside the install row");
        }
        else
            DetailNames(primary).Should().NotContain("implicitInstall");

        _output.WriteLine(scratch.ExecutionMarker);
    }

    private static string JsonNamingState(string state) => state switch
    {
        "InstalledDisabled" => "installed_disabled",
        "InstalledEnabled" => "installed_enabled",
        _ => "discovered",
    };

    private static string JsonNamingEffect(string effect) => effect switch
    {
        "Immediate" => "immediate",
        "PartialUntilRestart" => "partial_until_restart",
        _ => "requires_restart",
    };

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_transition_without_a_principal_is_recorded_under_the_named_module_lifecycle_actor(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var moduleId = NewModuleId();

        await using (var db = scratch.NewDb())
            (await Registry(scratch, db, [new SqlTestModule(moduleId)], null, NewUnit(db, null)).EnableModuleAsync(moduleId)).Succeeded.Should().BeTrue();

        var rows = await AuditRowsAsync(scratch, moduleId);
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => (string)r["Actor"]! == ActorLiterals.SystemModuleLifecycle);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- refusals ----

    private sealed record Arranged(
        IReadOnlyList<IModule> Modules,
        string ModuleId,
        Transition Transition,
        string BlockedAction,
        string ExpectedPersistedState,
        string[] ExpectedCodes,
        ModuleLifecycleState ExpectedResultState,
        ModuleLifecycleEffect ExpectedResultEffect);

    private static async Task<Arranged> ArrangeRefusalAsync(AuditSqlScratch scratch, Refusal refusal)
    {
        var holder = NewModuleId();
        var candidate = NewModuleId();
        var holderModule = new SqlTestModule(holder);
        var candidateModule = new SqlTestModule(candidate, workflowId: holderModule.WorkflowId);
        IReadOnlyList<IModule> pair = [holderModule, candidateModule];
        switch (refusal)
        {
            case Refusal.InstallConflict:
                await SeedInstallationAsync(scratch, holder, enabled: true);
                return new Arranged(pair, candidate, Transition.Install, AuditActionNames.ModuleInstallBlocked, "discovered",
                    ["workflow_id_conflict"], ModuleLifecycleState.Discovered, ModuleLifecycleEffect.Immediate);
            case Refusal.EnableConflictNotInstalled:
                await SeedInstallationAsync(scratch, holder, enabled: true);
                return new Arranged(pair, candidate, Transition.Enable, AuditActionNames.ModuleEnableBlocked, "discovered",
                    ["workflow_id_conflict"], ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.Immediate);
            case Refusal.EnableConflictInstalledDisabled:
                await SeedInstallationAsync(scratch, holder, enabled: true);
                await SeedInstallationAsync(scratch, candidate, enabled: false);
                return new Arranged(pair, candidate, Transition.Enable, AuditActionNames.ModuleEnableBlocked, "installed_disabled",
                    ["workflow_id_conflict"], ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.Immediate);
            case Refusal.DisableReferenced:
                await SeedInstallationAsync(scratch, holder, enabled: true);
                await SeedWorkflowReferenceAsync(scratch, holderModule.WorkflowId);
                return new Arranged([holderModule], holder, Transition.Disable, AuditActionNames.ModuleDisableBlocked, "installed_enabled",
                    ["active_workflow_references"], ModuleLifecycleState.InstalledEnabled, ModuleLifecycleEffect.PartialUntilRestart);
            case Refusal.DisableNotInstalled:
                return new Arranged([holderModule], holder, Transition.Disable, AuditActionNames.ModuleDisableBlocked, "discovered",
                    ["not_installed"], ModuleLifecycleState.Discovered, ModuleLifecycleEffect.Immediate);
            case Refusal.UninstallReferenced:
                await SeedInstallationAsync(scratch, holder, enabled: false);
                await SeedWorkflowReferenceAsync(scratch, holderModule.WorkflowId);
                return new Arranged([holderModule], holder, Transition.Uninstall, AuditActionNames.ModuleUninstallBlocked, "installed_disabled",
                    ["active_workflow_references"], ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.RequiresRestart);
            default:
                return new Arranged([holderModule], holder, Transition.Uninstall, AuditActionNames.ModuleUninstallBlocked, "discovered",
                    ["not_installed"], ModuleLifecycleState.Discovered, ModuleLifecycleEffect.Immediate);
        }
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task A_refused_transition_changes_nothing_and_records_one_blocked_attempt_of_the_same_actor_with_codes_only(Refusal refusal, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (admin, principal) = await AdminAsync(scratch);
        var arranged = await ArrangeRefusalAsync(scratch, refusal);
        var before = await SnapshotAsync(scratch);
        var auditBefore = (await AllAuditRowsAsync(scratch)).Count;

        ModuleLifecycleTransitionResult result;
        await using (var db = scratch.NewDb())
        {
            result = await Run(Registry(scratch, db, arranged.Modules, principal, NewUnit(db, principal)), arranged.Transition, arranged.ModuleId);

            // Whatever saves next on the same context must not be able to persist anything the refused decision evaluated.
            await db.SaveChangesAsync();
        }

        result.Succeeded.Should().BeFalse();
        result.ModuleId.Should().Be(arranged.ModuleId);
        result.State.Should().Be(arranged.ExpectedResultState);
        result.Effect.Should().Be(arranged.ExpectedResultEffect);
        result.Blockers.Select(b => b.Code).Should().Equal(arranged.ExpectedCodes);
        result.Blockers.Should().OnlyContain(b => b.Message.Length > 0);

        (await SnapshotAsync(scratch)).Should().Be(before, "a refused transition leaves every installation row exactly as it was");
        var rows = await AuditRowsAsync(scratch, arranged.ModuleId);
        var row = rows.Should().ContainSingle("a refusal is one attempt row and no change row").Subject;
        (await AllAuditRowsAsync(scratch)).Should().HaveCount(auditBefore + 1);
        row["Action"].Should().Be(arranged.BlockedAction);
        row["Basis"].Should().Be(AuditBases.Attempt);
        row["OutcomeCode"].Should().Be(AuditOutcomeCodes.Blocked);
        row["Actor"].Should().Be(admin);
        row["TargetType"].Should().Be(AuditTargetTypes.Module);
        row["TargetId"].Should().Be(arranged.ModuleId);
        row["Category"].Should().Be(AuditCategories.Configuration);
        Detail(row, "state").Should().Be(arranged.ExpectedPersistedState);
        DetailList(row, "blockers").Should().Equal(arranged.ExpectedCodes);
        DetailNames(row).Should().BeEquivalentTo("state", "blockers");
        foreach (var message in result.Blockers.Select(b => b.Message))
            ((string)row["Details"]!).Should().NotContain(message, "a message is never recorded");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    /// <summary>
    /// Defect 1 against the real database: the enable conflict check used to set <c>Enabled = true</c> on the tracked installation
    /// it loaded, and the legacy append's save persisted it under a blocked row.
    /// </summary>
    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_blocked_enable_does_not_persist_enabled_on_an_installed_disabled_module(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var arranged = await ArrangeRefusalAsync(scratch, Refusal.EnableConflictInstalledDisabled);
        var before = (await InstallationRowsAsync(scratch, arranged.ModuleId)).Single();

        await using (var db = scratch.NewDb())
        {
            var registry = Registry(scratch, db, arranged.Modules, principal, NewUnit(db, principal));
            (await registry.EnableModuleAsync(arranged.ModuleId)).Succeeded.Should().BeFalse();
            db.ChangeTracker.Entries().Should().BeEmpty("nothing the decision evaluated is tracked");
            await db.SaveChangesAsync();
        }

        var after = (await InstallationRowsAsync(scratch, arranged.ModuleId)).Single();
        after["Enabled"].Should().Be(false);
        after["UpdatedAt"].Should().Be(before["UpdatedAt"]);
        after["Version"].Should().Be(before["Version"]);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    /// <summary>Defect 2: a blocked enable of a module that was not installed used to persist the installation it had added.</summary>
    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_blocked_enable_does_not_persist_the_implicit_installation_of_a_module_that_was_not_installed(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var arranged = await ArrangeRefusalAsync(scratch, Refusal.EnableConflictNotInstalled);

        await using (var db = scratch.NewDb())
        {
            var registry = Registry(scratch, db, arranged.Modules, principal, NewUnit(db, principal));
            (await registry.EnableModuleAsync(arranged.ModuleId)).Succeeded.Should().BeFalse();
            db.ChangeTracker.Entries().Should().BeEmpty("no installation was added to the tracker");
            await db.SaveChangesAsync();
        }

        (await InstallationRowsAsync(scratch, arranged.ModuleId)).Should().BeEmpty("the blocked enable left no installation");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- no-ops ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_request_that_changes_nothing_writes_nothing_and_leaves_the_update_time_alone(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var installed = NewModuleId();
        var enabled = NewModuleId();
        var disabled = NewModuleId();
        IReadOnlyList<IModule> modules = [new SqlTestModule(installed), new SqlTestModule(enabled), new SqlTestModule(disabled)];
        await SeedInstallationAsync(scratch, installed, enabled: false);
        await SeedInstallationAsync(scratch, enabled, enabled: true);
        await SeedInstallationAsync(scratch, disabled, enabled: false);
        var before = await SnapshotAsync(scratch);
        var auditBefore = (await AllAuditRowsAsync(scratch)).Count;

        await using (var db = scratch.NewDb())
        {
            var registry = Registry(scratch, db, modules, principal, NewUnit(db, principal));
            (await registry.InstallModuleAsync(installed)).Should().BeEquivalentTo(
                new ModuleLifecycleTransitionResult(true, installed, "install", ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.Immediate, []));
            (await registry.InstallModuleAsync(enabled)).State.Should().Be(ModuleLifecycleState.InstalledEnabled, "the answer reports the row as it is");
            (await registry.EnableModuleAsync(enabled)).Should().BeEquivalentTo(
                new ModuleLifecycleTransitionResult(true, enabled, "enable", ModuleLifecycleState.InstalledEnabled, ModuleLifecycleEffect.PartialUntilRestart, []));
            (await registry.DisableModuleAsync(disabled)).Should().BeEquivalentTo(
                new ModuleLifecycleTransitionResult(true, disabled, "disable", ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.PartialUntilRestart, []));
        }

        (await SnapshotAsync(scratch)).Should().Be(before, "no row, and no update time, moved");
        (await AllAuditRowsAsync(scratch)).Should().HaveCount(auditBefore, "an already-satisfied request is neither a change nor an attempt");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public async Task A_module_the_host_does_not_discover_is_refused_and_nothing_is_written(Transition transition)
    {
        var scratch = _catalogs.For(false);
        var (_, principal) = await AdminAsync(scratch);
        var moduleId = NewModuleId();
        var before = await SnapshotAsync(scratch);
        var auditBefore = (await AllAuditRowsAsync(scratch)).Count;

        await using (var db = scratch.NewDb())
        {
            var act = async () => await Run(Registry(scratch, db, [new SqlTestModule(NewModuleId())], principal, NewUnit(db, principal)), transition, moduleId);
            var thrown = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
            thrown.Should().NotBeOfType<AuditInvariantException>();
            thrown.Message.Should().Be($"Module '{moduleId}' is not discovered.");
        }

        (await SnapshotAsync(scratch)).Should().Be(before);
        (await AllAuditRowsAsync(scratch)).Should().HaveCount(auditBefore);
    }

    // ---- races ----

    /// <summary>
    /// Two enables of one uninstalled module, queued on the lifecycle lock in both orders. The first installs and enables the module
    /// (two rows, one operation, the first caller's actor). The second reads what the first committed under the lock and finds the
    /// module enabled at the discovered version: a no-op that writes and records nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(Races))]
    public async Task Two_enables_of_one_uninstalled_module_leave_one_installation_and_one_install_and_enable_pair_of_the_first_caller(bool firstIsA, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (adminA, principalA) = await AdminAsync(scratch);
        var (adminB, principalB) = await AdminAsync(scratch);
        var moduleId = NewModuleId();
        IReadOnlyList<IModule> modules = [new SqlTestModule(moduleId)];

        await using var holder = await ModuleLifecycleLockRace.HoldAsync(scratch);
        var firstPrincipal = firstIsA ? principalA : principalB;
        var secondPrincipal = firstIsA ? principalB : principalA;
        var firstAdmin = firstIsA ? adminA : adminB;
        var first = RequestAsync(scratch, modules, firstPrincipal, Transition.Enable, moduleId);
        await ModuleLifecycleLockRace.WaitForWaitersAsync(scratch, 1, first);
        var second = RequestAsync(scratch, modules, secondPrincipal, Transition.Enable, moduleId);
        await ModuleLifecycleLockRace.WaitForWaitersAsync(scratch, 2, first, second);
        await holder.ReleaseAsync();
        var one = await first;
        var two = await second;

        one.Error.Should().BeNull();
        two.Error.Should().BeNull();
        one.Value!.Succeeded.Should().BeTrue();
        two.Value!.Should().BeEquivalentTo(one.Value, "the second caller reports the same enabled module");
        (await InstallationRowsAsync(scratch, moduleId)).Should().ContainSingle().Which["Enabled"].Should().Be(true);
        var rows = await AuditRowsAsync(scratch, moduleId);
        rows.Select(r => (string)r["Action"]!).Should().BeEquivalentTo(AuditActionNames.ModuleInstalled, AuditActionNames.ModuleEnabled);
        rows.Should().OnlyContain(r => (string)r["Actor"]! == firstAdmin, "the second request wrote no row");
        rows.Select(r => (string)r["OperationEventId"]!).Distinct().Should().ContainSingle("both rows belong to one operation");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    /// <summary>
    /// An enable and a disable of one installed, disabled module, queued in both orders. Each decides against what the other
    /// committed, so the final state is the last effective committer's and every committed transition has its row; a disable that
    /// runs first finds the module already disabled and is a no-op.
    /// </summary>
    [Theory]
    [MemberData(nameof(Races))]
    public async Task An_enable_and_a_disable_of_one_module_end_in_the_last_committers_state_with_a_row_for_each_committed_change(bool enableFirst, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (adminA, principalA) = await AdminAsync(scratch);
        var (adminB, principalB) = await AdminAsync(scratch);
        var moduleId = NewModuleId();
        IReadOnlyList<IModule> modules = [new SqlTestModule(moduleId)];
        await SeedInstallationAsync(scratch, moduleId, enabled: false);

        await using var holder = await ModuleLifecycleLockRace.HoldAsync(scratch);
        var firstTransition = enableFirst ? Transition.Enable : Transition.Disable;
        var secondTransition = enableFirst ? Transition.Disable : Transition.Enable;
        var first = RequestAsync(scratch, modules, principalA, firstTransition, moduleId);
        await ModuleLifecycleLockRace.WaitForWaitersAsync(scratch, 1, first);
        var second = RequestAsync(scratch, modules, principalB, secondTransition, moduleId);
        await ModuleLifecycleLockRace.WaitForWaitersAsync(scratch, 2, first, second);
        await holder.ReleaseAsync();
        var one = await first;
        var two = await second;

        one.Error.Should().BeNull();
        two.Error.Should().BeNull();
        one.Value!.Succeeded.Should().BeTrue();
        two.Value!.Succeeded.Should().BeTrue();
        var rows = await AuditRowsAsync(scratch, moduleId);
        var installation = (await InstallationRowsAsync(scratch, moduleId)).Should().ContainSingle().Subject;
        if (enableFirst)
        {
            installation["Enabled"].Should().Be(false, "the disable committed last");
            rows.Select(r => (string)r["Action"]!).Should().Equal(AuditActionNames.ModuleEnabled, AuditActionNames.ModuleDisabled);
            rows[0]["Actor"].Should().Be(adminA);
            rows[1]["Actor"].Should().Be(adminB);
            Detail(rows[1], "previousState").Should().Be("installed_enabled", "the disable read the enable's committed row");
        }
        else
        {
            installation["Enabled"].Should().Be(true, "the enable committed last");
            rows.Select(r => (string)r["Action"]!).Should().Equal(AuditActionNames.ModuleEnabled);
            rows[0]["Actor"].Should().Be(adminB, "the disable found the module already disabled and wrote nothing");
        }

        _output.WriteLine(scratch.ExecutionMarker);
    }

    /// <summary>
    /// Two modules that provide the same workflow id, enabled together in both orders. The lock makes the second read the first's
    /// committed enable, so it is refused with the conflict and a blocked attempt: the two are never both enabled. This is the
    /// consistency the one lock exists for, since each decision depends on the other module's state.
    /// </summary>
    [Theory]
    [MemberData(nameof(Races))]
    public async Task Two_modules_with_conflicting_contributions_enabled_together_are_never_both_enabled(bool firstIsA, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (adminA, principalA) = await AdminAsync(scratch);
        var (adminB, principalB) = await AdminAsync(scratch);
        var moduleA = new SqlTestModule(NewModuleId());
        var moduleB = new SqlTestModule(NewModuleId(), workflowId: moduleA.WorkflowId);
        var idA = (await moduleA.BuildDescriptorAsync()).ModuleId;
        var idB = (await moduleB.BuildDescriptorAsync()).ModuleId;
        IReadOnlyList<IModule> modules = [moduleA, moduleB];
        await SeedInstallationAsync(scratch, idA, enabled: false);
        await SeedInstallationAsync(scratch, idB, enabled: false);

        await using var holder = await ModuleLifecycleLockRace.HoldAsync(scratch);
        var winnerId = firstIsA ? idA : idB;
        var loserId = firstIsA ? idB : idA;
        var winnerPrincipal = firstIsA ? principalA : principalB;
        var loserPrincipal = firstIsA ? principalB : principalA;
        var winnerAdmin = firstIsA ? adminA : adminB;
        var loserAdmin = firstIsA ? adminB : adminA;
        var first = RequestAsync(scratch, modules, winnerPrincipal, Transition.Enable, winnerId);
        await ModuleLifecycleLockRace.WaitForWaitersAsync(scratch, 1, first);
        var second = RequestAsync(scratch, modules, loserPrincipal, Transition.Enable, loserId);
        await ModuleLifecycleLockRace.WaitForWaitersAsync(scratch, 2, first, second);
        await holder.ReleaseAsync();
        var one = await first;
        var two = await second;

        one.Error.Should().BeNull();
        two.Error.Should().BeNull();
        one.Value!.Succeeded.Should().BeTrue();
        two.Value!.Succeeded.Should().BeFalse("the second decision read the first's committed enable");
        two.Value.Blockers.Select(b => b.Code).Should().Equal("workflow_id_conflict");

        (await InstallationRowsAsync(scratch, winnerId)).Single()["Enabled"].Should().Be(true);
        (await InstallationRowsAsync(scratch, loserId)).Single()["Enabled"].Should().Be(false, "never both enabled");
        var winnerRows = await AuditRowsAsync(scratch, winnerId);
        winnerRows.Should().ContainSingle().Which["Action"].Should().Be(AuditActionNames.ModuleEnabled);
        winnerRows[0]["Actor"].Should().Be(winnerAdmin);
        var loserRows = await AuditRowsAsync(scratch, loserId);
        var attempt = loserRows.Should().ContainSingle().Subject;
        attempt["Action"].Should().Be(AuditActionNames.ModuleEnableBlocked);
        attempt["Actor"].Should().Be(loserAdmin);
        DetailList(attempt, "blockers").Should().Equal("workflow_id_conflict");
        Detail(attempt, "state").Should().Be("installed_disabled");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    /// <summary>
    /// A writer that bypassed the lifecycle lock inserts the installation and holds it uncommitted. On a snapshot catalog the
    /// transition does not see an uncommitted row, so its insert blocks on <c>PK_ModuleInstallationStates</c> and, once the row
    /// commits, fails on exactly that key; the decision is made again against the row that won (an install finds the module
    /// installed and does nothing; an enable updates the winner's row and records one enable). On a locking catalog the first read
    /// itself waits for the row and the normal path decides against it, so no insert is attempted. The observable outcome is the
    /// same on both modes.
    /// </summary>
    [Theory]
    [MemberData(nameof(BackstopCases))]
    public async Task An_installation_committed_by_a_writer_that_bypassed_the_lock_is_decided_against_not_failed(bool enable, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (admin, principal) = await AdminAsync(scratch);
        var moduleId = NewModuleId();
        IReadOnlyList<IModule> modules = [new SqlTestModule(moduleId)];

        await using var third = await LockedRowHolder.BeginAsync(scratch.ConnectionString);
        await third.ExecuteAsync(
            $"""
            INSERT INTO ModuleInstallationStates (EnvironmentId, ModuleId, Version, Enabled, InstalledAt, UpdatedAt)
            VALUES (N'{Environment}', N'{moduleId}', N'{Version}', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);

        var failures = new FailedSaveNumbers();
        var request = RequestAsync(scratch, modules, principal, enable ? Transition.Enable : Transition.Install, moduleId, failures);
        await third.WaitUntilBlockedAsync(scratch, "ModuleInstallationStates", request);
        await third.CommitAsync();
        var outcome = await request;

        outcome.Error.Should().BeNull();
        outcome.Value!.Succeeded.Should().BeTrue();
        if (rcsi)
            failures.Numbers.Should().Equal([2627], "the read did not see the uncommitted row, so the insert lost on the primary key and decided again");
        else
            failures.Numbers.Should().BeEmpty("the read waited for the row and found it, so no insert was attempted");

        var installation = (await InstallationRowsAsync(scratch, moduleId)).Should().ContainSingle().Subject;
        var rows = await AuditRowsAsync(scratch, moduleId);
        if (enable)
        {
            outcome.Value.State.Should().Be(ModuleLifecycleState.InstalledEnabled);
            installation["Enabled"].Should().Be(true);
            var row = rows.Should().ContainSingle("the winner's row was updated, so there is no installation of ours to record").Subject;
            row["Action"].Should().Be(AuditActionNames.ModuleEnabled);
            row["Actor"].Should().Be(admin);
            Detail(row, "previousState").Should().Be("installed_disabled");
        }
        else
        {
            outcome.Value.State.Should().Be(ModuleLifecycleState.InstalledDisabled, "the module is installed: the already-installed answer");
            installation["Enabled"].Should().Be(false);
            rows.Should().BeEmpty("nothing of this request committed");
        }

        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- M6 ----

    private async Task<(AuditSqlScratch Scratch, IPrincipalContext Principal, AuditedOperationM6Runner Runner)> RunnerAsync(Transition transition)
    {
        var scratch = await NewScratchAsync(rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        var moduleId = NewModuleId();
        IReadOnlyList<IModule> modules = [new SqlTestModule(moduleId)];
        if (transition is Transition.Disable or Transition.Uninstall)
            await SeedInstallationAsync(scratch, moduleId, enabled: true);
        var runner = new AuditedOperationM6Runner(
            scratch,
            () => SnapshotAsync(scratch),
            ActionOf(transition),
            async (db, unit) => (await Run(Registry(scratch, db, modules, principal, unit), transition, moduleId)).Succeeded.Should().BeTrue());
        return (scratch, principal, runner);
    }

    [Theory]
    [MemberData(nameof(M6Transitions))]
    public async Task An_audit_insert_failure_leaves_the_installation_state_unchanged(Transition transition)
    {
        var (scratch, principal, runner) = await RunnerAsync(transition);
        await using (scratch)
            await runner.AuditFailureLeavesNothingAsync(principal);
    }

    [Theory]
    [MemberData(nameof(M6Transitions))]
    public async Task A_lost_acknowledgement_is_verified_and_the_committed_transition_is_reconstructed(Transition transition)
    {
        var (scratch, principal, runner) = await RunnerAsync(transition);
        await using (scratch)
            await runner.LostAcknowledgementIsVerifiedAndReconciledAsync(principal);
    }

    [Theory]
    [MemberData(nameof(M6Transitions))]
    public async Task A_confirmed_rollback_reruns_the_transition_once(Transition transition)
    {
        var (scratch, principal, runner) = await RunnerAsync(transition);
        await using (scratch)
            await runner.ConfirmedRollbackRerunsOnceAsync(principal);
    }

    [Theory]
    [MemberData(nameof(OutcomeCases))]
    public async Task An_unknown_outcome_is_wholly_committed_or_wholly_absent(Transition transition, bool committed)
    {
        var (scratch, principal, runner) = await RunnerAsync(transition);
        await using (scratch)
        {
            var rowsBefore = await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM ModuleInstallationStates;");
            await runner.UnknownOutcomeIsWhollyCommittedOrWhollyAbsentAsync(principal, committed);
            var rowsAfter = await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM ModuleInstallationStates;");
            var expected = committed
                ? transition switch
                {
                    Transition.Install or Transition.EnableImplicit or Transition.Enable => rowsBefore + 1,
                    Transition.Uninstall => rowsBefore - 1,
                    _ => rowsBefore,
                }
                : rowsBefore;
            rowsAfter.Should().Be(expected);
        }
    }

    [Theory]
    [MemberData(nameof(M6Transitions))]
    public async Task An_operation_lock_that_cannot_be_taken_is_unknown_and_changes_nothing(Transition transition)
    {
        var (scratch, principal, runner) = await RunnerAsync(transition);
        await using (scratch)
            await runner.OperationLockNotGrantedIsUnknownAndChangesNothingAsync(principal);
    }
}
