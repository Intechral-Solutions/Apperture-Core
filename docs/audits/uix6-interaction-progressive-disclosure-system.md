# UIX-6 — Interaction and Progressive Disclosure System

**Status:** Implemented (automated); operator smoke pending
**Date:** 2026-08-05
**Related:** [UIX-5](./uix5-supporting-surface-pattern-audit.md), [UIX-2](./uix2-breadcrumb-shell-stabilization.md), [UIX-3](./uix3-affected-record-anchors-return-context.md), [UIX-4](./uix4-contextual-breadcrumbs-link-integrity.md), [BDS-6](./bds6-cross-run-business-record-activity-lineage.md), [pattern guide](../design-system/interaction-patterns.md), [roadmap](../00-overview/pre-release-product-roadmap.md)

---

## 1. Executive summary

UIX-6 turns the UIX-5 recommendation into an Apperture-native interaction system: a published
decision guide, four shared components (`Disclosure`, `TechnicalDetails`, `ManagementDialog`,
`ConfirmActionDialog`), centralized interaction tokens and a documented layering model, aligned
feedback rules, and representative migrations.

All six native `window.confirm` call sites are gone. Six representative raw `<details>`
disclosures across four features now use the shared primitives. No panel, drawer, popover, or
accordion was introduced.

UIX-5 recommended a narrow "technical disclosure and confirm-action consistency" epic. UIX-6
broadened that into a coherent interaction vocabulary while keeping the same scope boundary:
no inspection panel, no overlay engine.

## 2. Design principles

Business information first · progressive disclosure for secondary detail · routable work stays
routable · dialogs are intentional · local interactions stay local · accessibility is part of
the component contract. Full statements in the [pattern guide](../design-system/interaction-patterns.md).

## 3. Pattern decision guide

| Situation | Surface |
| --- | --- |
| Essential record facts and actions | Inline |
| Secondary content already loaded | `Disclosure` |
| Identifiers, metadata, diagnostics | `TechnicalDetails` |
| Short nonessential explanation | Tooltip |
| Transient outcome | Toast |
| Outcome tied to a control/section | `ActionFeedback` |
| Persistent page state | `PageMessage` |
| Blocking consequential decision | `ConfirmActionDialog` |
| Filtered, paged, shareable work | Dedicated route |
| Inspect beside context | Deferred (UIX-5 criteria) |

Explicit "do not use" guidance per pattern lives in the guide (§2, §4–§8).

## 4. Components implemented

| Component | Location | Purpose |
| --- | --- | --- |
| `Disclosure` | `Molecules/Disclosure.razor` | Semantic `<details>` primitive; `Summary`, `SummaryContent`, `InitiallyOpen`, `Dense`, `Class` |
| `TechnicalDetails` | `Molecules/TechnicalDetails.razor` | Opinionated identifier/metadata disclosure over `Disclosure`, definition-list body, optional `Note` |
| `TechnicalDetail` | `Molecules/TechnicalDetail.razor` | One `dt`/`dd` pair; monospace by default; `—` placeholder |
| `ManagementDialog` | `Organisms/ManagementDialog.razor` | Accessible modal foundation |
| `ConfirmActionDialog` | `Organisms/ConfirmActionDialog.razor` | Consequential-action confirmation |

Supporting shared logic (testable, no Razor):

| Type | Purpose |
| --- | --- |
| `ManagementInteractionSurface` | The interaction vocabulary as an enum |
| `ManagementInteractionSurfaces` | Class-name constants + documented layering model |
| `ConfirmActionUiHelpers` / `ConfirmActionEmphasis` | Labels, busy label, emphasis → button treatment |
| `TechnicalDetailsUiHelpers` | Default summary, value/label formatting |
| `ToastPolicy` | Duration, ARIA live level, duplicate signature |

`TechnicalDetail` is justified: identifier rows repeat 6–10 times per consumer, and the
placeholder/monospace/wrapping rules would otherwise be copied into every page.

## 5. Components deliberately deferred

| Component | Reason |
| --- | --- |
| Inspection panel / drawer | UIX-5 trigger criteria unmet; no second inspect-beside-context workflow |
| `Popover` | No current consumer needs interactive contextual content with explicit open/close and focus movement |
| `Accordion` | No multi-section peer consumer; `Disclosure` covers today's usage. `ManagementAdvancedSection` remains for advanced forms |
| Shared `Tooltip` | Existing `SettingInfoAffordance` + `title=` are consistent enough; a JS-positioned tooltip would add interop risk for no proven need |
| Dialog service / overlay host | Rendered-component ownership is sufficient (see §6) |

## 6. Dialog architecture

**Chosen model: rendered component ownership.** The page renders its own
`ConfirmActionDialog` and owns `IsOpen`.

Why this fits Apperture:

- Confirmations are page-local and already have page state (`_confirmDeleteOpen`, pending row).
- No cross-cutting caller needs to raise a dialog from a service.
- Keeps state explicit and testable by source inspection, matching repository test conventions.
- Avoids a global host competing with `ToastHost`/`MainLayout` lifetimes and the one-WASM-mount invariant.
- A typed confirmation service can be added later without changing the component contract.

Responsibility split:

| Owner | State |
| --- | --- |
| Page | `IsOpen`, the action itself, authorization, reload/navigation |
| `ConfirmActionDialog` | busy, error, duplicate-submission guard, labels |
| `ManagementDialog` | focus, Escape, scrim, scroll ownership, sizing, semantics |

Failure contract: `OnConfirm` throws → dialog stays open with the reason; `OnConfirmed` fires
only on success. Pages that already surface per-row inline errors return a success flag and
the caller rethrows so both the dialog and the row show the failure.

**JavaScript interop** is confined to four read/focus helpers on the existing
`appertureShell` object: `captureFocusedElementId`, `focusElementById`, `focusEdgeIn`,
`setModalOpen`. None reparent or rewrite Blazor-owned DOM; `setModalOpen` toggles a class on
`document.body`, which is outside the Blazor render root, and is reference counted. Every call
is wrapped for `JSException` / `InvalidOperationException` / `TaskCanceledException`, and the
component is `IAsyncDisposable` so a teardown mid-flight releases scroll ownership.

## 7. Disclosure architecture

`Disclosure` is a thin semantic wrapper — no JavaScript, no URL state, native keyboard
behavior, focus-visible styling, reduced-motion-safe marker rotation.

`TechnicalDetails` composes `Disclosure` and adds the identifier conventions: collapsed by
default, canonical summary, definition-list semantics, monospace + `user-select: all` values,
`overflow-wrap` for long GUIDs and JSON, and an optional trailing `Note`.

Feature markup keeps ownership of *which* identifiers to show; the component owns *how* they
look and behave.

## 8. Feedback and help conventions

- Non-error toasts auto-dismiss after **6s** (`ToastPolicy.DefaultDuration`).
- **Error toasts persist until dismissed** and are announced assertively; `ToastHost` now uses
  two live regions (assertive for errors, polite otherwise) instead of one polite region.
- Repeating an identical outcome **re-arms** the existing toast rather than stacking a second
  announcement (`ToastPolicy.BuildSignature`); the original timer no longer dismisses a re-armed toast.
- Errors confirmed inside a dialog stay in the dialog — no duplicate toast.
- `ActionFeedback` remains the pattern for section-scoped outcomes; `PageMessage` for
  persistent page state. Neither component was replaced.
- Help boundary documented: visible copy for required instructions, `SettingInfoAffordance`
  for field definitions, `title=` for nonessential tips, links for deep background.

## 9. Theme and layering tokens

Added to `management-ui-primitives.css` (`:root`):

`--z-dialog-scrim` · `--z-dialog` · `--z-toast` · `--interaction-duration` ·
`--interaction-easing` · `--dialog-radius` · `--dialog-width-sm|md|lg` · `--dialog-margin` ·
`--dialog-scrim-color` · `--disclosure-border` · `--disclosure-bg` · `--interaction-focus-ring`

Layering model (also asserted in tests via `ManagementInteractionSurfaces.LayeringModel`):

```text
page (0)
control panel (20)
nav rail (40)
dropdown / menu (50)
shell topbar (60)
dialog scrim (80)
dialog (81)
toast (100)
```

`ToastHost` moved from the arbitrary `z-[100]` utility to `.ui-toast-layer` using `--z-toast`.
A test now fails if any Razor file reintroduces a `z-[...]` arbitrary value.

## 10. Accessibility behavior

**Dialog:** `role="dialog"`, `aria-modal="true"`, `aria-labelledby` to the title, `aria-busy`
while running, initial focus on the panel, focus contained by sentinels that wrap to the first
or last tabbable control, focus restored to the opener by id, Escape closes (suppressed while
busy), explicit close button (disabled while busy), internal body scroll with background scroll
locked, mobile full-width layout with stretched footer buttons, entry animation removed under
`prefers-reduced-motion`.

**Disclosure:** native summary semantics and keyboard activation, visible focus ring, marker
rotation plus native state (not color alone), reduced-motion respected.

**Toast:** dual live regions, dismiss control with accessible label, errors persist.

**Known gap:** true background `inert` is not applied — modality is conveyed by
`aria-modal="true"` plus focus containment and the scrim. Recorded in §15.

## 11. Representative migrations

**Confirmations — all six native `confirm` calls replaced:**

| Page | Action | Emphasis |
| --- | --- | --- |
| `IntegrationDetail` | Delete integration | Destructive |
| `IntegrationDetail` | Remove binding | Caution |
| `ConnectionDetail` | Retire connection | Caution |
| `IntegrationSettings` | Clear setting | Destructive when required, else Caution |
| `IntegrationSettings` | Reset scoped override | Caution |
| `PlatformSettings` | Clear / reset platform override | Caution |

`IJSRuntime` injection was removed from `ConnectionDetail`, `IntegrationSettings`, and
`PlatformSettings` where it became unused.

**Disclosures:**

| Consumer | Before | After |
| --- | --- | --- |
| `DemoCustomerDetail` (Sources) | raw `<details>` "Platform identifiers" | `TechnicalDetails` + `Note` |
| `DemoCustomerActivity` (entries) | raw `<details>` "Technical details" | `TechnicalDetails` dense |
| `RunOperationItemizationPanel` (group) | raw `<details>` | `TechnicalDetails` custom summary |
| `RunOperationItemizationPanel` (item) | raw `<details>` "Details" | `TechnicalDetails` dense |
| `IntegrationDetail` (procedure) | raw `<details>` single mono line | `TechnicalDetails` — revealed the composed-identifier layout need |
| `NotificationInstanceDetail` (body, render context) | raw `<details>` with `<pre>` | `Disclosure` — proves the generic primitive for non-identifier content |

## 12. Remaining migration inventory

Raw `<details>` still in place (deliberate — batch opportunistically, not mechanically):

| File | Blocks | Kind |
| --- | --- | --- |
| `Pages/Control/ControlCenter.razor` | 5 | mixed diagnostics + guidance |
| `Pages/Integrations/IntegrationsList.razor` | 1 | row diagnostics |
| `Pages/Integrations/IntegrationConfigurationOverview.razor` | 2 | configuration provenance |
| `Pages/Integrations/IntegrationNotifications.razor` | 1 | delivery detail |
| `Pages/Schedules/SchedulesList.razor` | 1 | row diagnostics (carries `@onclick:stopPropagation`) |
| `Pages/Runs/RunsList.razor` | 1 | row diagnostics |
| `Pages/Config/Notifications/NotificationDiagnostics.razor` | 1 | diagnostics |
| `Pages/Execution/ExecutionStart.razor` | 1 | launch detail |

`ManagementAdvancedSection` (its own `<details>`) stays as the advanced-**form** wrapper and is
not a migration target.

## 13. Automated test results

| Suite | Result |
| --- | --- |
| `Apperture.Management.Ui.Shared.Tests` (incl. new `Uix6InteractionSystemTests`, 28 tests) | **572 passed, 0 failed** |
| `Apperture.Management.Ui.Client` build | **succeeded, 0 errors** |
| `Apperture.UnitTests` | **not run** — build blocked by locked module DLLs |
| `Apperture.ArchTests` | **not run** — build blocked by locked module DLLs |

The two blocked suites failed at `MSB3027`/`MSB3021` while copying module assemblies:
`Apperture.Management.Host` and `Apperture.RuntimeHost.Windows.WorkerHost` were running
(Aspire session) and held `Sample.*`, `Unisa.*`, `Proof.*`, and core DLLs. This is the known
repository pitfall, not a code failure — no compilation error was reported for any changed
file. Re-run both after stopping those hosts. UIX-6 touches only
`Apperture.Management.Ui.Client` and `Apperture.Management.Ui.Shared`, both of which built and
tested cleanly.

Preserved suites confirmed green within the shared run: `Uix2*`, `Uix3*`, `Uix4*`, `Bds5*`,
`Bds6*`, `AuthShell*`, `Epic101*`, `Sdk3D*`, `Sdk4C*`.

Four pre-existing source-integrity tests were updated (not weakened) because the markup they
asserted moved into shared components: `Bds5DemoCustomersSurfaceTests`,
`Sdk3DOperationItemizationPanelTests`, `Sdk4COperationItemizationRetryUiTests`,
`Epic111PlatformSettingsPageTests` — the last now asserts the **absence** of `"confirm"`.

New integrity guards: no production `window.confirm`; migrated pages use `ConfirmActionDialog`;
representative pages use `TechnicalDetails`; no `z-[` arbitrary values; no
`InspectionPanel`/`Drawer`/`SidePanel`/`Popover`/`Accordion` component was added; the pattern
guide exists.

## 14. Runtime smoke

**Not executed in this session.** The operator checklist (technical disclosures, destructive
confirmations including keyboard/Escape/focus-return/failure path, responsive tiers, reduced
motion, toast behavior, console gate) is defined in the UIX-6 epic and remains open, alongside
the still-pending BDS-6 smoke.

## 15. Known limitations

1. **No true background `inert`.** Modality relies on `aria-modal`, focus containment, and the
   scrim. Revisit if a screen-reader gap is observed.
2. **Focus restoration needs an element id.** Triggers without an `id` restore nothing (focus
   falls back to the document). Migrated triggers have ids where a dialog closes in place.
3. **Focus wrap depends on JavaScript.** If interop fails, focus falls back to the dialog panel
   — contained, but without cycling.
4. **Nested dialogs are unsupported by design.** `setModalOpen` is reference counted for safety,
   but no consumer stacks dialogs and none should.
5. **Remaining raw `<details>`** (§12) still vary in summary wording.
6. **No bUnit/component-render tests** exist in this repository; dialog behavior is covered by
   helper unit tests plus source-integrity assertions, not simulated interaction.
7. **Reduced motion** is handled in CSS only; not verified at runtime this session.

## 16. Inspection-panel extension seam

A future panel reuses, without new abstractions today:

- overlay/surface tokens (`--dialog-*`, `--z-*`, `--interaction-*`);
- focus helpers (`captureFocusedElementId`, `focusElementById`, `focusEdgeIn`);
- Escape and close-control conventions from `ManagementDialog`;
- accessible naming pattern (`aria-labelledby` to a rendered title);
- responsive sizing conventions and reduced-motion rules.

The UIX-5 trigger criteria still gate it: two real inspect-beside-context workflows, genuine
comparison need, defined URL/Back semantics, and full-screen mobile behavior. No unused panel
abstraction was created.

## 17. Recommended next roadmap step

**UIX-7 — remaining disclosure consolidation and operator smoke closure** is *not* proposed as
a separate epic. Instead:

1. Close the outstanding **BDS-6 + UIX-6 operator smoke** together in one runtime session.
2. Fold the §12 disclosure migrations into whichever epic next touches those pages.
3. Return to product tracks (Track 3 SDK/demo modules or Track 2 profile/preferences); revisit
   an inspection panel only when the UIX-5 criteria fire.
