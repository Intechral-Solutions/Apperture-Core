# UIX-7 — Visual Language and Theme Overhaul

**Status:** Implemented (automated verification partial; no recorded operator smoke)
**Date:** 2026-08-27 · *amended same day after a deeper legacy-styling sweep*
**Amendment note:** A follow-up inventory corrected four findings in the first pass and added one
structural discovery. Corrected: token parity is 141 light / 135 dark with 6 theme-independent light-only
tokens (§3.1, previously stated as a flat 135/135); hand-built button styling is 13, not 0 (§9(13)); raw
inputs are genuinely 0, confirmed by full enumeration (§9(14)); and 8 tables render unstyled — the
audit's one Category A finding — where the first pass classified `RunDetail` as fully migrated (§5,
§9(17)). Added: `app.css` has **no build pipeline anywhere in the repository** and cannot be regenerated
(§2.1). Also newly recorded: reduced-motion coverage is narrow rather than merely unverified (§7.2(3)),
focus treatment is mechanically inconsistent (§7.2(4)), and dead CSS is now quantified at ~320
declarations (§9(4)). The §13 disposition is unchanged.
**Type:** Read-only completion audit / closure record
**Scope of change:** `3680730..0a14b1e` — 9 commits, 105 files, +4405 / −2337
**Predecessor:** [AUP-1](./aup1-account-profile-personal-preferences.md) (functionality) · [UIX-6](./uix6-interaction-progressive-disclosure-system.md) (interaction system)
**Authoritative guides:** [management-ui-design-system.md](../01-architecture/management-ui-design-system.md) · [interaction-patterns.md](../design-system/interaction-patterns.md)

---

## 1. Executive summary

UIX-7 rebuilt the Management UI's visual language on a **symmetric light/dark token system** and migrated
**every operator-facing route** plus the shared component layer onto design-system primitives. It is the
final bullet of roadmap Track 1 ("Final visual / UI pass, after major surfaces exist").

What was actually delivered:

| Delivered | Evidence |
|---|---|
| Both themes rebuilt as **sibling token sets** | `.dark` (`app.css` 2324–2462) overrides **all 135** theme-dependent tokens defined in the light block, with **zero** dark-only tokens. The 6 light-only tokens are theme-*independent* by design (3 font families, 3 radii) — see §3.1 |
| Primitive layer nearly tripled | `management-ui-primitives.css` **+1014 / −252**; `app.css` **+612** |
| Site-wide restyle in 9 reviewed batches | 62 `Pages`, 14 `Molecules`, 8 `Organisms`, 3 `Atoms`, 1 `Layout` file changed |
| A **new automated guard** | `NoRawColorUtilitiesTests` was created by UIX-7's first commit (`df7e8f7`) and walks every `.razor` file under the client |
| AUP-1's handoff checklist honoured | Account pages moved off `rounded border border-default bg-surface p-4` onto `ui-settings-card`; `AccountSectionNav` adopted a real `ui-tabs` primitive |
| Design-system docs updated | Theme table, token families, anti-patterns, and batch status in `management-ui-design-system.md`; working catalog in `.planning/management-ui/pattern-catalog.md` |

What UIX-7 did **not** do, and did not claim to:

- It did not reduce the raw `<details>` inventory carried over from UIX-6 §12 (14 blocks in 9 files remain).
  Instead it made them **colour-correct** via a global fallback rule, without giving them disclosure chrome.
- It did not add component-render (bUnit) tests, contrast measurements, or a recorded operator smoke pass.
- It did not tokenize shell chrome layering (`.ui-topbar` and `.ui-menu-panel` still use raw `z-index`,
  and the nav rail / control panel carry raw `z-20`/`z-30`/`z-40`/`z-60` utilities in markup).
- It did not migrate **8 tables** on `RunDetail` and `IntegrationNotifications` onto `ui-data-table`.
  These are the audit's only genuinely *unstyled* surfaces — see §5 and §9(17).
- It did not restore a Tailwind build pipeline. `app.css` is a **checked-in build artifact with no
  source**: there is no `tailwind.config.*`, `package.json`, `postcss.config.*`, or input CSS anywhere in
  the repository (verified). Consequently the hand-authored token layer and the generated utility layer
  live in the same unregenerable 3,191-line file — see §2.1.

**Disposition:** UIX-7 is complete. Residual items are Category B/C/D (see §9) and do not constitute a
coherent operator-facing gap. **No UIX-8 is justified.** See §13.

---

## 2. Final visual architecture

### 2.1 Where tokens live

Only two stylesheets exist, loaded in order (`_Layout.cshtml` dev host, `index.html` WASM):

| File | Role |
|---|---|
| `wwwroot/css/app.css` | Tailwind v4 build output **plus** the hand-authored semantic token blocks |
| `wwwroot/css/management-ui-primitives.css` | All `ui-*` primitives; consumes tokens, defines no palette |

`app.css` is a hybrid: generated utility classes coexist with the authored token blocks around lines
2170–2462. This matters for §9 — generated `dark:*` palette utilities exist in the compiled output even
though no `.razor` file uses them.

**`app.css` cannot currently be regenerated, and this is the audit's most consequential structural
finding.** Line 1 identifies it as Tailwind v4.1.8 output, but a repository-wide search finds **no**
`tailwind.config.*`, **no** `package.json`, **no** `postcss.config.*`, and **no** input/source CSS. The
file is therefore simultaneously the generated artifact *and* the sole copy of the hand-authored token
layer:

| Lines | Nature |
|---|---|
| 1–118 | Generated Tailwind `@layer theme` (palette tokens) |
| 119–263 | Generated preflight / base reset |
| 264–2169 | Generated `@layer utilities` |
| **2170–2462** | **Hand-authored `:root` + `.dark` semantic tokens** |
| **2463–2902** | **Hand-authored base rules and semantic utilities** |
| 2903–3191 | Hand-authored `@layer components` + generated `@property`/`@keyframes` |

Two consequences run through the rest of this audit. First, every `app.css` cleanup in §9 requires
hand-editing a generated file rather than adjusting a build config — which is why the dead-CSS findings
are recorded but **not** recommended for removal (§13). Second, missing utilities cannot simply be
generated: `management-ui-primitives.css:924` carries the comment *"Above scrollable page content and
page-header toolbars (Tailwind z-30 is not in app.css)"* — a raw `z-index: 60` written specifically to
work around this. Restoring or explicitly documenting the pipeline is a prerequisite for the cleanup
work, not an optional extra.

> **RESOLVED — see [§15 CSS pipeline closure](#15-css-pipeline-closure-resolves-21).** The reproducibility
> defect described above has been fixed: the authored layer now lives in `styles/theme.css`, and
> `wwwroot/css/app.css` is regenerated from committed source by a pinned Tailwind 4.1.8 standalone CLI.
> The dead-CSS finding in §9(4) was cleared as a side effect of the first regeneration — 279 unused
> selectors dropped out because nothing references them.

### 2.2 Token hierarchy

Three tiers, consumed top-down:

1. **Raw palette / mix inputs** — `--color-tint-teal|sand|sage`, accent ramp, literal hexes such as
   `--color-bg: #f1efea` (light) / `#0a0c0f` (dark).
2. **Semantic role tokens** — `--color-surface`, `--color-text-heading`, `--color-table-header-bg`,
   `--color-row-hover-bg`, `--color-disclosure-bg`, `--color-metadata-label-bg`. Many are derived with
   `color-mix()` from tier 1, which is why the two themes stay in step: e.g.
   `--color-row-hover-bg: color-mix(in srgb, var(--color-accent-wash) 80%, var(--color-surface) 20%)`
   in light and `90%/10%` in dark.
3. **Utility / primitive consumption** — semantic Tailwind-ish utilities (`bg-surface`, `text-heading`,
   `bg-table-header`, `hover:bg-row-hover`) and `ui-*` primitive classes.

### 2.3 Token groups present

| Group | Representative tokens | Notes |
|---|---|---|
| Surfaces | `--color-bg`, `--color-bg-sunken`, `--color-surface`, `--color-surface-elevated`, `--color-surface-muted`, `--color-surface-header` | Light reserves pure `#FFFFFF` for elevated surfaces only |
| Tables | `--color-table-header-bg`, `--color-row-hover-bg` | Added/retuned by UIX-7; dark variants are cool, not greige |
| Disclosure / metadata | `--color-disclosure-bg`, `--color-disclosure-border`, `--color-metadata-bg`, `--color-metadata-label-bg` | Added by UIX-7 to fix warm-greige bleed |
| Text | `--color-text-heading` … `--color-text-subtle`, `--color-fg` | |
| Borders | `--color-border-subtle`, `--color-border`, `--color-border-strong` | Ink-alpha light / white-alpha dark |
| Accent | `--color-accent`, `-hover`, `-wash`, `-line`, `-ring`, `-text`, `-on` | |
| Status tints | `--color-tint-{sage,sand,teal}` + `-line` + `-text`; `--color-error-light/-dark`, `--color-success*`, `--color-warning*` | Success/warning/error/pending/neutral/unknown/forbidden all map through `ui-status-pill--*` |
| Destructive | `--color-destructive`, `--color-destructive-fg` | |
| Buttons | `--color-btn-primary-*`, `--color-btn-secondary-*`, `--color-btn-primary-disabled-*` | |
| Chrome | `--color-rail-bg`, `--color-topbar-bg`, `--color-hamburger-line`, `--color-hover-overlay` | |
| Typography | `--font-sans`, `--font-serif`, `--font-mono` (`IBM Plex Mono` first) | Sizes/weights remain utility-driven, not tokenized |
| Radii | `--r-sm: 6px`, `--r-md: 9px`, `--r-lg: 14px` | |
| Elevation | `--inset-hi`, `--shadow-card`, `--shadow-segmented-active` | Redefined per theme (dark needs different treatment) |
| Focus | `--interaction-focus-ring` → `--color-accent-ring` | Applied as `outline: 2px solid` |
| Motion | `--interaction-duration: 150ms`, `--interaction-easing: cubic-bezier(0.2,0,0.2,1)` | UIX-6 origin, retained |
| Layering | `--z-dialog-scrim: 80`, `--z-dialog: 81`, `--z-toast: 100` | UIX-6 origin, retained |
| Dialog geometry | `--dialog-width-sm/md/lg`, `--dialog-radius`, `--dialog-margin`, `--dialog-scrim-color` | |

**Spacing is not tokenized.** Rhythm is expressed through primitives (`ui-page-stack` gap, section
padding) and utility classes. This is a deliberate, coherent choice, not an omission — but it means
"spacing" guidance in the docs is pattern-level, not token-level.

### 2.4 Hard-coded visual values in feature code

Effectively eliminated. Verified by repository-wide search of all `.razor` under the client:

| Check | Result |
|---|---|
| `dark:` variants in `.razor` | **none** |
| Hex / `rgb(` / `hsl()` in `.razor` | **none** |
| Raw Tailwind palette utilities (`bg-slate-*`, `text-red-500`, …) | **none** |
| Arbitrary colour values (`bg-[#…]`) | **none** |
| Inline `style=` | **1** — `RunOperationItemizationPanel.razor:325` `style="min-width: 12rem;"` (layout, not colour) |

---

## 3. Light and dark themes

### 3.1 Parity — measured, not asserted

Diffing the full authored light block (`app.css` 2175–2323) against the `.dark` block (2324–2462):

```
light authored tokens: 141
dark  authored tokens: 135
defined in LIGHT only:   6   (--font-sans, --font-serif, --font-mono, --r-sm, --r-md, --r-lg)
defined in DARK  only:   0
```

**Every one of the 135 theme-dependent tokens is overridden in dark, and dark introduces none of its
own.** The 6 light-only tokens are the three font families and three radii — deliberately
theme-independent, and correctly *not* duplicated in `.dark`. So the meaningful figure is 135/135 role
coverage with 0 dark-only tokens, not 141/141.

Two caveats so this is reproducible. First, the light tokens sit inside the same `:root, :host` rule
Tailwind v4 uses for its own theme layer, so a naive count of that whole region returns 265 — the extra
124 are Tailwind's built-in palette and scales, which dark neither overrides nor should. Second, where
you start the light slice changes the answer: `--inset-hi` and `--shadow-card` sit at 2184–2185 among
the shape tokens, so a slice beginning at 2186 mis-reports them as dark-only, and a slice beginning at
2184 excludes the fonts and radii and yields a tidy but misleading 135/135 with zero asymmetry. The
line ranges above are the correct ones.

This is the strongest single piece of evidence in the audit. Because the same token *names* carry both
themes and markup never branches, **a page cannot be styled for one theme only**. Structural parity is
therefore guaranteed by construction for every surface using tokens — which, per §2.4, is all of them.

### 3.2 System appearance

Resolution chain (documented in `management-ui-design-system.md` §Accessibility and AUP-1 §8):

**account preference → this browser's `localStorage` mirror → platform default (`System`)**, where
`System` resolves to `prefers-color-scheme` at paint time. A pre-boot inline script in
`index.html` / `_Layout.cshtml` reads `localStorage` and toggles `html.dark` **before first paint**,
which is what prevents a wrong-theme flash. `ThemeService` re-applies on OS change while preference is
`System`; `AppearancePreferenceCoordinator` pushes the account preference through `ThemeService`.
`ThemePreferenceParserTests` (3 facts) covers preference parsing.

### 3.3 Semantic-state equivalence

Both themes define the full status vocabulary (`ui-status-pill--success|warning|error|pending|neutral|unknown|forbidden`)
from tint families. Light uses tint fills with matching `-line`/`-text`; dark derives mixes from accent
and status so chips stay legible on a cool-black floor rather than reusing light tints.

### 3.4 Defects found and fixed during UIX-7

Two real dark-theme parity defects were found by operator review during implementation and fixed:

1. **Warm-greige bleed on cool-black surfaces** — Runs table header, row hover, and run-detail metadata
   label bars were inheriting warm light-theme surfaces. Fixed by introducing the dedicated
   `--color-table-header-bg`, `--color-row-hover-bg`, `--color-metadata-bg`,
   `--color-metadata-label-bg` token pairs (commit `f3a8ced` and follow-ups), and finally by migrating
   `RunsList`/`SchedulesList` row hover from `hover:bg-surface-muted` to `hover:bg-row-hover` in the
   final sweep (`0a14b1e`).
2. **Browser-painted `<summary>` in remaining raw `<details>`** — browsers rendered summaries like
   visited links (brown/maroon), clashing with both themes. Fixed globally at
   `management-ui-primitives.css:419–441` with an explicit comment naming the cause.

### 3.5 Residual asymmetry

**None found in tokens.** Dark introduces no tokens of its own and leaves none of the 135 theme-dependent
roles unoverridden. The 5 orphaned tokens in §9(16) are dead in *both* themes, so they carry no parity
consequence either. The one area where the two themes are not equally *proven* is empirical contrast: no
measured contrast ratios exist for either theme (§7).

---

## 4. Shared component changes

### 4.1 Migration strategy

UIX-7 used a **batch-and-review** strategy: nine commits, each scoped to a route family, each visually
reviewed by the operator before the next began. The mechanism was consistently *substitution*, not
redesign — ad-hoc markup was replaced by an existing primitive, and a new token was introduced only when
a **role** was missing in both themes.

Notably, the **final sweep** (`0a14b1e`) targeted shared `Organisms`/`Molecules` rather than pages,
which is where the last real inconsistencies were hiding: the page-by-page batches had missed them
because they are not routes.

### 4.2 Responsibility moved into the design system

| Concern | Now design-system owned | Still feature-owned |
|---|---|---|
| Page scaffolding | `ui-page-stack`, `PageHeader` (+ eyebrow/toolbar slots), `ui-page-header__toolbar` | Which toolbar actions exist, and their order |
| Section surfaces | `ManagementSection`, `ui-section-panel`, `ui-detail-block`, `ui-settings-card` | Section titles, help copy, layer choice |
| Summary/cards | `ManagementSummaryCard`, `ui-summary-card-grid` | Card content and hrefs |
| Buttons | `.btn` / `btn-primary` / `btn-secondary` / `btn-danger`, `ui-toolbar-btn` (+ `--danger`) | Enablement, capability gating, confirm wiring |
| Inputs | `ui-input` on `input`/`select`/`textarea`, `TextInput` | Validation, binding, help text |
| Choosers | `ThemeCardGroup`, `SegmentedControl`, `SearchableList`, **`ui-tabs`** | Option sets |
| Status | `StatusPill`, `AttentionPill`, `ui-status-pill--*`, `ManagementUiSemantics` | Domain → semantic mapping |
| Tables | `ui-data-table`, `bg-table-header`, `hover:bg-row-hover` | Columns, row actions, sorting |
| Metadata | `MetadataGrid` / `MetadataItem`, `ui-metadata-item` | Which facts to show |
| Code/technical | `ui-code-block`, `ui-inline-code` | Payload content |
| Disclosure | `Disclosure`, `TechnicalDetails`, `ManagementAdvancedSection` | What is secondary |
| Dialogs | `ManagementDialog`, `ConfirmActionDialog` | Consequence copy, emphasis |
| States | `PageMessage` + `LoadingState`/`EmptyState`/`ErrorState`/`ForbiddenState`, `ui-state-panel` | Copy |
| Feedback | `ActionFeedback`, `ui-feedback-banner`, `ToastHost`/`ui-toast` | Result messages |
| Chrome | `ui-nav-rail`, `ui-topbar`, `ui-menu-panel`/`ui-menu-item`, `ui-breadcrumbs*` | Menu contents |
| Account/avatar | `AccountAvatar`, `BuiltInAvatarGlyph` (`currentColor`), `AvatarPicker` | Which avatar is selected |

### 4.3 Newly promoted in UIX-7

- **`ui-tabs` / `ui-tabs__link`** — `AccountSectionNav` was carrying its own classes at AUP-1 handoff;
  it now consumes a real primitive. This directly satisfies AUP-1 §20's conditional request.
- **`ui-settings-card` family** adopted across all four account pages (11–22 usages each).
- **Table and disclosure tokens** (§2.3) as first-class roles rather than reused surfaces.

---

## 5. Surface migration map

Classification from primitive-adoption counts plus inspection.

| Surface | Status | Evidence / residual |
|---|---|---|
| Dashboard / home | **Fully migrated** | `ui-page-stack`, `PageHeader`, 6 `ManagementSection`; no tables, no raw details |
| Integrations list | **Mostly migrated** | 2 `PageHeader`, 3 sections, `ui-data-table`, 8 `ui-input`; **1 raw `<details>`** (row diagnostics, `:292`) |
| Integration detail | **Mostly migrated** | 21 `ManagementSection`, 4 `ui-detail-block`, 12 `ui-input`, 2 `TechnicalDetails`; **1 raw `<details>`** (`:423`); retains the legacy key-value settings block (`:467`, a *product* gap — see §13) |
| Runs list | **Mostly migrated** | `ui-data-table`, row hover now tokenized; **1 raw `<details>`** (`:223`) |
| Run detail | **Mostly migrated with minor legacy styling** | 12 sections, 4 `ui-detail-block`; itemization panel migrated in final sweep. **But 4 tables (`:452, :506, :544, :582`) have no `ui-data-table` wrapper at all**, so they receive no header fill, cell padding, uppercase header treatment, row hover, or card border. This is the audit's most visible residual and it is on a primary surface — see §9(17) |
| Affected records (itemization panel) | **Fully migrated** | `ui-detail-block`, `ui-input`, `ui-status-pill`, `TechnicalDetails`; 1 inline `style=` for column width |
| Schedules list / detail | **Mostly migrated** | `ui-data-table`, tokenized hover; **1 raw `<details>`** carrying `@onclick:stopPropagation` (`:124`) — migration is genuinely non-trivial here |
| Notifications (hub, definition, instance, delivery, diagnostics) | **Fully migrated** | Batch 8; `ui-code-block`, `MetadataGrid`, `ui-data-table`; **1 raw `<details>`** in `NotificationDiagnostics:236` |
| Integration notifications (`IntegrationNotifications`) | **Mostly migrated with minor legacy styling** | Batch 8 migrated buttons, inputs, and detail blocks, but **4 tables (`:247, :385, :473, :511`) have neither a `ui-data-table` wrapper nor an `overflow-x-auto` container** — unstyled *and* a genuine narrow-viewport overflow risk (§7.2). Also **1 raw `<details>`** (`:206`) and 2 tint-backed action buttons |
| Integration schedule diagnostics | **Fully migrated** | Batch 8. Its 3 tables (`:160, :209, :353`) omit the `ui-data-table__table` class but **do** sit inside `ui-data-table` wrappers, which carry all the styling via descendant selectors; `__table` only sets `font-size: 0.875rem`, which `text-sm` already supplies. **Visually identical to compliant tables** — a naming nit, not a defect |
| Config / settings, auth, Entra, contacts, governance | **Fully migrated** | Batch 8 |
| Control Center | **Mostly migrated** | 27 sections, 3 `ui-data-table`, 6 `ui-detail-block`; **5 raw `<details>`** — the largest single cluster |
| Blocked Work | **Mostly migrated with minor legacy structure** | `ui-page-stack` + `ui-data-table`, but **no `PageHeader` and no `ManagementSection`** — the only reviewed operational route without the standard page scaffold |
| Demo Customers list / detail / activity | **Fully migrated** | `TechnicalDetails` ×2 each on detail/activity; `ui-detail-block` |
| Account / Profile / Preferences / Notifications / Security (AUP-1) | **Fully migrated** | `ui-settings-card` ×11/22/13/20; legacy panel pattern **absent**; deliberately *not* using `ui-page-stack`/`ManagementSection` because the settings shell is its own reference archetype |
| Developer / internal (`DeveloperTasks`, dashboard stubs, `NestedNavDemo`) | **Intentionally deferred** | `PageHeader` + sections present; dashboard management is roadmap-deferred and labelled as such. Visual identity is adequate; no work justified |
| Legacy route stubs (`OperationStart`, `WorkflowStart`) | **Fully migrated** | `ManagementSection` + `btn` |

**No surface is materially inconsistent.** Two recurring residuals: the raw `<details>` cluster (colour-correct
but without disclosure chrome), and the 8 unwrapped tables on `RunDetail` and `IntegrationNotifications`.

A note on how the table finding was resolved, because a naive grep overstates it. Searching for
`<table>` without `ui-data-table__table` returns **11** hits across 3 files. But all the actual styling
is applied by the **wrapper** through descendant selectors (`.ui-data-table thead th`,
`.ui-data-table tbody td`, `.ui-data-table tbody tr:hover`); `.ui-data-table__table` alone contributes
only `font-size`. Checking each call site's wrapper splits the 11 cleanly: **3 are wrapped and therefore
visually correct** (`IntegrationScheduleDiagnostics`), **8 are unwrapped and completely unstyled**
(`RunDetail` ×4, `IntegrationNotifications` ×4). Only the 8 matter.

---

## 6. AUP-1 visual integration

The historical split is intact and worth preserving in the record.

| Concern | AUP-1 (functionality) | UIX-7 (presentation) |
|---|---|---|
| Profile | Platform-owned editable display name, read-only IdP fields, dirty state, inline validation | Moved to `ui-settings-card`; `TechnicalDetails` ×2 and a `Disclosure` for roles/links |
| Avatar | Four curated inline-SVG illustrations; SkiaSharp upload pipeline, EXIF stripped, square PNG 64/128/256; confirmed removal | `AccountAvatar` sized by `SizeClass`; `BuiltInAvatarGlyph` themes through `currentColor`, so token changes flow automatically with no artwork edits |
| Notification preferences | Per-notification email toggle enforced by `SqlNotificationRecipientResolver`; in-app channel stated as required in prose rather than a disabled checkbox | Card-based rows; the stacked narrow variant AUP-1 asked to preserve is retained |
| Appearance & regional | Account-persisted appearance with documented precedence; 12/24-h clock; IANA-canonical timezone selector with browser detection | `ThemeCardGroup` swatches, `SegmentedControl`, `SearchableList`; 22 `ui-settings-card` usages — the densest reference surface |
| Security / account nav | `/account/{profile,notifications,preferences,security}` with redirects in | `AccountSectionNav` promoted onto the new `ui-tabs` primitive |
| User account menu | Menu-closing behaviour, top-bar adjustments (`3680730`) | `ui-menu-panel` / `ui-menu-item` chrome primitives |

**AUP-1 §20 handoff items — all three addressed:**

1. *"Account pages use plain `rounded border border-default bg-surface p-4` … straightforward migration
   candidates if UIX-7 introduces a card component."* → Migrated; that pattern is **absent** from
   `Pages/Account/`.
2. *"If UIX-7 introduces a canonical tab or segmented-control primitive, `AccountSectionNav` should adopt
   it."* → Adopted `ui-tabs`.
3. *"Keep a non-colour signal for selection when restyling."* → Preserved; the documented convention is
   `data-state="on"` plus an `sr-only`/badge marker, and the anti-pattern table still forbids a
   `· Selected` prose suffix.

AUP-1's own statement — *"No colours, typography, spacing, or component visuals were changed. That is
UIX-7"* (§201) — remains an accurate description of the boundary.

---

## 7. Responsive and accessibility findings

### 7.1 Validated by inspection

**Responsive.** A coherent breakpoint ladder exists in `management-ui-primitives.css`: `30rem`
(dialog narrow), `40rem`, `48rem` (primary desktop threshold, used by `ui-page-stack`, metadata grids,
summary grids), `56rem`, `64rem` (wide). Specific provisions found:

- Dialogs: `max-width: min(var(--dialog-width-*), …)` with `max-height: calc(100vh - 2*margin)` and a
  `@media (max-width: 30rem)` rule that releases the width cap — dialogs should not clip on mobile.
- Content measure capped (`max-width: 72ch`, `70ch`) for prose blocks.
- Metadata and summary grids collapse to one column below `48rem`.
- Tables are wrapped in `overflow-x-auto` — either directly or via `ui-data-table`, which sets it — at
  every call site **except the four in `IntegrationNotifications` (`:247, :385, :473, :511`)**, which
  have no scroll container at all. Those four are the audit's one **actual** responsive defect rather
  than a polish item: a wide table with no scroll ancestor forces horizontal overflow on the shell at
  narrow widths. Severity is bounded by it being a secondary, integration-scoped configuration surface.
- `ui-menu-panel` uses `max-width: calc(100vw - 2rem)`.

**Accessibility (structural).**

- Every interactive primitive **has** an explicit `:focus-visible` treatment — inputs, toolbar buttons,
  menu items, segmented items, theme cards, searchable-list options, dialogs, disclosure summaries. None
  suppress focus without replacing it. (The *consistency* of those treatments is a separate finding, §7.2.)
- Colour is not the sole carrier of meaning in the documented conventions: status pills combine fill,
  border, and **text label**; selection combines border/background/accent with an assistive marker.
- UIX-6 dialog semantics preserved (§8 below): `role="dialog"`, `aria-modal`, `aria-labelledby`,
  focus containment and restoration, Escape (blocked while busy), `aria-busy`.
- `PageMessage` uses `role="alert"` for errors, `role="status"` otherwise. Toasts are polite by default,
  assertive for errors.
- `AccountSectionNav` uses real links with `aria-current="page"`.

### 7.2 Residual — verification gaps, not known defects

These are stated as **unverified**, not as passing:

1. **No measured contrast ratios exist** for either theme, in the repository or in any audit. The dark
   theme was substantially re-toned during UIX-7 (including `--color-fg: #c6cfda` on `#0a0c0f`), so the
   values changed without measurement. **No WCAG claim is made here.**
2. **No runtime responsive verification** at any viewport tier for UIX-7 (§11).
3. **Reduced-motion coverage is narrow, not merely unverified.** There is exactly **one**
   `@media (prefers-reduced-motion: reduce)` block (`management-ui-primitives.css:660`) and it disables
   exactly **two** things: the `.ui-dialog` entry animation and the `.ui-disclosure__summary::before`
   marker transition. Against **14** `transition`/`animation` declarations in the two stylesheets — and,
   more importantly, the `transition-transform duration-300` utilities on `NavRail` and `ControlPanel`,
   which are the largest movements in the product — the preference is largely **not honoured**. The
   shell slide, hover transitions, and toast motion all continue at full amplitude. This is a real gap
   rather than an unverified provision, and it is cheap to close by broadening the existing block.
4. **Focus treatment is consistent in presence but not in mechanism.** Two different techniques are in
   use: `outline: 2px solid` + `outline-offset` (dialogs, disclosure summaries, toolbar buttons) and
   `outline: none` + `box-shadow: var(--ring)` (`.focus-ring`, `ui-input`, `ui-menu-item`,
   `ui-segmented__item`, `ui-theme-card`, `ui-searchable-list__option`, `ui-inbox-list__open`). The
   outline variants further draw from **three** different colour sources — `--color-accent`,
   `--interaction-focus-ring`, and a bespoke `color-mix(--color-primary 55%, --color-border 45%)` on
   `ui-toolbar-btn` — and there are three near-synonymous tokens (`--color-focus-ring`,
   `--color-glow-focus`, `--interaction-focus-ring`) all resolving to `--color-accent-ring`. Two
   consequences worth runtime checking: focus rings drawn as `box-shadow` are **clipped by any scrolling
   ancestor**, and `.ui-data-table` sets `overflow-x: auto`, so a focused control inside a table may
   have its ring cropped. Unverified — but it is a specific thing to look for in the §13 smoke pass, not
   a generic caveat.
5. **Touch target sizing is not audited.** `.btn` is `height: 37px`; `ui-toolbar-btn` and dense table
   actions are smaller. No convention or minimum is documented.
6. **No component-render (bUnit) tests exist** in the repository (UIX-6 §15.6), so keyboard and focus
   behaviour is covered by helper unit tests plus source-integrity assertions, not simulated interaction.
7. **Eight arbitrary font sizes bypass the type scale** — `text-[10px]` / `text-[11px]` in
   `EntraDirectoryUsers` (×2), `EntraDirectoryGroups`, `AvatarPicker` (×2), `PersonalNotifications`,
   `PersonalNotificationMenuItem`, `ScheduleDetail`. These are below the smallest scale step and are the
   audit's only text-size findings. Cause is identified in §8.1: the type-scale utilities
   (`text-helper`, `text-eyebrow`, `text-ui`, …) **exist in CSS but are entirely unused**, so authors
   reach for arbitrary values. `NoRawColorUtilitiesTests` bans arbitrary *colours* only, so these pass
   the guard legitimately.
8. **Raw `<details>` focus affordance** — the 14 remaining blocks get themed colour but **not** the
   `ui-disclosure__summary:focus-visible` outline, so their focus treatment is browser-default rather
   than the design-system ring. This is the one residual with a plausible (if minor) accessibility edge,
   and it is the strongest argument for finishing that migration opportunistically.

### 7.3 A note on the `.btn` alignment fix

During UIX-7 the operator reported top-aligned button text. Root cause was the base `.btn` class lacking
flex centring and carrying `line-height: 1.6`. It was fixed once, centrally (`display: inline-flex;
align-items: center; justify-content: center; line-height: 1`), which corrected every `.btn` consumer
without touching components. This is the design system behaving as intended and is worth recording as
evidence that the token/primitive layer is load-bearing rather than decorative.

---

## 8. Design-system documentation

### 8.1 What is now authoritative

Two documents, with a clean division:

| Document | Authoritative for |
|---|---|
| [`docs/01-architecture/management-ui-design-system.md`](../01-architecture/management-ui-design-system.md) | Semantic colours and token families, surfaces, buttons/actions, forms, status treatment, tables, metadata, layers/archetypes, light/dark theme strategy, anti-patterns, action-surface and feedback placement, batch status |
| [`docs/design-system/interaction-patterns.md`](../design-system/interaction-patterns.md) | Surface choice, progressive disclosure, dialogs and confirmation, feedback routing (toast vs inline vs page), help/tooltips, URL state, interaction accessibility requirements, deferred patterns, agent checklist |

Working inventory (not normative): `.planning/management-ui/pattern-catalog.md`,
`catalog-schema.md`, `disclosure-audit.md`.

Coverage against the required checklist:

| Topic | Covered | Where |
|---|---|---|
| Semantic colours | Yes | design-system §Token families, §Theme |
| Spacing | **Partial** | Pattern-level only (`ui-page-stack`, section padding); no spacing scale documented |
| Typography | **Partial** | Font-family tokens listed; no documented type scale. A scale *does* exist in CSS (`text-eyebrow`, `text-helper`, `text-ui`, `text-card-title`, `text-page-title`, `text-faint`) but is **undocumented and unused by any markup** — directly causing the arbitrary `text-[10px]`/`text-[11px]` values in §7.2(7) |
| Surfaces | Yes | §Theme, §Visual density (Epic 97), §Chrome primitives |
| Buttons / actions | Yes | §Form conventions, §Action surface, §Anti-patterns |
| Forms | Yes | §Form and control conventions, §Settings and chooser primitives |
| Status treatment | Yes | §Status semantics (full 7-value vocabulary) |
| Tables | Yes | `ui-data-table` in §Visual density; table tokens in §Token families |
| Interaction states | Yes | interaction-patterns §6, §11; focus in §Accessibility |
| Dark / light themes | Yes | §Theme (light/dark siblings), §Accessibility (appearance precedence) |
| Progressive disclosure | Yes | design-system §Progressive disclosure + interaction-patterns §4, §5 |
| Dialogs | Yes | interaction-patterns §7 |
| Responsive design | **Weak** | One line in design-system §Accessibility and responsiveness; the breakpoint ladder in §7.1 above is **undocumented** |
| Accessibility | Yes for interaction | interaction-patterns §11; design-system §Accessibility. No contrast or touch-target guidance |
| Anti-patterns | Yes | design-system §Anti-patterns + pattern-catalog (extended in the final sweep with four new entries and an explicit "still acceptable" list) |

### 8.2 Contradictions between documentation and implementation

Three found. All are documentation defects, not code defects.

1. **`confirm()` guidance is stale and directly contradicts UIX-6.**
   `management-ui-design-system.md` §Destructive actions says *"Keep existing **`confirm()`** /
   `ConfirmDeleteAsync` flows and permission gates unchanged."* But `interaction-patterns.md` §7 says
   *"Never use `window.confirm`"*, and the code agrees: no `window.confirm` exists anywhere in the
   client (the only match is a comment in `ConfirmActionDialog.razor` describing what it replaced).
   The design-system line should defer to `interaction-patterns.md`.
2. **`ui-tabs` is undocumented.** The primitive exists and is consumed by `AccountSectionNav`, but
   `ui-tabs` appears **nowhere** in `docs/`. The design-system doc still describes `AccountSectionNav`
   as *"Real links + `aria-current`; only tab pattern in shell today"* without naming the primitive it
   now uses — so a developer or agent adding a second tabbed surface would not discover it.
3. **`interaction-patterns.md` §12 forbids "arbitrary z-index values"**, but shell chrome carries two:
   `.ui-topbar { z-index: 60 }` and `.ui-menu-panel { z-index: 50 }`. The ordering is still coherent
   (menus 50 < topbar 60 < scrim 80 < dialog 81 < toast 100), so there is no visual defect — but either
   the rule should be scoped to overlays or these two should become tokens.

Additionally, the roadmap's Track 1 epic table stops at **UIX-6**; there is no UIX-7 row, and no
UIX-7 audit existed before this document.

---

## 9. Legacy styling inventory

Categorised as requested: **A** = design-system violation · **B** = reasonable feature-local styling ·
**C** = opportunistic migration only · **D** = dead/obsolete.

| # | Finding | Count | Cat. | Notes |
|---|---|---|---|---|
| 1 | Hard-coded colours in `.razor` | **0** | — | Verified across all client `.razor` |
| 2 | Raw Tailwind palette utilities in `.razor` | **0** | — | Guarded by `NoRawColorUtilitiesTests` |
| 3 | `dark:` variants in `.razor` | **0** | — | Guarded |
| 4 | Dead CSS in `app.css` — now quantified | **~320 declarations** | **D** | 144 compiled palette utility classes (`.bg-slate-900`, `.text-red-800`, …), 69 palette `@layer theme` tokens, and 63 `.dark\:*` utilities, **none referenced by any `.razor`**. Normally a build-config fix (content/safelist) — but per §2.1 **there is no build config**, so removal means hand-editing generated output. **Recommend leaving in place** until the pipeline question is resolved; the cost is stylesheet size only |
| 5 | Raw `<details>` outside `Disclosure`/`ManagementAdvancedSection` | **14 blocks / 9 files** | **C** | `ControlCenter` ×5 (99, 263, 318, 328, 348); `IntegrationConfigurationOverview` ×2 (160, 208); `IntegrationsList` (292); `IntegrationDetail` (423); `IntegrationNotifications` (206); `RunsList` (223); `SchedulesList` (124, carries `@onclick:stopPropagation`); `NotificationDiagnostics` (236); `ExecutionStart` (237). Unchanged from the UIX-6 §12 baseline. Colour-corrected globally at primitives `:419–441`; no disclosure chrome or focus ring |
| 6 | Inline `style=` | **1** | **B** | `RunOperationItemizationPanel.razor:325` `min-width: 12rem` on a `<td>` — layout, no colour, no token equivalent |
| 7 | Raw layering values — CSS **and** markup | **4 CSS + 5 markup** | **C** | CSS: `.ui-topbar` 60, `.ui-menu-panel` 50, plus two legitimate local `z-index: 1` contexts. **Markup (not previously counted):** `NavRail` `z-60` and `z-40`, `ControlPanel` `z-20` and `z-30`, `NavRailItem` `z-10`. Only 3 of the model's tiers are tokenized (`--z-dialog-scrim/-dialog/-toast`); the entire shell stack is magic numbers split across two languages. Ordering is still coherent, and the UIX-6 guard only bans *arbitrary* `z-[…]`, so standard utilities pass legitimately. See §8.2(3) |
| 8 | Token-backed but non-primitive panels | **2** | **B** | `WizardFlowPanel.razor:17` (`bg-surface-muted/30` list row) and `NotificationPipelineCutoverStatusCard.razor:52` (tint-backed preflight row, `bg-success-light`/`bg-warning-light`). Converting these to `ui-detail-block` would nest padding and flatten the tint semantics |
| 9 | Chrome hover overlays using `hover:bg-surface-muted` | ~8 | **B** | `ControlPanelNavNode`, `UserAccountMenu`, `ThemeMenu`, `PersonalNotificationMenu`, `ControlPanel` resize handle, `AccountAvatar` fallback. Token-backed and appropriate for chrome; row-hover semantics do not apply |
| 10 | Table cell padding utilities (`px-2 py-1`) | several | **B** | Inside `ui-data-table` wrappers; density is a per-table decision |
| 11 | Tint-backed action buttons (`bg-success-light`, `bg-message-warning`) | 3 | **B** | `IntegrationNotifications:183,193`, `UserDetail:157` — semantic affirm/warn affordances, not generic buttons |
| 12 | `!important` | **0** | — | |
| 13 | Duplicated button styling (hand-built `rounded`/`px-`/`py-` instead of `btn`/`ui-toolbar-btn`) | **13** | **C** | **Corrected from an earlier "0 remaining" reading**, which had checked only the one `rounded bg-surface-muted px-3 py-2` recipe removed in the final sweep. All 13 use semantic tokens, so none is a colour violation — this is *structural* duplication. Two clear clusters: an **icon-button** (`rounded p-2 … hover:bg-surface-muted`) copied 4× across `ThemeMenu`, `UserAccountMenu` ×2, `PersonalNotificationMenu`; and a **tab-button** (`px-3 py-2 text-sm font-medium` + `border-b-2 border-body -mb-px`) copied 3× across `PlatformSettingsCategoryTabs` and `ConnectionsPage` ×2 — the latter duplicating what `ui-tabs` already solves (§8.2(2)). Remainder: `HamburgerToggle`, `SettingInfoAffordance`, `UserAccountMenu:23`, `UserDetail:157`, `IntegrationNotifications:183,193` |
| 14 | Raw inputs without `ui-input` | **0** | — | Confirmed by full enumeration of every `input`/`select`/`textarea` (excluding checkbox/radio/file/hidden). The single apparent miss, `Atoms\TextInput.razor:4`, composes `ui-input` into `CssClass` at line 22. Adoption is genuinely complete |
| 15 | `BlockedWork` missing `PageHeader`/`ManagementSection` | 1 route | **C** | Structural, not visual-token; page reads acceptably today |
| 16 | Orphaned theme tokens | **5 pairs** | **D** | `--color-accent-warm`, `--color-accent-earth`, `--color-success-dark`, `--color-info-dark`, `--color-selected-fg` — each defined in *both* themes, **zero `var()` references** repo-wide (verified individually against a live control token). Symmetrically dead, so no parity impact. Note the first two are the warm-accent names from the palette that UIX-7's dark retone moved away from. Separately, `NoRawColorUtilitiesTests` bans four retired literals (`#2F3E52`, `#3B4A5F`, `#2563eb`, `rgba(0, 0, 0, 0.25)`) and they are absent |
| 17 | Tables without `ui-data-table` styling | **8 real** (11 raw hits) | **A** | `RunDetail` ×4 (`:452, :506, :544, :582`) and `IntegrationNotifications` ×4 (`:247, :385, :473, :511`) have **no wrapper**, so they get no header fill, cell padding, uppercase headers, row hover, or card border — 35 other tables do. The 3 in `IntegrationScheduleDiagnostics` are wrapped and therefore visually identical to compliant tables (§5). **The highest-value residual in this table**: `RunDetail` is a primary operator surface, and the fix is 8 wrapper/class edits |
| 18 | Duplicated warning-callout panel | **5 of 7 panels** | **C** | `rounded border border-message-warning bg-message-warning px-3 py-2 text-xs` hand-built in `ExpectedRunMissingCutoverConfigWarnings`, `NotificationDefinitionPostCreatePrompt`, `RoleDetail` ×2, `IntegrationScheduleDiagnostics:46`. Token-correct but begging for one `ui-callout--warning` primitive. The other 2 panels are row (14) and (8) findings |
| 19 | Unused `ui-*` primitives | **4** | **D** | `ui-action-row--end`, `ui-settings-card--quiet`, `ui-selected`, `ui-row` — defined, never consumed. Notable only because these are the *intended* vocabulary |
| 20 | Unused hand-authored semantic utilities | **44** | **D** | Includes the whole `bg-primary`/`bg-secondary`/`text-primary`… family (12), 7 status fills, and the **6 type-scale utilities** whose disuse causes §7.2(7). Careful: `bg-table-header` and `bg-surface-raised-hover` are dead as *utilities* but their backing tokens are live inside primitives CSS — delete class, keep token |
| 21 | Stale second WASM mount point | **1** | **D** | `wwwroot/index.html` carries its own `<div id="app">` + `blazor.webassembly.js`, duplicating `Server/Pages/_Layout.cshtml`. Not on the served path, so inert today — but it is an unguarded copy of the pre-boot theme script and mount contract, i.e. a latent UIX-2 "one WASM mount" divergence if it drifts. No test covers it |
| 22 | Arbitrary font sizes bypassing the type scale | **8** | **C** | See §7.2(7). Passes `NoRawColorUtilitiesTests` legitimately (colour-only guard) |
| 23 | Raw colours in primitives CSS outside token blocks | **3** | **B** | `management-ui-primitives.css:1374–1382` — the theme-preview swatch gradients (`#f5f6f8`, `#07090b`, `#0a0c0f`). These *depict* the themes rather than consume them, so literals are correct here. The only such instance in 1,909 lines |

**Category A: one finding, row 17** — the 8 unwrapped tables. An earlier reading of this audit recorded
"no Category A outstanding"; that was wrong, and the correction is deliberate. These tables produce
visibly different output from the other 35 in the product, on `RunDetail`, which is a primary operator
surface, and 4 of them additionally lack any scroll container. That meets the bar for a violation rather
than an opportunistic migration.

It does **not** change the §13 disposition. The fix is 8 wrapper/class edits in 2 files — defect-sized,
and it is listed as closure action 1. The remaining 21 findings are Category B (correct feature-local
choices), C (cosmetic or structural parity), and D (dead code, now fully quantified rather than
"not determined").

**One methodological caution for future sweeps.** Three of this section's findings — rows 13, 14, and 17
— were each initially mis-counted in *both* directions: row 13 read as 0 when 13 exist, row 14 read as 1
when 0 exist, row 17 read as 11 when 8 matter. In each case a grep for a *class name* diverged from the
*rendered result*, because `ui-data-table` styles descendants from a wrapper and `TextInput` composes
`ui-input` in C#. Primitive-adoption counts are a starting point for this codebase, not evidence.

---

## 10. Automated validation

Exact results, run against the audited tree.

| Check | Result |
|---|---|
| `Apperture.Management.Ui.Client` build | **Succeeded**, 0 errors, 2 warnings (pre-existing `CS8604` in `NotificationDefinitionEdit.razor:126` and `IntegrationNotificationDefinitionCreate.razor:69` — unrelated to UIX-7) |
| `Apperture.Management.Ui.Shared.Tests` | **674 passed, 0 failed, 0 skipped** (507 ms) |
| `Apperture.ArchTests` | **Not run** — build blocked |
| `Apperture.UnitTests` | **Not run** — not attempted after the Arch failure |
| `Apperture.IntegrationTests` / `EndToEndTests` | **Not run** |

**Why Arch/Unit could not run.** `MSB3027` / `MSB3021`: `Apperture.RuntimeHost.Windows.WorkerHost`
(PID 66656) was running from the developer's Aspire session and held a lock on
`Unisa.CampusNexus.PaymentPlans.Persistence.dll`. A retry with `-m:1` reproduced it; an earlier attempt
also hit an unrelated `MSB4166` child-node crash. **This is the known repository pitfall**, recorded
identically in UIX-6 §13 and in the roadmap re-entry §7 — not a code failure. UIX-7 touches only
`Apperture.Management.Ui.Client`, `Apperture.Management.Ui.Shared`, and the two stylesheets, all of
which built and tested cleanly. Arch tests assert assembly dependency rules and are not affected by
CSS or markup. **Re-run both suites with the Aspire session stopped before treating UIX-7 as fully
verified.**

### UI test inventory (all within the 674)

| Test class | Facts | Asserts |
|---|---|---|
| `NoRawColorUtilitiesTests` | 2 | **Created by UIX-7** (`df7e8f7`). Fact 1 walks every `.razor` under the client (excluding `bin`/`obj`), failing on arbitrary colours, raw palette utilities, `dark:` variants, interpolated class fragments, and hex inside `class=`/`style=`. Locates the repo root by ascending to `AGENTS.md`, so it is a **real** guard, not a fixture. Fact 2 is narrow: it bans four specific retired literals in the primitives CSS |
| `Epic97UiVisualPolishTests` | 7 | Visual density/polish source integrity (pre-UIX-7, Epic 97) |
| `Epic93UiInteractionPrimitiveTests` | 4 | Toolbar/metadata/section-action primitive usage |
| `Uix6InteractionSystemTests` | 22 | Disclosure/dialog integrity: no production `window.confirm`, migrated pages use `ConfirmActionDialog`, representative pages use `TechnicalDetails`, no `z-[` arbitrary values, no panel/drawer/popover/accordion component added, pattern guide exists |
| `Uix2ManagementBreadcrumbStabilizationTests` | 38 | UIX-2 shell/breadcrumb stability |
| `Uix3AffectedRecordNavigationTests` | 14 | UIX-3 anchors/return context |
| `Uix4ContextualBreadcrumbsTests` / `Uix4LinkIntegrityTests` | 13 / 14 | UIX-4 breadcrumbs and link integrity |
| `ThemePreferenceParserTests` | 3 | Appearance preference parsing |
| `Aup1AccountUiSurfaceTests` | — | Modified by UIX-7 (+31/−) to follow markup into the new primitives |

**Gap:** there is **no UIX-7-specific structural guard** beyond `NoRawColorUtilitiesTests`. Nothing
asserts, for example, that inputs carry `ui-input`, that tables use `ui-data-table`, or that no
hand-built button classes reappear. The sweep found violations in shared organisms precisely because no
test covered them. A small source-integrity test would cheaply prevent regression — recommended in §13
as maintenance, not an epic.

### UIX-2 … UIX-6 constraint preservation

| Constraint | Status | Evidence |
|---|---|---|
| No `window.confirm` (UIX-6) | **Preserved** | Zero occurrences; only a descriptive comment in `ConfirmActionDialog.razor:3` |
| No arbitrary `z-[` values (UIX-6 guard) | **Preserved** | Guard green; no `z-[` in `.razor` |
| No panel/drawer/popover/accordion added (UIX-5/6 deferral) | **Preserved** | Guard green |
| Disclosure remains semantic `<details>`, no JS, no URL state | **Preserved** | `Disclosure.razor:7` |
| Interaction/layering tokens centralised | **Preserved** | `--z-dialog*`, `--z-toast`, `--interaction-*` intact; two chrome exceptions noted in §9(7) |
| One WASM mount / no route-dependent shell remounts / plain breadcrumbs (UIX-2–UIX-4) | **Preserved per test evidence** | `Uix2*` (38), `Uix3*` (14), `Uix4*` (27) all green within the 674. Not independently re-derived from source in this audit |

---

## 11. Runtime smoke

**No UIX-7 runtime smoke is recorded in the repository.** No smoke section, checklist result, or console
gate outcome exists for UIX-7 in `docs/`, and no UIX-7 audit existed before this one. To be explicit:
this audit does **not** claim runtime validation.

What *is* known, and its provenance:

| Observation | Source | Recorded in repo? |
|---|---|---|
| Each of the nine batches was visually reviewed and approved by the operator before the next began | Implementation session history | **No** |
| Two real visual defects were found by that review and fixed: `.btn` text top-alignment, and warm-greige surfaces on dark-theme Runs table header / row hover / metadata label bars | Session history; fixes visible in `f3a8ced` and `0a14b1e` | Fixes yes; the review that found them, no |
| An Aspire `TaskCanceledException` during a rebuild was resolved operationally by an AppHost restart | Session history | **No** |
| A Blazor WASM `GenerateWasmBootJson` duplicate-key failure was cleared by `dotnet clean` + full build | Session history | **No** |

This is genuine operator review, and it is why the theme defects were caught at all — but it is
*iterative visual review during implementation*, not a structured smoke pass against a checklist. The
following remain unexecuted for UIX-7: viewport-tier verification (wide / laptop / tablet / narrow),
keyboard traversal and focus-ring visibility on restyled controls, reduced-motion behaviour at runtime,
contrast measurement in both themes, and a browser-console cleanliness gate.

A closure checklist is proposed in §13 so this gap can be closed in one short session rather than by
opening an epic.

---

## 12. Known limitations

Real gaps only.

1. **No contrast measurement in either theme.** The dark palette was substantially re-toned without
   measured ratios. No WCAG conformance is claimed anywhere in this audit.
2. **No recorded runtime smoke for UIX-7** (§11), including no responsive-tier and no reduced-motion
   runtime verification.
3. **Arch and Unit suites unverified** due to the file-lock pitfall (§10). Low risk for a CSS/markup
   epic, but unproven.
4. **14 raw `<details>` remain** in 9 files. Colour-correct, but without disclosure chrome or the
   design-system focus ring — the one residual with a minor accessibility edge.
5. **No structural regression guard** for primitive adoption beyond colour (§10). The final sweep's
   findings in shared organisms are direct evidence that page-scoped review misses shared components.
6. **Spacing and typography are not tokenized** and not documented as scales; both are pattern- and
   utility-driven.
7. **Touch-target sizing is unaudited and undocumented.** `.btn` is 37px; toolbar and dense table
   controls are smaller.
8. **`app.css` has no build pipeline and cannot be regenerated** (§2.1). No `tailwind.config.*`,
   `package.json`, `postcss.config.*`, or input CSS exists. The hand-authored token layer lives inside a
   3,191-line generated artifact, ~320 declarations of which are dead (§9(4)). This is the **largest
   structural debt** the audit found: it blocks the dead-CSS cleanup, and it has already forced one
   documented workaround (`management-ui-primitives.css:924`). It is a maintainability gap, not an
   operator-facing one — hence maintenance, not an epic.
9. **Reduced motion is largely not honoured** — 2 rules covered out of 14+ animated declarations, and
   the 300ms nav-rail / control-panel slides are excluded (§7.2(3)). Previously recorded as merely
   "unverified"; it is a real gap.
10. **Focus treatment is mechanically inconsistent** — two techniques, three ring colour sources, three
    synonymous tokens, and `box-shadow` rings that can be clipped by `ui-data-table`'s scroll context
    (§7.2(4)).
11. **8 tables render unstyled** on `RunDetail` and `IntegrationNotifications`, 4 of them without a
    scroll container (§9(17)). The audit's only Category A finding.
12. **Three documentation defects** (§8.2): stale `confirm()` guidance, undocumented `ui-tabs`, and the
    z-index rule contradicted by shell chrome — now also by 5 raw z-index utilities in markup. Plus no
    UIX-7 row in the roadmap, and an undocumented-and-unused type scale (§8.1).
13. **`BlockedWork` lacks the standard page scaffold** — structural inconsistency, not a token problem.
14. **No component-render tests exist** in the repository, so interaction behaviour is asserted by
    source integrity, not simulation (inherited from UIX-6 §15.6).
15. **A stale second WASM mount point** (`wwwroot/index.html`) duplicates the mount and pre-boot theme
    contract, unguarded by tests (§9(21)).

---

## 13. Roadmap recommendation

> ### **UIX-7 is complete; remaining visual migrations should be opportunistic.**

**Why not UIX-8.** A follow-up epic would need a coherent operator-facing gap. There is none:

- Token parity is **measured**: all 135 theme-dependent tokens are overridden in dark with zero dark-only
  tokens, so no surface can be single-theme styled.
- Every operator-facing route and the shared component layer are migrated (§5). No surface is classified
  materially inconsistent.
- The residual inventory (§9) contains **one Category A finding** — 8 unwrapped tables — and it is
  defect-sized: 8 class edits in 2 files, listed as closure action 1. One bounded defect on one surface
  is not a coherent operator-facing gap; it is a bug to fix. The other 21 findings are Category B, C,
  and D.
- The largest residual — raw `<details>` — was **explicitly designated opportunistic** by UIX-6 §12 and
  by the roadmap re-entry's deferred list (*"Remaining raw `<details>` migrations — opportunistic, per
  UIX-6 §12 — not an epic"*). UIX-7 not reducing it is consistent with that standing decision, not a
  regression against it.
- The project's own guidance is to prefer concrete product work over another indefinite polish arc.

**Why not an accessibility/responsive follow-up epic either.** This amendment found **two real
accessibility gaps**, not only verification gaps: reduced motion covers 2 of 14+ animated declarations
and excludes the largest movements in the shell (§7.2(3)), and focus rings use two mechanisms with three
colour sources, some clippable by table scroll contexts (§7.2(4)). Both are still closure actions rather
than epic material — the first is a few lines inside an existing `@media` block, the second is a
consolidation of existing rules with no new pattern required. The remaining items (contrast measurement,
touch targets, viewport-tier verification) are genuine verification gaps, and opening an epic to *check*
work is disproportionate. The right instruments remain a one-session closure checklist now, and Track 9
(*Final Release Readiness* — which already owns security review, docs completion, and RC validation) for
formal contrast/WCAG assessment. Elevating that into a named epic now would also pre-empt Track 9's
scope.

**What would change this verdict.** If the closure actions are not taken and the same findings resurface
at Track 9, the honest response is a short defect batch, still not an epic — none of these findings
requires new design, new patterns, or operator-visible rework beyond the 8 tables.

### Closure actions (maintenance, not an epic)

Small, bounded, and worth doing before the next epic starts. Ordered by value.

1. **Wrap the 8 unstyled tables** (§9(17)) — `RunDetail` ×4, `IntegrationNotifications` ×4: add the
   `ui-data-table` wrapper and `ui-data-table__table`. This is the only Category A finding, it is on a
   primary operator surface, and it simultaneously fixes the horizontal-overflow defect in §7.1. Eight
   edits.
2. **Broaden the reduced-motion block** (§7.2(3)) to cover the nav-rail and control-panel transforms and
   the remaining transitions — currently 2 of 14+. A few lines in one existing `@media` block.
3. ~~**Resolve the `app.css` pipeline question** (§2.1)~~ — **Done.** The pipeline was restored rather than
   declared hand-maintained; see [§15](#15-css-pipeline-closure-resolves-21). §9(4) dead CSS is cleared.
4. **Operator smoke, one session** — both themes × four viewport tiers on ~6 representative routes
   (Dashboard, Integration Detail, Run Detail + Affected records, Control Center, Account Preferences,
   Notification Diagnostics): keyboard traversal and focus-ring visibility, one destructive
   `ConfirmActionDialog` including Escape/failure path, reduced-motion on, console clean. Record the
   result as a §11 amendment to this document.
5. **Fix the documentation defects** in §8.2 — delete the stale `confirm()` sentence in favour of
   `interaction-patterns.md`, document `ui-tabs`, and either scope the z-index rule to overlays or
   tokenize the shell layers. Add two items found by this amendment: document the **type scale** that
   already exists in CSS (§8.1), since its invisibility is what produces the arbitrary `text-[11px]`
   values, and record the layering tiers that `ManagementInteractionSurfaces` already encodes in C# but
   CSS does not tokenize.
6. **Add a UIX-7 row** to the roadmap Track 1 epic table linking this audit, and mark Track 1's
   "Final visual / UI pass" complete.
7. **Add one source-integrity guard** extending `NoRawColorUtilitiesTests`: `<table>` sits inside
   `ui-data-table`, no hand-built icon-button/tab-button class recipes, and no arbitrary `text-[…]`
   sizes. Skip the `ui-input` assertion — adoption is already complete (§9(14)), and a naive version
   would false-positive on `TextInput`, which composes the class in C#. Guard the *rendered* contract,
   not the presence of a class name (see the §9 methodological caution).
8. **Re-run Arch + Unit** with the Aspire session stopped. `Apperture.ArchTests` references the UI client
   assembly directly, which is why the WorkerHost lock blocks it.
9. **Opportunistic, when a batch next touches the file:** raw `<details>` → `Disclosure`/`TechnicalDetails`
   (prioritise `ControlCenter`'s five); the 13 hand-built buttons, ideally by extracting the icon-button
   and tab-button clusters (§9(13)); one `ui-callout--warning` primitive for the 5 duplicated warning
   panels (§9(18)); the 8 arbitrary font sizes once the type scale is documented; `BlockedWork` page
   scaffold; delete the stale `wwwroot/index.html` after confirming nothing serves it (§9(21)).
   **Not** the dead-CSS removal — that waits on action 3.

---

## 14. Next product handoff

> ### **INT-1 — Integration Authoring Depth (module-defined settings schema)** — Track 8

**Why this is next.** With Track 1 closed by UIX-7 and Track 2 by AUP-1, this is the only remaining
**named, operator-visible, non-deferred** product gap that is architecturally ready:

- It is still visible in a live operator surface. `IntegrationDetail.razor:467` reads: *"Key-value pairs.
  Module-specific schema support is deferred."* An operator configuring a module instance types free-form
  key-value pairs with no typing, validation, or help — while SDK-6C/6D already shipped
  declaration-driven settings and readiness for *connections*. The asymmetry is real and reachable today.
- Every other candidate is explicitly gated:

| Candidate | Status |
|---|---|
| Track 4 — Systems / Services model | Deferred by ADR-0034; no forcing evidence |
| Track 5 remainder (platform `BusinessRecord`, cross-integration records) | BDS-1 gate: needs a **second** entity type to prove reuse. SDK-7 delivered the identity/lookup contract; the entity model stays deferred |
| Track 7 — Azure / deployment | Roadmap: *"Do not start this track as a substitute for product completeness"* |
| Track 9 — Final Release Readiness | Explicitly last |
| Inspection panel / drawer / popover / accordion | UIX-5 trigger criteria still unmet |
| Dashboard management, calendar scheduling | Deferred, and labelled as such in the UI |
| Schedule delete | Defect-sized, not an epic (roadmap re-entry §3); no ADR either way |

- The roadmap re-entry doc already parked this as candidate **C** (*"INT-1 — Integration authoring depth
  (module-defined settings schema)"*), verdict *"fold opportunistically"*. That verdict was correct when
  JOB-1 and SDK-7 were competing for the slot; both are now delivered, and it is what remains.
- Track 8 ("Module UI / configuration / template foundations") is the natural home, and the roadmap notes
  it "may interleave with Tracks 1 and 3" — both of which are now materially complete.

**Shape, briefly** (not a plan — a plan belongs in the epic): extend the module SDK settings declaration
to cover instance settings with types, requiredness, defaults, validation, and help; render it through
the existing declaration-driven pattern proven by SDK-6C/6D rather than a new schema framework; keep the
legacy key-value section as a fallback for modules that declare nothing, and remove the "deferred"
framing from the UI. Principal risk is SDK contract ripple to module authors, which argues for an
additive declaration and a compatibility path.

**Prerequisite worth confirming first:** whether SDK-6C/6D's declaration contract can carry instance
settings without a breaking change. If it cannot, the epic starts with a contract design slice.

---

## 15. CSS pipeline closure (resolves §2.1)

**Status:** Implemented · bounded maintenance, not an epic · closes §13 action 3 and §9(4).

### 15.1 Former reproducibility defect

`wwwroot/css/app.css` was simultaneously the generated Tailwind 4.1.8 artifact *and* the only copy of the
hand-authored token layer. `dotnet build`, publish, Aspire, and CI only ever **copied** it, and the
`build-ui` / `watch-ui` commands called an inert npm-oriented scaffold that printed
*"UI asset pipeline not configured yet"* and returned success. Deleting the file from a clean checkout
would have produced a green build and an unstyled UI, with no command able to recreate it.

### 15.2 Final source/output architecture

| File | Ownership |
|---|---|
| `Apperture.Management.Ui.Client/styles/app.css` | **Source** — Tailwind entry, explicit `@source` scan rules |
| `Apperture.Management.Ui.Client/styles/theme.css` | **Source** — authored `:root` / `.dark` tokens, base rules, semantic utilities, `.btn*`, `.focus-ring`, nav rail |
| `wwwroot/css/management-ui-primitives.css` | **Source** — authored `ui-*` primitives, unchanged and still loaded second |
| `wwwroot/css/app.css` | **Generated output**, committed |

The 775 authored lines (old `app.css` 2169–2943) moved verbatim into `theme.css`; no visual values were
changed. Automatic source detection is disabled (`@import "tailwindcss" source(none)`) so the scanned set
is explicit: client `**/*.razor` and `**/*.cs`, plus `Apperture.Management.Ui.Shared/**/*.cs` for
`Epic33UiDisplayHelpers.GetStatusCssClass`. Browser load order is unchanged.

### 15.3 Pinned Tailwind version and tooling

Official **Tailwind CSS standalone CLI 4.1.8** — the exact version in the pre-existing banner, so
regeneration was a like-for-like recompile. No Node, npm, `package.json`, Vite, or PostCSS.

The binary is downloaded on first use to `scripts/.runtime/tailwind/4.1.8/` (already gitignored, not
vendored) over HTTPS from the GitHub release, and verified against a SHA256 pinned in
`scripts/lib/Apperture.UiAssets.ps1` (values taken from the release's `sha256sums.txt`). A mismatch
deletes the download and throws. Hashes are pinned for Windows x64, Linux x64/arm64, and macOS
x64/arm64, so Linux CI needs no script change.

### 15.4 `build-ui`

One-shot compile of `styles/app.css` → `wwwroot/css/app.css`. Throws (non-zero exit) on unsupported
platform, download failure, checksum mismatch, missing source, or Tailwind error — it never falls back to
the stale committed stylesheet. Exposed unchanged via `Manage.ps1 -Area dev -Command build-ui` and the
deployment area.

### 15.5 `watch-ui`

Same pinned CLI with `--watch`, in the foreground so Ctrl+C stops it cleanly. Rebuilds on `.razor`, `.cs`,
and source-CSS changes, including newly used utility classes. It starts nothing else: Aspire, the
Management API, SQL, and the worker are untouched. **AppHost was not changed** — the watcher is optional
and normally runs in a second terminal.

### 15.6 Generated-file policy

`wwwroot/css/app.css` stays committed and now carries a `/*! … GENERATED FILE … */` marker naming the
sources and the rebuild command. Ordinary Tailwind comments are stripped by Lightning CSS, so the marker
is a bang comment and survives every rebuild including watch mode. Never edit it by hand; add tokens to
`styles/theme.css` and run `build-ui`.

### 15.7 CI policy

Unchanged this pass: CI remains `dotnet restore` / `build` / `test` and copies the committed output, so no
CI job downloads Tailwind. **Tradeoff:** a developer who changes a source stylesheet or adds a utility
class must run `build-ui` and commit the result, and nothing currently fails if they forget. The
deterministic staleness check for later adoption is documented in `scripts/README.md`:
`build-ui` then `git diff --exit-code -- "*wwwroot/css/app.css"`.

### 15.8 First-regeneration findings

Generated to a temporary file and compared before overwriting.

| Check | Result |
|---|---|
| Banner | `tailwindcss v4.1.8`, unchanged |
| Size | 88,514 → 62,709 bytes (−29%) |
| Light tokens | 141 → **141**, none missing |
| Dark tokens | 135 → **135**, none missing |
| Light-only | Exactly the 6 theme-independent tokens (3 fonts, 3 radii) |
| Dark-only | **0** — sibling model intact |
| Authored rules | `.btn*`, `.focus-ring`, `.nav-rail-btn*`, toast strips, `.bg-surface` sheen all present |
| Selectors removed | 279 |
| Selectors added | 88 |
| Determinism | Two runs byte-identical (`sha256 fe32f592…`) |

The **88 added** utilities are classes already used in markup that the stale artifact never contained —
the committed file had drifted behind the Razor it was supposed to style. The **279 removed** are the
dead palette quantified in §9(4): every one was checked against all `.razor`, `.cs`, `.cshtml`, `.js`, and
`management-ui-primitives.css`; 9 matched only as loose substrings and all 9 failed whole-token
verification (for example `container` appears solely in a JS comment). None was preserved.

### 15.9 Dynamic-class and safelist decisions

**No safelist was required.** Tailwind cannot see class strings assembled at runtime, so the composed
sites were verified explicitly against the new output: `ActionButton.BuildClass` (21 classes, including
every `btn-*` variant and state), `AccountAvatar` sizing, `PageMessage`, `Epic33UiDisplayHelpers`
status text classes, semantic aliases, toast strips, and nav rail — **0 missing**. This holds because each
site interpolates whole literal class names that appear in scanned source, and
`NoRawColorUtilitiesTests` already bans fragment interpolation (`$"bg-{…}"`). The one non-obvious case,
Shared-assembly literals, is covered by an explicit `@source` rather than a safelist. `ui-*` classes are
correctly absent — they belong to `management-ui-primitives.css`.

### 15.10 Tests

New `Uix7CssPipelineTests` (12 facts) guards: sources exist; the entry imports Tailwind and the theme and
scans the Shared assembly; the output carries the pinned version and the generated marker; tokens and the
authored `.btn`/`.focus-ring`/`.nav-rail-btn` rules are declared in source; no token exists only in
generated output; 141/135 counts hold; dark adds no tokens and light-only is exactly fonts plus radii;
primitives are neither imported nor scanned; and the script contains no npm scaffold while retaining a
pinned version and `Get-FileHash` verification.

Results: Management UI Shared **686 passed**, Unit **1,581 passed**, Arch **38 passed**, all zero failures;
`NoRawColorUtilitiesTests` and `Epic97UiVisualPolishTests` green; `Apperture.Management.Ui.Client` builds
with no new warnings; `build-ui` byte-reproducible across runs.

The §13 action 8 file-lock issue reproduced (`MSB3027` from a running dev stack) and is unrelated to this
change; Unit and Arch tests pass once the dev environment is stopped.

### 15.11 Runtime smoke

Verified against a running Aspire stack on `:9001` with `watch-ui` in a second terminal:

- The running UI served the regenerated stylesheet **byte-identical to disk without a rebuild or
  restart**, confirming static web assets resolve to the source path.
- A temporary `--color-accent: #ff00ff` edit in `styles/theme.css` was recompiled and served in under
  500 ms.
- A temporary `rotate-45` added to `Login.razor` — a utility absent from the previous output — was
  emitted by the watcher, confirming markup scanning works incrementally.
- Both temporary edits were reverted and `app.css` returned to the committed bytes
  (`sha256 fe32f592…`); `git diff` shows no stray visual change.
- The watcher performed 7 incremental rebuilds and started/stopped nothing else — Aspire, the API, SQL,
  and the worker were untouched throughout.
- All of `/`, `/dashboard`, `/runs`, `/account/preferences`, `/login` returned 200, and the host document
  still loads `css/app.css` before `management-ui-primitives.css`.

A Development-only cache header was added in `Management.Ui.Server/Program.cs`: `/css/*` now returns
`no-store, max-age=0` in Development, because the watcher rewrites `app.css` in place under an
unversioned URL and a normal refresh would otherwise serve the cached copy. Measured before the change
the response carried **no** `Cache-Control` header; after the change it carries `no-store, max-age=0`.
Production caching is unchanged.

**Verification limits.** No browser automation was available, so the smoke test was performed over HTTP
against the dev server plus file-level comparison. Route responses confirm the host document and
stylesheet delivery, but because the Management UI is a Blazor WebAssembly SPA these do **not** confirm
client-side rendering. Light/dark appearance, dialog behaviour, and browser-console cleanliness were
**not** visually confirmed; the token-level guarantees rest on the 141/135 parity checks, the unchanged
authored values, and `Uix7CssPipelineTests`. A visual pass in a browser is still worth doing.

One operational note: `watch-ui` passes `--watch=always` because the plain `--watch` form exits as soon
as stdin closes, which happens under non-interactive shells. In an interactive terminal Ctrl+C stops the
watcher normally; killing the wrapper process without a console signal can orphan the Tailwind child.

### 15.12 Remaining future option

**Razor/C# Hot Reload is still not wired and remains deliberately out of scope.** `.razor` edits need a
rebuild and a resource restart. The optional future step is `dotnet watch --project src/Apperture.AppHost`
for per-resource .NET hot reload; it is independent of this work and should not be conflated with the CSS
loop. Aspire's `defaultWatchEnabled` is the wrong tool — it restarts the whole topology.

---

## Appendix — evidence index

| Claim | Where verified |
|---|---|
| 9 commits, 105 files, +4405/−2337 | `git diff --shortstat 3680730 0a14b1e` |
| CSS churn: primitives +1014/−252, app.css +612 | `git diff --stat … -- "*wwwroot/css/*"` |
| 141 light / 135 dark authored tokens; 6 light-only (fonts + radii), 0 dark-only | Scripted diff of `app.css` light block (2175–2323) vs `.dark` block (2324–2462); see §3.1 caveats on slice boundaries |
| No Tailwind build pipeline exists (at audit time) | Recursive search for `tailwind.config.*`, `package.json`, `postcss.config.*`, and any input CSS across the repo (excluding `bin`/`obj`/`node_modules`) → **zero results**; only 5 `.css` files exist, 3 of them in `poc/`. **Superseded by §15** — a pinned standalone-CLI pipeline now exists |
| Pinned CLI hashes are official | `sha256sums.txt` asset on the `v4.1.8` GitHub release; Windows x64 = `c9117f6f…` |
| Regenerated output is deterministic | `build-ui` run twice → identical `sha256 fe32f592…`; 62,709 bytes |
| 279 removed selectors are genuinely dead | Each checked against all client/shared/server `.razor`, `.cs`, `.cshtml`, `.js` and `management-ui-primitives.css`; 9 loose substring matches, 0 whole-token matches |
| No safelist needed | Composed class sites (`ActionButton`, `AccountAvatar`, `PageMessage`, `Epic33UiDisplayHelpers`, toasts, nav rail) checked against new output → 0 missing |
| Source files are not deployed | `dotnet msbuild -getItem:None` / `-getItem:Content` → `styles/*.css` are `None` items, absent from `Content` and static web assets |
| 8 tables unstyled, 3 wrapped, 35 compliant | Enumerated every `<table class=…>` in client `.razor` (46 total), then inspected each non-compliant call site's 4 preceding lines for a `ui-data-table` wrapper |
| `.ui-data-table` styling is wrapper-based | `management-ui-primitives.css:867–905` — `thead`, `thead th`, `tbody td`, `tbody tr:hover` are all descendant selectors; `.ui-data-table__table` sets only `font-size` |
| 5 orphaned tokens | Per-token `var(--name)` search across both stylesheets and all client `.razor` → 0 each; control token `--color-table-header-bg` → 2, confirming the method |
| Reduced motion covers 2 rules of 14+ | Single `prefers-reduced-motion` block at `management-ui-primitives.css:660–668`; `transition`/`animation` declaration counts: `app.css` 3, primitives 11, plus markup-level `duration-300` utilities |
| Two focus mechanisms, three ring colours | `app.css:2886` (`.focus-ring`, `outline: none` + `box-shadow`); primitives `:349`, `:491`, `:534`, `:753` (outline variants), `:1013`, `:1253`, `:1317`, `:1361`, `:1509`, `:1805` (box-shadow variants) |
| Stale second WASM mount | `wwwroot/index.html:27,30` vs `Management.Ui.Server/Pages/_Layout.cshtml:74,77` |
| `NoRawColorUtilitiesTests` created by UIX-7 | `git log --diff-filter=A -- …/NoRawColorUtilitiesTests.cs` → `df7e8f7` |
| 674 tests passing | `dotnet test tests/Apperture.Management.Ui.Shared.Tests` |
| Arch blocked by PID 66656 file lock | `dotnet test tests/Apperture.ArchTests` → `MSB3027`/`MSB3021` |
| Zero `dark:` / hex / raw palette in `.razor` | Repository-wide grep of client `.razor` |
| 14 raw `<details>` in 9 files | Grep `<details` across client `.razor`, excluding the two primitive components |
| 1 inline `style=` | Grep `style=` across client `.razor` |
| No `window.confirm` | Grep across client `.razor` |
| `ui-tabs` undocumented | Grep `ui-tabs` across `docs/` → no matches |
| Account legacy panel pattern absent | Grep `rounded border border-default bg-surface p-4` in `Pages/Account/` → no matches |
| Reduced motion present | `management-ui-primitives.css:660`, `:665` |
| Layering tokens | `management-ui-primitives.css:289–301` |
| Raw z-index in chrome | `management-ui-primitives.css:925` (`.ui-topbar` 60), `:1768` (`.ui-menu-panel` 50) |
| Global raw-`<details>` colour fallback | `management-ui-primitives.css:419–441` |
| Legacy integration settings gap | `IntegrationDetail.razor:467` |
