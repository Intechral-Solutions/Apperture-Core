using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Scheduling;
using Apperture.ModuleSdk.Integration.Readiness;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.ControlPlane.Security;

/// <inheritdoc />
public sealed class IntegrationReadinessService : IIntegrationReadinessService
{
    private readonly IIntegrationManagementService _integrationManagement;
    private readonly IIntegrationRepository _integrationRepository;
    private readonly IModuleSlotRegistry _moduleSlotRegistry;
    private readonly IBindingRepository _bindingRepository;
    private readonly IConnectionRepository _connectionRepository;
    private readonly IScheduleRepository _scheduleRepository;
    private readonly IRunQueryService _runQuery;
    private readonly IAttentionIncidentTracker _attentionIncidentTracker;
    private readonly IScheduleExpectationEvaluator _scheduleExpectation;
    private readonly TimeProvider _timeProvider;
    private readonly IntegrationDispatchReadinessEvaluator _dispatchReadinessEvaluator;

    public IntegrationReadinessService(
        IIntegrationManagementService integrationManagement,
        IIntegrationRepository integrationRepository,
        IModuleSlotRegistry moduleSlotRegistry,
        IBindingRepository bindingRepository,
        IConnectionRepository connectionRepository,
        IScheduleRepository scheduleRepository,
        IRunQueryService runQuery,
        IAttentionIncidentTracker attentionIncidentTracker,
        IScheduleExpectationEvaluator scheduleExpectation,
        TimeProvider timeProvider,
        IntegrationDispatchReadinessEvaluator dispatchReadinessEvaluator)
    {
        _integrationManagement = integrationManagement;
        _integrationRepository = integrationRepository;
        _moduleSlotRegistry = moduleSlotRegistry;
        _bindingRepository = bindingRepository;
        _connectionRepository = connectionRepository;
        _scheduleRepository = scheduleRepository;
        _runQuery = runQuery;
        _attentionIncidentTracker = attentionIncidentTracker;
        _scheduleExpectation = scheduleExpectation;
        _timeProvider = timeProvider;
        _dispatchReadinessEvaluator = dispatchReadinessEvaluator;
    }

    /// <inheritdoc />
    public ValueTask<IntegrationReadinessProjection?> GetReadinessAsync(
        IntegrationId id,
        CancellationToken cancellationToken = default) =>
        EvaluateForIntegrationAsync(id, procedureId: null, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<IntegrationReadinessProjection?> GetProcedureReadinessAsync(
        IntegrationId id,
        string procedureId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(procedureId);
        return await EvaluateForIntegrationAsync(id, procedureId, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<IntegrationReadinessProjection?> EvaluateForIntegrationAsync(
        IntegrationId id,
        string? procedureId,
        CancellationToken cancellationToken)
    {
        var integration = await _integrationManagement.GetIntegrationAsync(id, cancellationToken);
        if (integration is null)
            return null;

        var bindings = await _bindingRepository.GetByIntegrationIdAsync(id, cancellationToken);
        var schedules = await _scheduleRepository.GetByIntegrationIdAsync(id, cancellationToken);
        return await _dispatchReadinessEvaluator
            .EvaluateAsync(integration, bindings, schedules, procedureId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IntegrationOperatorDetailProjection?> GetOperatorDetailAsync(IntegrationId id, CancellationToken cancellationToken = default)
    {
        var integration = await _integrationManagement.GetIntegrationAsync(id, cancellationToken);
        if (integration is null)
            return null;

        var bindings = await _bindingRepository.GetByIntegrationIdAsync(id, cancellationToken);
        var schedules = await _scheduleRepository.GetByIntegrationIdAsync(id, cancellationToken);
        var readiness = await _dispatchReadinessEvaluator
            .EvaluateAsync(integration, bindings, schedules, procedureId: null, cancellationToken)
            .ConfigureAwait(false);

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
        var activeExpectedRunMissing = await _attentionIncidentTracker
            .GetIntegrationIdsWithActiveConditionAsync(AttentionConditionKeys.ExpectedRunMissing, cancellationToken)
            .ConfigureAwait(false);
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
            var readiness = await _dispatchReadinessEvaluator
            .EvaluateAsync(integration, bindings, schedules, procedureId: null, cancellationToken)
            .ConfigureAwait(false);
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
            var snapshot = BuildScheduleExpectationSnapshotFromReadyState(
                integration,
                readiness,
                schedules,
                utcNow,
                sinceUtc,
                attention,
                operationalRuns,
                scheduleOrigin);
            var expectedRuns = IntegrationFleetAttention.FromScheduleExpectationEvaluation(
                snapshot.Evaluation,
                snapshot.ReferenceScheduleId,
                operationalRuns.EvidenceWindowStartUtc,
                snapshot.LatestScheduleOriginAuthorizedRun?.RunId,
                snapshot.LatestScheduleOriginAuthorizedRun?.RunAt);

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
                topCodes,
                activeExpectedRunMissing.Contains(integration.Id.Value)));
        }

        var filtered = ApplyFleetFilters(rows, query);
        return ApplyFleetSort(filtered, query.Sort);
    }

    /// <inheritdoc />
    public async ValueTask<ScheduleExpectationSnapshot?> EvaluateScheduleExpectationAsync(
        IntegrationId id,
        CancellationToken cancellationToken = default)
    {
        var integration = await _integrationManagement.GetIntegrationAsync(id, cancellationToken).ConfigureAwait(false);
        if (integration is null)
            return null;
        return await BuildScheduleExpectationSnapshotAsync(integration, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ScheduleExpectationSnapshot?> EvaluateScheduleExpectationForBackgroundAsync(
        IntegrationId id,
        CancellationToken cancellationToken = default)
    {
        var integration = await _integrationRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (integration is null)
            return null;
        return await BuildScheduleExpectationSnapshotAsync(integration, cancellationToken).ConfigureAwait(false);
    }

    private ScheduleExpectationSnapshot BuildScheduleExpectationSnapshotFromReadyState(
        DomainIntegration integration,
        IntegrationReadinessProjection readiness,
        IReadOnlyList<ScheduleDefinition> schedules,
        DateTimeOffset utcNow,
        DateTimeOffset sinceUtc,
        IntegrationFleetAttentionFlags attention,
        IntegrationFleetOperationalRunAttention operationalRuns,
        FleetScheduleOriginRunEvidence? scheduleOrigin)
    {
        var state = integration.State.ToString();
        var windowStart = operationalRuns.EvidenceWindowStartUtc ?? sinceUtc;
        var latestScheduleOriginEvidence = scheduleOrigin is null
            ? null
            : new ScheduleOriginRunEvidenceInput(scheduleOrigin.AuthorizedRunId, scheduleOrigin.AuthorizedRunAt);
        var evalCtx = new ScheduleExpectationEvaluationContext(
            state,
            utcNow,
            windowStart,
            integration.CreatedAt,
            attention,
            readiness,
            integration.Id,
            schedules,
            latestScheduleOriginEvidence);
        var evaluation = _scheduleExpectation.Evaluate(evalCtx);
        var refSched = ReferenceScheduleIdForFleet(schedules, integration.Id);
        return new ScheduleExpectationSnapshot(
            evaluation,
            refSched,
            latestScheduleOriginEvidence,
            windowStart);
    }

    private async ValueTask<ScheduleExpectationSnapshot> BuildScheduleExpectationSnapshotAsync(
        DomainIntegration integration,
        CancellationToken cancellationToken)
    {
        var bindings = await _bindingRepository.GetByIntegrationIdAsync(integration.Id, cancellationToken).ConfigureAwait(false);
        var schedules = await _scheduleRepository.GetByIntegrationIdAsync(integration.Id, cancellationToken).ConfigureAwait(false);
        var readiness = await _dispatchReadinessEvaluator
            .EvaluateAsync(integration, bindings, schedules, procedureId: null, cancellationToken)
            .ConfigureAwait(false);
        var utcNow = _timeProvider.GetUtcNow();
        var sinceUtc = utcNow - IntegrationFleetRunEvidence.Lookback;
        var state = integration.State.ToString();
        var attention = IntegrationFleetAttention.Compute(state, readiness);
        FleetOperationalRunSignals? sig = null;
        if (string.Equals(state, "Active", StringComparison.OrdinalIgnoreCase))
        {
            var runSignals = await _runQuery.GetFleetOperationalRunSignalsAsync(
                    new[] { integration.Id.Value },
                    sinceUtc,
                    IntegrationFleetRunEvidence.BatchMaxRows,
                    cancellationToken)
                .ConfigureAwait(false);
            runSignals.TryGetValue(integration.Id.Value, out sig);
        }

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

        FleetScheduleOriginRunEvidence? scheduleOrigin = null;
        if (string.Equals(state, "Active", StringComparison.OrdinalIgnoreCase))
        {
            var originMap = await _runQuery.GetLatestAuthorizedScheduleOriginRunsAsync(
                    new[] { integration.Id.Value },
                    sinceUtc,
                    cancellationToken)
                .ConfigureAwait(false);
            originMap.TryGetValue(integration.Id.Value, out scheduleOrigin);
        }

        return BuildScheduleExpectationSnapshotFromReadyState(
            integration,
            readiness,
            schedules,
            utcNow,
            sinceUtc,
            attention,
            operationalRuns,
            scheduleOrigin);
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
