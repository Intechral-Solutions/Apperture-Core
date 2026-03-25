using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Scheduling;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.ControlPlane.Security;

/// <inheritdoc />
public sealed class IntegrationReadinessService : IIntegrationReadinessService
{
    private readonly IIntegrationManagementService _integrationManagement;
    private readonly IModuleSlotRegistry _moduleSlotRegistry;
    private readonly IBindingRepository _bindingRepository;
    private readonly IConnectionRepository _connectionRepository;
    private readonly IBindingEligibilityService _eligibilityService;
    private readonly IScheduleRepository _scheduleRepository;
    private readonly IConnectionVerificationReadinessPolicy _verificationReadinessPolicy;
    private readonly IRunQueryService _runQuery;
    private readonly IScheduleExpectationEvaluator _scheduleExpectation;
    private readonly TimeProvider _timeProvider;

    public IntegrationReadinessService(
        IIntegrationManagementService integrationManagement,
        IModuleSlotRegistry moduleSlotRegistry,
        IBindingRepository bindingRepository,
        IConnectionRepository connectionRepository,
        IBindingEligibilityService eligibilityService,
        IScheduleRepository scheduleRepository,
        IConnectionVerificationReadinessPolicy verificationReadinessPolicy,
        IRunQueryService runQuery,
        IScheduleExpectationEvaluator scheduleExpectation,
        TimeProvider timeProvider)
    {
        _integrationManagement = integrationManagement;
        _moduleSlotRegistry = moduleSlotRegistry;
        _bindingRepository = bindingRepository;
        _connectionRepository = connectionRepository;
        _eligibilityService = eligibilityService;
        _scheduleRepository = scheduleRepository;
        _verificationReadinessPolicy = verificationReadinessPolicy;
        _runQuery = runQuery;
        _scheduleExpectation = scheduleExpectation;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async ValueTask<IntegrationReadinessProjection?> GetReadinessAsync(IntegrationId id, CancellationToken cancellationToken = default)
    {
        var integration = await _integrationManagement.GetIntegrationAsync(id, cancellationToken);
        if (integration is null)
            return null;

        var bindings = await _bindingRepository.GetByIntegrationIdAsync(id, cancellationToken);
        var schedules = await _scheduleRepository.GetByIntegrationIdAsync(id, cancellationToken);
        return await EvaluateReadinessAsync(integration, bindings, schedules, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IntegrationOperatorDetailProjection?> GetOperatorDetailAsync(IntegrationId id, CancellationToken cancellationToken = default)
    {
        var integration = await _integrationManagement.GetIntegrationAsync(id, cancellationToken);
        if (integration is null)
            return null;

        var bindings = await _bindingRepository.GetByIntegrationIdAsync(id, cancellationToken);
        var schedules = await _scheduleRepository.GetByIntegrationIdAsync(id, cancellationToken);
        var readiness = await EvaluateReadinessAsync(integration, bindings, schedules, cancellationToken);

        var slots = _moduleSlotRegistry.GetSlotsForModule(integration.ModuleId);
        var requiredSlots = slots
            .Select(s => new IntegrationModuleSlotInfo(s.SlotName, s.ContractType))
            .ToList();

        var activeConnectionIds = bindings
            .Where(b => b.State == BindingState.Active && b.ConnectionId is not null)
            .Select(b => b.ConnectionId!.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var connectionById = new Dictionary<string, Connection>(StringComparer.Ordinal);
        foreach (var connId in activeConnectionIds)
        {
            var conn = await _connectionRepository.GetByIdAsync(new ConnectionId(connId), cancellationToken);
            if (conn is not null)
                connectionById[connId] = conn;
        }

        var bindingSummaries = bindings
            .Select(b =>
            {
                connectionById.TryGetValue(b.ConnectionId?.Value ?? string.Empty, out var conn);
                return new IntegrationBindingSummary(
                    b.Id.Value,
                    b.SlotName,
                    b.ContractType,
                    b.ConnectionId?.Value,
                    b.CapabilityId,
                    b.State.ToString(),
                    conn?.LastVerificationStatus.ToString(),
                    conn?.LastVerificationAtUtc);
            })
            .ToList();

        var scheduleSummaries = schedules
            .Select(d => new IntegrationScheduleSummary(
                d.Id.Value,
                d.Name,
                d.Enabled,
                d.RecurrenceExpression,
                d.IntegrationId?.Value))
            .ToList();

        return new IntegrationOperatorDetailProjection(
            integration.Id.Value,
            integration.DisplayName,
            integration.ModuleId,
            integration.ScopeKey,
            integration.State.ToString(),
            integration.Settings,
            integration.CreatedAt,
            integration.UpdatedAt,
            readiness,
            requiredSlots,
            bindingSummaries,
            scheduleSummaries);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<IntegrationFleetRowProjection>> ListFleetAsync(
        IntegrationFleetQuery query,
        CancellationToken cancellationToken = default)
    {
        var integrations = await _integrationManagement.ListIntegrationsAsync(cancellationToken);
        var utcNow = _timeProvider.GetUtcNow();
        var sinceUtc = utcNow - IntegrationFleetRunEvidence.Lookback;
        var activeIds = integrations
            .Where(i => i.State == IntegrationState.Active)
            .Select(i => i.Id.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var runSignals = await _runQuery.GetFleetOperationalRunSignalsAsync(
                activeIds,
                sinceUtc,
                IntegrationFleetRunEvidence.BatchMaxRows,
                cancellationToken)
            .ConfigureAwait(false);

        var scheduleOriginByIntegration = await _runQuery.GetLatestAuthorizedScheduleOriginRunsAsync(
                activeIds,
                sinceUtc,
                cancellationToken)
            .ConfigureAwait(false);

        var rows = new List<IntegrationFleetRowProjection>(integrations.Count);
        foreach (var integration in integrations)
        {
            var bindings = await _bindingRepository.GetByIntegrationIdAsync(integration.Id, cancellationToken);
            var schedules = await _scheduleRepository.GetByIntegrationIdAsync(integration.Id, cancellationToken);
            var readiness = await EvaluateReadinessAsync(integration, bindings, schedules, cancellationToken);
            var state = integration.State.ToString();
            var attention = IntegrationFleetAttention.Compute(state, readiness);
            var topCodes = IntegrationFleetAttention.TopIssueCodes(readiness);

            runSignals.TryGetValue(integration.Id.Value, out var sig);
            var s = sig ?? new FleetOperationalRunSignals(0, null, null, null, null, null);
            var operationalRuns = IntegrationFleetAttention.BuildOperationalRunAttention(
                state,
                sinceUtc,
                s.RecentAuthorizedFailedRunCount,
                s.MostRecentFailedRunId,
                s.MostRecentFailedRunAt,
                s.LatestAuthorizedRunId,
                s.LatestAuthorizedRunStatus,
                s.LatestAuthorizedRunAt);

            scheduleOriginByIntegration.TryGetValue(integration.Id.Value, out var scheduleOrigin);
            var windowStart = operationalRuns.EvidenceWindowStartUtc ?? sinceUtc;
            var evalCtx = new ScheduleExpectationEvaluationContext(
                state,
                utcNow,
                windowStart,
                integration.CreatedAt,
                attention,
                readiness,
                integration.Id,
                schedules,
                scheduleOrigin is null
                    ? null
                    : new ScheduleOriginRunEvidenceInput(scheduleOrigin.AuthorizedRunId, scheduleOrigin.AuthorizedRunAt));
            var evaluation = _scheduleExpectation.Evaluate(evalCtx);
            var refSched = ReferenceScheduleIdForFleet(schedules, integration.Id);
            var expectedRuns = IntegrationFleetAttention.FromScheduleExpectationEvaluation(
                evaluation,
                refSched,
                operationalRuns.EvidenceWindowStartUtc,
                scheduleOrigin?.AuthorizedRunId,
                scheduleOrigin?.AuthorizedRunAt);

            var tokens = IntegrationFleetAttention.ToAttentionTokens(attention).ToList();
            IntegrationFleetAttention.AppendOperationalAttentionTokens(tokens, state, operationalRuns);
            IntegrationFleetAttention.AppendExpectedRunAttentionTokens(tokens, expectedRuns);

            rows.Add(new IntegrationFleetRowProjection(
                integration.Id.Value,
                integration.DisplayName,
                integration.ModuleId,
                integration.ScopeKey,
                state,
                integration.CreatedAt,
                integration.UpdatedAt,
                readiness,
                attention,
                operationalRuns,
                expectedRuns,
                tokens,
                topCodes));
        }

        var filtered = ApplyFleetFilters(rows, query);
        return ApplyFleetSort(filtered, query.Sort);
    }

    private static List<IntegrationFleetRowProjection> ApplyFleetFilters(
        List<IntegrationFleetRowProjection> rows,
        IntegrationFleetQuery query)
    {
        IEnumerable<IntegrationFleetRowProjection> q = rows;
        if (!string.IsNullOrWhiteSpace(query.State) &&
            Enum.TryParse<IntegrationState>(query.State, ignoreCase: true, out var stateEnum))
        {
            var stateStr = stateEnum.ToString();
            q = q.Where(r => string.Equals(r.OperationalState, stateStr, StringComparison.OrdinalIgnoreCase));
        }

        if (query.Runnable is { } runnable)
            q = q.Where(r => r.Readiness.IsRunnable == runnable);

        if (query.HasBlocking is { } hasBlocking)
        {
            q = hasBlocking
                ? q.Where(r => r.Readiness.BlockingIssueCount > 0)
                : q.Where(r => r.Readiness.BlockingIssueCount == 0);
        }

        if (query.HasAdvisory is { } hasAdvisory)
        {
            q = hasAdvisory
                ? q.Where(r => r.Readiness.AdvisoryIssueCount > 0)
                : q.Where(r => r.Readiness.AdvisoryIssueCount == 0);
        }

        if (query.VerificationConcern == true)
        {
            q = q.Where(r =>
                r.Attention.HasVerificationFailed ||
                r.Attention.HasVerificationUnverified ||
                r.Attention.HasVerificationUnavailable);
        }

        if (!string.IsNullOrWhiteSpace(query.Verification))
        {
            q = query.Verification.Trim().ToLowerInvariant() switch
            {
                "failed" => q.Where(r => r.Attention.HasVerificationFailed),
                "unverified" => q.Where(r => r.Attention.HasVerificationUnverified),
                "unavailable" => q.Where(r => r.Attention.HasVerificationUnavailable),
                _ => q
            };
        }

        if (query.HasOperationalFailure is { } hasOpFail)
        {
            q = hasOpFail
                ? q.Where(r => r.OperationalRuns.HasRecentOperationalFailure)
                : q.Where(r => !r.OperationalRuns.HasRecentOperationalFailure);
        }

        if (query.HasUnexpectedSilence is { } hasSilence)
        {
            q = hasSilence
                ? q.Where(r => r.ExpectedRuns.HasUnexpectedSilence)
                : q.Where(r => !r.ExpectedRuns.HasUnexpectedSilence);
        }

        if (query.AttentionTokens is { Count: > 0 } wanted)
        {
            var normalized = wanted
                .Select(t => t.Trim().ToLowerInvariant())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal);
            if (normalized.Count > 0)
            {
                q = q.Where(r => r.AttentionTokens.Any(t => normalized.Contains(t.ToLowerInvariant())));
            }
        }

        return q.ToList();
    }

    private static IReadOnlyList<IntegrationFleetRowProjection> ApplyFleetSort(
        List<IntegrationFleetRowProjection> rows,
        string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return rows
                .OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var desc = sort.StartsWith('-');
        var key = (desc ? sort[1..] : sort).Trim();
        IOrderedEnumerable<IntegrationFleetRowProjection> ordered = key.ToLowerInvariant() switch
        {
            "moduleid" => desc
                ? rows.OrderByDescending(r => r.ModuleId, StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(r => r.ModuleId, StringComparer.OrdinalIgnoreCase),
            "updatedat" => desc
                ? rows.OrderByDescending(r => r.UpdatedAt)
                : rows.OrderBy(r => r.UpdatedAt),
            "recentfailedcount" => desc
                ? rows.OrderByDescending(r => r.OperationalRuns.RecentFailedRunCount)
                    .ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(r => r.OperationalRuns.RecentFailedRunCount)
                    .ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase),
            "lastfailedat" => desc
                ? rows.OrderByDescending(r => r.OperationalRuns.MostRecentFailedRunAt ?? DateTimeOffset.MinValue)
                    .ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(r => r.OperationalRuns.MostRecentFailedRunAt ?? DateTimeOffset.MaxValue)
                    .ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase),
            "unexpectedsilence" => desc
                ? rows.OrderByDescending(r => r.ExpectedRuns.HasUnexpectedSilence)
                    .ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(r => r.ExpectedRuns.HasUnexpectedSilence)
                    .ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase),
            _ => desc
                ? rows.OrderByDescending(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
        };
        return ordered.ToList();
    }

    private async Task<IntegrationReadinessProjection> EvaluateReadinessAsync(
        DomainIntegration integration,
        IReadOnlyList<Binding> bindings,
        IReadOnlyList<ScheduleDefinition> schedules,
        CancellationToken cancellationToken)
    {
        var issues = new List<IntegrationReadinessIssue>();

        if (integration.State != IntegrationState.Active)
        {
            issues.Add(NewBlockingIssue(
                IntegrationReadinessIssueCodes.IntegrationNotActive,
                $"Integration is {integration.State}; set the integration to Active before running workflows.",
                null,
                null,
                null));
        }

        var slots = _moduleSlotRegistry.GetSlotsForModule(integration.ModuleId);
        var activeBySlot = bindings
            .Where(b => b.State == BindingState.Active)
            .ToLookup(b => b.SlotName, StringComparer.Ordinal);

        foreach (var slot in slots)
        {
            var list = activeBySlot[slot.SlotName].ToList();
            if (list.Count == 0)
            {
                var nonActive = bindings.FirstOrDefault(b =>
                    string.Equals(b.SlotName, slot.SlotName, StringComparison.Ordinal) &&
                    b.State != BindingState.Active);
                if (nonActive is not null)
                {
                    issues.Add(NewBlockingIssue(
                        IntegrationReadinessIssueCodes.InactiveBinding,
                        $"Slot '{slot.SlotName}' has no active binding; the existing binding is {nonActive.State}. Create or restore an active binding for this integration.",
                        slot.SlotName,
                        nonActive.Id.Value,
                        nonActive.ConnectionId?.Value));
                }
                else
                {
                    issues.Add(NewBlockingIssue(
                        IntegrationReadinessIssueCodes.MissingSlotBinding,
                        $"Required slot '{slot.SlotName}' has no active binding for this integration. Bind it under this integration (contract: {slot.ContractType}).",
                        slot.SlotName,
                        null,
                        null));
                }

                continue;
            }

            if (list.Count > 1)
            {
                issues.Add(NewBlockingIssue(
                    IntegrationReadinessIssueCodes.DuplicateActiveBindingForSlot,
                    $"Slot '{slot.SlotName}' has multiple active bindings for this integration; revoke duplicates so only one active binding remains.",
                    slot.SlotName,
                    null,
                    null));
                continue;
            }

            var binding = list[0];
            if (!string.Equals(binding.ContractType, slot.ContractType, StringComparison.Ordinal))
            {
                issues.Add(NewBlockingIssue(
                    IntegrationReadinessIssueCodes.BindingContractMismatch,
                    $"Binding for slot '{slot.SlotName}' uses contract '{binding.ContractType}' but the module requires '{slot.ContractType}'.",
                    slot.SlotName,
                    binding.Id.Value,
                    binding.ConnectionId?.Value));
            }

            if (binding.ConnectionId is { } connId)
            {
                var connection = await _connectionRepository.GetByIdAsync(connId, cancellationToken);
                if (connection is null)
                {
                    issues.Add(NewBlockingIssue(
                        IntegrationReadinessIssueCodes.ConnectionMissing,
                        $"Binding for slot '{slot.SlotName}' references connection '{connId.Value}', which was not found.",
                        slot.SlotName,
                        binding.Id.Value,
                        connId.Value));
                }
                else if (connection.State != ConnectionState.Active)
                {
                    issues.Add(NewBlockingIssue(
                        IntegrationReadinessIssueCodes.ConnectionInactive,
                        $"Connection '{connection.Name}' ({connId.Value}) for slot '{slot.SlotName}' is not Active.",
                        slot.SlotName,
                        binding.Id.Value,
                        connId.Value));
                }
                else
                {
                    AddConnectionVerificationIssueIfNeeded(issues, integration.ModuleId, slot.SlotName, binding, connection);

                    var eligible = await _eligibilityService.GetEligibleConnectionsAsync(
                        integration.ModuleId,
                        slot.SlotName,
                        slot.ContractType,
                        cancellationToken);
                    if (!eligible.Any(c => c.Id.Value == connId.Value))
                    {
                        issues.Add(NewBlockingIssue(
                            IntegrationReadinessIssueCodes.BindingNotEligible,
                            $"Connection '{connection.Name}' is not eligible for slot '{slot.SlotName}' with contract '{slot.ContractType}' (connector or scope mismatch). Choose an eligible connection or fix the connection configuration.",
                            slot.SlotName,
                            binding.Id.Value,
                            connId.Value));
                    }
                }
            }
            else if (string.IsNullOrWhiteSpace(binding.CapabilityId))
            {
                issues.Add(NewBlockingIssue(
                    IntegrationReadinessIssueCodes.BindingMissingConnectionOrCapability,
                    $"Binding for slot '{slot.SlotName}' has neither a connection nor a capability reference.",
                    slot.SlotName,
                    binding.Id.Value,
                    null));
            }
        }

        foreach (var s in schedules)
        {
            if (s.IntegrationId != integration.Id)
            {
                issues.Add(NewBlockingIssue(
                    IntegrationReadinessIssueCodes.ScheduleOwnershipMismatch,
                    $"Schedule '{s.Name}' ({s.Id.Value}) is not correctly associated with this integration in persisted data.",
                    null,
                    null,
                    null));
            }
        }

        var blockingIssueCount = issues.Count(i => string.Equals(i.Severity, IntegrationReadinessIssueSeverity.Blocking.ToString(), StringComparison.Ordinal));
        var advisoryIssueCount = issues.Count(i => string.Equals(i.Severity, IntegrationReadinessIssueSeverity.Advisory.ToString(), StringComparison.Ordinal));
        var runnable = integration.State == IntegrationState.Active && blockingIssueCount == 0;
        return new IntegrationReadinessProjection(
            integration.Id.Value,
            integration.ModuleId,
            runnable,
            blockingIssueCount,
            advisoryIssueCount,
            issues);
    }

    private void AddConnectionVerificationIssueIfNeeded(
        List<IntegrationReadinessIssue> issues,
        string moduleId,
        string slotName,
        Binding binding,
        Connection connection)
    {
        IntegrationReadinessIssue? issue = connection.LastVerificationStatus switch
        {
            ConnectionVerificationStatus.Failed => new IntegrationReadinessIssue(
                IntegrationReadinessIssueCodes.ConnectionVerificationFailed,
                $"Connection '{connection.Name}' last verification failed: {connection.LastVerificationSummary ?? "No summary provided."}",
                slotName,
                binding.Id.Value,
                connection.Id.Value,
                _verificationReadinessPolicy.ResolveSeverity(moduleId, slotName, connection.LastVerificationStatus).ToString()),
            ConnectionVerificationStatus.NeverTested => new IntegrationReadinessIssue(
                IntegrationReadinessIssueCodes.ConnectionVerificationUnverified,
                $"Connection '{connection.Name}' has not been verified yet.",
                slotName,
                binding.Id.Value,
                connection.Id.Value,
                _verificationReadinessPolicy.ResolveSeverity(moduleId, slotName, connection.LastVerificationStatus).ToString()),
            ConnectionVerificationStatus.Unavailable => new IntegrationReadinessIssue(
                IntegrationReadinessIssueCodes.ConnectionVerificationUnavailable,
                $"Connection '{connection.Name}' cannot be verified by this connector.",
                slotName,
                binding.Id.Value,
                connection.Id.Value,
                _verificationReadinessPolicy.ResolveSeverity(moduleId, slotName, connection.LastVerificationStatus).ToString()),
            _ => null
        };

        if (issue is not null)
            issues.Add(issue);
    }

    private static IntegrationReadinessIssue NewBlockingIssue(
        string code,
        string message,
        string? slotName,
        string? bindingId,
        string? connectionId) =>
        new(code, message, slotName, bindingId, connectionId, IntegrationReadinessIssueSeverity.Blocking.ToString());

    /// <summary>
    /// First qualifying workflow/operation schedule id (stable ordering), aligned with <see cref="ScheduleExpectationEvaluator"/>.
    /// </summary>
    private static string? ReferenceScheduleIdForFleet(
        IReadOnlyList<ScheduleDefinition> schedules,
        IntegrationId integrationId) =>
        schedules
            .Where(s =>
                s.Enabled
                && s.IntegrationId == integrationId
                && !string.IsNullOrWhiteSpace(s.RecurrenceExpression)
                && (s.Target.JobType == ScheduledJobType.WorkflowExecution
                    || s.Target.JobType == ScheduledJobType.ModuleOperationExecution))
            .OrderBy(s => s.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault()
            ?.Id.Value;
}
