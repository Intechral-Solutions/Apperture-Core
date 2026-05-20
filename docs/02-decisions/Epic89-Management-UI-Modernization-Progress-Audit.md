# Epic 89 — Management UI modernization progress audit

## Status

Accepted — May 2026.

## 1. Purpose

After Epics **78–88** (groundwork, design system, five redesign pilots, consolidation, state-branch sweep, action groundwork, and `ActionFeedback` shell), audit whether:

- The design-system direction remains coherent
- Pilots preserved behavior (static review + build)
- Documentation matches code
- Next epics are grounded in evidence, not momentum

This is an **audit epic** — findings and documentation over implementation.

## 2. Scope

**Documentation reviewed:** Epic 78–88 decision records, `docs/01-architecture/management-ui-design-system.md`, `.ai/memory/decisions-index.md`.

**Components reviewed:** `Atoms/` (`ActionButton`, `StatusPill`, `AttentionPill`, `DrillInLink`), `Molecules/` (state wrappers, `ActionFeedback`, `ManagementSummaryCard`, `ManagementAdvancedSection`), `Organisms/` (`PageHeader`, `PageMessage`, `ManagementSection`), `management-ui-primitives.css`, `ManagementUiSemantics.cs`, `ManagementUiSemanticsTests.cs`.

**Pilot pages reviewed:** `IntegrationDetail`, `RunDetail`, `ControlCenter`, `SchedulesList`, `ScheduleDetail`, `ScheduleCreate`, `NotificationDefinitionDetail`, `NotificationDefinitionEdit`, `NotificationDefinitionCreate`.

**Broad sweep spot-check:** `BlockedWork`, `RunsList`, `IntegrationsList`, `ConnectionsPage`, `ConnectionDetail`, `NotificationInstancesList`, `NotificationDiagnostics`, `ModulesList`, `ModuleDetail`, `DashboardHome`, representative Epic 86 auth/notification/governance pages.

## 3. Summary judgment

**Coherent and safe to continue**, with one **corrective** theme that should precede broader `ActionFeedback` adoption: cooperative action banners on Run/Schedule detail are wired correctly in markup but **`LoadAsync` clears `_actionMessage` after every action**, so operators likely never see post-action feedback (pre-existing; Epic 88 wrapped the same branch).

No blocking build/test failures or evidence of route/permission/handler regressions from static review. Manual QA remains required for cooperative execution paths.

## 4. What is working

| Area | Evidence |
|------|----------|
| **Layering model** | Pilots use `PageHeader` + summary band/section + `ManagementSection` + `ManagementAdvancedSection` consistently |
| **State wrappers** | Epic 86 adoption on lists/hubs (e.g. `IntegrationsList`, `RunsList`, `ModulesList`, auth/notification pages) — `LoadingState`/`ErrorState`/`EmptyState`/`ForbiddenState` |
| **Summary card grid** | `ui-summary-card-grid` on Control Center, notification definition detail, pilots |
| **Semantics** | `ManagementUiSemantics` + unit tests; pilots use `StatusPill`/`AttentionPill` for lifecycle/outcome |
| **ActionFeedback seam** | Small wrapper over `PageMessage`; correct defaults (Title “Action”, Kind Info) |
| **Documentation chain** | Epics 78→88 cross-linked; design-system doc covers state + action guidance |
| **Behavior isolation** | Pilots did not introduce global services, operation registry, or backend changes |
| **Build health** | `Apperture.Management.Ui.Client` builds; `ManagementUiSemanticsTests` pass |

## 5. Findings

### Blocking

None identified from static review and build/test.

### Corrective

| ID | Area | Evidence | Recommended action | Follow-up |
|----|------|----------|-------------------|-----------|
| **COR-001** | Run/Schedule action feedback | `RunDetail.LoadAsync` and `ScheduleDetail.LoadAsync` set `_actionMessage = null` (and `_newRunId = null` on runs) before reload; both action handlers call `LoadAsync()` afterward | Preserve action message across post-action reload (e.g. optional flag on `LoadAsync`, or clear only on explicit Refresh) | **Corrective pass epic** (before claiming ActionFeedback works) |
| **COR-002** | ActionFeedback adoption gaps | `BlockedWork.razor`, `ModuleDetail.razor` still use raw `PageMessage` Info “Action” for `_actionMessage` | Adopt `ActionFeedback` where semantics match (after COR-001 pattern is fixed on Run/Schedule) | Blocked Work + module detail consistency |
| **COR-003** | Secondary action/load errors | `NotificationDiagnostics`, `NotificationInstanceDetail`, `IntegrationNotifications` render `_error` as muted `<p class="text-sm text-muted">` at page bottom | Promote to `ErrorState` or `PageMessage` Error when message is action/load failure | Limited consistency sweep |
| **COR-004** | Doc drift (fixed in audit) | Design-system “Proof usage” / “Future extension seams” listed only through Epic 85 | Updated in Epic 89 to include Epics 86–89 | Done |

### Advisory

| ID | Area | Evidence | Recommended action | Follow-up |
|----|------|----------|-------------------|-----------|
| **ADV-001** | Layout layering | `ui-page-stack` on ~10 routes (pilots + config landing); most list/hub pages use legacy `h1` headers | Incremental `PageHeader` adoption when pages are touched | Future layout pass |
| **ADV-002** | Cooperative error styling | Run/Schedule/Blocked Work show 403/409/HTTP failures as **Info** `ActionFeedback`/`PageMessage` | Consider `Kind=Error` only after operator copy review | UX polish epic |
| **ADV-003** | Integration Detail feedback | Heavy `IToastService` usage for mutations; section `PageMessage` warnings for bindings | Document as intentional; do not merge into ActionFeedback without UX review | Epic 87 deferral |
| **ADV-004** | Dashboard cards | `DashboardHome` uses custom attention spans, not `AttentionPill` | Optional pill adoption when dashboard epic runs | Module/dashboard epic |
| **ADV-005** | Outcome heuristics | `MapRunOutcomeSemantic` uses `Contains("success")` / `Contains("fail")` | Watch for mis-pills on unusual outcome strings; extend tests if new tokens appear | Semantic refinement epic |
| **ADV-006** | Login shells | `Login.razor` / `LoginLocal.razor` still raw `PageMessage` | Epic 86 deferral; low priority | Login cleanup |
| **ADV-007** | Schedule detail branch order | When `_actionMessage` non-empty, schedule detail body is skipped (unlike run detail which shows both) | Confirm intentional; align if COR-001 fixes persistence | Manual QA |

### Deferred / future-system

| ID | Theme | Source epics | Notes |
|----|-------|--------------|-------|
| **DEF-001** | Operation history / persistent shell | 85, 87, 88 | Needs backend lifecycle contracts |
| **DEF-002** | Operation registry / global service | 85, 87 | Explicitly out of scope |
| **DEF-003** | Schema-driven forms | 79, 85 | `ProcedureParameterForm` only |
| **DEF-004** | Module dashboards / stats / charts | 78, 85 | API aggregates required |
| **DEF-005** | Calendar / temporal UX | 78, 85 | Schedule APIs need temporal query contracts |
| **DEF-006** | Generic summary band component | 85 | Page-specific bands remain correct |
| **DEF-007** | Toast normalization (Integration Detail) | 87, 88 | Hub mutation pattern differs from run/schedule |
| **DEF-008** | Notification diagnostics empty vs degraded | 86 | “Health summary unavailable” semantics unclear |
| **DEF-009** | Metadata grid primitive | 85 | Repeated `dl`/`grid` patterns; defer until third identical pilot |

## 6. Behavior preservation review

| Risk area | Static review | Manual QA recommended |
|-----------|---------------|------------------------|
| **Routes / query params** | Pilot `@page` directives unchanged; runs list URL filters preserved | Navigate drill-ins from Control Center, schedules, run detail |
| **Permissions** | `IOperatorCapabilitiesService` / `CanInvoke` gates still present on pilots | Verify forbidden vs omitted on dashboard, integrations, governance |
| **Action handlers** | No handler signature changes in audit scope | Run cancel/retry/park/resume; schedule enable/disable/trigger; blocked-work retry |
| **Polling** | Run Detail poll checkbox + timer logic untouched | Enable poll during running run; confirm refresh |
| **Form validation** | Notification authoring validation lists unchanged | Save invalid definition; Entra tenant form errors |
| **Navigation** | `DrillInLink` / `Epic33UiDisplayHelpers` usage on pilots | Audit link from schedule; new-run link after retry |
| **Action feedback visibility** | COR-001 — banners may not show after reload | **Priority QA:** confirm whether operators ever see action banners today |

## 7. Semantic correctness review

| Primitive | Assessment |
|-----------|------------|
| **StatusPill** | Used appropriately on pilots for lifecycle/outcome; semantics from `ManagementUiSemantics` |
| **AttentionPill** | Notification definition warnings, schedule stale claim — aligned with API tokens |
| **Loading/Empty/Error/Forbidden** | Epic 86 sweep consistent on spot-checked pages; dashboard distinguishes empty (permissions) vs provider failure |
| **ActionFeedback** | Markup correct; **effectiveness** limited by COR-001 |
| **PageMessage** | Still correct for static warnings, form errors, integration section warnings |
| **ManagementUiSemantics** | Tests cover health, integration state, run lifecycle/outcome; heuristic outcomes noted (ADV-005) |

**Misleading-state checks:** No new conflation of empty vs forbidden on spot-checked Epic 86 pages. Muted `_error` text (COR-003) understates failures but does not label them as empty.

## 8. Documentation review

| Item | Status |
|------|--------|
| Epic 78–88 files present | Yes |
| Design-system action + state sections | Match implemented primitives |
| Epic 88 claims Run/Schedule proof | Accurate for markup; COR-001 notes runtime visibility gap |
| Cross-links Epic 85→89 | Updated in Epic 89 |
| Decisions index | Updated with Epic 89 |
| Deferred items across epics | Consolidated in §9 below |

## 9. Deferred item register (grouped)

| Candidate epic | Items |
|----------------|-------|
| **Corrective pass (recommended next)** | COR-001 action message persistence; COR-002 ActionFeedback on Blocked Work/ModuleDetail; COR-003 muted errors |
| **Blocked Work ActionFeedback adoption** | Subset of COR-002 after COR-001 |
| **Operation history groundwork** | DEF-001 — backend first |
| **Semantic mapping refinement** | ADV-005, ADV-002 |
| **Notification diagnostics clarity** | DEF-008, COR-003 |
| **Login / state cleanup** | ADV-006 |
| **Layout/PageHeader sweep** | ADV-001 |
| **Dashboard / module dashboards** | ADV-004, DEF-004 |
| **Schema-driven forms** | DEF-003 |
| **Stats/charts/calendar** | DEF-004, DEF-005 |

## 10. Recommended next epic

**Corrective pass (Epic 90 tentative):** Fix **COR-001** (preserve `_actionMessage` / `_newRunId` across post-action reload on Run and Schedule detail), then adopt **`ActionFeedback`** on Blocked Work and ModuleDetail (**COR-002**), and normalize muted action errors on notification/integration diagnostic pages (**COR-003**). Small scope, high signal, unblocks honest validation of Epic 88.

Do **not** start operation history groundwork until COR-001 is resolved and manual QA confirms cooperative feedback behavior.

## 11. Validation

Recorded at audit time:

- `dotnet build` on `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj` — **succeeded**
- `dotnet test` on `tests/Apperture.UnitTests/Apperture.UnitTests.csproj` — filter `FullyQualifiedName~ManagementUiSemanticsTests` — **23 passed**

No production code changes in Epic 89 beyond documentation truth corrections.
