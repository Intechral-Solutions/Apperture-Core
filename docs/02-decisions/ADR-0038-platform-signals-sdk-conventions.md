# ADR-0038: Platform Signals SDK conventions

## Status

Accepted

## Context

Epic 77 delivered durable **platform events**, notification definitions, and a first module-facing emitter (`IModulePlatformEventEmitter` on `IStepExecutionContext.PlatformEvents`). Operational logging and telemetry interfaces exist but are unevenly surfaced: handlers often use raw `ILogger`, telemetry is a no-op, and `ModuleTestHarness` is a stub.

ADR-0037 defines the platform-wide signal taxonomy and notification system. Module authors need a **stable, documented contract** for when to emit events vs write logs vs record telemetry, how correlation works, and what can trigger notifications—without conflating audit, diagnostics, or workflow failure semantics.

This ADR does **not** implement runtime code; it is the accepted design direction for Epic 103A and subsequent Signals epics (see [Epic103A implementation plan](./Epic103A-Platform-Signals-SDK-Stabilization-Implementation-Plan.md)).

## Decision

### 1. Module author contract (mental model)

| Surface | Meaning | Durable? | v1 notification trigger? | Workflow impact |
|---------|---------|----------|---------------------------|-----------------|
| **Platform event** | Instance-scoped **fact** with typed payload | Yes (`PlatformEvents`) | **Yes** (primary input) | Emit is **fail-fast** when platform events are enabled and emitter is registered (see §6) |
| **Operational log** | Correlated **diagnostics** explaining what happened | No (log sinks) | **No** | **Best-effort**; must not fail the step |
| **Telemetry** | Optional **measurement** (counters, timings) | No SQL table in v1 | **No** | **Best-effort**; no-op default |
| **Audit** | Governance evidence of **privileged actions** | Yes (`AuditLogs`) | Opt-in platform types only | Separate API; not module diagnostics |
| **Execution contract** | Operator run outcome (`OperatorExecutionResult`) | Yes (execution store) | Indirect via platform lifecycle events | **Canonical** workflow success/failure |

```text
Events   = durable facts with business/platform meaning.
Logs     = correlated diagnostics explaining what happened.
Telemetry = best-effort measurement over time.
Audit    = governance evidence, not module diagnostics.
Notifications (v1) = driven by durable platform events (and incident transitions where wired).
```

Modules **must not** call SMTP, notification orchestrators, or `IAuditService` for operational outcomes.

### 2. When to use each surface

#### Emit a platform event when

- An outcome is a **durable fact** operators or automation should query later (timelines, rules, subscriptions).
- The module declares the signal in `ModuleManifest.Signals` and may ship built-in notification definitions keyed to that type.
- The fact has stable business meaning beyond a single log line (sync failed, threshold breached, certificate expiring).

#### Write an operational log when

- Explaining **how** work proceeded (progress, retries, row-level skips).
- Volume is high or content is diagnostic (not a notification trigger in v1).
- Details should be correlated to run/integration/module but need not be persisted as platform events.

#### Record telemetry when

- Capturing **metrics or timings** for dashboards/SRE (loss acceptable).
- Measurement is optional and must not drive operator alerts in v1.

#### Do not use signals for

- Routine workflow failure declaration → use `OperatorExecutionResult.Fail*` / exceptions first.
- Privileged admin actions → platform audit / governance APIs.
- Replacing workflow state or introducing message-bus-style orchestration.

### 3. Platform-owned vs module-owned events

| Owner | `source` | Who emits | Examples |
|-------|----------|-----------|----------|
| **Platform** | `/platform/{domain}` | Control plane or runtime observers | `apperture.schedule.expected_run.missing`, `apperture.execution.run.failed` |
| **Module** | `/modules/{normalizedModuleId}` | Module step via SDK | `apperture.module.sample.helloworld.demo.completed` |

**Boundaries:**

- **Platform** emits **cross-cutting lifecycle and readiness** facts (schedule expectation, run terminal state) that apply regardless of module implementation.
- **Modules** emit **domain facts** declared in manifest (`Signals`); undeclared signal names are rejected at emit time.
- Platform `apperture.execution.run.failed` / `.completed` (planned) describe **run terminal state**; modules may additionally emit domain-specific failure/completion signals—rules should not assume both for the same condition without explicit product design.
- Catalog entries marked **planned** or **future** are not available emitters until shipped (see [platform-signal-catalog.md](../01-architecture/platform-signal-catalog.md)).

### 4. Target SDK shape (preferred authoring)

Step handlers receive a **unified signal surface** on execution context. Modules **must not** resolve signal services from DI for step code.

```csharp
// Illustrative — names finalized in Signals-02 implementation
public interface IStepExecutionContext : IModulePlatformEmitContext
{
    IStepExecutionSignals Signals { get; }

    // Compatibility — delegates to Signals.Events; obsolete after migration window
    IModulePlatformEventEmitter PlatformEvents { get; }
}

public interface IStepExecutionSignals
{
    IModulePlatformEventEmitter Events { get; }
    IModuleOperationalLogger Logs { get; }
    IModuleTelemetry Telemetry { get; }
    IModulePlatformEmitContext Correlation { get; } // same fields as emit context
}
```

**Authoring preference:**

```csharp
await context.Signals.Events.EmitAsync(context.Signals.Correlation, "sync.failed", payload, ct);
context.Signals.Logs.LogOperational(OperationalLogLevel.Information, "sync.page", "Fetched page 3/10");
context.Signals.Telemetry.RecordCounter("records_processed", rows);
```

`context.PlatformEvents` remains valid during compatibility window; new code uses `context.Signals.Events`.

### 5. Correlation and runtime enrichment

`IModulePlatformEmitContext` / `Signals.Correlation` exposes read-only scope:

| Field | Set by runtime |
|-------|----------------|
| `ModuleId` | Workflow/operation definition |
| `IntegrationId` | Start request / binding scope |
| `RunId`, `ProcedureId`, `ScheduleId` | `WorkflowStepExecutionScope` |
| `CorrelationId`, `CausationId` | Scope (workflow idempotency key may map to causation) |

On **platform event** emit, the runtime also sets:

| Field | Rule |
|-------|------|
| `type` | `ModulePlatformEventTypes.Format(manifestId, signalName)` or platform constant |
| `source` | `/modules/{normalizedModuleId}` or platform path |
| `severity` | Manifest default unless override |
| `subject` | `{moduleId}:{signalName}[:{runId}]` for module events |
| `time`, `specversion`, `datacontenttype` | Emitter |

Authors pass **signal name + payload only**; they must not embed correlation ids in payload as the authoritative source.

**Operational logs v1:** runtime adds structured logging scope (`ModuleId`, `IntegrationId`, `RunId`, `ProcedureId`, `ScheduleId`, `CorrelationId`) on every `Logs` call.

### 6. Payload, redaction, and PII

- Manifest `RequiredDataProperties` lists required top-level JSON keys (camelCase).
- Max serialized event payload ~16 KB; oversize fails validation.
- No secrets, connection strings, tokens, or raw credential material in event `data`.
- Avoid raw PII blobs; prefer opaque ids and redacted excerpts in notification-bound fields.
- For errors: use stable `failureCode` + `failureMessageExcerpt` (≤500 chars) in catalog types; full `failureMessage` is not template-allowlisted.
- Log-derived platform events (future) follow catalog log pattern: excerpt only, HTML templates disallowed, default subscription disabled.

### 7. Event emission semantics (current and v1 target)

| Condition | Behavior |
|-----------|----------|
| `PlatformEvents:Enabled=false` | Skip emit; return skipped result; **do not fail** step |
| Module emitter not registered (`NullModulePlatformEventEmitter`) | Skip; **do not fail** step |
| Enabled + registered + validation failure | **Fail-fast** — exception fails step |
| Enabled + registered + SQL append failure | **Fail-fast** — exception fails step |

**v1 default:** treat declared, enabled production emits as **required for step success** when the author intentionally emits before returning success.

**Future (not v1):** optional `TryEmitAsync` / best-effort emit API for non-critical signals—deferred unless a later epic explicitly needs it (see implementation plan Signals-08).

**Platform-owned lifecycle events (Epic 103D):** `apperture.execution.run.completed` / `.failed` are emitted by `ExecutionLifecyclePlatformEventEmitter` **after** terminal run state is committed. Emission is **best-effort** (log warning on failure; do not change run status). Module-authored emits remain fail-fast when enabled.

### 8. Operational logger v1

| Property | Value |
|----------|-------|
| Implementation | `ILogger`-backed (`ModuleOperationalLogger` pattern) |
| API | `LogOperational(level, code, message, data?)` |
| Correlation | Automatic scopes from execution context |
| Durability | None — no operational log SQL table |
| Notifications | **None** in v1 |
| Failure | **Best-effort** — logging sink errors must not fail the step |

Stable `code` values: lowercase `{domain}.{action}`; human `message`; optional structured `data` dictionary (no secrets).

### 9. Telemetry v1

| Property | Value |
|----------|-------|
| Default | `NoOpModuleTelemetry` — safe to call |
| Semantics | Best-effort; drops when disabled |
| Storage | **No** `TelemetryEvents` SQL usage in v1 |
| Notifications | **None** |
| Future | OpenTelemetry adapter epic (Signals-07) |

### 10. ModuleTestHarness v1 (acceptance criteria)

Author-facing test package must support:

- Execute a module step with **fake** `IStepExecutionContext` / `IStepExecutionSignals`
- **Assert** appended platform events (type, severity, correlation, data)
- **Assert** operational log entries (level, code, message, scopes)
- **Optionally assert** telemetry via `RecordingModuleTelemetry`
- **Validate** declare-before-emit: undeclared signal name throws or fails validation

No requirement for SQL or notification pipeline in harness v1.

### 11. Recommended defaults

| Concern | Default |
|---------|---------|
| Platform events (enabled) | **Required / fail-fast** for intentional emits |
| Operational logs | **Best-effort** |
| Telemetry | **Best-effort / no-op** |
| Notifications | **Events only** (+ incident transitions where platform wires them) |
| Run lifecycle | **Platform** emits `execution.run.*` when shipped (Signals-04) |
| Domain outcomes | **Modules** emit declared manifest signals |
| Workflow terminal state | **`OperatorExecutionResult`** first; platform lifecycle events supplement |

### 12. Non-goals (Epic 103A and SDK stabilization track)

- Runtime implementation in Epic 103A (documentation only)
- OpenTelemetry adapter (Signals-07)
- Log-source or telemetry aggregate notification rules
- Rewrite of [module-authoring-guide.md](../05-modules/module-authoring-guide.md) until Signals-06 gates pass
- Mandatory DI-resolved step handler factory refactor (document as future pattern only in Signals-05)

## Consequences

### Positive

- Module authors get one mental model aligned with ADR-0037 and Epic 77 production behavior.
- Catalog status prevents mistaking planned types for shippable emitters.
- Unified `Signals` surface reduces DI leakage and documents correlation in one place.

### Negative

- Compatibility period for `PlatformEvents` vs `Signals.Events` duplication.
- Fail-fast emits couple notification infrastructure to step success until optional TryEmit exists.

### Neutral

- ADR-0037 remains the authority for notification definitions, templates, and delivery; this ADR narrows **module SDK** conventions only.

## References

- [ADR-0037](./ADR-0037-platform-signals-events-and-notification-system.md) — platform signal and notification platform
- [ADR-0022](./ADR-0022-declared-execution-result-contracts.md), [ADR-0023](./ADR-0023-execution-contract-author-guidance-and-validation-signals.md) — execution contracts
- [module-authoring-guide.md](../05-modules/module-authoring-guide.md) — module developer guide (signals, testing, notifications)
- [module-manifest-spec.md](../05-modules/module-manifest-spec.md) — `ModuleManifest.Signals` and validation
- [execution-contract-handler-patterns.md](../05-modules/execution-contract-handler-patterns.md) — copyable handler snippets
- [platform-signal-catalog.md](../01-architecture/platform-signal-catalog.md) — type catalog with implementation status
- [Epic103A implementation plan](./Epic103A-Platform-Signals-SDK-Stabilization-Implementation-Plan.md)
- [Epic 104](./Epic104-Module-Authoring-Documentation.md) — authoring guide and doc consistency (104A/104B)
- [Epic 77 plan](./Epic77-Platform-Signals-and-Notification-System-Implementation-Plan.md) — delivered notification foundation
