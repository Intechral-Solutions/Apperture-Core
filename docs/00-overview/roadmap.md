# Roadmap

## How to read this document

Apperture is past a **foundation baseline** (Stabilization + Scheduling & Worker Management). It is **not** release-ready.

| Stage | Meaning | Status |
| ----- | ------- | ------ |
| Foundation / Stabilization | Reliability floor, auth, notifications, operator shell triage | **Complete** (S1–S13) |
| Scheduling & Worker Management foundation | Dispatch, TZ, heartbeat observe, schedule readiness, lifecycle quarantine | **Complete** (SWM-1–SWM-12) |
| Pre-release product development | Major product tracks (UI, demos, systems model, business data, Azure, etc.) | **Active.** Current next: **LFC-1** — see [`pre-release-product-roadmap.md`](pre-release-product-roadmap.md) |
| Final release readiness | Packaging, RC validation, security, ship docs | **Deferred** until major product tracks land |

Do **not** use “release candidate,” “v1 ready,” or “release-ready” for the current foundation baseline. See:

- [`pfb1-product-foundation-baseline-audit.md`](../audits/pfb1-product-foundation-baseline-audit.md)
- [`pre-release-product-roadmap.md`](pre-release-product-roadmap.md)

## Hosting strategy

**Azure hosting comes after a solid on-prem/dev foundation** — that foundation now exists for continued product work. Clean interfaces remain essential: workflow semantics, persistence, scheduling, and telemetry stay behind contracts so an Azure/Durable Functions adapter can plug in without coupling to on-prem specifics. Azure is a **pre-release product track**, not the current “almost shipped” path.

Worker process lifecycle remains **externally managed** (SWM-9/12). Do not restore Management-owned start/stop as the default hosting story.

## Completed foundation phases

1. **Documentation and repo foundation** — Complete. Docs, AI guidance, git config.
2. **Solution skeleton** — Complete. Projects, boundaries, dependency rules; compilable solution with arch tests.
3. **Golden path (on-prem / Aspire)** — Foundation delivered. HelloWorld module, DTFx adapter, WorkerHost, Management API/UI, settings, auth, integrations. Stabilization S1–S13 closed.
4. **Scheduling & worker management foundation** — Complete (SWM-1–SWM-12). SQL claim-based schedule dispatch + Cronos (not Quartz), plane identity, fire-time/TZ semantics, display TZ, heartbeat multi-host observe, next-fire/readiness, scheduler recovery ops. Lifecycle adapters deferred; quarantine in place.
5. **Notifications / email foundation** — Stabilized. Platform notification pipeline preferred; Epic 69 legacy removed (NTF-C, 2026-09-10).
6. **CLI / ops scripts** — Partial. PowerShell module and operational commands exist; expansion continues with product needs.

## Pre-release product development (next)

Canonical current-state track list and near-term order: [`pre-release-product-roadmap.md`](pre-release-product-roadmap.md).

**Current next (2026-09-29):** **LFC-1 — Connection/Schedule lifecycle cleanup.** IAM-8 and future
navigation/grouping remain not started. SEC-1 engineering is merged; remaining SEC-1 environment-dependent
smoke is pre-release/UAT, not a development blocker. **LFC-1.0 design gate is written. LFC-1.1 (schedule hard-delete), LFC-1.2 (schedule detail delete UI), and LFC-1.3 (retired connection copy) are implemented. Planned implementation slices are complete; epic closure has not been run** — [`lfc1-design-gate.md`](../01-architecture/transitions/connection-schedule-lifecycle/lfc1-design-gate.md).

Highlights (thematic tracks, not the current execution order):

- Product Experience & UI Overhaul
- User Profile & Preferences
- SDK & Demo Module Validation
- Systems / Applications / Services Model
- Business Data & Cross-Integration Records
- Job Operation Itemization & Retry
- Azure / Deployment Foundation
- Module UI / configuration / template foundations

~~**Recommended next implementation track (PFB-1):** SDK & Demo Module Validation.~~
~~**SDK-3A in progress / foundation** — durable OperationGroup/Item store + writer (see design audit). **Next after 3A: SDK-3B read APIs.**~~

Those two lines are **historical (PFB-1 era)** and must not be used for scheduling. SDK-3A–3D, SDK-4*, SDK-5*, SDK-6*, SDK-7, JOB-1, CTX, Components, Services, and SEC-1 have since closed. See the pre-release roadmap **Current next** block.

## Deferred until after major product work

7. **Final Release Readiness** — Packaging/installers, smoke automation, security review, release docs, RC validation. **Not** the current phase.
8. **Worker lifecycle productization** — Optional future epic; foundation explicitly deferred Management process control.

Epic 69 legacy removal is **complete** (removed by NTF-C, 2026-09-10 — see `stabilization-closure-check.md`); it previously appeared here as still-deferred, which was stale.

## Historical note

Older drafts of this file listed Quartz-backed scheduling and “stabilization toward v1.” Those are outdated. Scheduling foundation is the SWM SQL/Cronos path; “v1 / release readiness” language was premature and is replaced by the foundation → product → final release staging above.
