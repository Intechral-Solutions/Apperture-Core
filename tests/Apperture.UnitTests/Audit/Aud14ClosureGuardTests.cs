using System.Text.RegularExpressions;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
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
/// <para>Coverage map (not an executable guard). The converted writers of each family are pinned by their own slice's suites, which
/// this file does not repeat. The table below is a navigable map of those existing pins, kept here so a reviewer can find the test
/// that covers a family; nothing in it is checked, and a name in it can drift without any test failing:</para>
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
/// <item><term>Attention incident acknowledgement (SEC-2.1)</term><description><c>AttentionIncidentAcknowledgementServiceTests</c>, <c>AttentionIncidentAcknowledgementAuthorizationTests</c>; SQL: <c>AttentionIncidentAcknowledgementSqlServerTests</c>; host: <c>ManagementHostAttentionIncidentAcknowledgementHttpTests</c></description></item>
/// <item><term>The catalog itself</term><description><c>AuditActionCatalogTests</c> (M1 completeness and the pinned lists), <c>AuditDetailSchemaTests</c>, <c>AuditRowClassifierTests</c></description></item>
/// <item><term>This file</term><description>The executable guards: G1 catalog consistency and the retired set; G2 typed producers; G3 retired actions have no writer; G4 the legacy bridge (every action an appender writes is a literal or constant, or the file is declared dynamic); G5 persistence writers of the in-scope tables (set-level idioms and the file-level rule), each with self-tests that feed the scanners synthetic files</description></item>
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

            var analysis = AnalyzeAppends(file!, entry.Helpers ?? []);
            var resolved = analysis.Resolved;

            analysis.Statements.Should().BeGreaterThan(0, $"{entry.File} is listed as an appending file but contains no append");

            resolved.Where(v => !entry.Actions!.Contains(v, StringComparer.Ordinal)).Should().BeEmpty(
                $"{entry.File} appends an action that is not listed for it. A W1 or W2 action is recorded through the audited unit of work; a W3 or W4 action is added to the entry with its reason");
            if (!entry.Dynamic)
            {
                analysis.Unresolved.Should().BeEmpty(
                    $"{entry.File} appends through an action that is not a literal or a constant (a local variable, a parameter, a computed value). Name the action at the call, or mark the entry Dynamic and list every action it can append");
                entry.Actions!.Where(a => !resolved.Contains(a)).Should().BeEmpty(
                    $"{entry.File} no longer appends an action listed for it; remove it from the entry");
            }
            else
            {
                analysis.Unresolved.Should().NotBeEmpty($"{entry.File} is marked Dynamic but every action it appends resolves from the source; drop Dynamic and list them");
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

    /// <summary>What <see cref="AnalyzeAppends"/> found in one appending file.</summary>
    /// <param name="Statements">Appends, record constructions and helper calls found (zero means the file does not append).</param>
    /// <param name="Resolved">The actions that are literals or constants at a place that builds a record or calls a helper.</param>
    /// <param name="Unresolved">Record constructions and helper calls whose action is not a literal or a constant.</param>
    private sealed record AppendAnalysis(int Statements, HashSet<string> Resolved, List<string> Unresolved);

    private static readonly string[] NotDeclarationWords = ["await", "return", "new", "yield", "in", "is", "and", "or", "not", "throw", "else", "case", "when"];

    /// <summary>
    /// Reads how a file appends to the legacy bridge. Every <c>new AuditRecord(</c> and every call of a declared append helper must
    /// name its action as a literal or a constant; one that passes a local variable, a parameter or a computed value is
    /// <i>unresolved</i>. A record built inside a declared helper takes its action from the helper's parameter, so the helper's call
    /// sites are checked instead of the construction. An <c>AppendAsync</c> call that appends a record built elsewhere in the file
    /// adds no check of its own: that construction is checked where it happens.
    /// </summary>
    private static AppendAnalysis AnalyzeAppends(SourceFile file, string[] helpers)
    {
        var code = file.Code;
        var local = RepositorySource.LocalConstants(file);
        var spans = CSharpSource.StringSpans(code);
        var resolved = new HashSet<string>(StringComparer.Ordinal);
        var unresolved = new List<string>();
        var statements = AppendCall.Matches(code).Count;

        // The bodies of the declared helpers: a construction inside one is parameterised by the helper's caller.
        var helperBodies = new List<(int Start, int End)>();
        var helperCalls = new List<int>();
        foreach (var helper in helpers)
        {
            foreach (Match m in Regex.Matches(code, $@"\b{Regex.Escape(helper)}\s*\("))
            {
                if (!IsDeclaration(code, m.Index))
                {
                    helperCalls.Add(m.Index);
                    continue;
                }

                var open = code.IndexOf('{', m.Index);
                var semicolon = code.IndexOf(';', m.Index);
                var end = open >= 0 && (semicolon < 0 || open < semicolon)
                    ? CSharpSource.MatchingBrace(code, open, spans)
                    : semicolon < 0 ? code.Length : semicolon;
                helperBodies.Add((m.Index, end));
            }
        }

        void Check(int index, string kind)
        {
            statements++;
            var statement = CSharpSource.StatementAround(code, index);
            var values = ActionValuesIn(statement, local).ToList();
            if (values.Count == 0)
                unresolved.Add($"{kind}: {Regex.Replace(statement.Trim(), @"\s+", " ")[..Math.Min(statement.Trim().Length, 100)]}");
            foreach (var value in values)
                resolved.Add(value);
        }

        foreach (Match m in NewAuditRecord.Matches(code))
        {
            if (helperBodies.Any(b => m.Index >= b.Start && m.Index < b.End))
            {
                statements++;
                continue;
            }

            Check(m.Index, "new AuditRecord");
        }

        foreach (var index in helperCalls)
            Check(index, "helper call");

        return new AppendAnalysis(statements, resolved, unresolved);
    }

    /// <summary>True when the identifier at <paramref name="index"/> is preceded by a type (a method declaration) rather than by an operator or <c>await</c>.</summary>
    private static bool IsDeclaration(string code, int index)
    {
        var i = index - 1;
        while (i >= 0 && char.IsWhiteSpace(code[i]))
            i--;
        if (i < 0)
            return false;
        if (code[i] is '>' or ']')
            return !(code[i] == '>' && i > 0 && code[i - 1] == '=');
        if (!(char.IsLetterOrDigit(code[i]) || code[i] == '_'))
            return false;

        var end = i + 1;
        while (i >= 0 && (char.IsLetterOrDigit(code[i]) || code[i] == '_'))
            i--;
        return !NotDeclarationWords.Contains(code[(i + 1)..end], StringComparer.Ordinal);
    }

    // =====================================================================================================================
    // G5: persistence writers of the in-scope tables
    // =====================================================================================================================

    /// <summary>
    /// The entity sets (DbSet names of <c>AppertureDbContext</c>) that the AUD-1.4 families own: platform and integration settings,
    /// the secret store, integrations, schedules, bindings, connections, resource contexts, notification definitions, subscriptions,
    /// opt-outs and preferences, managed contacts, attention routing, attention incidents (SEC-2.1), module installation, the runtime shutdown request, the
    /// Entra registration (which owns a secret) and its probe telemetry (SEC-2.4, <c>EntraRegistrationDiagnostics</c>, listed so that
    /// no other file can write it and so the guard names the table the probe writes moved to). Not listed, by decision: the worker's
    /// runtime state, notification delivery, platform events, identity and governance (AUD-1.2), run and operator state (AUD-1.3),
    /// operational teams (LST-1.4).
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
        "AttentionIncidents",
        "ModuleInstallationStates",
        "RuntimeControlRequests",
        "EntraInstanceRegistrations",
        "EntraRegistrationDiagnostics",
    ];

    private sealed record PersistenceWriter(string File, string[] Sets, string Path);

    /// <summary>
    /// Every production file that writes an in-scope set, with the sets it writes and the audited path that owns the write or the
    /// reason it is excluded. A file that starts writing a set it is not listed for, and a new writer file, fail the test.
    /// </summary>
    private static readonly PersistenceWriter[] Writers =
    [
        new("Apperture.ControlPlane/Apperture.ControlPlane.Security/AttentionIncidentAcknowledgementService.cs",
            ["AttentionIncidents"],
            "Audited unit: attention_incident.acknowledged (SEC-2.1). The one writer of the acknowledgement columns: the integration row lock, the re-run authorization, the incident row lock, the three columns and the audit row in one transaction."),
        new("Apperture.ControlPlane/Apperture.ControlPlane.Security/AttentionRoutingManagementService.cs",
            ["InstanceAttentionRoutings", "IntegrationAttentionRoutings"],
            "Audited units: attention_routing.default.updated, attention_routing.override.created, .updated and .cleared (slice 7)."),
        new("Apperture.ControlPlane/Apperture.ControlPlane.Security/IntegrationManagementService.cs",
            ["Integrations", "PlatformSettingValues"],
            "The integration delete unit (integration.deleted): removes the integration and its integration-scoped overrides by row id in one unit (slice 3). Create, update and state changes write through SqlIntegrationRepository inside their own units."),
        new("Apperture.ControlPlane/Apperture.ControlPlane.Security/RuntimeControlService.cs",
            ["RuntimeControlRequests"],
            "The shutdown request unit (governance.runtime.shutdown.request.created, slice 7)."),
        new("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlAttentionIncidentTracker.cs",
            ["AttentionIncidents"],
            "EXCLUDED by decision: the observation worker opens, updates and resolves incidents as schedule evaluation moves (runtime state; no operator action, no audit row). It writes no acknowledgement column: since SEC-2.1 the tracker has no acknowledgement write, and a resolution writes only the columns it changes, so an acknowledgement made in between is kept."),
        new("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlBindingRepository.cs",
            ["Bindings"],
            "BindingService units: binding.created (module-slot and capability) and binding.deleted (slice 4)."),
        new("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlConnectionRepository.cs",
            ["Connections"],
            "ConnectionManagementService units (connection.created, .updated, .retired, .verified) and ResourceOwnershipMoveService (the narrow context change of an ownership move) (slice 5)."),
        new("Apperture.Persistence/Apperture.Persistence.SqlServer/SqlEntraInstanceRegistrationRepository.cs",
            ["EntraInstanceRegistrations", "EntraRegistrationDiagnostics"],
            "EntraInstanceRegistrations: ConfigEntraTenantsController units (entra.tenant_registration.*, AUD-1.2 with the secret events of slice 1). EntraRegistrationDiagnostics: OPERATIONAL TELEMETRY, unaudited by decision (finding F2 as resolved by SEC-2.4, gate P-7, P-8 and P-10; SR-7): UpdateOpenIdValidationOutcomeAsync and UpdateDirectoryDiscoveryOutcomeAsync record the probe outcome on the registration's 1:1 diagnostics row, outside any unit, with no audit row; AddAsync creates that row with the registration and the registration's delete removes it (cascade). G5_probe_telemetry_writes_only_the_diagnostics_table pins that the probe members never write the registration row."),
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
            ["AttentionIncidents", "NotificationDefinitions", "NotificationSubscriptions", "NotificationSubscriptionUserOverrides", "PlatformSettingValues", "RuntimeControlRequests"],
            "EXCLUDED by decision: IAM-9 start-time maintenance that rewrites stored actor ids to user ids (raw SQL over a fixed column list). Not a configuration change."),
    ];

    private static readonly Lazy<WriterScanModel> ScanModel = new(() => WriterScanModel.From(typeof(AppertureDbContext), InScopeSets));

    private static readonly Lazy<Dictionary<string, FileScan>> ProductionScan =
        new(() => PersistenceWriterScanner.ScanAll(RepositorySource.Production, ScanModel.Value));

    /// <summary>
    /// Production files that name an in-scope entity type and carry a persist signal but write none of the in-scope sets, each with
    /// the reason. The file-level rule (G5, second rule) asks for a review of every such file; these are the reviewed non-writers.
    /// An entry is removed when the file stops matching, and an entry for a file that writes an in-scope set belongs in
    /// <see cref="Writers"/> instead.
    /// </summary>
    private static readonly Dictionary<string, string> EntityReferencingNonWriters = new(StringComparer.Ordinal)
    {
    };

    [Fact]
    public void G5_the_in_scope_sets_are_real_entity_sets_of_the_context()
    {
        var sets = WriterScanModel.DbSetsOf(typeof(AppertureDbContext));

        InScopeSets.Where(s => !sets.ContainsKey(s)).Should().BeEmpty("every in-scope name is a DbSet of AppertureDbContext (a typo would make the scan vacuous)");
        InScopeSets.Should().OnlyHaveUniqueItems();
        ScanModel.Value.SetToEntity.Should().HaveCount(InScopeSets.Length, "every in-scope set has an entity type the file-level rule can look for");
    }

    [Fact]
    public void G5_only_reviewed_files_write_the_in_scope_tables()
    {
        var found = ProductionScan.Value
            .Where(f => f.Value.Sets.Count > 0)
            .ToDictionary(f => f.Key, f => f.Value.Sets, StringComparer.Ordinal);

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
    public void G5_every_file_that_names_an_in_scope_entity_and_persists_is_reviewed()
    {
        // The second rule. It does not resolve which set is written, so it also covers the idioms the first rule cannot follow
        // (a context-level verb on an entity handed in by name only, a generic repository, a state assignment on a held entry).
        var flagged = ProductionScan.Value.Where(f => f.Value.NamesEntityAndPersists).ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);
        flagged.Should().NotBeEmpty("the data access classes of the in-scope sets name their entity types and persist");

        var writerFiles = Writers.Select(w => w.File).ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var (file, scan) in flagged.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            if (writerFiles.Contains(file))
                continue;

            if (EntityReferencingNonWriters.ContainsKey(file))
            {
                scan.Sets.Should().BeEmpty($"{file} is listed as a non-writer but the set scan found it writing {string.Join(", ", scan.Sets)}; move it to Writers");
                continue;
            }

            problems.Add(
                $"UNREVIEWED {file} names {string.Join(", ", scan.NamedEntities)} and carries {string.Join(", ", scan.PersistSignals)}. "
                + "If it writes an in-scope table, route the write through an audited unit and add it to Writers; if it only reads those entities, list it in EntityReferencingNonWriters with the reason.");
        }

        foreach (var file in EntityReferencingNonWriters.Keys.Where(f => !flagged.ContainsKey(f)))
            problems.Add($"STALE NON-WRITER {file} no longer names an in-scope entity and persists; remove it from EntityReferencingNonWriters.");
        foreach (var file in EntityReferencingNonWriters.Keys.Where(writerFiles.Contains))
            problems.Add($"{file} is both a writer and a non-writer; keep it in Writers only.");
        foreach (var (file, reason) in EntityReferencingNonWriters)
            RepositorySource.Find(file).Should().NotBeNull($"{file} exists (reason: {reason})");

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

        EntityReferencingNonWriters.Values.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r));
    }

    // =====================================================================================================================
    // SEC-2.4 (finding F2; gate P-7, P-8, P-10; acceptance A4.3): probe outcomes are telemetry on a table of their own
    // =====================================================================================================================

    private const string RegistrationRepositoryFile = "Apperture.Persistence/Apperture.Persistence.SqlServer/SqlEntraInstanceRegistrationRepository.cs";

    /// <summary>
    /// The members of the registration repository that record a probe outcome: the two public methods and the helper they share.
    /// The telemetry classification is made on these, so a new member that records a probe must be added here by a reviewer.
    /// </summary>
    private static readonly string[] ProbeMembers = ["UpdateOpenIdValidationOutcomeAsync", "UpdateDirectoryDiscoveryOutcomeAsync", "RecordAsync"];

    /// <summary>The eight diagnostic fields that left the registration row.</summary>
    private static readonly string[] ProbeColumns =
    [
        "OpenIdConfigurationStatus", "LastOpenIdValidationUtc", "LastOpenIdValidationSummary", "LastOpenIdValidationDetail",
        "DirectoryDiscoveryStatus", "LastDirectoryDiscoveryUtc", "LastDirectoryDiscoverySummary", "LastDirectoryDiscoveryDetail",
    ];

    /// <summary>The members of the registration repository that write the registration's configuration (each is audited by its unit).</summary>
    private static readonly string[] ConfigurationMembers = ["GetActiveAsync", "GetByIdAsync", "ListAsync", "AddAsync", "UpdateAsync", "DeleteAsync", "SetActiveAsync"];

    /// <summary>
    /// Every declaration (all overloads) of the named members, then every member of the same file they call, and so on: the
    /// closure. Declarations are found with <see cref="IsDeclaration"/>; a body is a block or an expression. Keyed by member name,
    /// each value is the text of all declarations of that name.
    /// </summary>
    private static Dictionary<string, string> MemberClosure(SourceFile file, IEnumerable<string> roots)
    {
        var code = file.Code;
        var spans = CSharpSource.StringSpans(code);
        var declared = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(code, @"\b([A-Za-z_]\w*)\s*\("))
        {
            if (!IsDeclaration(code, m.Index))
                continue;

            var depth = 0;
            var i = m.Index + m.Length - 1;
            for (; i < code.Length; i++)
            {
                if (code[i] == '(')
                    depth++;
                else if (code[i] == ')' && --depth == 0)
                    break;
            }

            i++;
            while (i < code.Length && char.IsWhiteSpace(code[i]))
                i++;
            string text;
            if (i + 1 < code.Length && code[i] == '=' && code[i + 1] == '>')
                text = code[m.Index..(code.IndexOf(';', i) + 1)];
            else if (i < code.Length && code[i] == '{')
                text = code[m.Index..CSharpSource.MatchingBrace(code, i, spans)];
            else
                continue;

            var name = m.Groups[1].Value;
            if (!declared.TryGetValue(name, out var texts))
                declared[name] = texts = [];
            texts.Add(text);
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var pending = new Queue<string>(roots);
        while (pending.Count > 0)
        {
            var name = pending.Dequeue();
            if (result.ContainsKey(name) || !declared.TryGetValue(name, out var texts))
                continue;

            result[name] = string.Join("\n", texts);
            foreach (Match call in Regex.Matches(result[name], @"\b([A-Za-z_]\w*)\s*\("))
                pending.Enqueue(call.Groups[1].Value);
        }

        return result;
    }

    [Fact]
    public void G5_probe_telemetry_writes_only_the_diagnostics_table()
    {
        var file = RepositorySource.Find(RegistrationRepositoryFile);
        file.Should().NotBeNull();
        var members = MemberClosure(file!, ProbeMembers);
        members.Keys.Should().Contain(ProbeMembers, "every probe member is declared in the registration repository");
        members.Keys.Should().NotContain(ConfigurationMembers, "a probe member must not reach a configuration member (UpdateAsync, SetActiveAsync and the rest bump UpdatedUtc and the rowversion)");

        // A repository member that is neither a configuration operation nor a known probe is a new way to write, and is reviewed.
        typeof(IEntraInstanceRegistrationRepository).GetMethods().Select(m => m.Name)
            .Except(ConfigurationMembers).Except(ProbeMembers)
            .Should().BeEmpty("a new member of the registration repository is either a configuration operation (list it in ConfigurationMembers, audited) or a probe (list it in ProbeMembers)");

        var combined = string.Join("\n", members.Values);
        var scan = PersistenceWriterScanner.Scan(Synthetic(Class(combined)), ScanModel.Value);

        scan.Sets.Should().BeEquivalentTo(
            new[] { "EntraRegistrationDiagnostics" },
            $"a probe outcome is telemetry and is written to the diagnostics row only, never to the registration's configuration row (P-7, P-8); found by: {string.Join("; ", scan.Idioms)}");

        // Belt and braces for what the scanner cannot see: the registration's identity and concurrency columns, a tracked save
        // (which would write whatever else is tracked) and any audit.
        foreach (var forbidden in new[] { "UpdatedUtc", "RowVersion", "SaveChanges", "Entry(", "EntraInstanceRegistrations.", "Audit" })
            combined.Should().NotContain(forbidden, $"a probe member must not touch the registration row or the audit ({forbidden})");

        members["UpdateOpenIdValidationOutcomeAsync"].Should().Contain("EntraRegistrationDiagnostics");
        members["UpdateDirectoryDiscoveryOutcomeAsync"].Should().Contain("EntraRegistrationDiagnostics");
    }

    [Theory]
    [InlineData("tracked read, then a save",
        """async Task UpdateOpenIdValidationOutcomeAsync(string id) { var e = await _db.EntraInstanceRegistrations.FirstOrDefaultAsync(x => x.Id == id); e.UpdatedUtc = DateTimeOffset.UtcNow; await _db.SaveChangesAsync(); }""")]
    [InlineData("set-based update of the registration",
        """async Task UpdateDirectoryDiscoveryOutcomeAsync(string id) { await _db.EntraInstanceRegistrations.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedUtc, DateTimeOffset.UtcNow)); }""")]
    [InlineData("raw update of the registration",
        """async Task RecordAsync(string id) { await _db.Database.ExecuteSqlRawAsync("UPDATE [EntraInstanceRegistrations] SET [UpdatedUtc] = SYSDATETIMEOFFSET() WHERE [Id] = @id"); }""")]
    public void G5_self_test_a_probe_member_that_writes_the_registration_row_is_detected(string name, string code)
    {
        var scan = PersistenceWriterScanner.Scan(Synthetic(Class(code)), ScanModel.Value);

        scan.Sets.Should().Contain("EntraInstanceRegistrations", $"{name}: the guard must see a probe write to the configuration row; found by: {string.Join("; ", scan.Idioms)}");
    }

    [Fact]
    public void G5_self_test_the_probe_closure_follows_helpers_and_overloads()
    {
        var file = Synthetic("""
            public sealed class SyntheticRepository
            {
                private readonly AppertureDbContext _db;
                public Task UpdateOpenIdValidationOutcomeAsync(string id) => Touch(id);
                public Task UpdateOpenIdValidationOutcomeAsync(string id, int extra) { return Task.CompletedTask; }
                private async Task Touch(string id) { await _db.EntraInstanceRegistrations.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedUtc, DateTimeOffset.UtcNow)); }
                public Task UpdateAsync(string id) { return Task.CompletedTask; }
            }
            """);

        var closure = MemberClosure(file, ["UpdateOpenIdValidationOutcomeAsync"]);

        closure.Keys.Should().BeEquivalentTo("UpdateOpenIdValidationOutcomeAsync", "Touch");
        closure["UpdateOpenIdValidationOutcomeAsync"].Should().Contain("Touch(id)").And.Contain("int extra", "both overloads are in the closure");
        PersistenceWriterScanner.Scan(Synthetic(Class(string.Join("\n", closure.Values))), ScanModel.Value).Sets
            .Should().Contain("EntraInstanceRegistrations", "a probe that reaches the registration row through a helper is found");
    }

    [Fact]
    public void G5_the_probe_fields_live_on_the_diagnostics_entity_and_not_on_the_registration_entity()
    {
        var registration = typeof(EntraInstanceRegistrationEntity).GetProperties().Select(p => p.Name).ToList();
        registration.Should().NotContain(ProbeColumns, "the eight diagnostic fields moved to EntraRegistrationDiagnostics (P-8); a field back on the registration row would be a second source of truth");
        registration.Should().Contain("RowVersion").And.Contain("UpdatedUtc", "the configuration identity stays on the registration row");

        var diagnostics = typeof(EntraRegistrationDiagnosticsEntity).GetProperties().Select(p => p.Name).ToList();
        diagnostics.Should().BeEquivalentTo(ProbeColumns.Append("RegistrationId"),
            "the diagnostics row carries the eight fields and its key, and no configuration identity (no UpdatedUtc, no concurrency token)");
    }

    [Fact]
    public void G1_no_audit_action_records_an_entra_probe()
    {
        // P-7 and P-10 (SR-7): probe outcomes are telemetry with no audit row, so the catalog has no W1, W3 or W4 action for them.
        var probeWords = new[] { "probe", "validate_openid", "openid_validation", "directory_search", "directory_discovery", "discovery" };

        Catalog.Definitions
            .Select(d => d.Action)
            .Where(a => probeWords.Any(w => a.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .Should().BeEmpty("a probe outcome is operational telemetry and records no audit row");
    }

    // ---- self-tests: the scanning functions are fed synthetic files and must report each idiom ----

    private static SourceFile Synthetic(string code, string path = "Apperture.Persistence/Synthetic/SyntheticWriter.cs") => new(path, code);

    private const string ContextField = "private readonly AppertureDbContext _db; ";

    private static string Class(string body) => $"public sealed class SyntheticWriter {{ {ContextField}{body} }}";

    /// <summary>(name, code, expected in-scope sets, comma separated; empty means the scanner must report no write).</summary>
    private static readonly (string Name, string Code, string Sets)[] WriterIdioms =
    [
        // (a) context-level verbs: the write verb is not in the tail after a set.
        ("a: _db.Add of a new entity", Class("""void M() { _db.Add(new BindingEntity { Id = "b" }); _db.SaveChanges(); }"""), "Bindings"),
        ("a: _db.AddAsync", Class("""async Task M() { await _db.AddAsync(new ConnectionEntity()); await _db.SaveChangesAsync(); }"""), "Connections"),
        ("a: _db.AddRange", Class("""void M() { _db.AddRange(new List<ResourceContextEntity>()); _db.SaveChanges(); }"""), "ResourceContexts"),
        ("a: _db.Update of an entity parameter", Class("""void M(IntegrationEntity entity) { _db.Update(entity); _db.SaveChanges(); }"""), "Integrations"),
        ("a: _db.UpdateRange", Class("""void M(List<ScheduleDefinitionEntity> rows) { _db.UpdateRange(rows); _db.SaveChanges(); }"""), "ScheduleDefinitions"),
        ("a: _db.Remove", Class("""void M(NotificationDefinitionEntity row) { _db.Remove(row); _db.SaveChanges(); }"""), "NotificationDefinitions"),
        ("a: _db.RemoveRange", Class("""void M(List<ManagedContactEntity> rows) { _db.RemoveRange(rows); _db.SaveChanges(); }"""), "ManagedContacts"),
        ("a: _db.Attach", Class("""void M(SecretVersionEntity row) { _db.Attach(row); _db.SaveChanges(); }"""), "SecretVersions"),
        ("a: Entry state Modified", Class("""void M(ModuleInstallationStateEntity row) { _db.Entry(row).State = EntityState.Modified; _db.SaveChanges(); }"""), "ModuleInstallationStates"),
        ("a: Entry state Added", Class("""void M(RuntimeControlRequestEntity row) { _db.Entry(row).State = EntityState.Added; _db.SaveChanges(); }"""), "RuntimeControlRequests"),
        ("a: Entry state Deleted", Class("""void M(EntraInstanceRegistrationEntity row) { _db.Entry(row).State = EntityState.Deleted; _db.SaveChanges(); }"""), "EntraInstanceRegistrations"),
        ("a: Entry on a held entry", Class("""void M(ManagedContactGroupEntity row) { var entry = _db.Entry(row); entry.State = EntityState.Modified; _db.SaveChanges(); }"""), "ManagedContactGroups"),
        ("a: Entry property IsModified", Class("""void M(NotificationUserPreferenceEntity row) { _db.Entry(row).Property(x => x.IsEnabled).IsModified = true; _db.SaveChanges(); }"""), "NotificationUserPreferences"),
        ("a: a context declared with another name", "public sealed class S { async Task M(IDbContextFactory<AppertureDbContext> f, PlatformSettingValueEntity e) { await using var scratch = await f.CreateDbContextAsync(); scratch.Update(e); await scratch.SaveChangesAsync(); } }", "PlatformSettingValues"),

        // (b) an untracked read followed by a context-level update.
        ("b: AsNoTracking read then _db.Update", Class("""
            async Task M(string id) { var e = await _db.Integrations.AsNoTracking().FirstAsync(i => i.Id == id); e.Name = "x"; _db.Update(e); await _db.SaveChangesAsync(); }
            """), "Integrations"),
        ("b: AsNoTracking read then Entry state", Class("""
            async Task M(string id) { var e = await _db.Bindings.AsNoTracking().FirstAsync(i => i.Id == id); _db.Entry(e).State = EntityState.Modified; await _db.SaveChangesAsync(); }
            """), "Bindings"),

        // (c) a set or queryable kept in a local and written later.
        ("c: set kept in a local, then Add", Class("""
            void M() { var set = _db.PlatformSettingValues; set.Add(new PlatformSettingValueEntity()); _db.SaveChanges(); }
            """), "PlatformSettingValues"),
        ("c: set kept in a typed local, then Remove", Class("""
            void M(BindingEntity e) { DbSet<BindingEntity> bindings = _db.Bindings; bindings.Remove(e); _db.SaveChanges(); }
            """), "Bindings"),
        ("c: set kept in a field, then RemoveRange", "public sealed class S { private readonly DbSet<ConnectionEntity> _connections; public S(AppertureDbContext db) { _connections = db.Connections; } void M(List<ConnectionEntity> rows) { _connections.RemoveRange(rows); } }", "Connections"),
        ("c: queryable kept in a local, then ExecuteDelete", Class("""
            async Task M(string id) { var q = _db.Connections.Where(c => c.Id == id); await q.ExecuteDeleteAsync(); }
            """), "Connections"),
        ("c: queryable kept in a local, then a tracked read", Class("""
            async Task M() { var q = _db.SecretDescriptors; var rows = await q.ToListAsync(); rows[0].Name = "x"; await _db.SaveChangesAsync(); }
            """), "SecretDescriptors"),
        ("c: Set<T>() kept in a local, then Update", Class("""
            void M(ScheduleAssignmentEntity e) { var set = _db.Set<ScheduleAssignmentEntity>(); set.Update(e); _db.SaveChanges(); }
            """), "ScheduleAssignments"),
        ("c: ExecuteUpdate on the Entra probe telemetry set", Class("""
            async Task M(string id) { await _db.EntraRegistrationDiagnostics.Where(d => d.RegistrationId == id).ExecuteUpdateAsync(s => s.SetProperty(d => d.DirectoryDiscoveryStatus, 2)); }
            """), "EntraRegistrationDiagnostics"),

        // (d) a projection is not an aggregate; only a terminal aggregate operator is exempt.
        ("d: tracked read through Select(identity)", Class("""
            async Task M() { var rows = await _db.NotificationSubscriptions.Select(s => s).ToListAsync(); rows[0].IsEnabled = false; await _db.SaveChangesAsync(); }
            """), "NotificationSubscriptions"),
        ("d: tracked read through a navigation Select", Class("""
            async Task M() { var rows = await _db.ManagedContactIdentityLinks.Select(l => l.Contact).ToListAsync(); await _db.SaveChangesAsync(); }
            """), "ManagedContactIdentityLinks"),

        ("d: tracked read through a new shape that carries the row entity", Class("""
            async Task M() { var rows = await _db.Bindings.Select(b => new { b, b.Id }).ToListAsync(); rows[0].b.State = 1; await _db.SaveChangesAsync(); }
            """), "Bindings"),

        // (e) raw ADO.NET: no DbContext in the file, an alias, other statement forms.
        ("e: raw command with an alias (no DbContext in the file)", """
            public sealed class RawWriter { void M(SqlCommand cmd) { cmd.CommandText = "UPDATE i SET i.DisplayName = @n FROM Integrations i WHERE i.Id = @id"; cmd.ExecuteNonQuery(); } }
            """, "Integrations"),
        ("e: raw bracketed schema-qualified delete", """
            public sealed class RawWriter { void M(SqlCommand cmd) { cmd.CommandText = "DELETE FROM [dbo].[Bindings] WHERE Id = @id"; cmd.ExecuteNonQuery(); } }
            """, "Bindings"),
        ("e: raw lower case update", """
            public sealed class RawWriter { void M(SqlCommand cmd) { cmd.CommandText = "update integrations set Name = 'x'"; cmd.ExecuteNonQuery(); } }
            """, "Integrations"),
        ("e: raw insert with dbo prefix", """
            public sealed class RawWriter { void M(IDbCommand cmd) { cmd.CommandText = "INSERT INTO dbo.ScheduleAssignments (Id) VALUES (1)"; cmd.ExecuteNonQuery(); } }
            """, "ScheduleAssignments"),
        ("e: raw merge", """
            public sealed class RawWriter { void M(IDbCommand cmd) { cmd.CommandText = "MERGE INTO Connections AS t USING src ON t.Id = src.Id WHEN MATCHED THEN UPDATE SET Name = src.Name;"; cmd.ExecuteNonQuery(); } }
            """, "Connections"),
        ("e: raw verbatim multi-line delete with an alias and a join", """"
            public sealed class RawWriter { void M(SqlCommand cmd) { cmd.CommandText = @"DELETE b
            FROM dbo.Bindings AS b
            JOIN Integrations i ON i.Id = b.IntegrationId
            WHERE i.State = 3"; cmd.ExecuteNonQuery(); } }
            """", "Bindings"),
        ("e: ExecuteSqlRaw in an EF file", Class("""async Task M() { await _db.Database.ExecuteSqlRawAsync("DELETE FROM Integrations WHERE State = 3"); }"""), "Integrations"),
        ("e: raw insert-select into the probe telemetry table reads the registration table without writing it", Class("""
            async Task M() { await _db.Database.ExecuteSqlRawAsync("INSERT INTO [EntraRegistrationDiagnostics] ([RegistrationId]) SELECT r.[Id] FROM [EntraInstanceRegistrations] AS r WHERE r.[Id] = @id"); }
            """), "EntraRegistrationDiagnostics"),
        ("e: a literal that is just the table name in a raw-command file", """
            public sealed class RawWriter { void M(SqlCommand cmd, string key) { Run(cmd, "Integrations", key); cmd.ExecuteNonQuery(); } }
            """, "Integrations"),

        // What the scanner deliberately ignores.
        ("ignored: raw read of an in-scope table", """
            public sealed class RawReader { void M(SqlCommand cmd) { cmd.CommandText = "SELECT COUNT(*) FROM Integrations"; cmd.ExecuteScalar(); } }
            """, ""),
        ("ignored: raw write of a table that is not in scope", """
            public sealed class RawWriter { void M(SqlCommand cmd) { cmd.CommandText = "UPDATE ScheduleRuns SET State = 2 WHERE Id = @id"; cmd.ExecuteNonQuery(); } }
            """, ""),
        ("ignored: raw write of another table that only joins an in-scope table", """
            public sealed class RawWriter { void M(SqlCommand cmd) { cmd.CommandText = "UPDATE r SET r.State = 2 FROM ScheduleRuns r JOIN Integrations i ON i.Id = r.IntegrationId"; cmd.ExecuteNonQuery(); } }
            """, ""),
        ("ignored: English text that starts with a write word", """
            public sealed class Messages { SqlCommand _unused; string M() => "Update the Integrations page"; }
            """, ""),
        ("ignored: untracked read with other writes elsewhere", Class("""
            async Task M() { var rows = await _db.Integrations.AsNoTracking().ToListAsync(); _db.Add(new UserEntity()); await _db.SaveChangesAsync(); }
            """), ""),
        ("ignored: terminal aggregates in a persisting file", Class("""
            async Task M() { var n = await _db.Integrations.CountAsync(); var any = await _db.Bindings.AnyAsync(); await _db.SaveChangesAsync(); }
            """), ""),
        ("ignored: a grouped projection into a new shape", Class("""
            async Task M() { var counts = await _db.Integrations.GroupBy(e => e.State).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(); await _db.SaveChangesAsync(); }
            """), ""),
        ("ignored: Entry state Detached", Class("""void M(IntegrationEntity row) { _db.Entry(row).State = EntityState.Detached; }"""), ""),
        ("ignored: a list that is added to", Class("""void M(IntegrationEntity row) { var list = new List<IntegrationEntity>(); list.Add(row); }"""), ""),
        ("ignored: a write only named in a comment", Class("""
            // _db.Integrations.Add(entity); and "UPDATE Integrations SET x = 1" are only comments here
            void M() { }
            """), ""),
        ("ignored: a migration", Class("""void M() { _db.Integrations.Add(new IntegrationEntity()); }"""), ""),

        // Residual limit, pinned so it stays visible: a table name built at run time is not seen.
        ("limit: a built table name is not resolved", """
            public sealed class RawWriter { void M(SqlCommand cmd, string table) { cmd.CommandText = "UPDATE " + table + " SET Name = 'x'"; cmd.ExecuteNonQuery(); } }
            """, ""),
    ];

    public static IEnumerable<object[]> WriterIdiomCases() => WriterIdioms.Select(c => new object[] { c.Name, c.Code, c.Sets });

    [Theory]
    [MemberData(nameof(WriterIdiomCases))]
    public void G5_self_test_the_scanner_reports_each_write_idiom(string name, string code, string sets)
    {
        var path = name.StartsWith("ignored: a migration", StringComparison.Ordinal) ? "Apperture.Persistence/Migrations/Synthetic.cs" : "Apperture.Persistence/Synthetic/SyntheticWriter.cs";

        var scan = PersistenceWriterScanner.Scan(Synthetic(code, path), ScanModel.Value);

        scan.Sets.Should().BeEquivalentTo(sets.Split(',', StringSplitOptions.RemoveEmptyEntries), $"{name}; found by: {string.Join("; ", scan.Idioms)}");
    }

    /// <summary>(name, code, expected NamesEntityAndPersists).</summary>
    private static readonly (string Name, string Code, bool Flagged)[] FileLevelCases =
    [
        ("an entity handed to a generic helper, saved through a unit", "public sealed class S { void M(IntegrationEntity e) { Mutate(e); _unit.SaveChanges(); } }", true),
        ("an entity named and ExecuteSql used", "public sealed class S { Task M(BindingEntity e) => Run(\"x\"); void N(DbContext c) { c.Database.ExecuteSqlRaw(Sql); } }", true),
        ("an entity named and a context-level verb on another receiver name", "public sealed class S { private readonly AppertureDbContext _db; void M(ConnectionEntity e) { _db.Update(Map(e)); } }", true),
        ("an entity named with no persist signal", "public sealed class S { ConnectionEntity M(ConnectionEntity e) => e; }", false),
        ("a persist signal with no in-scope entity named", "public sealed class S { void M(UserEntity e) { _db.Add(e); _db.SaveChanges(); } }", false),
        ("an entity only in a comment", "public sealed class S { /* IntegrationEntity */ void M() { _db.SaveChanges(); } }", false),
    ];

    public static IEnumerable<object[]> FileLevelCaseData() => FileLevelCases.Select(c => new object[] { c.Name, c.Code, c.Flagged });

    [Theory]
    [MemberData(nameof(FileLevelCaseData))]
    public void G5_self_test_the_file_level_rule_flags_a_file_that_names_an_in_scope_entity_and_persists(string name, string code, bool flagged)
    {
        var scan = PersistenceWriterScanner.Scan(Synthetic(code), ScanModel.Value);

        scan.NamesEntityAndPersists.Should().Be(flagged, name);
    }

    [Fact]
    public void G5_self_test_the_scanner_does_not_read_migrations_the_audit_catalog_or_test_support()
    {
        const string code = "public sealed class S { void M(IntegrationEntity e, AppertureDbContext _db) { _db.Update(e); _db.SaveChanges(); } }";

        foreach (var path in new[]
                 {
                     "Apperture.Persistence/Migrations/20260101_X.cs",
                     "Apperture.Core/Apperture.Core.Application/Audit/AuditEvents.X.cs",
                     "Apperture.ModuleSdk/Apperture.ModuleSdk.Testing/Harness.cs",
                 })
        {
            PersistenceWriterScanner.Scan(Synthetic(code, path), ScanModel.Value).NamesEntityAndPersists.Should().BeFalse(path);
        }

        PersistenceWriterScanner.Scan(Synthetic(code), ScanModel.Value).NamesEntityAndPersists.Should().BeTrue("the same text in a production path is flagged");
    }

    // ---- G4 self-tests: an action that is not a literal or a constant is unresolved ----

    [Fact]
    public void G4_self_test_a_literal_or_constant_action_is_resolved()
    {
        var file = Synthetic("""
            public sealed class Appender { async Task M() { await _audit.AppendAsync(new AuditRecord(new AuditRecordId(Guid.NewGuid().ToString("N")), "scheduler.schedule.failed", actor), ct); } }
            """);

        var analysis = AnalyzeAppends(file, []);

        analysis.Statements.Should().BeGreaterThan(0);
        analysis.Resolved.Should().BeEquivalentTo("scheduler.schedule.failed");
        analysis.Unresolved.Should().BeEmpty();
    }

    [Fact]
    public void G4_self_test_an_action_passed_through_a_local_variable_is_unresolved()
    {
        var inline = Synthetic("""
            public sealed class Appender { async Task M(bool failed) { var action = failed ? "scheduler.schedule.failed" : "scheduler.schedule.dispatched"; await _audit.AppendAsync(new AuditRecord(new AuditRecordId("x"), action, actor), ct); } }
            """);
        var viaRecord = Synthetic("""
            public sealed class Appender { async Task M(string action) { var record = new AuditRecord(new AuditRecordId("x"), action, actor); await _audit.AppendAsync(record, ct); } }
            """);

        AnalyzeAppends(inline, []).Unresolved.Should().ContainSingle().Which.Should().Contain("new AuditRecord");
        AnalyzeAppends(viaRecord, []).Unresolved.Should().ContainSingle();
        AnalyzeAppends(inline, []).Resolved.Should().BeEmpty("the literals sit in a separate statement, so the local-variable append resolves none and must be declared Dynamic with its action list");
    }

    [Fact]
    public void G4_self_test_a_helper_declaration_is_not_a_call_and_its_calls_are_checked()
    {
        const string helper = """
            public sealed class Appender
            {
                private async Task AppendX(string action, ActorId actor, CancellationToken ct)
                {
                    var record = new AuditRecord(new AuditRecordId("x"), action, actor);
                    await _audit.AppendAsync(record, ct);
                }

                async Task A(CancellationToken ct) { await AppendX("auth.local.login.failed", actor, ct); }
            }
            """;

        var ok = AnalyzeAppends(Synthetic(helper), ["AppendX"]);
        ok.Resolved.Should().BeEquivalentTo("auth.local.login.failed");
        ok.Unresolved.Should().BeEmpty("the construction inside the helper takes its action from the helper's parameter; the call site names it");

        var viaLocal = Synthetic(helper.Replace("await AppendX(\"auth.local.login.failed\", actor, ct);", "var chosen = Pick(); await AppendX(chosen, actor, ct);"));
        AnalyzeAppends(viaLocal, ["AppendX"]).Unresolved.Should().ContainSingle().Which.Should().Contain("helper call");
    }

}
