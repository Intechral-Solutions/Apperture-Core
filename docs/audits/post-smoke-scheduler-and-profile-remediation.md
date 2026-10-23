# Post-smoke remediation: scheduler audit correlation and the profile photo dialog

Status: implemented on `fix/post-smoke-scheduler-profile`, local, not pushed. Base: `origin/master` at `6175874` (2026-10-19).

An operational smoke test of the running application (a Management Host, the UI server and a WorkerHost against SQL Server) found two defects that were already on `master`, and one scheduling observation. This record covers the two corrections, the result of investigating the observation, and what was left alone.

## D-S1: scheduled dispatches wrote no audit row

**Symptom.** A scheduled fire ran its workflow to completion, but no `scheduler.schedule.dispatched` row existed, and the worker logged an Entity Framework `fail:` entry with a SQL error on every fire.

**Root cause.** `ScheduleDispatchRunner` correlated the audit rows of a fire with `schedule:{scheduleId}:{fireTimeUtc:O}`. The fire time is a `DateTimeOffset` with a zero offset, which formats as `...0000000+00:00`, so the identifier is 67 characters for a generated 24-character schedule id. `AuditLogs.CorrelationId` is `nvarchar(64)` (migration `20260916173420_Ctx3B_AuditCorrelationId`). SQL Server rejected every insert (error 2628), and the runner's `catch { /* best-effort audit */ }` swallowed it. The unit tests that cover the runner use an in-memory database, which does not enforce column length, so nothing caught it. The same identifier was used by every scheduler audit write: dispatched, blocked by lifecycle, blocked by readiness, failed, recovered claim and the legacy-schedule warning.

**Correction.**
- `ScheduleFireCorrelation.ForAudit(scheduleId, fireUtc)` builds the identifier as `schedule:{scheduleId}:{fire as a UTC instant, ISO 8601 round trip}`, for example `schedule:7dc8a20b735d41e7b3dbc386:2026-10-23T06:19:00.0000000Z` (62 characters). It is the same instant as the start request's idempotency key and begins with the run's own correlation key `schedule:{scheduleId}`, so the existing readers that look for that prefix keep working.
- If a schedule id is too long for the per-fire form, it falls back to the run-level form `schedule:{scheduleId}`; if that does not fit either it returns null, which the audit row records as no correlation (the schedule id, the run id and the fire stay in their own columns and details). It never truncates. This is the same rule the audit event producers already follow for identifiers the schema cannot hold.
- Every scheduler audit write in the runner takes its identifier from that one helper.
- The best-effort catches now log a warning naming the schedule and the fire instead of failing silently. The dispatch itself is still not made to depend on its audit row: an unrecorded row is observable, a dispatched run is not undone.

**What was deliberately not changed.**
- The column was not widened. I found no reader that filters audit rows by correlation identifier, the column is bounded on purpose, and the established contract for audit identifiers (the `BoundedId` rule in the audit event producers) is to bound them rather than widen the schema.
- The start request's idempotency key (`schedule:{id}:{fire:O}`, 67 characters) is unchanged. It is stored in a 128-character column and parsed by the run-origin classifier, and changing its format would change what a fire is called across an upgrade.
- Success and failure semantics of the dispatch are unchanged.

**Evidence.**
- `ScheduleFireCorrelationTests` (unit, 7 cases) pins the original 67-character form, the new form, normalisation of offsets, per-fire uniqueness, the two fallbacks, and a source guard that no interpolated `new CorrelationId($"...` remains in the runner.
- `ScheduleDispatchAuditSqlServerTests` runs the real runner, real repositories and the real audit service against SQL Server on a locking READ COMMITTED catalog and a READ_COMMITTED_SNAPSHOT catalog (5 cases, each on both). They failed first with the production error (`Truncated value: 'schedule:...:...0000000+00'`) and pass now:
  - a scheduled dispatch executes and its `scheduler.schedule.dispatched` row is persisted (actor `system:scheduler`, details only the schedule and run ids, correlation as above);
  - a recovered stale claim writes `claim_recovered`, the legacy-compatibility warning and `dispatched` under one identifier;
  - the next fire of a recurring schedule dispatches under its own identifier;
  - a refused dispatch (module disabled) is recorded under the same form;
  - when the audit service fails, the dispatch still succeeds, no audit row exists and each unrecorded row logs one warning.
- Runtime, on the corrected build against an isolated catalog: two fires of an every-minute schedule produced two `scheduler.schedule.dispatched` rows of 62 characters with distinct identifiers, both runs completed on the worker, no truncation error and no unrecorded-row warning was logged, and a failed scheduled run was retried (202, completed, same `schedule:{id}` correlation, linked to the original run).

**Related observation, not changed.** In the SQL harness a failed insert through `SqlAuditService` leaves the entity tracked in the shared `DbContext`, and the next `SaveChanges` in that scope retries it and throws (the first red run showed this surfacing as an exception from the cycle). With the overlength fixed this is no longer triggered by the scheduler, but any best-effort audit append that fails on a shared context can poison it. Recommended for AUD-1.6: detach the entity when the append fails.

## D-S2: the profile page threw while rendering, and the remove-photo dialog could not work

There were two defects in `AvatarPicker.razor`, the second hidden by the first.

1. **Unsupported parameter.** The picker passed `IsBusy` to `ConfirmActionDialog`, which declares no such parameter (it keeps its own busy state while its action runs). Blazor threw `InvalidOperationException ... does not have a property matching the name 'IsBusy'` while rendering, on every load of `/account/profile` and after every save. It was introduced with AUP-1 (2026-08-19). Correction: remove the argument. The dialog's own busy state already disables both buttons and ignores a second confirm while the action runs, and the picker's own controls are disabled by its own flag.
2. **The confirmation never closed.** With the exception gone, the real browser showed that after confirming, the photo was removed on the server but the dialog stayed open over a stale "Using your uploaded photo". The picker cleared its open flag inside the action, which the dialog runs, so the picker (the dialog's parent) was never told to render again. Every other caller of `ConfirmActionDialog` closes through `OnConfirmed`, an event callback whose receiver is the parent. Correction: close through `OnConfirmed`, as the others do, and no longer inside the action.

**Evidence.**
- `AvatarPickerRemovePhotoTests` (bUnit, 6 cases) renders the real picker: it renders without the exception, the confirmation opens with its consequence text, cancel changes nothing and does not call the API, confirm removes the photo once, applies the default illustration, shows the toast and closes; while the removal runs both dialog buttons are disabled and a second confirm does nothing; a failed removal keeps the dialog open with the message and a retry succeeds. The cases failed first with the production exception, and after the first correction three still failed because the dialog did not close.
- `Uix6InteractionSystemTests.Every_ConfirmActionDialog_usage_passes_only_parameters_the_dialog_declares` reads the dialog's declared parameters and checks every `<ConfirmActionDialog` usage in the UI (12 callers); it failed on the original `IsBusy` line and passes now, which also shows no other caller passes an argument the dialog does not declare.
- Real Chromium against the running UI on the corrected build: the profile page loads with no console error beyond the expected 401 probe, the picker renders, a photo can be uploaded, Remove photo opens the confirmation, Cancel leaves the photo, Confirm removes it and the dialog closes with the default illustration shown, and a reload keeps it removed.

## Schedule timing observation: the first fire preceded the schedule's creation

**Result: intended, documented behaviour (SWM-3), with a product-semantics caveat. Not changed.**

When a schedule has no recorded last attempt, `CronScheduleEvaluator` evaluates from `asOf - 5 minutes` (`FirstRunLookbackMinutes`) and takes the earliest occurrence not yet claimed. The decision record `Epic-SWM3-Schedule-Timezone-Fire-Time-Semantics` states this ("after a bounded first-run lookback"), and the evaluator's comment gives the reason: bounding a cold start so a high-frequency schedule does not claim a large historical window. It is measured from the dispatch cycle, not from when the schedule was created.

Reproduced twice on the running worker: an every-minute schedule created at 08:02 had its first fire at 07:58:00, and a probe schedule `1 8 * * *` created at 08:04:06 was dispatched at 08:04:12 for the instant 08:01:00, three minutes before it existed. After that first fire the last attempt is recorded as the dispatch time, so the missed minutes in between are not replayed. It is not a clock or time-zone discrepancy and not a test artefact.

Consequence: creating a schedule can run it once, at once, for an occurrence of up to five minutes earlier (a daily 09:00 job created at 09:03 runs immediately). Severity: low. It is a product decision rather than a correctness or security defect, so it is recorded and left to the owner. If it is ever wanted otherwise, the bounded change is to floor the first-run lookback at the schedule's creation time, which needs a persisted creation time the schedule definition does not currently carry. Recommended ownership: the scheduling design, not this repair.

## Other smoke observations, recorded and not changed

- **D-S3, fonts.** `Management.Ui.Client/wwwroot/index.html` (lines 8 to 10) and `Management.Ui.Server/Pages/_Layout.cshtml` (lines 18 to 20) load IBM Plex Mono from `fonts.googleapis.com` and `fonts.gstatic.com` on every page view. A privacy and offline-deployment decision (self-host the font or drop it); belongs with OPS-1 or the release review.
- **"Manage roles" link.** `Pages/Governance/UsersList.razor` (line 104) shows the link to every viewer of the list. `UserDetail.razor` gates the role controls on `CanAssignRoles`, and the server enforces it, so a read-only viewer lands on a page without controls. A wording and gating polish item for the Governance UI.
- **Package advisories.** `System.Security.Cryptography.Pkcs` (NU1903 high, GHSA-846f-f764-76mx; NU1902 moderate, GHSA-2fmv-rv2c-59x7) is transitive, never referenced directly. It arrives through `Microsoft.Data.SqlClient` 7.0.3 (direct reference in `Apperture.Bootstrap`, `Apperture.ConnectorResolution` and `Apperture.RuntimePlane.Dispatch.SqlServer`, resolving Pkcs 9.0.13) and through `Microsoft.Data.SqlClient` 6.1.6 under `Microsoft.EntityFrameworkCore.SqlServer` 10.0.12 (`Apperture.Persistence.SqlServer`, resolving Pkcs 9.0.11). The patched version was not looked up. No upgrade was made; the remedy is a SqlClient or EF Core update, or a transitive pin, as one dependency change in the release review.
- **Worker started before the first migration.** A worker started at the same moment as the Management Host against an empty catalog hit `Invalid object name 'PlatformSettingValues'` and exited. Starting it after the host has migrated avoids it (and the orchestration does). Not investigated further.
- **Release significance.** None of these blocks the corrections. OPS-1, SDK-8, IAM-11 and the other program gates are unchanged.

## Validation

Environment: .NET SDK 10.0.402; SQL Server 2022 container `apperture-test-sql`; Chromium (Playwright 1.63 driver) headless.

Automated, on the final code (no failures, no skips):

| Suite | Result |
|---|---|
| Unit | 5,228 passed (includes the 7 new correlation cases) |
| Management UI client (bUnit) | 529 passed (includes the 6 new picker cases) |
| Management UI shared (source and surface) | 1,497 passed (includes the new dialog-parameter guard) |
| Architecture | 38 passed |
| SQL Server: the new scheduler audit class (10 cases, both isolation modes) plus the neighbouring scheduler, audit recorder, append bridge, timeline, correlation, ownership-move and runtime seam classes | 99 passed |

Not run: the full integration suite, the Browser suite and the full SQL suite (the changes touch the scheduler runner's audit writes and two UI components only). The new SQL cases fail rather than skip when SQL Server is absent, and they were seen failing against it before the fix.

Runtime (separate from the automated evidence): the corrected hosts ran against a throwaway catalog and login on the test SQL server, dropped afterwards, with all generated secrets kept out of retained logs (scanned; the only hit is the deliberate dummy credential inside a script). Scheduled dispatch, recurring fires, retry of a scheduled run, and the profile-photo flow in a real browser all behaved as described above. The final shutdown of the three hosts did not take the graceful route and the processes were stopped by exact process id; an earlier restart in the same session shut down gracefully.

Limitations: Windows only; Chromium only; the test worktree has a `.git` file rather than a directory, and a number of source-guard tests locate the repository by a `.git` directory, so the suites were run from a shim directory with a dummy `.git` and junctions to the worktree (without it several source-guard tests in the UI client and UI shared suites fail for that reason alone, before and after the change).

## Integration with the IAM-10.2b branch

These changes touch `ScheduleDispatchRunner.cs`, a new `ScheduleFireCorrelation.cs`, `AvatarPicker.razor` and test files. The IAM-10.2b branch (`05b833a`) is based on the same `origin/master` and does not touch those files, so the two should not conflict.

Suggested order: review and merge this branch to `master` independently (it is small and unrelated to IAM-10.2b); then bring `master` into the IAM-10.2b branch with the owner's approval, before WP4B-D, so that the full integration and Browser runs that the IAM merge requires include the new SQL cases. Nothing here changes an IAM-10.2b contract, and the IAM design records were not edited.
