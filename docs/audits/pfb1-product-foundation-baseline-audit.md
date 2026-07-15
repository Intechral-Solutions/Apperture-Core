# PFB-1 — Product Foundation Baseline Audit

**Date:** 2026-07-15 (reframed from prior RR-1 draft)  
**Type:** Product foundation baseline audit (docs/planning; no product-feature implementation)  
**Tracks closed:** Stabilization S1–S13; Scheduling & Worker Management SWM-1–SWM-12  
**Supersedes:** `docs/audits/rr1-v1-release-readiness-audit.md` (that file is a pointer only)

### Framing (important)

- This audit measures whether Apperture is **foundation-ready to enter the next major product-development phase**.
- It does **not** mean the product is **release-ready**, a **release candidate**, or a finished **v1**.
- **Release readiness** is reserved for a **later final phase** after major pre-release product tracks land (UI overhaul, Azure path, systems/services model, business data, job itemization, more demos, etc.).
- Current operable baseline = **narrow foundation**: Aspire AppHost or Manage.ps1 single-instance on-prem/dev path — not a packaged product release.

---

## 1. Executive summary

**Foundation-ready for continued product development (not release-ready)**

After Stabilization S1–S13 and Scheduling & Worker Management SWM-1–SWM-12, the platform has a **strong durable foundation**:

- Operator can run Management + UI + WorkerHost via Aspire or Manage.ps1
- Bootstrap/auth, settings registry, integrations/connections, runs/schedules
- Platform notification/email path verified (S13); Epic 69 legacy retained carefully
- Schedule dispatch (SQL claim + Cronos), next-fire/readiness, multi-host heartbeat observe
- Worker lifecycle externally managed; Management `runtime-worker/start` quarantined (410)

That is enough to **proceed into major product work**. It is **not** enough to ship a releasable product.

Major pre-release product work remains (see §11 and `docs/00-overview/pre-release-product-roadmap.md`): full UI/product experience overhaul, Azure/cloud deployment path, user profile/preferences overhaul, additional demo modules, cross-integration/business data, itemized job operations + per-item retry, Application/System/Service model, module UI foundations — and only then Final Release Readiness.

---

## 2. Foundation readiness scorecard

Statuses below mean “fit for **foundation/product-dev**,” not “fit for **GA release**.”

| Area | Status | Evidence | Notes for product phase |
| ---- | ------ | -------- | ----------------------- |
| Build/test | Foundation OK with caveat | Unit suite ~**1190** passed; solution builds. Integration needs live SQL (`APPERTURE_TEST_SQL*`). Soft-skip patterns exist. NU1903 on OpenApi 2.0.0. | Keep CI/SQL honesty; expand coverage with new product tracks |
| Dev startup | Foundation OK with caveat | AppHost preferred; Manage.ps1 native/aspire; plane identity fixed (SWM-1). | One-stack-only operator hygiene still needed in docs |
| Deployment/on-prem packaging | Incomplete (expected) | Publish Management.Host only; WorkerHost packaging TODO; windows-service deploy scaffold. SWM-9/12: no lifecycle adapters. | Full packaging belongs in Azure/Deployment + Final Release Readiness — not now |
| Bootstrap/auth/account | Foundation OK | First-admin, account/password, admin Entra linking; break-glass off by default | Profile/preferences overhaul is a separate pre-release track |
| Operator shell/UI | Foundation OK; product UX incomplete | Primary menus exist; quarantine UX for worker start | **Full UI/product experience overhaul** still required |
| Integrations | Foundation OK with caveats | Detail, readiness, schedules, procedure launch; wizard progressive | Module UI / deeper config / business data still open |
| Runs/schedules | Foundation OK | Triage, next-fire/readiness (SWM-10), TZ (SWM-3–5), recover (SWM-8) | Itemized ops / per-item retry not yet a product surface |
| Worker runtime | Foundation OK with caveat | Heartbeat + multi-host observe (SWM-6/7/11); lifecycle deferred | Remains externally managed until a future lifecycle epic |
| Notifications/email | Foundation OK with caveat | Platform pipeline preferred; SMTP operator-configured; Epic 69 retained | Digests/plugins/escalation parity deferred |
| Platform settings/secrets | Foundation OK with caveat | Registry + UI; secret refs | Refine with profile/settings UX tracks |
| Documentation | Mixed | Strong SWM/ops ADRs; overview roadmap historically stale (fixed under PFB-1) | Keep product roadmap current; avoid premature RC language |
| Known deferred product | Deferred (intentionally) | Calendar, designer, lifecycle adapters, System/Service taxonomy, module UI, Azure prod hosting, WS installer, etc. | See pre-release product roadmap |

---

## 3. Foundation gaps that still matter (not “v1 blockers”)

These remain **honest constraints** on the operable foundation. Fixing them is hygiene / later packaging — they do **not** redefine this audit as release readiness.

### G1 — Incomplete packaging / Windows Service claims

- **Evidence:** `Publish-DotNetProjects.ps1` WorkerHost TODO; deploy windows-service scaffold.
- **Risk:** Operators confuse scaffold with product install.
- **Disposition:** Document supported hosts = AppHost/Manage.ps1 process model; packaging deferred to Azure/Deployment + Final Release Readiness tracks.

### G2 — Stale overview docs (addressed in PFB-1)

- **Evidence (historical):** `roadmap.md` claimed Quartz / outdated phase language.
- **Disposition:** Corrected under this pass; keep product roadmap as source of phase truth.

No other **foundation-function** gaps prevent starting the next product-development phase, assuming SMTP (for email demos) and SQL are configured per env.

---

## 4. Hygiene / foundation fixes (non-product)

Useful small fixes that support continued development — **not** a release-candidate pack:

| Title | Surfaces | Scope | Acceptance |
| ----- | -------- | ----- | ---------- |
| Roadmap / README truth | overview roadmap, README Status, pre-release product roadmap | XS–S | Foundation vs product vs final release language clear; no Quartz-as-current |
| Operator config checklist | management-platform-configuration or ops companion | S | Required vs recommended vs deferred for Aspire/Manage path |
| Deployment honesty | `deploy/README.md`, publish scripts messaging | XS | Scaffold vs supported hosts explicit |
| Windows appsettings template completeness | `appsettings.windows.json.example` | S | SMTP, ShellUi, ScheduleDispatch, pipeline hints |
| Integration test env reminder | tests README / CI notes | XS | Aspire SQL port **14333** default called out |
| OpenApi advisory | packages | XS | Upgrade or documented exception |
| Epic 69 residual labeling | diagnostics copy | XS | Platform preferred; Epic 69 retained |

---

## 5. Deferred product backlog (pre-release — not “do not block v1”)

These are **major remaining product tracks**, not optional polish for an imminent release:

- Full UI / product experience overhaul (shell, layout, progressive disclosure, role separation, visual pass)
- User profile & preferences overhaul (profile edit, avatar, display TZ UI, notification prefs)
- Additional demo modules (import, file, API poll, event, schedule-driven, cross-integration)
- Application / System / Service model + explorers
- Business data surfaces + cross-integration records / provenance
- Itemized job/run operations + retry/skip/ignore of failed items
- Module UI / configuration / template foundations
- Azure hosting model + production deployment path
- Digest / journal notifications; provider plugins; Priority; team subscriptions
- Worker lifecycle adapters / Management process control (still deferred from SWM-9)
- Calendar / schedule designer
- Packaging/installers, RC smoke automation, security review — **Final Release Readiness only**
- Epic 69 deletion (after platform parity + cutover policy)

---

## 6. Hidden / quarantined / deferred surfaces

| Surface | State | Misleading? |
| ------- | ----- | ----------- |
| `POST /api/control/recovery/runtime-worker/start` | **410** quarantined | No — UI/docs say do not use (SWM-9a/12) |
| `GET .../runtime-worker/status` | Quarantined body | No |
| Break-glass login | Disabled → **404** | No |
| Developer/Internal | Secondary nav | Ok if not sold as primary ops |
| Publish/deploy windows-service commands | Scaffold | **Risk** — must label incomplete |
| Epic 69 ledger / parity diagnostics | Legacy retained | Acceptable if labeled legacy |
| Calendar / designer / fleet control / systems model | Absent or partial | Do not advertise as done |

Primary operator nav does **not** advertise WorkerHost start/stop.

---

## 7. Foundation verification checklist (dev / on-prem operable path)

Use for foundation confidence — **not** a release-candidate gate.

1. **Start one stack only** — AppHost *or* Manage.ps1 native (not both).  
2. Confirm SQL up (Aspire volume `apperture-aspire-sql` / port from AppHost, default **14333**).  
3. Confirm Management API `:9000`, UI `:9001`, WorkerHost running.  
4. Migrations apply on Management.Host startup.  
5. Bootstrap / first admin (or DevTestUser from Development overlay).  
6. Local login works; Account → password change works.  
7. (Optional) Admin Entra tenant + link user; unlinked Entra does not grant access incorrectly.  
8. Platform settings / Host config: SMTP host, from, password secret/ref; `ShellUi:ManagementHostUrl` = UI origin.  
9. Notification flags on; Config → diagnostics → **SMTP test** succeeds.  
10. Subscribe operator; **platform pipeline dry-run** then live test (or HelloWorld `demo.completed`).  
11. Create HelloWorld (or module) schedule with explicit TZ; list/detail show **next fire**.  
12. Wait one poll (≥15s); schedule fires once; no duplicate fire for same UTC instant.  
13. Control Center → Worker runtime: host `worker-local`, plane `local`, healthy/recent; observe count ≥1.  
14. Manual **run-cycle** / **recover-stale** work; confirm heartbeat not required for manual ops.  
15. Confirm **no** WorkerHost start button; `runtime-worker/start` returns 410.  
16. Execute procedure / start run from `/execute`; appear in Runs.  
17. Control Center attention/blocked signals render without empty crashes.

---

## 8. Configuration checklist (foundation path)

| Setting | Mark |
| ------- | ---- |
| SQL connection | **required** |
| Management API base URL | **required** |
| `ShellUi:ManagementHostUrl` on Management.Host | **required** for email |
| UI `ApiBaseUrl` / client ShellUi | **required** |
| `Notifications:Email` SmtpHost, FromAddress | **required** for email |
| SMTP password secret / env | **required** for email |
| Notification evaluation/instances/delivery flags | **required** for pipeline |
| `Notifications:UsePlatformPipeline:Enabled` | **recommended** |
| `PlatformEvents:Enabled` | **required** for platform notifications |
| `AttentionIncidents:Observation:Enabled` | **recommended** |
| Schedule assignment plane `local` | **required** |
| Worker `ScheduleDispatch:RuntimePlaneId=local` | **required** |
| Worker host id (e.g. `worker-local`) | **required** |
| `Scheduling:DefaultTimeZoneId` | **recommended** |
| Entra / LocalAuth | **optional** / often required for local demo |
| BootstrapPlatformAdmin | **recommended** first boot |
| BreakGlassAuth:Enabled | keep **false** unless explicitly needed |
| Windows Service / Azure hosting | **deferred** (later tracks) |

---

## 9. Test coverage gaps (foundation → product)

| Gap | Disposition |
| --- | ----------- |
| Scripted Aspire smoke (dispatch + heartbeat + SMTP mock) | Useful hygiene now; **RC automation** belongs in Final Release Readiness |
| Integration suite requires live SQL | Document; do not soft-skip silently in ops |
| E2E Playwright/operator path | Useful with UI overhaul |
| Epic 69 removal tests | Deferred with Epic 69 removal |
| Windows Service install/uninstall | Final Release / deployment track |
| Multi-host dual WorkerHost live Aspire | Manual/optional (unit covers dual heartbeat) |

---

## 10. Documentation corrections in this pass

| Doc | Action |
| --- | ------ |
| This audit | Replaces RR-1 “release readiness” framing |
| `docs/00-overview/roadmap.md` | Foundation complete → product development → release readiness deferred |
| `docs/00-overview/pre-release-product-roadmap.md` | New major-track roadmap |
| README Status | Foundation baseline + product phase (not RC) |
| `rr1-v1-release-readiness-audit.md` | Stub pointing here |

---

## 11. Pre-release product roadmap (summary)

Canonical detail: [`docs/00-overview/pre-release-product-roadmap.md`](../00-overview/pre-release-product-roadmap.md).

| Track | Intent |
| ----- | ------ |
| Product Experience & UI Overhaul | Shell, layout, disclosure, settings, role separation, visual pass |
| User Profile & Preferences | Profile, avatar, display TZ UI, notification prefs |
| SDK & Demo Module Validation | Additional demos validating SDK / integration interfaces |
| Systems / Applications / Services Model | App/system/service abstractions + explorers |
| Business Data & Cross-Integration Records | Shared records, projections, provenance |
| Job Operation Itemization & Retry | Itemized outcomes + per-item retry/skip/resolve |
| Azure / Deployment Foundation | Cloud hosting, pipeline, SQL/secrets/logging, runbooks |
| Final Release Readiness | Packaging, smoke automation, security, docs, RC — **last** |

**Do not** schedule Final Release Readiness until the major product tracks above have materially landed.

---

## 12. Recommended next implementation track

**Recommended next track: SDK & Demo Module Validation**

**Why (now):**

1. S1–S13 / SWM-1–12 already delivered a **usable operator foundation** and HelloWorld proof path — the next risk is thin SDK/validation coverage for real integration shapes (import rows, files, API poll, events, cross-integration).
2. Demos produce **evidence** that drives later Systems/Services modeling, business-data surfaces, and especially **job itemization + per-item retry** (those features need concrete operation shapes, not abstract UI).
3. **UI overhaul** remains high priority but benefits from redesigning against richer module surfaces rather than empty shells; sequence UI overhaul immediately after or interleaved with demo work once 1–2 new demos exist.
4. Systems/Services and Azure are larger architectural bets — better after more demo-driven interface pressure.
5. Final Release Readiness is explicitly **out of order**.

**Not chosen as next:** Final Release Readiness; Azure/Deployment Foundation; Job Itemization as a stand-alone first epic without demo modules.

---

## 13. Accomplishments preserved

| Track | Status |
| ----- | ------ |
| Stabilization S1–S13 | **Closed** |
| Scheduling & Worker Management SWM-1–SWM-12 | **Closed** (foundation) |
| Notifications / email stabilization | **Closed** for foundation (platform preferred; Epic 69 retained) |
| Operator shell stabilization | **Closed** for foundation (full product UX overhaul still ahead) |
| Schedule/worker runtime foundation | **Closed** |
| Worker lifecycle | **Externally managed**; Management start quarantined |
| Product release / RC | **Future** — after major product tracks |

---

## Appendix — Evidence anchors

| Topic | Path |
| ----- | ---- |
| Stabilization closed | `docs/audits/stabilization-closure-check.md` |
| SWM-1 plane fix | `docs/audits/swm1-runtime-plane-diagnosis.md` |
| Hosting / quarantine | `docs/03-operations/Worker-Hosting-Guidance-v1.md`, SWM-12 ADR |
| Config layers | `docs/03-operations/management-platform-configuration.md` |
| Break-glass | `docs/01-architecture/bootstrap-recovery-and-break-glass.md` |
| Notification cutover / Epic 69 | S12 ADRs; Host `appsettings.json` |
| Pre-release product roadmap | `docs/00-overview/pre-release-product-roadmap.md` |
| Publish gap | `scripts/deployment/commands/Publish-DotNetProjects.ps1` |
