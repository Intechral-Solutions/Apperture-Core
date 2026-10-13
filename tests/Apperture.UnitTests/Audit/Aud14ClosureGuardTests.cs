using System.Reflection;
using System.Text.RegularExpressions;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// AUD-1.4 closure guards (record section 19, decisions S8-1 and S8-2). They read the repository's production source once
/// (<see cref="RepositorySource"/>) and compare what they find with the audit catalog and with reviewed allow-lists.
///
/// <para>The guards are identifier based and allow-list based. They never use line numbers, formatting or whole-file text. An
/// allow-list names files and says why each is allowed; a new file, a new action or a new table fails the test until a reviewer
/// extends the list with a reason. A stale entry (a file that no longer does what its entry allows) also fails, so a list cannot
/// rot into a rubber stamp. They prove that the known surface is enumerated and reviewed (gate section 21, M1 and M3 for AUD-1.4);
/// they do not prove correct transaction placement, and the runtime enforcement of the bridge (M2 to M5) belongs to AUD-1.6.</para>
///
/// <para>Coverage matrix. The converted writers of each family are pinned by the tests below and by their own slice's suites,
/// which this file does not repeat (G6). The matrix is the way to find them:</para>
/// <list type="table">
/// <item><term>Identity, governance, Entra registration, account services</term><description>AUD-1.2: <c>Aud12ConvertedWritersTests</c>, <c>AuditActorCallSiteTests</c>; SQL: <c>IdentityGovernanceAuditSqlServerTests</c>, <c>EntraGovernanceAuditSqlServerTests</c>, <c>AccountServicesAuditSqlServerTests</c></description></item>
/// <item><term>Run and operator actions</term><description>AUD-1.3: <c>Aud13ConvertedWritersTests</c>; SQL: <c>OperatorActionAuditSqlServerTests</c></description></item>
/// <item><term>Secret store (slice 1)</term><description><c>SecretStoreSharesTheUnitContextTests</c>; SQL: <c>SecretStoreTransactionSqlServerTests</c></description></item>
/// <item><term>Platform and integration settings (slice 2)</term><description><c>SettingsServicesShareTheUnitContextTests</c>; SQL: <c>SettingsAuditSqlServerTests</c></description></item>
/// <item><term>Schedule and integration delete (slice 3)</term><description><c>ScheduleDeleteAuditEventTests</c>, <c>IntegrationDeleteAuditEventTests</c>, <c>ScheduleServiceSharesTheUnitContextTests</c>, <c>IntegrationServiceSharesTheUnitContextTests</c>; SQL: <c>ScheduleDeleteAuditSqlServerTests</c>, <c>IntegrationRowLockSqlServerTests</c>, <c>IntegrationSettingSecretDispositionSqlServerTests</c></description></item>
/// <item><term>Integration, binding and schedule lifecycle (slice 4)</term><description><c>Slice4LifecycleAuditEventTests</c>, <c>BindingServiceSharesTheUnitContextTests</c>; SQL: <c>IntegrationLifecycleAuditSqlServerTests</c>, <c>BindingLifecycleAuditSqlServerTests</c>, <c>ScheduleLifecycleAuditSqlServerTests</c>, <c>SchedulePatchSqlServerTests</c></description></item>
/// <item><term>Connections, resource contexts, ownership moves (slice 5)</term><description><c>Slice5AuditEventTests</c>, <c>ConnectionServiceSharesTheUnitContextTests</c>; SQL: <c>ConnectionLifecycleSqlServerTests</c>, <c>ConnectionVerifyBrowseSqlServerTests</c>, <c>ResourceContextSqlServerTests</c>, <c>OwnershipMoveSqlServerTests</c>, <c>OwnershipRealUnitRaceSqlServerTests</c></description></item>
/// <item><term>Notification definitions, subscriptions, managed contacts, diagnostic sends (slice 6)</term><description><c>Slice6AuditEventTests</c>, <c>Aud14Slice6ConvertedWritersTests</c>, <c>NotificationServicesShareTheUnitContextTests</c>; SQL: <c>NotificationDefinitionSqlServerTests</c>, <c>NotificationSubscriptionSqlServerTests</c>, <c>ManagedContactSqlServerTests</c>, <c>NotificationDiagnosticsSqlServerTests</c></description></item>
/// <item><term>Attention routing, module lifecycle, runtime shutdown request (slice 7)</term><description><c>Slice7AuditEventTests</c>, <c>Aud14Slice7ConvertedWritersTests</c>, <c>AttentionRoutingServiceSharesTheUnitContextTests</c>, <c>ModuleRegistryServiceSharesTheUnitContextTests</c>, <c>RuntimeControlServiceSharesTheUnitContextTests</c>; SQL: <c>AttentionRoutingSqlServerTests</c>, <c>ModuleLifecycleSqlServerTests</c>, <c>RuntimeShutdownRequestSqlServerTests</c></description></item>
/// <item><term>The catalog itself</term><description><c>AuditActionCatalogTests</c> (M1 completeness and the pinned lists), <c>AuditDetailSchemaTests</c>, <c>AuditRowClassifierTests</c></description></item>
/// <item><term>This file</term><description>G1 catalog consistency and the retired set; G2 typed producers; G3 retired actions have no writer; G4 the legacy bridge; G5 persistence writers of the in-scope tables</description></item>
/// </list>
/// </summary>
public sealed class Aud14ClosureGuardTests
{
    private static readonly AuditActionCatalog Catalog = AuditActionCatalog.Default;

    // =====================================================================================================================
    // G1: catalog consistency and the retired set (decision S8-2)
    // =====================================================================================================================

    /// <summary>
    /// The exact retired set. Adding an entry fails <see cref="G1_the_retired_set_is_exactly_the_reviewed_one"/> until a reviewer
    /// extends this list, so retiring an action is always a reviewed change.
    /// </summary>
    private static readonly string[] RetiredActions =
    [
        "governance.break_glass.admin_operation",
        "execution.operation_item.bulk_retry.requested",
        "execution.operation_item.bulk_retry.completed",
        "recovery.blocked_work.retry_dispatched",
    ];

    private static bool IsRequired(AuditActionDefinition d) =>
        d.WriteClass is AuditWriteClass.RequiredMutation or AuditWriteClass.RequiredSecurityEvent;

    [Fact]
    public void G1_the_retired_set_is_exactly_the_reviewed_one()
    {
        var retired = Catalog.Definitions.Where(d => d.Retired).Select(d => d.Action).ToList();

        retired.Should().BeEquivalentTo(
            RetiredActions,
            "retiring an action is a reviewed change: add it to the retired list of this test, with the commit that stopped writing it in its RetiredReason");
        RetiredActions.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void G1_every_active_required_action_has_a_detail_schema()
    {
        var missing = Catalog.Definitions
            .Where(d => !d.Retired && IsRequired(d) && d.Details is null)
            .Select(d => $"{d.Action} ({d.WriteClass})")
            .ToList();

        missing.Should().BeEmpty(
            "every active W1 or W2 action is written through a typed producer and so declares a detail schema; an action nothing writes any more is marked Retired with a reason");
    }

    [Fact]
    public void G1_every_required_action_without_a_schema_is_retired()
    {
        var unmarked = Catalog.Definitions
            .Where(d => IsRequired(d) && d.Details is null && !d.Retired)
            .Select(d => d.Action)
            .ToList();

        unmarked.Should().BeEmpty("a W1 or W2 action without a schema is either converted (give it a schema and a producer) or retired (S8-2)");
    }

    [Fact]
    public void G1_every_retired_entry_has_a_reason_no_schema_no_routine_outcomes_and_says_it_has_no_writer()
    {
        foreach (var definition in Catalog.Definitions.Where(d => d.Retired))
        {
            definition.RetiredReason.Should().NotBeNullOrWhiteSpace($"{definition.Action} states why it is retired");
            definition.Details.Should().BeNull($"{definition.Action} is retired: nothing produces it, so it declares no schema");
            definition.RoutineOutcomes.Should().BeEmpty($"{definition.Action} is retired: its finite history is never marked routine");
            definition.Writer.Should().Contain("no active writer", $"the Writer pointer of {definition.Action} must not name a file that no longer writes it");
            IsRequired(definition).Should().BeTrue($"{definition.Action} was a W1 or W2 action; only those are retired this way");
        }
    }

    [Fact]
    public void G1_active_entries_carry_no_retirement_reason()
    {
        Catalog.Definitions
            .Where(d => !d.Retired && d.RetiredReason is not null)
            .Select(d => d.Action)
            .Should().BeEmpty();
    }

    [Fact]
    public void G1_an_event_for_a_retired_action_cannot_be_built()
    {
        // AuditEvent refuses an action without a schema, and a retired action has none: nothing can stage a row for it.
        foreach (var action in RetiredActions)
        {
            Catalog.Get(action).Details.Should().BeNull();
            var build = () => AuditEvent.Create(Catalog.Get(action), target: null, AuditOutcomeCodes.Succeeded, new Dictionary<string, object?>());
            build.Should().Throw<Exception>($"{action} has no structured producer and must not be writable through the recorder");
        }
    }

    [Theory]
    [InlineData("governance.break_glass.admin_operation")]
    [InlineData("execution.operation_item.bulk_retry.requested")]
    [InlineData("execution.operation_item.bulk_retry.completed")]
    [InlineData("recovery.blocked_work.retry_dispatched")]
    public void G1_rows_already_written_for_a_retired_action_still_classify_from_the_catalog(string action)
    {
        var definition = Catalog.Get(action);
        definition.Retired.Should().BeTrue();
        var actors = new HistoricalActorClassifier([], [], []);

        // A pre-recorder row of this action, with each raw outcome its writer used (the mapped ones, or the generic one).
        var rawOutcomes = definition.OutcomeMap.Keys.DefaultIfEmpty("succeeded").ToList();
        foreach (var raw in rawOutcomes)
        {
            var set = AuditRowClassifier.Classify(new AuditLegacyRow("system", action, raw, null, Producer: null), actors, Catalog);

            set.Category.Should().Be(definition.Category).And.NotBe(AuditCategories.Other, $"{action} stays classified");
            set.Severity.Should().Be(definition.Severity);
            set.Basis.Should().Be(definition.Basis).And.NotBe(AuditBases.Unspecified);
            set.Visibility.Should().Be(AuditVisibilities.Default, "a retired action's history is never routine");
            set.OutcomeCode.Should().Be(
                definition.EraInvariant || definition.IntroducedAfterEpic37Migration
                    ? AuditActionCatalog.MapRecordedOutcome(definition, raw)
                    : AuditOutcomeCodes.Unknown);
        }
    }

    // =====================================================================================================================
    // G2: typed producers
    // =====================================================================================================================

    /// <summary>
    /// Actions that declare a schema but whose constant or literal no <c>AuditEvent*.cs</c> producer file names, each with the
    /// reason. Empty: every schema has a producer. A reviewer adds an entry only with a documented reason.
    /// </summary>
    private static readonly Dictionary<string, string> SchemaWithoutProducer = new(StringComparer.Ordinal);

    [Fact]
    public void G2_every_action_with_a_schema_is_named_by_a_typed_producer()
    {
        var producers = RepositorySource.Production
            .Where(f => f.RelativePath.StartsWith("Apperture.Core/Apperture.Core.Application/Audit/AuditEvent", StringComparison.Ordinal))
            .ToList();
        producers.Should().Contain(f => f.RelativePath.EndsWith("/AuditEvent.cs", StringComparison.Ordinal), "the AUD-1.1 producers live in AuditEvent.cs");
        producers.Count.Should().BeGreaterThan(10, "the producer files were found");

        var referenced = ActionsNamedBy(producers);

        var withoutProducer = Catalog.Definitions
            .Where(d => !d.Retired && d.Details is not null && !referenced.Contains(d.Action) && !SchemaWithoutProducer.ContainsKey(d.Action))
            .Select(d => $"{d.Action} ({d.WriteClass})")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        withoutProducer.Should().BeEmpty(
            "an action with a detail schema must be built by a typed producer in AuditEvent*.cs (naming AuditActionNames.X or the literal). Add the producer, or list the action in SchemaWithoutProducer with the reason");

        SchemaWithoutProducer.Keys.Where(a => referenced.Contains(a)).Should().BeEmpty("a listed exception that now has a producer is removed from the list");
    }

    [Fact]
    public void G2_every_active_required_action_is_named_by_a_typed_producer()
    {
        var producers = RepositorySource.Production
            .Where(f => f.RelativePath.StartsWith("Apperture.Core/Apperture.Core.Application/Audit/AuditEvent", StringComparison.Ordinal))
            .ToList();
        var referenced = ActionsNamedBy(producers);

        Catalog.Definitions
            .Where(d => !d.Retired && IsRequired(d))
            .Where(d => !referenced.Contains(d.Action))
            .Select(d => d.Action)
            .Should().BeEmpty("every active W1 or W2 action has a typed producer");
    }

    private static HashSet<string> ActionsNamedBy(IEnumerable<SourceFile> files)
    {
        var actions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            foreach (var value in ActionValuesIn(file.Code, RepositorySource.LocalConstants(file)))
                actions.Add(value);
        }

        return actions;
    }

    private static readonly Regex ActionLiteral = new(@"""(?<v>[a-z][a-z0-9_]*(?:\.[a-z0-9_]+)+)""", RegexOptions.CultureInvariant);
    private static readonly Regex QualifiedName = new(@"\b(?<t>[A-Za-z_]\w*)\.(?<n>[A-Za-z_]\w*)\b", RegexOptions.CultureInvariant);
    private static readonly Regex BareName = new(@"\b(?<n>[A-Za-z_]\w*)\b", RegexOptions.CultureInvariant);

    /// <summary>
    /// The action-shaped values a piece of code names: dotted lowercase string literals, <c>Type.Name</c> references to a string
    /// constant, and bare references to a constant declared in the same file. Identifier based: a name that is not a constant, or
    /// a constant that is not action-shaped, is ignored.
    /// </summary>
    private static IEnumerable<string> ActionValuesIn(string code, IReadOnlyDictionary<string, string> localConstants)
    {
        foreach (Match m in ActionLiteral.Matches(code))
            yield return m.Groups["v"].Value;

        foreach (Match m in QualifiedName.Matches(code))
        {
            if (RepositorySource.Constants.TryGetValue($"{m.Groups["t"].Value}.{m.Groups["n"].Value}", out var value) && IsActionShaped(value))
                yield return value;
        }

        foreach (Match m in BareName.Matches(code))
        {
            if (localConstants.TryGetValue(m.Groups["n"].Value, out var value) && IsActionShaped(value))
                yield return value;
        }
    }

    private static bool IsActionShaped(string value) => ActionLiteral.IsMatch($"\"{value}\"");

    // =====================================================================================================================
    // G3: a retired action has no writer
    // =====================================================================================================================

    /// <summary>
    /// The production files that may still mention a retired action, each by what it is. Each is a definition site: the catalog
    /// entry that keeps old rows classifying, or a name constant kept for the same history.
    /// </summary>
    private static readonly Dictionary<string, string> RetiredActionDefinitionSites = new(StringComparer.Ordinal)
    {
        ["Apperture.Core/Apperture.Core.Application/Audit/AuditActionCatalogEntries.cs"] =
            "The catalog entries themselves: they keep the outcome map and the legacy target rule so rows already written still classify.",
        ["Apperture.ControlPlane/Apperture.ControlPlane.Security/GovernanceAuditActionNames.cs"] =
            "BreakGlassAdminOperation, a historical name constant (documented as such); nothing references it.",
        ["Apperture.ModuleSdk/Apperture.ModuleSdk.Integration/OperationItems/OperationItemResolutionConstants.cs"] =
            "BulkRetryRequested and BulkRetryCompleted, historical name constants in the module SDK's public surface (documented as such); nothing references them.",
        ["Apperture.Core/Apperture.Core.Application/Recovery/RecoveryAuditActionNames.cs"] =
            "BlockedWorkRetryDispatched, a historical name constant (documented as such); nothing references it.",
    };

    [Fact]
    public void G3_no_production_file_writes_or_names_a_retired_action_outside_its_definition_sites()
    {
        var retiredValues = Catalog.Definitions.Where(d => d.Retired).Select(d => d.Action).ToHashSet(StringComparer.Ordinal);
        retiredValues.Should().BeEquivalentTo(RetiredActions);

        // Every constant that carries a retired value, found in the source, so a new alias is caught too.
        var constantNames = RepositorySource.Constants
            .Where(c => retiredValues.Contains(c.Value))
            .Select(c => c.Key[(c.Key.LastIndexOf('.') + 1)..])
            .Distinct(StringComparer.Ordinal)
            .ToList();
        constantNames.Should().NotBeEmpty("the historical name constants were found");

        var offenders = new List<string>();
        var mentioningFiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in RepositorySource.Production.Concat(RepositorySource.ProductionUi))
        {
            var mentioned = new List<string>();
            foreach (var value in retiredValues)
            {
                if (file.Text.Contains(value, StringComparison.Ordinal) && file.Code.Contains($"\"{value}\"", StringComparison.Ordinal))
                    mentioned.Add($"the string \"{value}\"");
            }

            foreach (var name in constantNames)
            {
                if (file.Mentions(name))
                    mentioned.Add($"the constant {name}");
            }

            if (mentioned.Count == 0)
                continue;

            mentioningFiles.Add(file.RelativePath);
            if (!RetiredActionDefinitionSites.ContainsKey(file.RelativePath))
                offenders.Add($"{file.RelativePath}: {string.Join(", ", mentioned.Distinct())}");
        }

        offenders.Should().BeEmpty(
            "a retired action has no writer. Writing one again means un-retiring it (give it a schema and a producer, clear Retired); only the definition sites may name it");

        RetiredActionDefinitionSites.Keys.Where(f => !mentioningFiles.Contains(f)).Should().BeEmpty(
            "an entry of RetiredActionDefinitionSites that no longer mentions a retired action is removed from the list");
    }

    // =====================================================================================================================
    // G4: the legacy append bridge
    // =====================================================================================================================

    private enum BridgeUse
    {
        /// <summary>The interface declaration.</summary>
        Definition,

        /// <summary>The SQL implementation of the bridge.</summary>
        Implementation,

        /// <summary>Reads the audit log; never appends.</summary>
        Query,

        /// <summary>Dependency injection registration; never appends.</summary>
        Registration,

        /// <summary>Resolves the service and hands it on; never appends itself.</summary>
        Passthrough,

        /// <summary>Appends rows of W3 (attempt) or W4 (telemetry) actions only.</summary>
        Append,
    }

    private sealed record BridgeFile(
        string File,
        BridgeUse Use,
        string Reason,
        string[]? Actions = null,
        string[]? Helpers = null,
        bool Dynamic = false);

    /// <summary>
    /// Every production file allowed to reference <c>IAuditService</c>, and what it does with it. A file that appends lists the exact
    /// actions it appends (each must be W3 or W4 in the catalog); one whose action is built at run time says so (<c>Dynamic</c>) and
    /// lists what it can append. Everything else fails: a W1 or W2 action is recorded through the audited unit of work.
    /// </summary>
    private static readonly BridgeFile[] BridgeFiles =
    [
        new("Apperture.Core/Apperture.Core.Application/Services/IAuditService.cs", BridgeUse.Definition,
            "The bridge interface: append, query, count and read by id."),
        new("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlAuditService.cs", BridgeUse.Implementation,
            "The SQL implementation. Appends complete, classified rows with Producer = append; builds records when it reads."),
        new("Apperture.ControlPlane/Apperture.ControlPlane.Security/Audit/AuditTimelineQueryService.cs", BridgeUse.Query,
            "The timeline reads through QueryAsync and GetByIdAsync (AUD-1.5 owns the query contract)."),
        new("Apperture.ControlPlane/Apperture.ControlPlane.Security/Dashboard/AuditDashboardCardProvider.cs", BridgeUse.Query,
            "The dashboard card counts rows."),
        new("Apperture.ControlPlane/Apperture.ControlPlane.Security/ServiceCollectionExtensions.Governance.cs", BridgeUse.Registration,
            "Registers SqlAuditService for the Management Host."),
        new("Apperture.RuntimeHost/Apperture.RuntimeHost.Windows/Apperture.RuntimeHost.Windows.WorkerHost/Program.cs", BridgeUse.Registration,
            "Registers SqlAuditService for the Worker Host."),
        new("Apperture.RuntimeHost/Apperture.RuntimeHost.Windows/Apperture.RuntimeHost.Windows.WorkerHost/StartRequestWorker.cs", BridgeUse.Passthrough,
            "Resolves the service and passes it to the two start-request processors, which append the definition-unavailable telemetry rows below."),
        new("Apperture.ControlPlane/Apperture.Management.Api/Controllers/AuditController.cs", BridgeUse.Append,
            "The audit read endpoints query the log. A denied read is recorded best effort as a W3 attempt (AUD-1.5 owns the denial actor).",
            Actions: ["audit.read.denied"]),
        new("Apperture.ControlPlane/Apperture.Management.Api/Controllers/AuthController.cs", BridgeUse.Append,
            "Sign-in failures and rejections are W3 attempts (IAM-11.4 owns them). Sign-in successes and sign-out go through the recorder.",
            Actions:
            [
                "auth.local.login.failed",
                "auth.unlinked_principal",
                "auth.break_glass.login.failed",
                "auth.break_glass.rejected_disabled",
                "auth.emergency.login.failed",
            ],
            Helpers: ["AppendAuthAuditAsync"]),
        new("Apperture.RuntimePlane/Apperture.RuntimePlane.Dispatch.SqlServer/ScheduleDispatchRunner.cs", BridgeUse.Append,
            "The schedule dispatch outcomes are W4 telemetry (nothing in the scheduling path executes steps; it enqueues a durable start).",
            Actions:
            [
                "scheduler.schedule.dispatched",
                "scheduler.schedule.blocked_by_lifecycle",
                "scheduler.schedule.blocked_by_readiness",
                "scheduler.schedule.failed",
                "scheduler.schedule.legacy_without_integration",
                "scheduler.claim.recovered",
            ]),
        new("Apperture.RuntimePlane/Apperture.RuntimePlane.Engine/WorkflowStartRequestProcessor.cs", BridgeUse.Append,
            "A workflow run whose definition is no longer executable is recorded as W4 telemetry by the worker, best effort.",
            Actions: ["execution.workflow_run.failed_definition_unavailable"]),
        new("Apperture.RuntimePlane/Apperture.RuntimePlane.Engine/OperationStartRequestProcessor.cs", BridgeUse.Append,
            "An operation run whose definition is no longer executable is recorded as W4 telemetry by the worker, best effort.",
            Actions: ["execution.operation_run.failed_definition_unavailable"]),
        new("Apperture.Persistence/Apperture.Persistence.SqlServer/ModuleCompositionBootstrapHostedService.cs", BridgeUse.Append,
            "Startup composition: a degraded module composition is W4 telemetry (the instance keeps starting).",
            Actions: ["runtime.composition.invalid_at_startup"]),
        new("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlModuleRegistryService.cs", BridgeUse.Append,
            "Module discovery is W4 telemetry (AUD-1.5 owns it as routine). The four lifecycle transitions record through the audited unit; the action here is built from the discovery result.",
            Actions: ["module.lifecycle.discover"],
            Dynamic: true),
    ];

    private static readonly Regex NewAuditRecord = new(@"\bnew\s+AuditRecord\s*\(", RegexOptions.CultureInvariant);
    private static readonly Regex AppendCall = new(@"\.\s*AppendAsync\s*\(", RegexOptions.CultureInvariant);

    [Fact]
    public void G4_only_reviewed_files_reference_the_legacy_audit_bridge()
    {
        var allowed = BridgeFiles.Select(b => b.File).ToHashSet(StringComparer.Ordinal);
        var referencing = RepositorySource.Production.Where(f => f.Mentions("IAuditService")).Select(f => f.RelativePath).ToList();

        var unreviewed = referencing.Where(f => !allowed.Contains(f)).ToList();
        unreviewed.Should().BeEmpty(
            "a converted writer records through IAuditedUnitOfWork or IAuditEventWriter and must not take IAuditService. If a new file really needs the bridge (a W3 attempt or W4 telemetry), add it to BridgeFiles with its reason and the exact actions it appends");

        var stale = allowed.Where(f => !referencing.Contains(f, StringComparer.Ordinal)).ToList();
        stale.Should().BeEmpty("a BridgeFiles entry whose file no longer references IAuditService is removed from the list");
    }

    [Fact]
    public void G4_only_reviewed_files_build_a_legacy_audit_record()
    {
        var allowed = BridgeFiles.Where(b => b.Use is BridgeUse.Append or BridgeUse.Implementation).Select(b => b.File).ToHashSet(StringComparer.Ordinal);

        var builders = RepositorySource.Production
            .Where(f => f.Text.Contains("AuditRecord", StringComparison.Ordinal) && NewAuditRecord.IsMatch(f.Code))
            .Select(f => f.RelativePath)
            .ToList();

        builders.Where(f => !allowed.Contains(f)).Should().BeEmpty(
            "building an AuditRecord is the legacy bridge. Record a configuration change through IAuditedUnitOfWork, or add the file to BridgeFiles with its reason");
        allowed.Where(f => !builders.Contains(f, StringComparer.Ordinal) && BridgeFiles.Single(b => b.File == f).Use == BridgeUse.Append)
            .Should().BeEmpty("an appending BridgeFiles entry that no longer builds a record is removed from the list");
    }

    [Fact]
    public void G4_files_that_do_not_append_never_call_append()
    {
        foreach (var entry in BridgeFiles.Where(b => b.Use is BridgeUse.Query or BridgeUse.Registration or BridgeUse.Passthrough or BridgeUse.Definition))
        {
            var file = RepositorySource.Find(entry.File);
            file.Should().NotBeNull($"{entry.File} exists");
            AppendCall.IsMatch(file!.Code).Should().BeFalse($"{entry.File} is allowed to reference the bridge as {entry.Use} only, not to append ({entry.Reason})");
            NewAuditRecord.IsMatch(file.Code).Should().BeFalse($"{entry.File} is allowed to reference the bridge as {entry.Use} only, not to build a record");
        }
    }

    [Fact]
    public void G4_every_action_an_appending_file_writes_is_an_attempt_or_telemetry_action_listed_for_it()
    {
        foreach (var entry in BridgeFiles.Where(b => b.Use == BridgeUse.Append))
        {
            entry.Actions.Should().NotBeNullOrEmpty($"{entry.File} appends, so it lists the exact actions it may append");
            var file = RepositorySource.Find(entry.File);
            file.Should().NotBeNull($"{entry.File} exists");

            var local = RepositorySource.LocalConstants(file!);
            var resolved = new HashSet<string>(StringComparer.Ordinal);
            var statements = 0;
            foreach (var statement in AppendStatements(file!, entry.Helpers ?? []))
            {
                statements++;
                foreach (var value in ActionValuesIn(statement, local))
                    resolved.Add(value);
            }

            statements.Should().BeGreaterThan(0, $"{entry.File} is listed as an appending file but contains no append");

            resolved.Where(v => !entry.Actions!.Contains(v, StringComparer.Ordinal)).Should().BeEmpty(
                $"{entry.File} appends an action that is not listed for it. A W1 or W2 action is recorded through the audited unit of work; a W3 or W4 action is added to the entry with its reason");
            if (!entry.Dynamic)
            {
                entry.Actions!.Where(a => !resolved.Contains(a)).Should().BeEmpty(
                    $"{entry.File} no longer appends an action listed for it; remove it from the entry");
            }
            else
            {
                resolved.Should().BeEmpty($"{entry.File} builds its action at run time, so none can be resolved from the source; if one can, drop Dynamic and list it");
            }

            foreach (var action in entry.Actions!)
            {
                var definition = Catalog.Find(action);
                definition.Should().NotBeNull($"{action} (appended by {entry.File}) is declared in the audit catalog");
                definition!.WriteClass.Should().BeOneOf(
                    new[] { AuditWriteClass.Attempt, AuditWriteClass.Telemetry },
                    $"{action} is appended by {entry.File} on the legacy bridge, which carries only W3 attempts and W4 telemetry (gate section 14)");
                definition.Details.Should().BeNull($"{action} is a bridge action; one with a detail schema is written by a typed producer");
                definition.Retired.Should().BeFalse();
            }
        }
    }

    [Fact]
    public void G4_the_bridge_files_exist_and_name_each_action_once()
    {
        foreach (var entry in BridgeFiles)
            RepositorySource.Find(entry.File).Should().NotBeNull($"{entry.File} exists; remove or fix the entry");

        BridgeFiles.Select(b => b.File).Should().OnlyHaveUniqueItems();
        foreach (var entry in BridgeFiles.Where(b => b.Actions is not null))
            entry.Actions.Should().OnlyHaveUniqueItems(entry.File);
    }

    /// <summary>The statements of a file that append: an <c>AppendAsync</c> call, a new <c>AuditRecord</c>, or a call of a local append helper.</summary>
    private static IEnumerable<string> AppendStatements(SourceFile file, string[] helpers)
    {
        var code = file.Code;
        var starts = new List<int>();
        starts.AddRange(AppendCall.Matches(code).Select(m => m.Index));
        starts.AddRange(NewAuditRecord.Matches(code).Select(m => m.Index));
        foreach (var helper in helpers)
            starts.AddRange(Regex.Matches(code, $@"\b{Regex.Escape(helper)}\s*\(").Select(m => m.Index));

        foreach (var index in starts.Distinct().OrderBy(i => i))
            yield return CSharpSource.StatementAround(code, index);
    }

    // =====================================================================================================================
    // G5: persistence writers of the in-scope tables
    // =====================================================================================================================

    /// <summary>
    /// The entity sets (DbSet names of <c>AppertureDbContext</c>) that the AUD-1.4 families own: platform and integration settings,
    /// the secret store, integrations, schedules, bindings, connections, resource contexts, notification definitions, subscriptions,
    /// opt-outs and preferences, managed contacts, attention routing, module installation, the runtime shutdown request, and the
    /// Entra registration (which owns a secret). Not listed, by decision: the worker's runtime state, notification delivery, platform
    /// events, identity and governance (AUD-1.2), run and operator state (AUD-1.3), operational teams (LST-1.4).
    /// </summary>
    private static readonly string[] InScopeSets =
    [
        "PlatformSettingValues",
        "SecretDescriptors", "SecretVersions", "SecretAudits",
        "Integrations",
        "ScheduleDefinitions", "ScheduleAssignments",
        "Bindings",
        "Connections",
        "ResourceContexts",
        "NotificationDefinitions", "NotificationSubscriptions", "NotificationSubscriptionUserOverrides", "NotificationUserPreferences",
        "ManagedContacts", "ManagedContactIdentityLinks", "ManagedContactGroups",
        "InstanceAttentionRoutings", "IntegrationAttentionRoutings",
        "ModuleInstallationStates",
        "RuntimeControlRequests",
        "EntraInstanceRegistrations",
    ];

    private sealed record PersistenceWriter(string File, string[] Sets, string Path);

    /// <summary>
    /// Every production file that writes an in-scope set, with the sets it writes and the audited path that owns the write or the
    /// reason it is excluded. A file that starts writing a set it is not listed for, and a new writer file, fail the test.
    /// </summary>
    private static readonly PersistenceWriter[] Writers =
    [
        new("Apperture.ControlPlane/Apperture.ControlPlane.Security/AttentionRoutingManagementService.cs",
            ["InstanceAttentionRoutings", "IntegrationAttentionRoutings"],
            "Audited units: attention_routing.default.updated, attention_routing.override.created, .updated and .cleared (slice 7)."),
        new("Apperture.ControlPlane/Apperture.ControlPlane.Security/IntegrationManagementService.cs",
            ["Integrations", "PlatformSettingValues"],
            "The integration delete unit (integration.deleted): removes the integration and its integration-scoped overrides by row id in one unit (slice 3). Create, update and state changes write through SqlIntegrationRepository inside their own units."),
        new("Apperture.ControlPlane/Apperture.ControlPlane.Security/RuntimeControlService.cs",
            ["RuntimeControlRequests"],
            "The shutdown request unit (governance.runtime.shutdown.request.created, slice 7)."),
        new("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlBindingRepository.cs",
            ["Bindings"],
            "BindingService units: binding.created (module-slot and capability) and binding.deleted (slice 4)."),
        new("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlConnectionRepository.cs",
            ["Connections"],
            "ConnectionManagementService units (connection.created, .updated, .retired, .verified) and ResourceOwnershipMoveService (the narrow context change of an ownership move) (slice 5)."),
        new("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlEntraInstanceRegistrationRepository.cs",
            ["EntraInstanceRegistrations"],
            "ConfigEntraTenantsController units (entra.tenant_registration.*, AUD-1.2 with the secret events of slice 1). EXCLUDED, unaudited: UpdateOpenIdValidationOutcomeAsync and UpdateDirectoryDiscoveryOutcomeAsync record probe results on the registration row outside any unit (AUD-1.2 family; recorded as a finding for AUD-1.6's writer inventory)."),
        new("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlIntegrationRepository.cs",
            ["Integrations"],
            "IntegrationManagementService units (integration.created, .updated, .activated, .deactivated, .retired) and ResourceOwnershipMoveService (slices 3 to 5)."),
        new("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlManagedContactGroupRepository.cs",
            ["ManagedContactGroups"],
            "ManagedContactManagementService.ImportEntraGroupsAsync units (managed_contact.group_imported, slice 6)."),
        new("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlManagedContactRepository.cs",
            ["ManagedContacts", "ManagedContactIdentityLinks"],
            "ManagedContactManagementService units and the subscription unit that creates a contact (managed_contact.* , slice 6). Documented exclusion: TouchEntraLinkSyncAsync refreshes a link's sync times after a no-op import, touching no contact field."),
        new("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlModuleRegistryService.cs",
            ["ModuleInstallationStates"],
            "The four module lifecycle units: module.lifecycle.install, .enable, .disable, .uninstall (slice 7). Discovery writes no installation row."),
        new("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlNotificationDefinitionStore.cs",
            ["NotificationDefinitions", "NotificationSubscriptions", "NotificationSubscriptionUserOverrides", "NotificationUserPreferences"],
            "NotificationDefinitionService units: notification.definition.created, .updated, .enabled, .disabled, .deleted, including the delete's cascade in code (slice 6)."),
        new("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlNotificationSubscriptionStore.cs",
            ["NotificationSubscriptions", "NotificationSubscriptionUserOverrides"],
            "NotificationSubscriptionService units (notification.subscription.*, notification.user_opt_out.*, slice 6) and the account's own opt-in through AccountProfileService (account.notification.*, AUD-1.2)."),
        new("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlNotificationUserPreferenceStore.cs",
            ["NotificationUserPreferences"],
            "AccountProfileService units: account.notification.* (AUD-1.2). The definition delete's cascade is removed by SqlNotificationDefinitionStore."),
        new("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlPlatformSettingValueRepository.cs",
            ["PlatformSettingValues"],
            "PlatformSettingsService and IntegrationSettingsService units: platform.settings.set and .reset, integration.settings.set and .reset (slice 2)."),
        new("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlResourceContextRepository.cs",
            ["ResourceContexts"],
            "ResourceContextManagementService units: resource_context.created, .updated, .archived, .unarchived (slice 5)."),
        new("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlScheduleRepository.cs",
            ["ScheduleDefinitions", "ScheduleAssignments"],
            "SqlScheduleManagementService units (schedule.created, .assigned, .updated, .enabled, .disabled, .deleted) and the ownership move (slices 3 and 4). The same file writes the worker's runtime state (ScheduleRuns, ScheduleStates), which is excluded by decision."),
        new("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlSecretStore.cs",
            ["SecretDescriptors", "SecretVersions", "SecretAudits"],
            "The secret store joins its owner's audited unit (slice 1): connection, platform and integration setting and Entra secret events are Secrets-category rows on the owning entity. SecretAudits is the store's own evidence and stays separate (gate section 7)."),
        new("Apperture.RuntimeHost/Apperture.RuntimeHost.Windows/Apperture.RuntimeHost.Windows.WorkerHost/RuntimeShutdownRequestApplier.cs",
            ["RuntimeControlRequests"],
            "EXCLUDED by decision: the worker applies a queued shutdown request and marks it applied. Runtime state, not a configuration change; the applied transition has no audit row (record section 7, unassigned; AUD-1.6 writer inventory)."),
        new("Apperture.Persistence/Apperture.Persistence.SqlServer/HistoricalActorNormalization.cs",
            ["NotificationDefinitions", "NotificationSubscriptions", "NotificationSubscriptionUserOverrides", "PlatformSettingValues", "RuntimeControlRequests"],
            "EXCLUDED by decision: IAM-9 start-time maintenance that rewrites stored actor ids to user ids (raw SQL over a fixed column list). Not a configuration change."),
    ];

    private static readonly Regex WriteVerb = new(
        @"\.\s*(Add|AddAsync|AddRange|AddRangeAsync|Remove|RemoveRange|Update|UpdateRange|Attach|AttachRange|ExecuteUpdate|ExecuteUpdateAsync|ExecuteDelete|ExecuteDeleteAsync)\s*\(",
        RegexOptions.CultureInvariant);

    private static readonly Regex Materializer = new(
        @"\.\s*(FirstOrDefaultAsync|SingleOrDefaultAsync|FirstAsync|SingleAsync|FindAsync|ToListAsync|ToArrayAsync|AsAsyncEnumerable|FromSql\w*)\s*\(",
        RegexOptions.CultureInvariant);

    private static readonly Regex Aggregate = new(
        @"\.\s*(CountAsync|AnyAsync|LongCountAsync|SumAsync|MaxAsync|MinAsync|AllAsync|Select)\s*\(",
        RegexOptions.CultureInvariant);

    private static readonly Regex PersistSignal = new(@"SaveChanges(Async)?\s*\(|ExecuteUpdate|ExecuteDelete|ExecuteSql", RegexOptions.CultureInvariant);
    private static readonly Regex RawExecution = new(@"CreateCommand\s*\(|ExecuteNonQuery|ExecuteSql", RegexOptions.CultureInvariant);

    private static readonly Regex RawWrite = new(
        @"(INSERT\s+INTO|UPDATE|DELETE\s+FROM|MERGE(\s+INTO)?)\s+\[?(dbo\]?\.\[?)?(?<table>\w+)\]?",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    [Fact]
    public void G5_the_in_scope_sets_are_real_entity_sets_of_the_context()
    {
        var sets = DbSetsOf(typeof(AppertureDbContext));

        InScopeSets.Where(s => !sets.ContainsKey(s)).Should().BeEmpty("every in-scope name is a DbSet of AppertureDbContext (a typo would make the scan vacuous)");
        InScopeSets.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void G5_only_reviewed_files_write_the_in_scope_tables()
    {
        var found = ScanWriters();

        var problems = new List<string>();
        foreach (var (file, sets) in found.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            var entry = Writers.SingleOrDefault(w => w.File == file);
            if (entry is null)
            {
                problems.Add($"NEW WRITER {file} writes {string.Join(", ", sets)}. Route the write through an audited unit (IAuditedUnitOfWork) and add the file to Writers with the audited path, or list it as an exclusion with the reason.");
                continue;
            }

            var added = sets.Except(entry.Sets, StringComparer.Ordinal).ToList();
            if (added.Count > 0)
                problems.Add($"{file} now also writes {string.Join(", ", added)}, which its Writers entry does not allow. Audit the new write and extend the entry, with the audited path.");
            var gone = entry.Sets.Except(sets, StringComparer.Ordinal).ToList();
            if (gone.Count > 0)
                problems.Add($"{file} no longer writes {string.Join(", ", gone)}; remove it from its Writers entry.");
        }

        foreach (var entry in Writers.Where(w => !found.ContainsKey(w.File)))
            problems.Add($"STALE ENTRY {entry.File} no longer writes any in-scope table; remove it from Writers.");

        problems.Should().BeEmpty();
    }

    [Fact]
    public void G5_every_writer_names_its_audited_path_or_its_exclusion()
    {
        Writers.Select(w => w.File).Should().OnlyHaveUniqueItems();
        foreach (var writer in Writers)
        {
            RepositorySource.Find(writer.File).Should().NotBeNull($"{writer.File} exists");
            writer.Sets.Should().NotBeEmpty(writer.File);
            writer.Sets.Should().OnlyHaveUniqueItems(writer.File);
            writer.Sets.Except(InScopeSets).Should().BeEmpty($"{writer.File} lists sets that are in scope");
            writer.Path.Should().NotBeNullOrWhiteSpace($"{writer.File} states the audited path that owns the write, or why it is excluded");
        }
    }

    /// <summary>
    /// File, then the in-scope sets it writes. A write is: a set followed in the same statement by Add, Remove, Update, Attach,
    /// ExecuteUpdate or ExecuteDelete; a tracked read of a set (no AsNoTracking, no aggregate or projection) in a file that persists
    /// changes; raw SQL that writes a set's table; or a raw-command file that names the table as a string. Only files that mention a
    /// DbContext are read.
    /// </summary>
    private static Dictionary<string, SortedSet<string>> ScanWriters()
    {
        var sets = DbSetsOf(typeof(AppertureDbContext));
        var alternation = string.Join("|", InScopeSets);
        var entities = string.Join("|", InScopeSets.Where(sets.ContainsKey).Select(s => Regex.Escape(sets[s])));
        var setAccess = new Regex($@"\.\s*(?<set>{alternation})\b|\bSet<(?<entity>{entities})>\s*\(\s*\)", RegexOptions.CultureInvariant);
        var entityToSet = InScopeSets.Where(sets.ContainsKey).ToDictionary(s => sets[s], s => s, StringComparer.Ordinal);

        var result = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var file in RepositorySource.Production)
        {
            if (!file.Text.Contains("DbContext", StringComparison.Ordinal))
                continue;

            var code = file.Code;
            var persists = PersistSignal.IsMatch(code);
            var written = new SortedSet<string>(StringComparer.Ordinal);

            foreach (Match match in setAccess.Matches(code))
            {
                var set = match.Groups["set"].Success ? match.Groups["set"].Value : entityToSet[match.Groups["entity"].Value];
                var tail = CSharpSource.TailStatement(code, match.Index);
                if (WriteVerb.IsMatch(tail))
                    written.Add(set);
                else if (persists && !tail.Contains("AsNoTracking", StringComparison.Ordinal) && Materializer.IsMatch(tail) && !Aggregate.IsMatch(tail))
                    written.Add(set);
            }

            foreach (Match match in RawWrite.Matches(code))
            {
                var table = match.Groups["table"].Value;
                if (InScopeSets.Contains(table, StringComparer.Ordinal))
                    written.Add(table);
            }

            if (RawExecution.IsMatch(code))
            {
                foreach (var set in InScopeSets.Where(s => code.Contains($"\"{s}\"", StringComparison.Ordinal)))
                    written.Add(set);
            }

            if (written.Count > 0)
                result[file.RelativePath] = written;
        }

        return result;
    }

    private static Dictionary<string, string> DbSetsOf(Type context) =>
        context.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .ToDictionary(p => p.Name, p => p.PropertyType.GetGenericArguments()[0].Name, StringComparer.Ordinal);
}
