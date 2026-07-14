# Stabilization Closure & v1 Baseline

- **Date:** 2026-07-14 (hygiene patch applied same day)
- **Status:** **Closed** — S1–S13 product track
- **Naming:** Stabilization closeout is **not** Epic 121. Epic 121 is already used for HelloWorld demo notification seed + Sample.HelloWorld demo integration settings.
- **Scope of hygiene patch:** unit-test emitter fix, docs/config truth, this note. No features, no scheduling implementation, no Epic 69 removal.

---

## Closure statement

**Stabilization S1–S13 is closed.**

Product baseline:

- Operator shell (auth, dashboard, integrations, runs/schedules triage, procedure launch)
- **Platform notification pipeline** (`UsePlatformPipeline` on in Management.Host committed config)
- Manual email delivery **confirmed**
- HelloWorld `demo.completed` E2E **verified**: emit → S13A poller → instance → inbox/outbox → SMTP

Legacy Epic 69: **retained temporarily** with S12 removal criteria (escalation gap). Prefer platform; do not dual-send.

Scheduling Aspire timer plane mismatch: **not** a stabilization blocker — moves to next track.

---

## Hygiene patch results

### Unit test

`HelloWorldWorkflowTruthfulnessTests` no longer uses the 2-arg `DefaultWorkflowExecutor` (Null emitter). It wires `RecordingModulePlatformEventEmitter` and asserts `demo.completed`. S13B fail-loud Complete behavior is unchanged.

### Integration tests (environment)

Closure validation stopped Aspire hosts; integration tests then targeted a **stale** Aspire SQL endpoint (`127.0.0.1,58361`) and failed with connection refused. That is an **environment** issue, not a product defect.

**To rerun integration tests cleanly:**

1. Start Aspire (preferred) or native docker SQL:
   - Aspire: `dotnet run --project src/Apperture.AppHost/Apperture.AppHost.csproj`  
     SQL host port defaults to **14333** (`Aspire:SqlServer:HostPort` in AppHost `appsettings.json`).
   - Or native: `.\scripts\Manage.ps1 -Area dev -Command start` (compose SQL on **1433**).
2. Point tests at a live SQL instance, e.g. set `APPERTURE_TEST_SQL_CONNECTION_STRING` (or the Aspire-injected connection string variables your harness reads) to the **current** endpoint — do not reuse a closed Aspire ephemeral port.
3. `dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj`

### Docs/config truth

Corrected: S9 opt-out claim; cutover defaults vs Host baseline; Aspire SQL **14333**; `ShellUi:ManagementHostUrl` on Management.Host for email; Epic 121 naming (HelloWorld only — allocate a new id for next production integration settings).

---

## Next track

| | |
|--|--|
| **Track** | Scheduling & Worker Management |
| **First epic** | Runtime-plane identity alignment under Aspire (`local` vs `worker-local`) |
| **Evidence** | `docs/audits/scheduling-runtime-audit.md` |

---

## Related

- Scheduling audit: [scheduling-runtime-audit.md](scheduling-runtime-audit.md)
- HelloWorld notify ADR: [S13A-helloworld-notification-pipeline-debugging.md](../02-decisions/S13A-helloworld-notification-pipeline-debugging.md)
- Legacy removal plan: [S12-notification-pipeline-verification-legacy-removal-plan.md](../02-decisions/S12-notification-pipeline-verification-legacy-removal-plan.md)
