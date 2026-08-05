# UIX-4 — Contextual Breadcrumbs and Link Integrity

**Status:** Implemented (automated); operator smoke pending  
**Date:** 2026-08-05  
**Related:** [UIX-3](./uix3-affected-record-anchors-return-context.md), [UIX-2](./uix2-breadcrumb-shell-stabilization.md), [roadmap](../00-overview/pre-release-product-roadmap.md)

---

## 1. Executive summary

UIX-4 composes top-bar breadcrumbs from validated URL navigation context (`from` + `fromKey`) introduced in UIX-3. A Demo Customer drill-through to Run Detail renders `Demo Customers / {key}` as the breadcrumb parents with page title `Run`, replacing the canonical `Automation / Runs` spine. Malformed context falls back to the route-derived trail. Link helpers centralize preserve/clear/replace. Breadcrumbs remain plain anchors; UIX-2 shell invariants are unchanged.

## 2. Final context model

| Layer | Type | Role |
| --- | --- | --- |
| URL identity | `from`, `fromKey` | Typed context only (no labels/routes) |
| Seam | `ManagementBreadcrumbNavigationContext` | Validated Type + Key for resolver |
| Definition | `IManagementNavigationContextDefinition` | App-owned prefix, return href/label |
| Resolved | `ManagementResolvedNavigationContext` | BreadcrumbPrefix + ReturnHref + ReturnLabel |
| Catalog | `ManagementNavigationContextCatalog` | Registration + parse/resolve |

## 3. Supported context types

| Type | Key | Prefix | Return |
| --- | --- | --- | --- |
| `demo-customer` | business key (safe identifier) | Demo Customers → list; `{key}` → detail | `Back to demo customer {key}` → detail |

## 4. Breadcrumb composition rules

1. Resolve canonical orientation from path.
2. If context resolves and path is a run detail (`/runs/{id}…`), strip Automation/Runs root and prepend context prefix.
3. Otherwise keep canonical trail.

```text
Canonical:     Automation / Runs   + title Run
Contextual:    Demo Customers / C-100   + title Run
Incorrect:     Demo Customers / C-100 / Runs / Run
```

**Current-page convention:** Top-bar title is the primary current-page heading; breadcrumb parents are navigable only (terminal not duplicated in the top-bar trail). Full `Resolve()` still appends the terminal `IsCurrent` item for consumers that need a complete trail.

## 5. Canonical fallback

Incomplete (`from` or `fromKey` alone), unsupported type, invalid key, or non-run destination → canonical route-derived breadcrumbs. No throw, redirect, or broken links.

## 6. Context preserve / clear / replace

| Rule | Mechanism |
| --- | --- |
| Preserve | `PreserveReturnContextOnRunHref` / `WithContext` on same-run investigation links |
| Clear | `WithoutContext` (explicit); global rail/list links never append context |
| Replace | `ReplaceContext` / new Demo Customer source link with new `fromKey` |

In-page UIX-3 return affordance is retained alongside contextual breadcrumbs.

## 7. Link-helper implementation

`AffectedRecordNavigation`:

- `WithContext` / `WithoutContext` / `ReplaceContext`
- Existing build/parse and return affordance (affordance resolves via catalog)

## 8. Link-integrity test strategy

`Uix4LinkIntegrityTests` + `Uix4ContextualBreadcrumbsTests`: param names, route helpers, round-trip, no arbitrary `from=/…`, duplicate-key prevention, clear both keys, contextual hrefs app-generated, plain-anchor breadcrumb markup, ordinary routes unchanged.

## 9. Authorization and trust boundaries

Context is presentation/return metadata only. It does not authorize Run or Demo Customer access and does not prove run↔customer lineage.

## 10. Automated test results

| Suite | Filter | Result |
| --- | --- | --- |
| `Apperture.Management.Ui.Shared.Tests` | `Uix4\|Uix3\|Uix2\|Epic101\|Bds5\|AuthShell` | **200 passed**, 0 failed |
| `Apperture.Management.Ui.Shared.Tests` | `Sdk3D\|Sdk4C` | **20 passed**, 0 failed |
| `Apperture.Management.Ui.Client` | build | **succeeded** (2 pre-existing CS8604 warnings) |

## 11. Runtime smoke

Operator checklist in epic DoD (canonical vs contextual, replace, clear via Runs/rail, query-only, malformed, console clean). **Not executed in this agent session** — run before marking roadmap fully closed.

## 12. Remaining limitations

- Only Demo Customer → Run Detail composition in this epic.
- Entity breadcrumb label is the business key (no API label lookup).
- No nested origin contexts.
- No module-contributed context registration.

## 13. BDS-6 handoff

**BDS-6 — Cross-Run Business Record Activity and Lineage** is **implemented** — [`bds6-cross-run-business-record-activity-lineage.md`](./bds6-cross-run-business-record-activity-lineage.md). It reuses UIX-3/4 typed context, breadcrumbs, and affected-record links for Demo Customer activity.

## 14. UIX-5 handoff

**UIX-5 — Supporting surface and investigation pattern audit** is **complete** — [`uix5-supporting-surface-pattern-audit.md`](./uix5-supporting-surface-pattern-audit.md). Recommendation: no shared panel yet; next implementation is **UIX-6** (technical disclosure + confirm dialogs).
