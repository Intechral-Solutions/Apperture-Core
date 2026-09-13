# Epic SWM-10 — Schedule Management Readiness & Next-Fire Clarity

**Status:** Delivered  
**Track:** Scheduling & Worker Management  
**Depends on:** SWM-3 (timezone), SWM-4/5 (display), SWM-6/7 (heartbeat), SWM-8 (ops hints)  
**Delivered:** 2026-07-14

## Contract (v1)

1. **Next fire is compute-only** — derived from cron + `ScheduleDefinitions.TimeZone` via `ScheduleTimeZoneResolver` + Cronos. **Not persisted.**
2. List and detail schedule APIs return additive clarity fields:
   - `nextFireUtc`
   - `scheduleTimeZoneId`
   - `healthStatus`
   - `statusReasons[]` (`code` + operator `message`)
3. Look-ahead window defaults to **32 days**; no occurrence in window → `nextFireUtc = null`.
4. Disabled schedules → `nextFireUtc = null`, `healthStatus = Inactive`.
5. Corrupt persisted timezone IDs fall back to UTC at evaluation time (SWM-3); write validation still rejects invalid create/update input.
6. Shared scheduler/worker heartbeat is fetched **once** per list/detail request (no per-row integration readiness lookups).
7. Catch-up policy is explained in `statusReasons` (`catch_up_policy`) and UI advanced help — **one due fire per scheduler cycle**; uniqueness remains `(ScheduleId, FireTimeUtc)`.

## Timezone model

| Clock | Purpose |
|-------|---------|
| Schedule time zone | Cron evaluation (SWM-3) |
| Operator display time zone | `TimestampDisplay` / SWM-4–5 formatting of UTC instants |

Next fire is always a normalized UTC `DateTimeOffset` (`Offset = 0`). UI never uses unlabeled `.ToLocalTime()`.

## Why next-fire is not persisted

`ScheduleStates.NextDueUtc` is not a reliable operator projection (often cleared by dispatch). Persisting next-fire would duplicate Cronos semantics and drift under timezone/DST edits. Projection on read keeps list/detail coherent with SWM-3 without a new storage model.

## Catch-up (operator-facing)

- Missed fires drain **one** unclaimed due fire per scheduler cycle.
- Duplicate prevention uses schedule id + UTC fire instant.
- A multi-minute backlog drains across cycles — **not** a duplicate-fire condition.
- Next-fire projection is “next cron occurrence after now,” not the catch-up drain order.

## UI

- **Schedules list:** next fire, health pill, concise primary status reason; catch-up help in advanced section.
- **Schedule detail:** next fire + cron timezone near top; why-not-firing hints (SWM-8); catch-up in advanced help.
- **Integration detail schedules:** loads enriched integration schedules when available; shows next fire when present.

## Limitations / deferred

- Platform calendar and date-period scheduling — later epic.
- Full schedule designer — deferred.
- Multi-host worker inventory — deferred (SWM-9 track).
- Integration **runnable** status on list — not loaded (avoid N+1); use last outcome / missing integration / claim / heartbeat instead.
- Look-ahead beyond 32 days returns null next-fire (operators still see cron + diagnostics).

## Status reason codes

`disabled`, `not_due_yet`, `stale_claim`, `scheduler_heartbeat_unknown`, `scheduler_heartbeat_stale`, `last_failed`, `missing_integration`, `catch_up_policy`.

## Implementation anchors

- `ScheduleNextFireCalculator`, `ScheduleReadinessClassifier`, `ScheduleClarityProjector`
- `SchedulesController` / `IntegrationSchedulesController` (list/detail enrichment)
- `RunScheduleOperatorUiHelpers.ScheduleCatchUpHelpText`
