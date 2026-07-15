# Roadmap

## How to read this document

Apperture is past a **foundation baseline** (Stabilization + Scheduling & Worker Management). It is **not** release-ready.

| Stage | Meaning | Status |
| ----- | ------- | ------ |
| Foundation / Stabilization | Reliability floor, auth, notifications, operator shell triage | **Complete** (S1–S13) |
| Scheduling & Worker Management foundation | Dispatch, TZ, heartbeat observe, schedule readiness, lifecycle quarantine | **Complete** (SWM-1–SWM-12) |
| Pre-release product development | Major product tracks (UI, demos, systems model, business data, Azure, etc.) | **Upcoming / active next** |
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
5. **Notifications / email foundation** — Stabilized. Platform notification pipeline preferred; Epic 69 legacy retained until later cutover policy.
6. **CLI / ops scripts** — Partial. PowerShell module and operational commands exist; expansion continues with product needs.

## Pre-release product development (next)

Canonical track list: [`pre-release-product-roadmap.md`](pre-release-product-roadmap.md).

Highlights:

- Product Experience & UI Overhaul
- User Profile & Preferences
- SDK & Demo Module Validation
- Systems / Applications / Services Model
- Business Data & Cross-Integration Records
- Job Operation Itemization & Retry
- Azure / Deployment Foundation
- Module UI / configuration / template foundations

**Recommended next implementation track (PFB-1):** SDK & Demo Module Validation.  
**SDK-2 implemented** — `Sample.FileImport` (see [`docs/superpowers/specs/SDK-2-Sample-FileImport-Demo.md`](../superpowers/specs/SDK-2-Sample-FileImport-Demo.md)). **Next epic: SDK-3 Itemized Operation Model.**

## Deferred until after major product work

7. **Final Release Readiness** — Packaging/installers, smoke automation, security review, release docs, RC validation. **Not** the current phase.
8. **Worker lifecycle productization** — Optional future epic; foundation explicitly deferred Management process control.
9. **Epic 69 legacy removal** — After platform parity and cutover sign-off.

## Historical note

Older drafts of this file listed Quartz-backed scheduling and “stabilization toward v1.” Those are outdated. Scheduling foundation is the SWM SQL/Cronos path; “v1 / release readiness” language was premature and is replaced by the foundation → product → final release staging above.
