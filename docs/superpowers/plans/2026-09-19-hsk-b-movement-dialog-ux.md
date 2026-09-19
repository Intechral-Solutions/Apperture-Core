# HSK-B — Resource Movement Dialog UX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax. This is a presentation/interaction refinement of an already-correct flow — keep diffs tight, reuse existing primitives, invent no second visual language.

**Goal:** Turn the functionally-correct but under-designed ownership-move dialog (`MoveOwnershipDialog.razor`) into a calm, structured, human-readable one: a prominent Source → Target header with the real select integrated into it, structured sections (Will move / Used by / Blocked by / Notes / History), Context *names* instead of raw IDs, and deliberate treatments for no-target / loading / allowed / blocked / error / busy / changed-since-preview states — readable in dark + light themes and at narrow widths.

**Architecture:**
- **Pure presentation logic** goes into a new `MoveOwnershipPresenter` in `Apperture.Management.Ui.Shared` (plain C#, next to `MoveOwnershipDialogModel`), unit-tested. It maps blocker/effect *codes + structured DTO fields* (+ a context-name resolver and an optional resolved-names dictionary) to operator copy. Server `Message` text is only a fallback for unknown codes. **No server, API, validator, transaction, gate, audit or persistence change.**
- **Model** (`MoveOwnershipDialogModel`) keeps all existing correctness machinery (generation guard, `CanSubmit`, submit-in-flight) and gains only: a computed `Phase`, a `ChangedSincePreview` flag set by a generation-guarded `ApplySubmitConflict`, and a `SubmitErrorKind` (Failed/Busy).
- **Client**: one narrow change — a typed `OwnershipMoveBusyException : InvalidOperationException` so the UI can tell the transient busy-gate 409 from other errors (the wire `code` is currently dropped). Wire contract unchanged; existing `catch (InvalidOperationException)` keeps working.
- **Razor/CSS**: restructure the dialog body using existing tokens/primitives (`ManagementDialog` Size `md`, `ui-input`, `PageMessage`, `LoadingState`, `StatusPill`, `DrillInLink`), new small `move-*` classes in `management-ui-primitives.css` only.

**Spec:** the HSK-B handoff brief (session instructions). Discovery (three read-only passes at HEAD `35fbae5`) is the factual basis; findings are cited inline.

**Tech stack:** C#/.NET 10, Blazor WASM client, xUnit + FluentAssertions. **No bUnit anywhere** — UI tests are (a) pure-logic unit tests on extracted helpers/models and (b) razor *source-text* assertions (`MovementUiSourceTests` convention).

## Global Constraints

- **Do not change movement semantics.** Preview semantics, server-side revalidation on Move, ownership transaction, ownership gate, blockers/effects semantics, authorization, movement audit, shell View behavior, stale-preview generation protection, attached-Schedule rules and API routes are all preserved. `ResourceOwnershipMoveService`, controllers, validators, persistence are **not touched**. (So `Apperture.IntegrationTests` is optional unless the final review names a risk.)
- **No HSK-C scope**: no slug query strings, no routing/localStorage identity changes. Display-name rendering *inside this dialog* only.
- **No other dialogs redesigned; no Toast, nav, Components, Services, lifecycle, IAM work.** No global CSS change for `<select>`/`<option>`.
- **Context names, not IDs, in normal operator copy.** Use `ResourceContextViewState.DescribeContext(id)` (returns name / "Name (archived)" / `"Unknown context"`, never the raw id). A missing target (`target_not_found`) renders `The selected target context no longer exists.` — never `preview.Target.Name` (the server sets it to the raw id when the context doesn't exist). Raw IDs still appear only in hrefs.
- **Never compare a raw string literal to a blocker/effect code** — use `ResourceMoveBlockerCodes.*` / `ResourceMoveEffectCodes.*` (existing `MovementUiSourceTests` intent).
- **Design system only**: tokens `--color-surface|surface-muted|surface-elevated`, `--color-border|border-subtle|border-strong`, `--color-text-heading|body|muted|subtle`, `--color-error|error-light|error-dark`, `--color-warning`, `--color-tint-sand*`, `--color-success`, `--color-tint-sage*`, `--r-sm|md|lg`. Every `var(--…)` used must exist in `wwwroot/css/app.css`. No magic colors, no gradients, no giant colored cards. Do not rely on color alone (text labels/glyphs distinguish Allowed vs Blocked).
- **Responsive**: stack at `max-width: 30rem` (the dialogs' existing breakpoint); long names wrap (`overflow-wrap:anywhere`); select never overflows; footer stays usable. Dialog `Size="md"` (34rem), not wider.
- **Accessibility**: labelled select, `ManagementDialog` semantics untouched, status region `aria-live="polite"`, errors `role=alert` via `PageMessage`, disabled controls clear, keyboard/focus unchanged.
- **Preserve existing tests** (`MoveOwnershipDialogModelTests`, `MovementUiSourceTests` assertions). The one assertion that must *move* (blocker-constants-not-literals) is retargeted to the presenter source in Task 3 with the same intent.
- Every commit leaves the branch buildable. Stage files explicitly (no `git add -A`). If `dotnet build` fails with `MSB3021/MSB3027` file-lock errors from the user's running Aspire stack, **do not kill processes** — use `--artifacts-path artifacts/hskb-<n>` (git-ignored) for build/test.

## Discovery facts the tasks rely on

- Dialog today: `Size="sm"`, title "Move to another context…", bare `<select id="move-target-select">` (only select of 69 without `ui-input`; `color-scheme: dark` already on `html.dark`; no `option` rules), `<ul class="move-effects|move-blockers">` (no CSS exists), links for 3 blocker codes, `PageMessage Error "Action failed"`, `<p class="ui-help-text">History stays with @SourceContextName.</p>`.
- DTOs: `ResourceMovePreviewDto(ResourceType, ResourceId, Source, Target, CanMove, Blockers, Effects)`; `ResourceContextRef(Id, Name, Slug)`; blocker `(Code, Message, RelatedResourceType, RelatedResourceId, RelatedDisplayName, RelatedResourceContextId)`; effect `(Code, Message, RelatedResourceType, RelatedResourceIds)` — effects carry **ids only, no names**.
- Raw-id leaks in server messages: `target_not_found` (target id, service :734), `binding_connection_unreachable` (owner ctx id, :461), `binding_integration_unreachable` (ctx id, :571). Structured fields for all three already carry what a client presenter needs (`RelatedDisplayName`, `RelatedResourceContextId`, `RelatedResourceId`).
- Blocker fields: `target_archived`→(ResourceContext, Id, DisplayName, ContextId of target); `already_in_target`→none; `binding_connection_unreachable`→(Connection, Id, Name, ContextId); `schedule_context_inconsistent`→(Schedule, Id, Name, ContextId?); `binding_integration_unreachable`→(Integration, Id, Name, ContextId); `unowned_binding_requires_shared`→(Binding, Id only, not navigable); `schedule_attached_to_integration`→(Integration, Id, DisplayName?, ContextId). Effects: `schedules_move_with_integration`→(Schedule, Ids); `binding_references_missing_connection|integration`→(Binding, [bindingId]; message has only a slot name); `used_by_integration`→(Integration, Ids).
- Busy: move POST 409 `{error, code}` (server `ResourceMoveHttpMapper.MapBusy`) is thrown by `ManagementClient.SendMoveAsync` (~:1540) as a bare `InvalidOperationException`; the `code` is dropped. Preview is a GET (never busy).
- Existing client reads usable for name resolution: `GetIntegrationAsync(id)` → `IntegrationDto?` (has `DisplayName`), `GetScheduleAsync(id, runtimePlaneId)` → `ScheduleDetailDto?`, `GetConnectionAsync(id)` → `ConnectionDto?`. `ResourceContextViewState` also exposes `EnsureContextsKnownAsync(ids)` (refresh missing ids once), `FindContext`, `SharedContext`, `KnownActiveApplications`.
- Host pages pass `ResourceId`, `Kind`, `CurrentResourceContextId` (null = Shared): `IntegrationDetail.razor:48`, `ConnectionDetail.razor:74`, `ScheduleDetail.razor:94` (they know the resource display name; the dialog currently does not).
- Buttons: no primary variant exists in the design system; keep `ui-toolbar-btn` for Cancel/Move (same as `ConfirmActionDialog`). `PageMessage` kinds: Loading|Empty|Info|Warning|Error|Forbidden. `StatusPill(Label, Semantic)` classes `ui-status-pill--success|warning|error|...`.

---

## File Structure

**Task 1 (Ui.Shared logic + tests):**
- Create `src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/MoveOwnershipPresenter.cs`
- Modify `src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/MoveOwnershipDialogModel.cs`
- Create `tests/Apperture.Management.Ui.Shared.Tests/ResourceContexts/MoveOwnershipPresenterTests.cs`
- Create `tests/Apperture.Management.Ui.Shared.Tests/ResourceContexts/MoveOwnershipDialogModelStateTests.cs` (do not edit the existing `MoveOwnershipDialogModelTests.cs`)

**Task 2 (client busy exception):**
- Modify `src/Apperture.ControlPlane/Apperture.Management.Client/ManagementClient.cs` (`SendMoveAsync` busy branch), `ResourceMoveDtos.cs` (exception type + mirrored busy-code constant if the server has one)
- Tests: extend `ManagementClientResourceMoveTests.cs` (+ contract-parity test if a server constant exists)

**Task 3 (razor + CSS + hosts + source tests):**
- Modify `Organisms/MoveOwnershipDialog.razor`, `wwwroot/css/management-ui-primitives.css` (Ui.Client)
- Modify the three host pages (one optional-parameter line each)
- Modify `tests/Apperture.Management.Ui.Shared.Tests/ResourceContexts/MovementUiSourceTests.cs`

---

### Task 1: Presenter + model state (pure logic, Ui.Shared)

**Files:** see File Structure.

**1a. `MoveOwnershipDialogModel` additions (keep every existing member/behavior/signature working):**
- `enum MoveDialogPhase { NoTarget, Loading, Failed, Allowed, Blocked }` and computed `Phase`: no selected target → `NoTarget`; `IsLoadingPreview` → `Loading`; `Preview is null` and `SubmitError` set → `Failed`; `Preview is null` (no error, target selected — awaiting preview) → `Loading`; `Preview.CanMove` → `Allowed`; else `Blocked`.
- `enum MoveSubmitErrorKind { Failed, Busy }` and `SubmitErrorKind` property. `SetSubmitError(string? message, MoveSubmitErrorKind kind = MoveSubmitErrorKind.Failed)` (existing 1-arg calls keep compiling). Kind resets to `Failed` whenever the error is cleared (`SelectTarget`, `ApplyPreview`, `Reset`, `SetSubmitError(null)`).
- `bool ChangedSincePreview` and `void ApplySubmitConflict(int generation, ResourceMovePreviewDto result)`: no-op unless `_previewGuard.IsCurrent(generation)`; otherwise sets `Preview = result`, `ChangedSincePreview = true`, `IsLoadingPreview = false`, `SubmitError = null`. `ChangedSincePreview` clears in `SelectTarget`, `BeginPreview`, `ApplyPreview`, `Reset`.
- Do **not** alter `CanSubmit`, `BeginPreview`, `ApplyPreview` (other than clearing the new flag), `FailPreview`, or the generation guard semantics.

**1b. `MoveOwnershipPresenter` (static, pure, no I/O):**
```csharp
public sealed record MoveItem(string Text, string? LinkText = null, string? Href = null);
public sealed record MovePresentation(
    IReadOnlyList<MoveItem> WillMove,   // empty when blocked
    IReadOnlyList<MoveItem> UsedBy,
    IReadOnlyList<MoveItem> BlockedBy,
    IReadOnlyList<MoveItem> Notes,      // informational effects (e.g. binding_references_missing_*)
    string History);
public readonly record struct MoveRelatedRef(string ResourceType, string Id);

public static MovePresentation Present(
    ResourceMovePreviewDto preview, string sourceName, string targetName, string? resourceName,
    Func<string, string> describeContext, IReadOnlyDictionary<string, string>? relatedNames = null);
public static IReadOnlyList<MoveRelatedRef> GetRelatedNamesToResolve(ResourceMovePreviewDto preview); // capped, see below
public static string RelatedNameKey(string resourceType, string id);   // e.g. $"{type}:{id}"; used by razor + presenter
public const int MaxResolvedNames = 10;
```
Rules (all operator copy uses names; known-code branches must never emit a raw context id or raw resource id in `Text`/`LinkText`; ids appear only in `Href`):
- **Noun** from `preview.ResourceType` (case-insensitive: integration/connection/schedule; unknown → "resource"). Tolerate null `Source`/`Target` (existing tests build previews with `default!`) and null lists.
- **History** = `Historical activity remains attributed to {sourceName}.`
- **WillMove** (only when `preview.CanMove`): first item `"{Noun} '{resourceName}'"` (or `"This {noun}"` when no name); then for effect `schedules_move_with_integration`: each schedule id with a resolved name in `relatedNames` → `MoveItem("Schedule", name, "/schedules/{id}")` (cap `MaxResolvedNames`); remainder/unresolved → one text item `"{k} more attached schedule(s)"`; if none resolved → single item `"{n} attached schedule(s)"`.
- **UsedBy** (effect `used_by_integration`, shown whenever present, allowed or blocked): resolved names → `MoveItem("", name, "/integrations/{id}")` (cap); remainder → `"and {k} more"`; none resolved → `"Used by {n} integration(s)"`.
- **Notes**: effects `binding_references_missing_connection|integration` → `MoveItem(effect.Message)` (message contains only a slot name); any **unknown effect code** → `MoveItem(effect.Message)` (readable fallback).
- **BlockedBy** (each is an *actionable* item: sentence + optional link):
  - `target_not_found` → `The selected target context no longer exists.` (never use Target.Name/id)
  - `target_archived` → `{RelatedDisplayName ?? targetName} is archived and can't receive new ownership.`
  - `already_in_target` → `This {noun} is already in {targetName}.`
  - `binding_connection_unreachable` → `Connection '{RelatedDisplayName}' is owned by {describeContext(RelatedResourceContextId ?? shared)}, so it would become unreachable from {targetName}.` + link (`"View connection"`, `/connections/{RelatedResourceId}`). **Before adding a remedy sentence** ("move the connection to {target} or Shared first, or choose another destination") verify the rule in `ResourceOwnershipMoveService` (~lines 430-470); include it only if it is exactly what the rule implies, else omit it.
  - `schedule_context_inconsistent` → server `Message` is safe (no ids) — use it + link (`"View schedule"`, `/schedules/{RelatedResourceId}`) when id present.
  - `binding_integration_unreachable` → `Integration '{RelatedDisplayName}' in {describeContext(RelatedResourceContextId)} would no longer be able to use this connection.` + link (`"View integration"`, `/integrations/{id}`).
  - `unowned_binding_requires_shared` → server `Message` (safe; binding has no page, no link).
  - `schedule_attached_to_integration` → `This schedule belongs to integration '{RelatedDisplayName}'. Move the integration instead — its schedules move with it.` (drop the name clause if null) + link `"Go to integration"` (`/integrations/{RelatedResourceId}`).
  - **Unknown blocker code** → `MoveItem(blocker.Message)` (readable fallback), no link.
  - Whenever `RelatedDisplayName` is null, use a neutral noun phrase (e.g. "a connection"), never an id.
- `GetRelatedNamesToResolve`: ids from `used_by_integration` (type Integration) and `schedules_move_with_integration` (type Schedule), de-duplicated, at most `MaxResolvedNames` per type, empty when none.

**Tests (TDD — write first, see red, then green):**
- `MoveOwnershipPresenterTests`: one test per blocker code and effect code; a **no-raw-id** test per leaky code using recognisable raw ids (e.g. `ctx-raw-9f3a`) asserting no `Text`/`LinkText` contains them while `describeContext` output (a fake lambda) does; `target_not_found` copy; unknown blocker/effect code fallback readable; WillMove empty when blocked, populated when allowed; used-by resolved / unresolved / >cap overflow; history line; null Source/Target tolerated; `GetRelatedNamesToResolve` dedupe + cap; `RelatedNameKey` stability; hrefs use ids.
- `MoveOwnershipDialogModelStateTests`: every `Phase` transition; `ApplySubmitConflict` sets `ChangedSincePreview` and blocked preview; `ApplySubmitConflict` with a **stale generation is discarded**; flag clears on `SelectTarget`/`BeginPreview`/`ApplyPreview`/`Reset`; `SetSubmitError(msg, Busy)` sets kind, clearing resets to Failed; 1-arg `SetSubmitError` still works; `CanSubmit` unchanged semantics (spot-check: still false while blocked, true again after busy error with valid preview).
- All existing `MoveOwnershipDialogModelTests` must pass untouched.

- [ ] Steps: read the three existing files; write failing tests; implement 1a and 1b; run `dotnet test tests/Apperture.Management.Ui.Shared.Tests/... --filter "FullyQualifiedName~MoveOwnership"`; run full Ui.Shared.Tests; `dotnet build Apperture.slnx`; commit `HSK-B: add movement presenter and dialog state model` (stage explicit files).

---

### Task 2: Typed busy exception in the client

**Files:** see File Structure.

- In `ResourceMoveDtos.cs` (or the closest existing home for client exception types) add `public sealed class OwnershipMoveBusyException : InvalidOperationException` (ctor takes the message). If the server defines the busy code as a constant (look in `ResourceMoveHttpMapper.cs` / `ResourceOwnershipBusyException`), mirror it as a client constant (e.g. `ResourceMoveErrorCodes.OwnershipOperationBusy`) and add it to the existing `ResourceMoveDtoContractParityTests` so drift fails a test. If no server constant exists, use the exact literal the mapper emits, define the client constant, and note it.
- In `ManagementClient.SendMoveAsync`, in the 409 **non-preview** branch, parse the payload's `code`; when it equals the busy code throw `OwnershipMoveBusyException(message)`; for any other non-preview 409 keep throwing plain `InvalidOperationException` exactly as today. The 400 branch and everything else unchanged. Message text unchanged.
- Test in `ManagementClientResourceMoveTests`: busy 409 → throws `OwnershipMoveBusyException` (which `Should().BeAssignableTo<InvalidOperationException>()`), message preserved; other 409-with-code and 400 → plain `InvalidOperationException`, *not* the busy type; blocked-preview 409 still returns a renderable preview.
- [ ] Steps: TDD; run `dotnet test tests/Apperture.UnitTests/... --filter "FullyQualifiedName~ResourceMove"`; full UnitTests; build; commit `HSK-B: distinguish the transient ownership-busy error in the client`.

---

### Task 3: Dialog razor + CSS + hosts + source tests

**Files:** see File Structure. Depends on Tasks 1-2.

**Structure (adapt to what the primitives allow; do not follow literally if a shipped primitive fits better):**
```
Move ownership                                   (Title; Size="md")
<Integration|Connection|Schedule> 'Name'         (only if ResourceDisplayName supplied; Description slot or a quiet line)

Current context   →   Move to
Shared                [ Development ▼ ]           (route header; select IS the real control, class ui-input, labelled)

<status region aria-live=polite>
  NoTarget : quiet hint "Choose a target context to see what will happen."
  Loading  : <LoadingState Message="Checking what this move affects…" />
  Failed   : PageMessage Error "Couldn't load the preview" + SubmitError + [Try again] button (re-runs LoadPreviewAsync)
  Allowed  : StatusPill "Ready to move" (success) ; Will move ; Used by ; Notes ; History
  Blocked  : StatusPill "Can't move yet" (error/warning) ; [changed-since-preview notice] ; Blocked by (primary, strongest) ; Used by ; Notes ; History   (Will move hidden)
  SubmitError (after a valid preview): Busy → PageMessage Warning "Another change is in progress" + retry copy;  Failed → PageMessage Error "The move didn't complete"
[Cancel] [Move]
```
- **Route header**: compact grid `1fr auto 1fr`; source end = label "Current context" + name (`describeContext`, null → "Shared"); arrow is `aria-hidden` text glyph; target end = `<label for="move-target-select">Move to</label>` + `<select id="move-target-select" class="ui-input" disabled="@_model.IsSubmitting" …>`. Do not repeat the target value elsewhere. Placeholder option "Choose a context…".
- **Sections**: `Will move`, `Used by`, `Blocked by`, `Notes`(subdued, only if items) and a quiet `History` line. Render a section only when it has items. Blocked-by is visually strongest (error-tinted left border + label using `--color-error*`); Used by and Will move are neutral (`--color-surface-muted`, `--color-border-subtle`); History/Notes are subdued muted text (no colored box). Items: text then optional `DrillInLink`-styled link; wrap long text. Do not turn every sentence into an alert box.
- **Changed-since-preview**: when `Phase == Blocked && ChangedSincePreview`, show `PageMessage Kind=Warning` (or Info) "Things changed since this preview was loaded. Review the blockers below."
- **Razor renders only presenter output** — it must not read `blocker.Message`, `effect.Message` or `RelatedResourceContextId` directly (source test enforces).
- **Submit path**: catch `OwnershipMoveBusyException` **before** `InvalidOperationException` → `_model.SetSubmitError(ex.Message, MoveSubmitErrorKind.Busy)`; on `!result.CanMove` capture `var generation = _model.CurrentGeneration` before the call and use `_model.ApplySubmitConflict(generation, result)` (replaces `ApplyPreview(_model.CurrentGeneration, …)`). Keep `IsBusy`, `ResetDialogState`, `OnMoved`, the existing catches and all messages. Move stays enabled after a busy/failed error when the preview is still valid (retryable); Cancel/close behavior unchanged.
- **Related-name resolution (best effort, I/O in the razor `@code`)**: after a preview (initial or submit-conflict) is applied, call `MoveOwnershipPresenter.GetRelatedNamesToResolve(preview)`, resolve each id via existing reads (`GetIntegrationAsync`→`DisplayName`, `GetScheduleAsync`→schedule name; confirm the real property names), in parallel with per-id try/catch (401/403/404/network → simply unresolved), then merge into a `Dictionary<string,string>` keyed by `RelatedNameKey`. After every `await`, verify `_model.CurrentGeneration == generation && ReferenceEquals(_model.Preview, preview)` before mutating state; clear the dictionary on target change/`ResetDialogState`. Call `StateHasChanged()` after the preview applies (so it shows immediately) and again after names arrive. Also `await ViewState.EnsureContextsKnownAsync(...)` for blockers' `RelatedResourceContextId`s if its signature fits, so owner names resolve. Never let resolution block or fail the dialog.
- **ResourceDisplayName** optional `[Parameter] string? ResourceDisplayName`; pass it from `IntegrationDetail` (`DisplayName`), `ConnectionDetail` (connection name), `ScheduleDetail` (schedule name) — one attribute each; confirm property names. Keep `OnMoved="OnMovedAsync"` etc. exactly (source tests pin them).
- **Select fix**: `class="ui-input"` (+ `mt-1` only if the header needs it). Do NOT add `option` rules or touch `color-scheme`/tokens. If, after this, you believe a dialog-scoped rule is still needed, stop and report instead of adding it (browser-controlled popups can't be verified here).
- **CSS** (in `management-ui-primitives.css`, one clearly-commented `move-*` block): `.move-dialog`, `.move-route*` (grid; `@media (max-width: 30rem)` → single column, arrow rotated/hidden), `.move-section`, `.move-section--blocked`, `.move-section__label`, `.move-list`, `.move-item`, `.move-history`, `.move-hint`. Tokens only; verify each `var(--…)` exists in `app.css`. Style focus/disabled via existing patterns.
- **Source tests** (`MovementUiSourceTests`): keep the four still-valid assertions unchanged (`disabled="@(!_model.CanSubmit)"`, `@inject ResourceContextViewState ViewState` / not a parameter, `SetExplicitAsync` absent across the 4 files, "Move to another application" absent across 5 files). **Retarget** the blocker-constants test to also read `MoveOwnershipPresenter.cs` (add a `ReadShared` helper; repo-root search via `.git` like `ReadRazor`): presenter contains `ResourceMoveBlockerCodes.ScheduleAttachedToIntegration` and no `"schedule_attached_to_integration"` literal; dialog razor contains no literal code strings. Add: select has `class="ui-input"` and a `<label for="move-target-select">`; razor does **not** contain `blocker.Message`, `effect.Message`, or `RelatedResourceContextId`; razor catches `OwnershipMoveBusyException`; razor uses `ApplySubmitConflict`; select is disabled while submitting; dialog `Size="md"`; `MoveOwnershipPresenter` is referenced.
- [ ] Steps: read files; implement razor → CSS → hosts; update/add source tests; `dotnet build Apperture.slnx` (isolated artifacts path if locked); run Ui.Shared.Tests + Ui.Client.Tests + UnitTests; commit `HSK-B: restructure the movement dialog around source, target and outcome`.

---

## Out of scope

Server messages/DTOs/validators/service/transaction/gate/audit; HSK-C; other dialogs; global select/option styling; Toast; nav; a custom combobox; a localization framework; bUnit.

## Validation for this plan

`dotnet build Apperture.slnx` 0 errors; full `Apperture.UnitTests`, `Apperture.ArchTests`, `Apperture.Management.Ui.Shared.Tests`, `Apperture.Management.Ui.Client.Tests`; `Apperture.IntegrationTests` only if a reviewer names a risk (no server/persistence change is planned). One final whole-branch review on the most capable model. Browser smoke cannot be performed by the agent — a manual checklist is delivered to the user.
