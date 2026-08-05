# UIX-5 — Supporting Surface and Investigation Pattern Audit

**Status:** Complete (audit only; no panel/drawer implementation)  
**Date:** 2026-08-05  
**Related:** [UIX-1](./bds1-business-data-surface-design-audit.md) (BDS-1 §8–9), [UIX-2](./uix2-breadcrumb-shell-stabilization.md), [UIX-3](./uix3-affected-record-anchors-return-context.md), [UIX-4](./uix4-contextual-breadcrumbs-link-integrity.md), [BDS-5](./bds5-demo-customers-surface.md), [BDS-6](./bds6-cross-run-business-record-activity-lineage.md), [shared-record reverse links](../superpowers/specs/2026-08-05-shared-record-reverse-links-design.md), [roadmap](../00-overview/pre-release-product-roadmap.md)

**Runtime validation:** Not performed in this audit session (code and prior UIX/BDS reports only).

---

## 1. Executive conclusion

**Product question:** When should Apperture show supporting information inline, in an expandable region, in a side panel/drawer, in a modal, or on a dedicated page?

**Answer (validated against current Management UI):**

| Pattern | When |
| --- | --- |
| **Inline** | Primary record facts, conflict recognition, source observations at current scale, activity summary |
| **Inline expansion** (`<details>` / disclosure) | Technical IDs, raw metadata, secondary diagnostics already loaded on the page |
| **Dedicated page** | Filtered/paged investigation (Demo Customer Activity); primary list/detail subjects |
| **Modal** | Blocking consequential confirmations (delete, irreversible settings) — not long read-only content |
| **Side panel / drawer** | **Not justified yet** as a shared platform pattern |

**Shared panel pattern:** **1 — No shared panel pattern is justified yet.**

Evidence: current investigation workflows already have clear homes (inline Sources/Conflicts; dedicated Activity route; Run Detail group expand + row actions + reverse shared-record links). No second consumer needs the same “inspect while comparing underlying page” interaction that a URL-aware panel would uniquely unlock. Introducing a drawer now would add URL/focus/shell risk against UIX-2–4 invariants without proportional operator gain.

**Keep as-is:** Demo Customer Activity route; customer summary/Sources/Conflicts inline layout; Affected-records group expansion; UIX-3/4 navigation contracts; reverse shared-record links; toast/ActionFeedback for non-blocking outcomes.

**Change (small consistency):** standardize technical-detail disclosure; replace browser `window.confirm` with an accessible confirm dialog for destructive actions.

**Next implementation epic:** **UIX-6 — Technical disclosure and confirm-action consistency** (not a panel framework).

---

## 2. Existing pattern inventory

| Surface / component | Kind | Consumers (representative) | Route / state | Browser nav | Notes |
| --- | --- | --- | --- | --- | --- |
| `ManagementSection` | Inline section chrome | Most Management pages | Page-owned | Yes | Layers: Summary / Detail / Advanced / Help |
| `PageHeader` + breadcrumbs | Page chrome | All main pages | Route + UIX-4 context | Yes | Plain anchors; UIX-2 invariants |
| `PageMessage` / `ActionFeedback` | Inline messaging | Runs, integrations, modules, … | Local | No | Non-blocking outcomes |
| `ToastHost` | Transient overlay toast | MainLayout | Service | No | Success/error flashes |
| `LoadingState` / `EmptyState` / `ErrorState` / `ForbiddenState` | Page states | Demo, runs, itemization, … | Page-owned | Yes | Consistent vocabulary |
| `ManagementAdvancedSection` | Inline expansion (`<details>`) | Advanced settings-style blocks | Local `open` | No | Shared wrapper; underused vs raw `<details>` |
| Raw `<details>` | Inline expansion | Demo Customer Detail/Activity, `RunOperationItemizationPanel`, Control Center, Integrations, Schedules, Notifications, Runs list, Execution | Local | No | Dominant technical-disclosure pattern (~14+ files) |
| Group expand (“View items”) | Inline expansion | `RunOperationItemizationPanel` | Component state `_expandedGroupId` | Partial — UIX-3 query expands on load | Required for item anchors |
| Item retry form | Inline expansion | `RunOperationItemizationPanel` | Component state | No | Consequential but reversible via child run model; not modal |
| `NotificationDefinitionPostCreatePrompt` | Inline banner (`role="status"`) | Notification definition detail | Local | No | Correct non-modal post-create |
| `SettingInfoAffordance` / `title=` tooltips | Tooltip | Platform settings | Hover/title | No | Help only — not essential workflow |
| `NavRailItem` tooltip | Tooltip | Shell rail | Hover | No | Compact labels |
| `UserAccountMenu` / `ThemeMenu` / `PersonalNotificationMenu` | Dropdown overlay | Top bar | Local + `@onclick:stopPropagation` | No | Shell chrome; not investigation |
| `ControlPanel` | Shell side panel | Nav rail domain menu | Shell state | No | **Navigation**, not record inspection |
| `window.confirm` via `IJSRuntime` | Native modal | Integration delete/binding remove, connection retire, integration settings | Browser dialog | No | Only confirm pattern; not accessible/design-system |
| Dedicated child route | Dedicated page | `/demo/customers/{key}/activity` | Route | Yes | BDS-6 filters + load more |
| `EntityRecentActivityPanel` | Inline table | Integration/module recent activity | Page-owned | Drill-in links | Platform activity, not BDS-6 |
| Shared-record reverse link | Plain anchor | Itemization rows | Route to Demo Customer | Yes | Catalog-driven; clears run context |

**Absent (not found as shared components):** `Modal`, `Dialog`, `Drawer`, `SidePanel`, `InspectionPanel`, offcanvas/slide-over investigation surfaces.

**Do not assume suitability:** `ControlPanel` is not an investigation drawer. Native `confirm` is not an accessible confirm pattern. Raw `<details>` works but is inconsistent in labeling (`Technical details` vs `Platform identifiers` vs `Group technical details`).

---

## 3. Operator-task analysis

### Demo Customer Detail (`/demo/customers/{key}`)

| Task | Current surface | Fit |
| --- | --- | --- |
| Recognize customer identity/status | Inline Customer section | Correct |
| See activity volume / attention | Inline Record activity summary → dedicated Activity | Correct split |
| Recognize conflicts | Inline Conflicts + summary pills | Correct for recognition + small comparison |
| Compare source field values | Inline Sources cards (display name, email, status, …) | Correct at 1–N sample sources |
| Open provenance run/item | Plain anchors (UIX-3) | Correct |
| View platform IDs | `<details>` | Correct progressive disclosure |

### Demo Customer Activity (`…/activity`)

| Task | Current surface | Fit |
| --- | --- | --- |
| Scan chronological cross-run history | Dedicated page list | Correct (paged, filtered, bookmarkable) |
| Filter / load more | Page filters + local skip | Correct; filters are client-local (not URL) — acceptable at current scale |
| Inspect entry outcome | Row + error text + `<details>` | Sufficient |
| Deep investigation | Drill to Run Detail | Correct primary path |

A side panel for activity entries would **compete** with Run Detail drill-through and would not replace filters/paging. Panel would be supplementary at best — not justified now.

### Sources / Conflicts

| Task | Need | Current |
| --- | --- | --- |
| Quick recognition | Badges / short message | Inline |
| Comparison | Values by source | Inline conflict cards + source field grids |
| Resolution | Not implemented | Defer presentation until resolution epic |
| Audit history | Not implemented | Defer |

### Run Detail Affected records

| Task | Current surface | Fit |
| --- | --- | --- |
| Scan groups | Group cards | Correct |
| Open items | Expand group (local) | Correct; UIX-3 expands via query |
| Focus/highlight item | Anchor + highlight | Correct |
| Retry failed item | Inline form | Acceptable (hint + reason); not destructive to source item |
| Open shared record | Reverse link | Correct |
| Technical metadata | Row/group `<details>` | Correct but label-inconsistent |

Dedicated operation-item routes are **not** warranted: operators investigate in run context; deep links already encode `group`/`item`; reverse links exit to business records.

### Technical diagnostics

Operators need IDs occasionally for support/debug. Prefer collapsed disclosure everywhere; never lead with GUIDs.

### Consequential actions

| Action | Current | Gap |
| --- | --- | --- |
| Integration delete / binding remove / connection retire / settings confirm | `window.confirm` | Inaccessible; inconsistent styling |
| Item retry | Inline form | OK for reversible child-run creation |
| Post-create next steps | Inline banner | Correct (not modal) |

---

## 4. Decision matrix

| Situation | Preferred surface | Do **not** use |
| --- | --- | --- |
| Essential record information | **Inline** | Modal, hover-only tooltip |
| Small secondary metadata / IDs | **Inline expansion** | Dedicated page, modal |
| Large filtered / paged history | **Dedicated page** | Drawer as primary, modal |
| Quick inspection that must sit beside mutable primary content **and** share across ≥2 workflows | **Side panel** (future) | Nested drawers; panel as only history UI |
| Blocking consequential confirmation | **Modal** (accessible) | Native `confirm` long-term; panel for delete |
| Short definition / settings help | **Tooltip / visible help text** | Hover-only for required operating steps |
| Primary business subject (customer, run, integration) | **Dedicated page** | Modal detail |
| Shell domain navigation | **Control panel (existing)** | Reuse as record inspector |

---

## 5. Feature-specific recommendations (A–G)

### A. Customer source observation

**Decision: remain inline** (cards under Sources).

Content: source kind, observed fields, provenance label, drill links, collapsed platform IDs.  
At current sample scale (few sources per customer), inline comparison works. Expandable row adds little. Side panel only if sources become numerous **and** operators need to keep Conflicts visible while inspecting one source — not evidenced yet. Dedicated source page unjustified.

### B. Customer conflict details

**Initial: remain inline** (recognition + value-by-source comparison already present).

| Phase | Presentation |
| --- | --- |
| Recognize | Status pills + Conflicts section (keep) |
| Compare | Inline values-by-source (keep) |
| Resolve (future) | Likely **modal** or short **dedicated resolution page** if multi-step; optional **side panel** only if resolution must stay beside live Sources |
| Resolution history | Dedicated subsection or Activity filter later — **defer** |

Do not design full resolution UX in UIX-5.

### C. Customer Activity entry details

**Decision:** keep **dedicated Activity route** as authoritative filtered history. Keep row + progressive disclosure.  
**Do not** add activity-entry routes.  
**Do not** replace Activity with a panel.  
Optional later: deepen inline expansion if operators need more business fields without leaving the list — still not a panel epic.

### D. Run operation-item details

**Decision:** keep **group expand + dense row + `<details>` + inline retry + reverse link**.  
Selected-item panel: defer (would need query sync with UIX-3 anchors, focus restoration, mobile full-screen rules — high cost).  
Dedicated item route: **no** — no independent operator subject beyond run + shared record.

### E. Technical identifiers and metadata

**Decision: standardize inline expansion** via a shared **`TechnicalDetails`** component wrapping `<details>`:

- Consistent summary label (“Technical details” / optional subtitle)
- Monospace definition list slot
- Default collapsed
- No URL state

Migrate Demo Customer Detail/Activity, itemization group/item disclosures first; then other pages opportunistically.

### F. Consequential actions

| Action class | Pattern |
| --- | --- |
| Destructive / irreversible (delete integration, retire connection) | **Accessible confirm modal** required |
| Semi-destructive (remove binding, reset sensitive setting) | Confirm modal |
| Retry item (creates child run; original unchanged) | **Inline** form OK; optional confirm if product later treats retry as high-cost |
| Enable/disable with easy undo | Inline + ActionFeedback/toast; confirm if hard to reverse |
| Bulk destructive | Confirm modal with explicit count |
| Transient success | Toast / ActionFeedback — never modal |

### G. Help and explanations

| Kind | Pattern |
| --- | --- |
| Essential operating instruction | Visible `PageMessage` / section Help / inline copy |
| Field definition | `SettingInfoAffordance` / short help text |
| Nonessential tip | Tooltip OK |
| Deep docs | Documentation link |

**Do not** hide required workflow steps in hover-only tooltips.

---

## 6. Panel / drawer recommendation

### Conclusion

**1 — No shared panel pattern is justified yet.**

BDS-1 §8 anticipated later slide-overs for Sources; after BDS-5/6 + UIX-3/4 + reverse links, Sources remain small and Activity/Run Detail already cover deep investigation. A panel would not remove the need for the Activity route or group expansion.

### If revisited later (not UIX-6)

Trigger criteria (any two):

1. Conflict **resolution** requires comparing editable choices beside live Sources.
2. Operation-item inspection needs medium payloads while keeping group list visible, **and** URL-synced selection is required for share/refresh.
3. A second business-entity type repeats the same inspect-beside-list pattern.

Then prefer **option 2 — narrow shared inspection panel**, not a broad surface framework.

### Behavioral sketch (deferred implementation)

| Concern | Requirement |
| --- | --- |
| Desktop | End-docked complementary region (~24–28rem); nonmodal by default |
| Mobile | Full-screen routed sheet or dedicated page — **not** shrunk overlay |
| Overlay vs push | Prefer overlay with dimmed-but-interactive background only if nonmodal; push optional on xl |
| Focus | Nonmodal: no trap; Modal drawer: trap + inert background |
| Escape / close | Explicit close; Escape closes; restore focus to opener |
| URL | Query id for shareable inspection (`?inspect=item:{id}` or similar) — sync with Back |
| Breadcrumbs | Do **not** invent new context types casually; prefer query on current resource |
| Stacking | **No** nested drawers; **no** drawer-over-modal without exception design |
| Shell | Must not remount WASM app; no external DOM mutation; plain anchors for exits |

---

## 7. URL-state rules

| Case | State home |
| --- | --- |
| Stable record / filtered history / shareable focus | **Route or query** (Activity route; Run `group`/`item`/`from`/`fromKey`) |
| Ephemeral disclosure of already-loaded metadata | **Local** (`<details>`, retry form open) |
| Group expand without deep link | Local OK; deep link **must** expand via query (UIX-3 already) |
| Confirm dialogs | **Local only** — never URL |
| Panel inspect (future) | Query on current page; Back closes; refresh restores |
| Do not | Encode labels/routes in return URLs; globally propagate contextual query params (UIX-4) |

**Activity filters:** currently local component state. Acceptable now. Promote to query only if operators need shareable filtered Activity views — optional small follow-on, not a panel epic.

---

## 8. Accessibility standard

Any future modal or panel **must**:

- Use correct role (`dialog` when modal; `complementary` or region when nonmodal inspection)
- Provide accessible name (labelledby / aria-label)
- Move initial focus deterministically
- Trap focus **only** when modal
- Restore focus to invoking control on close
- Support Escape + visible close
- Set background inert for modal surfaces
- Keep all actions keyboard-reachable
- Never make essential content hover-only
- Announce loading/errors to assistive tech
- Not rely on color alone (reuse `StatusPill` semantics)
- Respect reduced motion
- Preserve browser Back/Forward semantics when URL-backed

Native `window.confirm` fails several of these — replace for Management destructive actions.

---

## 9. Proposed shared components

Only components with ≥2 real consumers or platform-wide behavior:

| Abstraction | Consumers now | Shared behavior | Feature-owned | URL | JS interop |
| --- | --- | --- | --- | --- | --- |
| **`TechnicalDetails`** | Demo Detail, Activity, itemization, integrations, control center, … | Collapsed `<details>`, summary label, body slot, typography | Field list content | No | No |
| **`ConfirmActionDialog`** | Integration delete/binding, connection retire, settings confirm | Modal dialog, title, body, confirm/cancel, focus trap, Escape | Message copy, confirm handler | No | Prefer Blazor dialog over `confirm` |
| **`ManagementAdvancedSection`** | Exists | Keep for advanced **forms**; do not force technical IDs through it | — | No | No |

**Do not generalize now:** InspectionPanel, SupportingSurfaceState engine, drawer host, nested overlay manager.

---

## 10. Migration recommendations

### Keep

- Demo Customer Detail structure (Customer / Activity summary / Conflicts / Sources)
- Activity dedicated route + drill-through
- Affected-records group expand + UIX-3 anchors + reverse links
- Inline retry form
- Toast / ActionFeedback / post-create banner
- ControlPanel as **nav** only
- UIX-2–4 shell and navigation contracts

### Small consistency improvement → **UIX-6**

- Introduce `TechnicalDetails`; migrate highest-traffic disclosures
- Introduce `ConfirmActionDialog`; replace `IJSRuntime` `confirm` call sites
- Align disclosure summary labels
- Document “when to use which surface” in a short UI playbook snippet (optional)

### Substantive follow-on epic

- Shared inspection panel — **only after** trigger criteria in §6
- Conflict resolution UX
- URL-synced Activity filters (if operators demand shareable filters)

### Defer

- Generic drawer framework
- Operation-item detail pages
- Activity redesign / entry routes
- Entity 360 / module-contributed panels
- Nested overlays

---

## 11. Recommended next sequence

```text
[done] UIX-1 … UIX-4
[done] BDS-1 … BDS-6 (+ shared-record reverse links)
[done] UIX-5 — Supporting surface pattern audit (this report)
[next] UIX-6 — Technical disclosure and confirm-action consistency
[later] Inspection panel — only if §6 triggers fire (e.g. with conflict resolution)
[later] Broader entity-platform UI — after second business entity proves reuse
```

Avoid lettered sub-epics. Do not start a panel framework epic without the trigger criteria.

---

## 12. Risks and unresolved questions

1. **Operator smoke for Activity filters / disclosure UX** — BDS-6 smoke still pending; density judgments may shift after real usage.
2. **Activity filter URL sync** — not proven necessary; local state may frustrate share/refresh of filtered views.
3. **Retry as modal** — product may later want stronger confirmation for expensive retries; not required by current copy (“creates child run”).
4. **Conflict resolution shape** — panel vs page vs modal cannot be finalized without the resolution workflow.
5. **Mobile investigation** — no dedicated responsive audit of Activity/itemization tables in this session; wide tables may need horizontal scroll conventions in UIX-6 or a later polish pass.
6. **`@onclick:stopPropagation` on menus/`details`** — present in shell menus and SchedulesList; UIX-2 warned against invalid patterns on breadcrumbs — keep monitoring; not expanded here.

---

## Appendix — UIX-2–4 constraints any future panel must honor

- One WASM mount; no route-dependent shell `@key` remounts
- No external mutation of Blazor-owned DOM for navigation chrome
- Plain-anchor navigation for exits where appropriate
- Route-derived + typed contextual breadcrumbs; no hidden history store
- No arbitrary return URLs; no global query leakage
- Query-only focus must re-apply without remounting the shell
