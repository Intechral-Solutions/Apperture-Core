# Epic 66 — Schedule lifecycle evidence and grace correctness

## Status

Accepted — implemented.

## Context

Epic 65 centralized expected-run evaluation in `ScheduleExpectationEvaluator` using cron/timezone-aware due windows and Epic 64 schedule-origin satisfaction. Grace was still anchored only on **integration creation** (`SilenceGraceAfterIntegrationCreate`). The platform could not honestly explain or suppress silence attention when a schedule was recently enabled, disabled, or materially edited.

## Decision

### 1. Persisted lifecycle evidence (Option A)

Add three nullable `datetimeoffset` columns on `ScheduleDefinitions`:

| Column | Meaning |
|--------|--------|
| `LastScheduleEnabledAtUtc` | Last transition to enabled, or initial create when `Enabled = true` |
| `LastScheduleDisabledAtUtc` | Last transition to disabled |
| `MaterialScheduleSemanticsChangedAtUtc` | Last change to **cron** or **timezone** |

Grace anchor at evaluation time is `max(LastScheduleEnabledAtUtc, MaterialScheduleSemanticsChangedAtUtc)` per qualifying schedule (nulls ignored). No separate denormalized `GraceAnchorAt` column.

**Pre-Epic-66 rows:** all columns `NULL`. Schedules contribute **no** lifecycle anchor; grace falls back to integration-create 48h only (Epic 63/65 behavior). This is explicit and honest.

### 2. Write semantics

Implemented in `ScheduleLifecycleEvidenceApplier` and invoked from `SqlScheduleManagementService` (with injected `TimeProvider`) on create, `UpdateScheduleAsync`, `EnableAsync`, and `DisableAsync`.

- **Create, enabled:** set `LastScheduleEnabledAtUtc` and `MaterialScheduleSemanticsChangedAtUtc` to now.
- **Create, disabled:** set `LastScheduleDisabledAtUtc` to now; do not set material baseline.
- **Enable (false→true):** set `LastScheduleEnabledAtUtc` to now.
- **Disable (true→false):** set `LastScheduleDisabledAtUtc` to now.
- **Material change** (`ScheduleMaterialSemanticsComparer`): recurrence or timezone string differs → set `MaterialScheduleSemanticsChangedAtUtc` to now.
- **Non-material edits** (e.g. name): no lifecycle timestamp changes.
- **Idempotent:** no transition and no material change → previous lifecycle unchanged.

`IScheduleRepository.UpdateDefinitionAsync` only writes lifecycle columns when `ScheduleDefinition.Lifecycle` is non-null; otherwise existing DB values are preserved.

### 3. Evaluator (Epic 65 extension)

- **Schedule aggregate grace:** among **enabled qualifying** schedules (same filter as Epic 65), take the **maximum** in-window lifecycle anchor. If any such schedule is in grace, the integration row is in schedule grace (conservative; avoids false “missing run” when any schedule recently changed).
- **Combined grace:** integration-create grace **or** schedule lifecycle grace (same 48h duration; `SilenceGraceAfterScheduleLifecycleAnchor` aliases the integration constant for clarity).
- **Winning explanation:** among active grace contributors, the **latest** anchor wins for `GraceReason` and `GraceAnchorUtc`.
- **Per-schedule reason:** if `max(enable, material)` ties at the same instant → `RecentlyEnabled`; if material timestamp is strictly later → `RecentlyMateriallyChanged`.
- **Disabled explanation:** when there are workflow/operation-shaped schedules for the integration but **all** are disabled → `NoExpectation` with operator line: “No run was due because the schedule is disabled.”

Epic 64 satisfaction and Epic 62 operational run boundaries are unchanged.

### 4. Fleet / API / UI

`ScheduleExpectationEvaluation` exposes `GraceReason` and `GraceAnchorUtc`. Fleet projections and `IntegrationFleetExpectedRunAttentionDto` include `ExpectedRunGraceReason` and `ExpectedRunGraceAnchorUtc`. Management UI shows a compact grace anchor line when in grace.

## Relationship to Epics 63–65

- **63:** Integration-create grace remains; schedule evidence adds parallel anchors.
- **64:** Schedule-origin-only satisfaction unchanged.
- **65:** Evaluator remains the single source of expected-run truth; inputs extended with lifecycle on `ScheduleDefinition`.

## Non-goals

- Full event-sourcing or schedule diagnostics history UI.
- Changing Epic 62 last-run/failure semantics.
- Reconstructing exact historical lifecycle for rows that predate this migration.

## Consequences

Silence and grace judgments can be explained from **persisted** schedule lifecycle facts where present, with a documented fallback for legacy data.
