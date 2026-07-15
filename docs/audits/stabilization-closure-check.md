# Stabilization Closure & Foundation Baseline

- **Date:** 2026-07-14 (hygiene patch applied same day; framing updated 2026-07-15 under PFB-1)
- **Status:** **Closed** — S1–S13 foundation track
- **Naming:** Stabilization closeout is **not** Epic 121. Epic 121 is already used for HelloWorld demo notification seed + Sample.HelloWorld demo integration settings.
- **Scope of hygiene patch:** unit-test emitter fix, docs/config truth, this note. No features, no scheduling implementation, no Epic 69 removal.
- **Framing:** This closes **stabilization**, not a product release. Later SWM-1–SWM-12 closed scheduling/worker **foundation**. Current product phase is **pre-release product development** — see [`pfb1-product-foundation-baseline-audit.md`](pfb1-product-foundation-baseline-audit.md) and [`../00-overview/pre-release-product-roadmap.md`](../00-overview/pre-release-product-roadmap.md). Do not treat this as “v1 / release readiness.”

---

## Closure statement

**Stabilization S1–S13 is closed.**

Foundation baseline (operator/on-prem-dev operable — not a finished release):

- Operator shell (auth, dashboard, integrations, runs/schedules triage, procedure launch)
- **Platform notification pipeline** (`UsePlatformPipeline` on in Management.Host committed config)
- Manual email delivery **confirmed**
- HelloWorld `demo.completed` E2E **verified**: emit → S13A poller → instance → inbox/outbox → SMTP

Legacy Epic 69: **retained temporarily** with S12 removal criteria (escalation gap). Prefer platform; do not dual-send.

Scheduling Aspire timer plane mismatch: **resolved under SWM-1–SWM-12** (not a stabilization reopen).

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

## Next track (updated after SWM + PFB-1)

Stabilization closed → **Scheduling & Worker Management (SWM-1–SWM-12) completed** as foundation.

| | |
|--|--|
| **Current planning stage** | Pre-release product development |
| **Recommended next implementation track** | **SDK & Demo Module Validation** |
| **Canonical docs** | [`pfb1-product-foundation-baseline-audit.md`](pfb1-product-foundation-baseline-audit.md), [`../00-overview/pre-release-product-roadmap.md`](../00-overview/pre-release-product-roadmap.md) |

Do **not** treat the next step as Final Release Readiness / RC.

---

## Related

- Product foundation baseline: [pfb1-product-foundation-baseline-audit.md](pfb1-product-foundation-baseline-audit.md)
- Pre-release product roadmap: [../00-overview/pre-release-product-roadmap.md](../00-overview/pre-release-product-roadmap.md)
- Scheduling audit (historical): [scheduling-runtime-audit.md](scheduling-runtime-audit.md)
- HelloWorld notify ADR: [S13A-helloworld-notification-pipeline-debugging.md](../02-decisions/S13A-helloworld-notification-pipeline-debugging.md)
- Legacy removal plan: [S12-notification-pipeline-verification-legacy-removal-plan.md](../02-decisions/S12-notification-pipeline-verification-legacy-removal-plan.md)
