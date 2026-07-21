using System.Collections.Concurrent;
using System.Data;
using Apperture.Core.Application.Execution;
using Apperture.Core.Application.OperationItems;
using Apperture.Core.Domain.Operations;
using Apperture.Core.Domain.Procedures;
using Apperture.ModuleSdk.Abstractions;
using Apperture.ModuleSdk.Abstractions.Signals;
using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ModuleSdk.Integration.Testing;

public enum IntegrationExecutionOrigin
{
    Manual = 0,
    Scheduled = 1,
}

public sealed record IntegrationExecutionIdentity(
    string RunId,
    string IntegrationId,
    string ModuleId,
    string? ProcedureId,
    IntegrationExecutionOrigin Origin,
    string? CorrelationId,
    string? ScheduleId)
{
    public static IntegrationExecutionIdentity Create(
        string moduleId,
        string? procedureId,
        IntegrationExecutionOrigin origin = IntegrationExecutionOrigin.Manual,
        string? integrationId = null,
        string? runId = null,
        string? correlationId = null,
        string? scheduleId = null) =>
        new(
            runId ?? Guid.NewGuid().ToString("N")[..24],
            integrationId ?? $"{moduleId}.test",
            moduleId,
            procedureId,
            origin,
            correlationId ?? Guid.NewGuid().ToString("N"),
            origin == IntegrationExecutionOrigin.Scheduled ? scheduleId ?? Guid.NewGuid().ToString("N")[..24] : scheduleId);
}

public sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public TestTimeProvider(DateTimeOffset? utcNow = null) =>
        _utcNow = utcNow ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan by) => _utcNow = _utcNow.Add(by);
}

public sealed record CapturedPlatformEvent(
    string Type,
    string SignalName,
    object? Payload,
    DateTimeOffset OccurredAtUtc,
    string ModuleId,
    string? IntegrationId,
    string? RunId,
    string? ProcedureId,
    string? ScheduleId,
    string? CorrelationId);

public sealed record CapturedOperationalLog(
    OperationalLogLevel Level,
    string Code,
    string Message,
    IReadOnlyDictionary<string, object?>? Data);

public sealed record IntegrationProcedureExecution(
    IntegrationExecutionIdentity Identity,
    ProcedureExecutionOutcome Outcome,
    object? Result,
    IReadOnlyList<CapturedPlatformEvent> PublishedEvents,
    IReadOnlyList<CapturedOperationalLog> Logs,
    IReadOnlyList<OperationGroup> OperationGroups,
    IReadOnlyList<OperationItem> OperationItems);

public sealed class IntegrationProcedureTestHost
{
    private readonly IModule _module;
    private readonly IReadOnlyList<IOperationItemRetryHandler> _retryHandlers;
    private readonly Dictionary<string, object?> _slots = new(StringComparer.Ordinal);

    public IntegrationProcedureTestHost(IModule module, IEnumerable<IOperationItemRetryHandler>? retryHandlers = null)
    {
        _module = module;
        _retryHandlers = retryHandlers?.ToList() ?? [];
    }

    public static IntegrationProcedureTestHost For(IModule module, IEnumerable<IOperationItemRetryHandler>? retryHandlers = null) =>
        new(module, retryHandlers);

    public IntegrationProcedureTestHost WithSlot(string slotName, object slotValue)
    {
        _slots[slotName] = slotValue;
        return this;
    }

    public async Task<IntegrationProcedureExecution> ExecuteProcedureAsync(
        string procedureId,
        IReadOnlyDictionary<string, object?>? input = null,
        IntegrationExecutionIdentity? identity = null,
        TestTimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        var manifest = _module.GetManifest();
        var procedure = manifest.Procedures?.FirstOrDefault(x => string.Equals(x.Id, procedureId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Procedure '{procedureId}' was not found on module '{manifest.Id}'.");

        identity ??= IntegrationExecutionIdentity.Create(manifest.Id, procedureId);
        timeProvider ??= new TestTimeProvider();

        return procedure.LaunchKind switch
        {
            ProcedureLaunchKind.Workflow => await ExecuteWorkflowAsync(
                new WorkflowDefinitionId(procedure.TargetDefinitionId),
                input,
                identity,
                timeProvider,
                cancellationToken).ConfigureAwait(false),
            ProcedureLaunchKind.Operation => await ExecuteOperationAsync(
                new OperationId(procedure.TargetDefinitionId),
                input,
                identity,
                timeProvider,
                cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported procedure launch kind '{procedure.LaunchKind}'.")
        };
    }

    public Task<IntegrationProcedureExecution> ExecuteWorkflowDefinitionAsync(
        string workflowDefinitionId,
        IReadOnlyDictionary<string, object?>? input = null,
        IntegrationExecutionIdentity? identity = null,
        TestTimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        identity ??= IntegrationExecutionIdentity.Create(_module.GetManifest().Id, procedureId: null);
        return ExecuteWorkflowAsync(new WorkflowDefinitionId(workflowDefinitionId), input, identity, timeProvider ?? new TestTimeProvider(), cancellationToken);
    }

    public async Task<IntegrationProcedureExecution> ExecuteRetryAsync(
        OperationItem item,
        OperationGroup group,
        string clientIdempotencyKey,
        TestTimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        var handler = _retryHandlers.FirstOrDefault(x => x.Supports(item, group))
            ?? throw new InvalidOperationException($"No retry handler supports module '{group.ModuleId}' / operation '{group.OperationKind}'.");

        var eligibility = handler.Evaluate(item, group);
        if (!eligibility.CanRetry)
            throw new InvalidOperationException(eligibility.BlockReasonMessage ?? eligibility.BlockReasonCode ?? "Retry blocked.");

        var retryAttemptId = Guid.NewGuid().ToString("N")[..24];
        var startPlan = handler.BuildStartPlan(new OperationItemRetryBuildContext(
            item,
            group,
            retryAttemptId,
            clientIdempotencyKey,
            group.RunId));

        var identity = IntegrationExecutionIdentity.Create(
            group.ModuleId,
            startPlan.ProcedureId,
            IntegrationExecutionOrigin.Manual,
            startPlan.IntegrationId ?? group.IntegrationId,
            runId: Guid.NewGuid().ToString("N")[..24],
            correlationId: retryAttemptId);

        return await ExecuteWorkflowAsync(
            startPlan.WorkflowDefinitionId,
            startPlan.Parameters,
            identity,
            timeProvider ?? new TestTimeProvider(),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IntegrationProcedureExecution> ExecuteWorkflowAsync(
        WorkflowDefinitionId workflowDefinitionId,
        IReadOnlyDictionary<string, object?>? input,
        IntegrationExecutionIdentity identity,
        TestTimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var definition = await _module.GetDefinitionsAsync(cancellationToken)
            .FirstOrDefaultAsync(x => x.Id == workflowDefinitionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Workflow definition '{workflowDefinitionId.Value}' was not found.");

        var registry = BuildRegistry();
        var recorder = new TestExecutionRecorder(_module.GetManifest(), identity, timeProvider, ResolveSlot);
        var parameters = SeedParameters(input, identity);

        object? lastResult = null;
        try
        {
            foreach (var stepName in definition.StepNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lastResult = await registry.Resolve(_module.GetManifest().Id, stepName)
                    .ExecuteAsync(recorder.Context, parameters, cancellationToken)
                    .ConfigureAwait(false);
            }

            return recorder.BuildExecution(
                ProcedureExecutionOutcome.Succeeded(),
                lastResult);
        }
        catch (OperationCanceledException)
        {
            return recorder.BuildExecution(
                ProcedureExecutionOutcome.Cancelled("Execution cancelled."),
                null);
        }
        catch (OperatorExecutionFailureException ex)
        {
            return recorder.BuildExecution(
                ProcedureExecutionOutcome.Failed("operator_execution_failed", ex.Summary),
                ex.Diagnostics);
        }
        catch (Exception ex)
        {
            return recorder.BuildExecution(
                ProcedureExecutionOutcome.Failed("unexpected_exception", ex.Message),
                ex);
        }
    }

    private async Task<IntegrationProcedureExecution> ExecuteOperationAsync(
        OperationId operationId,
        IReadOnlyDictionary<string, object?>? input,
        IntegrationExecutionIdentity identity,
        TestTimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var definitionProvider = _module as IOperationDefinitionProvider
            ?? throw new InvalidOperationException($"Module '{identity.ModuleId}' does not provide operations.");
        var definition = await definitionProvider.GetOperationsAsync(cancellationToken)
            .FirstOrDefaultAsync(x => x.Id == operationId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Operation definition '{operationId.Value}' was not found.");

        var registry = BuildRegistry();
        var recorder = new TestExecutionRecorder(_module.GetManifest(), identity, timeProvider, ResolveSlot);
        var parameters = SeedParameters(input, identity);

        try
        {
            var result = await registry.Resolve(_module.GetManifest().Id, definition.Name)
                .ExecuteAsync(recorder.Context, parameters, cancellationToken)
                .ConfigureAwait(false);
            return recorder.BuildExecution(ProcedureExecutionOutcome.Succeeded(), result);
        }
        catch (OperationCanceledException)
        {
            return recorder.BuildExecution(ProcedureExecutionOutcome.Cancelled("Execution cancelled."), null);
        }
        catch (OperatorExecutionFailureException ex)
        {
            return recorder.BuildExecution(
                ProcedureExecutionOutcome.Failed("operator_execution_failed", ex.Summary),
                ex.Diagnostics);
        }
        catch (Exception ex)
        {
            return recorder.BuildExecution(
                ProcedureExecutionOutcome.Failed("unexpected_exception", ex.Message),
                ex);
        }
    }

    private StepRegistry BuildRegistry()
    {
        if (_module is not IModuleStepHandlerRegistrar registrar)
            throw new InvalidOperationException($"Module '{_module.GetManifest().Id}' does not register step handlers.");

        var registry = new StepRegistry();
        registrar.RegisterStepHandlers(registry);
        return registry;
    }

    private object? ResolveSlot(string slotName)
    {
        if (!_slots.TryGetValue(slotName, out var value))
            return null;

        if (value is Func<object?> factory)
            return factory();

        if (value is Func<IDbConnection> dbFactory)
            return dbFactory();

        return value;
    }

    private static Dictionary<string, object?> SeedParameters(
        IReadOnlyDictionary<string, object?>? input,
        IntegrationExecutionIdentity identity)
    {
        var parameters = input is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(input, StringComparer.Ordinal);

        parameters["__appertureRunId"] = identity.RunId;
        if (!string.IsNullOrWhiteSpace(identity.CorrelationId))
            parameters["correlationId"] = identity.CorrelationId;
        return parameters;
    }

    private sealed class StepRegistry : IStepHandlerRegistry
    {
        private readonly ConcurrentDictionary<string, IStepHandler> _handlers = new(StringComparer.Ordinal);

        public void Register(string moduleId, string stepName, IStepHandler handler) =>
            _handlers[$"{moduleId}:{stepName}"] = handler;

        public IStepHandler? Get(string moduleId, string stepName) =>
            _handlers.TryGetValue($"{moduleId}:{stepName}", out var handler)
                ? handler
                : null;

        public IStepHandler Resolve(string moduleId, string stepName) =>
            Get(moduleId, stepName)
            ?? throw new InvalidOperationException($"No step handler is registered for '{moduleId}:{stepName}'.");
    }

    private sealed class TestExecutionRecorder
    {
        private readonly RecordingPlatformEvents _events;
        private readonly RecordingOperationalLogs _logs;
        private readonly RecordingTelemetry _telemetry;
        private readonly InMemoryOperationItems _operationItems;

        public TestExecutionRecorder(
            ModuleManifest manifest,
            IntegrationExecutionIdentity identity,
            TestTimeProvider timeProvider,
            Func<string, object?> slotResolver)
        {
            _events = new RecordingPlatformEvents(manifest, timeProvider);
            _logs = new RecordingOperationalLogs();
            _telemetry = new RecordingTelemetry();
            _operationItems = new InMemoryOperationItems(identity, timeProvider);
            Context = new TestStepExecutionContext(identity, _events, _logs, _telemetry, _operationItems, slotResolver);
        }

        public TestStepExecutionContext Context { get; }

        public IntegrationProcedureExecution BuildExecution(ProcedureExecutionOutcome outcome, object? result) =>
            new(
                Context.Identity,
                outcome,
                result,
                _events.Items,
                _logs.Items,
                _operationItems.Groups,
                _operationItems.Items);
    }

    private sealed class TestStepExecutionContext : IStepExecutionContext
    {
        private readonly Func<string, object?> _slotResolver;

        public TestStepExecutionContext(
            IntegrationExecutionIdentity identity,
            IModulePlatformEventEmitter events,
            IModuleOperationalLogger logs,
            IModuleTelemetry telemetry,
            IOperationItemWriter operationItems,
            Func<string, object?> slotResolver)
        {
            Identity = identity;
            _slotResolver = slotResolver;
            Signals = new TestSignals(this, events, logs, telemetry, operationItems);
            PlatformEvents = events;
        }

        public IntegrationExecutionIdentity Identity { get; }
        public string ModuleId => Identity.ModuleId;
        public string? IntegrationId => Identity.IntegrationId;
        public string? RunId => Identity.RunId;
        public string? ProcedureId => Identity.ProcedureId;
        public string? ScheduleId => Identity.ScheduleId;
        public string? CorrelationId => Identity.CorrelationId;
        public string? CausationId => Identity.RunId;
        public IStepExecutionSignals Signals { get; }
        public IModulePlatformEventEmitter PlatformEvents { get; }

        public ValueTask<T?> ResolveSlotAsync<T>(string slotName, CancellationToken cancellationToken = default)
            where T : class =>
            ValueTask.FromResult(_slotResolver(slotName) as T);
    }

    private sealed class TestSignals : IStepExecutionSignals
    {
        public TestSignals(
            IModulePlatformEmitContext correlation,
            IModulePlatformEventEmitter events,
            IModuleOperationalLogger logs,
            IModuleTelemetry telemetry,
            IOperationItemWriter operationItems)
        {
            Correlation = correlation;
            Events = events;
            Logs = logs;
            Telemetry = telemetry;
            OperationItems = operationItems;
        }

        public IModulePlatformEventEmitter Events { get; }
        public IModuleOperationalLogger Logs { get; }
        public IModuleTelemetry Telemetry { get; }
        public IModulePlatformEmitContext Correlation { get; }
        public IOperationItemWriter OperationItems { get; }
    }

    private sealed class RecordingPlatformEvents : IModulePlatformEventEmitter
    {
        private readonly ModuleManifest _manifest;
        private readonly TestTimeProvider _timeProvider;
        private readonly List<CapturedPlatformEvent> _items = [];

        public RecordingPlatformEvents(ModuleManifest manifest, TestTimeProvider timeProvider)
        {
            _manifest = manifest;
            _timeProvider = timeProvider;
        }

        public IReadOnlyList<CapturedPlatformEvent> Items => _items;

        public ValueTask<ModulePlatformEventEmitResult> EmitAsync(
            IModulePlatformEmitContext context,
            string signalName,
            object? payload,
            string? severityOverride = null,
            CancellationToken cancellationToken = default)
        {
            var signal = _manifest.Signals?.FirstOrDefault(x => string.Equals(x.Name, signalName, StringComparison.Ordinal));
            var type = $"apperture.module.{context.ModuleId.ToLowerInvariant()}.{signalName.Replace('_', '.').ToLowerInvariant()}";
            _items.Add(new CapturedPlatformEvent(
                type,
                signalName,
                payload,
                _timeProvider.GetUtcNow(),
                context.ModuleId,
                context.IntegrationId,
                context.RunId,
                context.ProcedureId,
                context.ScheduleId,
                context.CorrelationId));

            return ValueTask.FromResult(new ModulePlatformEventEmitResult(
                Emitted: true,
                EventId: Guid.NewGuid().ToString("N"),
                SkipReason: null,
                SkippedBecauseDisabled: false));
        }
    }

    private sealed class RecordingOperationalLogs : IModuleOperationalLogger
    {
        private readonly List<CapturedOperationalLog> _items = [];

        public IReadOnlyList<CapturedOperationalLog> Items => _items;

        public void LogOperational(OperationalLogLevel level, string code, string message, IReadOnlyDictionary<string, object?>? data = null) =>
            _items.Add(new CapturedOperationalLog(level, code, message, data));
    }

    private sealed class RecordingTelemetry : IModuleTelemetry
    {
        public void RecordCounter(string name, double value = 1, IReadOnlyDictionary<string, string>? tags = null)
        {
        }

        public IDisposable MeasureDuration(string name, IReadOnlyDictionary<string, string>? tags = null) =>
            NullScope.Instance;

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose()
            {
            }
        }
    }

    private sealed class InMemoryOperationItems : IOperationItemWriter
    {
        private readonly IntegrationExecutionIdentity _identity;
        private readonly TestTimeProvider _timeProvider;
        private readonly List<OperationGroup> _groups = [];
        private readonly List<OperationItem> _items = [];

        public InMemoryOperationItems(IntegrationExecutionIdentity identity, TestTimeProvider timeProvider)
        {
            _identity = identity;
            _timeProvider = timeProvider;
        }

        public IReadOnlyList<OperationGroup> Groups => _groups;
        public IReadOnlyList<OperationItem> Items => _items;

        public ValueTask<OperationGroup> BeginGroupAsync(
            string operationKind,
            string? sourceLabel = null,
            string? summaryJson = null,
            string? metadataJson = null,
            CancellationToken cancellationToken = default)
        {
            var now = _timeProvider.GetUtcNow();
            var group = new OperationGroup(
                Guid.NewGuid().ToString("N"),
                _identity.RunId,
                _identity.IntegrationId,
                _identity.ModuleId,
                _identity.ProcedureId,
                operationKind,
                sourceLabel,
                OperationGroupStatuses.Running,
                0,
                0,
                0,
                0,
                now,
                null,
                summaryJson,
                metadataJson,
                now,
                now);
            _groups.Add(group);
            return ValueTask.FromResult(group);
        }

        public ValueTask<OperationItem> AppendItemAsync(AppendOperationItemRequest request, CancellationToken cancellationToken = default)
        {
            var now = _timeProvider.GetUtcNow();
            var item = new OperationItem(
                Guid.NewGuid().ToString("N"),
                request.OperationGroupId,
                request.RunId,
                request.Sequence,
                request.SubjectKey,
                request.DisplayLabel,
                request.Status,
                request.ErrorCode,
                request.ErrorMessage,
                request.IsRetryable,
                request.RetryCount,
                request.LastAttemptedAtUtc ?? now,
                request.CompletedAtUtc ?? now,
                request.ChangeSummaryJson,
                request.MetadataJson,
                now,
                now);
            _items.Add(item);
            return ValueTask.FromResult(item);
        }

        public ValueTask<OperationGroup> CompleteGroupAsync(CompleteOperationGroupRequest request, CancellationToken cancellationToken = default)
        {
            var existing = _groups.First(x => x.OperationGroupId == request.OperationGroupId);
            var updated = existing with
            {
                Status = request.Status,
                TotalCount = request.TotalCount,
                SucceededCount = request.SucceededCount,
                FailedCount = request.FailedCount,
                SkippedCount = request.SkippedCount,
                SummaryJson = request.SummaryJson ?? existing.SummaryJson,
                CompletedAtUtc = request.CompletedAtUtc ?? _timeProvider.GetUtcNow(),
                UpdatedAtUtc = _timeProvider.GetUtcNow(),
            };
            _groups[_groups.FindIndex(x => x.OperationGroupId == request.OperationGroupId)] = updated;
            return ValueTask.FromResult(updated);
        }
    }
}
