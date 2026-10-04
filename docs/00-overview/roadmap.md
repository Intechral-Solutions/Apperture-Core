# Roadmap

## How to read this document

Apperture is past a **foundation baseline** (Stabilization + Scheduling & Worker Management). It is **not** release-ready.

| Stage | Meaning | Status |
| ----- | ------- | ------ |
| Foundation / Stabilization | Reliability floor, auth, notifications, operator shell triage | **Complete** (S1–S13) |
| Scheduling & Worker Management foundation | Dispatch, TZ, heartbeat observe, schedule readiness, lifecycle quarantine | **Complete** (SWM-1–SWM-12) |
| Pre-release product development | Major product tracks (UI, demos, contexts, components, services, security, role lifecycle) | **Complete through IAM-8** (merged to `master`, 2026-09-30) |
| RC1 program | Identity, audit, authorization, navigation, actions, lists, module runtime, operability | **Current.** Program gate approved 2026-10-03; no implementation epic started — see [`rc1-program-gate.md`](../01-architecture/rc1-program-gate.md) |
| RC1 validation | Smoke automation, security review, docs, tag | After the RC1 program |

Do **not** use “release candidate,” “v1 ready,” or “release-ready” for the current baseline. RC1 is reached only through the RC1 program and its validation epic. See:

- [`pfb1-product-foundation-baseline-audit.md`](../audits/pfb1-product-foundation-baseline-audit.md)
- [`pre-release-product-roadmap.md`](pre-release-product-roadmap.md)

## Hosting strategy

**RC1 supports one production topology:** Windows Server, SQL Server, and the Management Host, Management UI Server and Worker Host as Windows Services. Azure and Docker or Linux production hosting are outside RC1 support and remain architectural goals.

**Azure hosting comes after a solid on-prem/dev foundation** — that foundation now exists for continued product work. Clean interfaces remain essential: workflow semantics, persistence, scheduling, and telemetry stay behind contracts so an Azure/Durable Functions adapter can plug in without coupling to on-prem specifics. Azure is a **pre-release product track**, not the current “almost shipped” path.

Worker process lifecycle remains **externally managed** (SWM-9/12). Do not restore Management-owned start/stop as the default hosting story.

## Completed foundation phases

1. **Documentation and repo foundation** — Complete. Docs, AI guidance, git config.
2. **Solution skeleton** — Complete. Projects, boundaries, dependency rules; compilable solution with arch tests.
3. **Golden path (on-prem / Aspire)** — Foundation delivered. HelloWorld module, SQL start-request dispatch with inline execution (the DTFx adapter is an unimplemented placeholder), WorkerHost, Management API/UI, settings, auth, integrations. Stabilization S1–S13 closed.
4. **Scheduling & worker management foundation** — Complete (SWM-1–SWM-12). SQL claim-based schedule dispatch + Cronos (not Quartz), plane identity, fire-time/TZ semantics, display TZ, heartbeat multi-host observe, next-fire/readiness, scheduler recovery ops. Lifecycle adapters deferred; quarantine in place.
5. **Notifications / email foundation** — Stabilized. Platform notification pipeline preferred; Epic 69 legacy removed (NTF-C, 2026-09-10).
6. **CLI / ops scripts** — Partial. PowerShell module and operational commands exist; expansion continues with product needs.

## Pre-release product development (next)

Canonical current-state track list and near-term order: [`pre-release-product-roadmap.md`](pre-release-product-roadmap.md).

**Current (2026-10-03):** the **RC1 program**. Its gate (RC-0) is complete and no implementation epic has started.
First bodies, on the product owner's instruction: IAM-9.0, QW-1 and LST-1.0. Contract:
[`rc1-program-gate.md`](../01-architecture/rc1-program-gate.md). IAM-8 and LFC-1 are complete and merged to `master`.
Workspaces are a Direction with implementation deferred; that direction supersedes the earlier cross-context
grouping item. Context-scoped IAM stays deferred. SEC-1 engineering is merged; its remaining
environment-dependent smoke is part of RC1 validation.

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

7. **Final Release Readiness** — Now defined as OPS-1 (packaging, install, upgrade, backup) and RCV-1 (smoke automation, security review, docs, validation) inside the RC1 program.
8. **Worker lifecycle productization** — Optional future epic; foundation explicitly deferred Management process control.

Epic 69 legacy removal is **complete** (removed by NTF-C, 2026-09-10 — see `stabilization-closure-check.md`); it previously appeared here as still-deferred, which was stale.

## Historical note

Older drafts of this file listed Quartz-backed scheduling and “stabilization toward v1.” Those are outdated. Scheduling foundation is the SWM SQL/Cronos path; “v1 / release readiness” language was premature and is replaced by the foundation → product → final release staging above.
