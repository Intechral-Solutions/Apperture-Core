# HSK-A — Small Operator Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. This is a small housekeeping body of work — keep each task's diff tight and do not opportunistically clean up unrelated code.

**Goal:** Four small, independent operator-facing fixes on top of the completed CTX work: (1) remove the View onboarding hint entirely, (2) show "Integrations using this module" on Module detail via a proper server-side filter, (3) make Create-button eligibility on five create surfaces track current local form validity instead of waiting for blur, including correcting Connection create's dynamic required-field gap and NotificationDefinition's stale-validation gap. A fifth item — the reported-duplicate `IAuditService` DI registration — was investigated in discovery and found **not** to be a true duplicate (two separate composition roots), so it is **out of scope for code changes**; report only.

**Architecture:** No new architecture. Every task reuses an existing pattern:
- Task 1 deletes a self-contained onboarding-hint feature (markup + JS interop + CSS + state), leaving the real View-selection persistence (`apperture.view.<userId>`) untouched.
- Task 2 extends the existing ResourceContext-filtered Integrations list path (client → controller → service → repository, filter pushed to SQL) with a second, composable `moduleId` filter, mirroring exactly how `resourceContextId`/`contextId` already flows through that stack.
- Tasks 3-5 make Blazor `@bind` live (`@bind:event="oninput"`) on already-required fields, extract the already-duplicated connector-required-field check into one shared predicate used by both Connection create surfaces, and extend the existing `MarkDirty()` mechanism to every NotificationDefinition payload field that currently bypasses it.

**Spec:** The HSK-A handoff brief (session instructions) is the authority for this plan; there is no separate spec document. Discovery evidence below was independently re-verified against current HEAD (`bd4787e`) by four read-only discovery passes immediately before this plan was written — every file:line citation in this plan reflects the current code, not the original discovery pass.

**Tech stack:** C#/.NET (Blazor WebAssembly client, ASP.NET Core management host), xUnit, FluentAssertions. No bUnit anywhere in this repo — confirmed by repo-wide grep. UI-logic tests follow the existing convention of testing extracted plain-C# state/helper classes and service/repository test harnesses, not rendered Razor components.

## Global Constraints

- **No scope leakage.** Do not touch HSK-B (movement dialog), HSK-C (Context slug routing), Toast/Notification redesign, Components/Services design or implementation, nav redesign, Connection/Schedule lifecycle work, or Context IAM. Do not opportunistically clean up unrelated code.
- **Task 1:** Do not touch `apperture.view.<userId>` (the real remembered-View localStorage key), View resolution, selector visibility rules, or selector behavior. Only the hint (`apperture.view.hintDismissed`, `_hintDismissed`, `isViewHintDismissed`, `dismissViewHint`, `.ui-hint-bubble`) is removed. The orphaned localStorage key in existing browsers is left as-is — no migration.
- **Task 2:** Do not implement via client-side fetch-all-then-filter. The `moduleId` filter must be threaded through client → controller → service → repository and pushed to SQL (`Integration.ModuleId == moduleId`), composable with the existing `resourceContextId` filter (both apply when both are supplied — no mutually exclusive overloads). Preserve the existing authorization path exactly: the `moduleId` filter is added to the same authorized method, never a new bypass path. No Integration machine IDs prominently displayed in the new UI section; use `DisplayName`. Do not add Components-contributed-by-Module or relocate Modules under Components — that's later roadmap work.
- **Tasks 3-5 (general form-performance rule):** Immediate (no network call) for: local required-field completeness, local syntax/length rules that already exist, dirty-state tracking. Never immediate unless already designed that way: remote uniqueness checks, API validation, connector verification. Do not invent new validation rules (no new min-lengths, no new required fields). Do not run NotificationDefinition's server-side `Validate` on keystroke — it stays an explicit, button-triggered action.
- **Task 4 (Connection required fields):** Use `ConnectionFieldCatalogDto.IsRequired` (already exists, `IManagementClient.cs:1272-1277`) — no new required-field semantics. Do not duplicate the missing-required-field predicate a third time; extract the existing logic (currently duplicated in `ConnectionNew.razor.CreateAsync()` and `IntegrationConnections.razor.CreateConnectionInFlowAsync()`) into one shared check both the standalone and in-flow surfaces call for both submit-time validation and Create-button eligibility.
- **Task 5 (NotificationDefinition):** Only mark fields dirty that actually feed `BuildRuleJson`/`BuildTemplateJson`/the validation payload. Do not invalidate on fields that don't affect validation (see analysis in Task 5) unless discovered otherwise during implementation.
- **DI cleanup (reported, not a task):** `AddScoped<IAuditService, SqlAuditService>()` at `ServiceCollectionExtensions.Governance.cs:96` (inside `AddAppertureGovernance`, called only from `Apperture.Management.Host/Program.cs:55`) and `:214` (inside `AddAppertureEmergencyOperatorServices`, called only from `Apperture.Cli.Host/EmergencyCli.cs:106`) are **two separate composition roots** — confirmed by grepping every call site of both methods; neither host calls both. A third independent registration exists at `Apperture.RuntimeHost.Windows.WorkerHost/Program.cs:87`. This contradicts `docs/01-architecture/transitions/resource-contexts/ctx3b-closure.md:352-357`'s "harmless, last registration wins" characterization, which only holds within a single container. **Do not remove either registration.** No task in this plan touches DI registration; the final HSK-A report documents this finding and, if useful, a follow-up correction to the closure doc's mischaracterization may be made as part of Task 1's documentation pass (see Task 1 Step "living doc note") — but only as a factual correction, not a code change.
- Every commit must leave the branch buildable (`dotnet build Apperture.slnx` clean).

---

## File Structure

**Task 1 — modified:**
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Molecules/ViewSelector.razor`
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/wwwroot/js/apperture-shell.js`
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/wwwroot/css/management-ui-primitives.css`
- `docs/01-architecture/transitions/resource-contexts/ctx3-design-gate.md` (smallest clarification note only — do not rewrite the historical design content)

**Task 2 — modified:**
- `src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs` and its impl (`GetIntegrationsAsync` — add optional `moduleId`)
- `src/Apperture.ControlPlane/.../IntegrationsController.cs` (`List` action)
- `src/Apperture.Core/.../IIntegrationManagementService.cs` + `IntegrationManagementService.cs`
- `src/Apperture.Core/.../IIntegrationRepository.cs` + `src/Apperture.Persistence/.../SqlIntegrationRepository.cs`
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Modules/ModuleDetail.razor`
- Tests: extend `IntegrationFleetContextFilterTests.cs`-style harness tests; controller test if one exists for `IntegrationsController.List`.

**Task 3 — modified:**
- `IntegrationNew.razor`, `ConnectionNew.razor`, `IntegrationConnections.razor`, `ScheduleCreate.razor`, `NotificationDefinitionAuthoringForm.razor` (binding only)

**Task 4 — modified:**
- `ConnectionNew.razor`, `IntegrationConnections.razor` (extract + reuse shared required-field-complete check)
- Possibly a small shared helper (static method or component-local reused via a shared base/partial — implementer's judgment on the smallest reuse mechanism; do not build a new framework)

**Task 5 — modified:**
- `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Organisms/Notifications/NotificationDefinitionAuthoringForm.razor`

---

### Task 1: Remove View onboarding hint

**Files:** see File Structure above.

**Confirmed current shape (discovery, re-verified):**
- Hint markup: `ViewSelector.razor:29-40` — `@if (!_hintDismissed) { <div class="ui-hint-bubble" role="status"> <span>Use View to focus lists on an application.</span> <button @onclick="DismissHintAsync">&times;</button> </div> }`
- State: `_hintDismissed` field (`ViewSelector.razor:116`, defaults `true`), set from JS in `OnInitializedAsync` (`:139-142`), `DismissHintAsync` method (`:144-148`).
- JS interop (`apperture-shell.js:35-42`): `isViewHintDismissed()` / `dismissViewHint()` — both operate only on `apperture.view.hintDismissed`. Distinct from `getViewPreference`/`setViewPreference` (`:28-34`), which key on `apperture.view.<userId>` — **do not touch these**.
- CSS: `.ui-hint-bubble` + 2 descendant rules, `management-ui-primitives.css:2025-2065` — used nowhere else in the repo (confirmed by repo-wide grep). Safe to delete entirely.
- Living doc requiring the smallest clarification: `docs/01-architecture/transitions/resource-contexts/ctx3-design-gate.md` lines 123, 789, 896 describe the hint as current/normative behavior. `ctx3a-closure.md:69` is a historical closure record — leave untouched.

- [ ] **Step 1: Remove the hint markup and state from `ViewSelector.razor`**
  - Delete the `@if (!_hintDismissed) { ... }` block (lines ~29-40).
  - Delete the `_hintDismissed` field and its initialization from JS in `OnInitializedAsync`.
  - Delete `DismissHintAsync`.
  - Delete any now-unused JS-interop plumbing in the component (e.g. a `IJSObjectReference`/module import) *only if* it was used exclusively for the hint — check other uses in the same file before deleting.

- [ ] **Step 2: Remove JS interop functions**
  - Delete `isViewHintDismissed` and `dismissViewHint` from `apperture-shell.js:35-42`.
  - Confirm no other caller references these two functions (`grep -r "isViewHintDismissed\|dismissViewHint"`).

- [ ] **Step 3: Remove CSS**
  - Delete `.ui-hint-bubble` and its descendant rules from `management-ui-primitives.css:2025-2065`.

- [ ] **Step 4: Living-doc clarification**
  - In `ctx3-design-gate.md`, at each of the three passages (123, 789, 896) that describe the hint as current behavior, add the smallest possible note that HSK-A removed the onboarding hint after the feature matured (do not rewrite the historical design content itself — follow the repo's existing convention for marking superseded normative text, e.g. a short bracketed/blockquote annotation near each passage). Do not touch `ctx3a-closure.md` (historical closure record).
  - Optionally, if convenient in the same doc, correct `ctx3b-closure.md:352-357`'s "harmless, last registration wins" characterization of the `IAuditService` registrations with a one-line factual correction (two separate composition roots, not one container) — this is a documentation-only correction, not a DI code change. Skip if it would require touching content outside this immediate passage.

- [ ] **Step 5: Regression coverage (only if it fits existing conventions cheaply)**
  - No existing test covers the hint. If `ViewSelector`'s hint state can be tested without introducing bUnit (e.g., nothing testable remains once the hint is deleted — the removal itself is the fix), skip rather than force a test. Do not introduce bUnit or any new UI test framework for this.

- [ ] **Step 6: Build and verify**
  ```
  dotnet build Apperture.slnx
  ```
  Expected: 0 errors. Grep the repo once more for `ui-hint-bubble`, `hintDismissed`, `_hintDismissed`, `isViewHintDismissed`, `dismissViewHint` to confirm complete removal (excluding the doc clarification note and any historical closure doc).

- [ ] **Step 7: Commit**
  ```
  git add <changed files>
  git commit -m "HSK-A: remove View onboarding hint"
  ```

---

### Task 2: Module detail — "Integrations using this module"

**Files:** see File Structure above.

**Confirmed current shape (discovery, re-verified):**
- Module detail page: `Pages/Modules/ModuleDetail.razor`. Structure: `PageHeader` → lifecycle actions → "State" `MetadataGrid` → `ManagementSection Title="Recent activity"` (uses `EntityRecentActivityPanel` w/ `EmptyMessage`) → "Links". `ManagementSection` (line ~119) is the established titled-section wrapper — reuse it. Empty-state convention: `EmptyMessage` prop, or top-level `<EmptyState Title=".." Message=".."/>`.
- Integration list stack:
  - Client: `IManagementClient.GetIntegrationsAsync(string? contextId, ...)` → `ManagementClient.cs:634` builds `integrations?contextId=...`.
  - Controller: `IntegrationsController.List([FromQuery] string? contextId, ...)` (`IntegrationsController.cs:66-85`) → `ResolveContextFilterAsync` → `_integrationService.ListIntegrationsAsync(contextFilter, ct)`.
  - Service: `IIntegrationManagementService.ListIntegrationsAsync(ResourceContextId?, ct)` — interface default method does in-memory filtering (test doubles only); production `IntegrationManagementService.cs:57-76` does authz (actor resolution + `_authorizationEvaluation.EvaluateAsync` for `ActionConstants.IntegrationRead`) then `_integrationRepository.GetVisibleAsync(scopeKey, resourceContextId, ct)`.
  - Repository: `IIntegrationRepository.GetVisibleAsync(scopeKey, resourceContextId?, ct)` — interface default is in-memory only; `SqlIntegrationRepository.cs:33-52` pushes the context filter to SQL, then applies scope-containment filtering in-memory (pre-existing — leave as-is).
- `Integration.ModuleId`: `Integration.cs:17`. `IntegrationDto.ModuleId`: `IntegrationsController.cs:840` (populated at `:635`). `IntegrationEntity.ModuleId` persisted (confirmed via `SqlIntegrationRepository.UpdateAsync:69`).
- Integration detail link convention: `/integrations/{id}` (`IntegrationsList.razor:222,339`), displayed text is `row.DisplayName` (`:224`), id only used URL-escaped in the href.
- Test convention: no bUnit; filter tests live at service/repository level against `IntegrationFleetTestHarness` (seeding via EF InMemory), asserting on the returned domain set (see `IntegrationFleetContextFilterTests.cs`).

- [ ] **Step 1: Thread `moduleId` through the stack (repository → service → controller → client), mirroring `resourceContextId` exactly**
  - `IIntegrationRepository.GetVisibleAsync`: add `string? moduleId = null` parameter. Update the interface default (in-memory) implementation to also filter by `ModuleId` when supplied. In `SqlIntegrationRepository.cs`, extend the `query.Where(...)` chain to additionally filter `e.ModuleId == moduleId` when `moduleId` is non-null, alongside (not replacing) the existing `ResourceContextId` `Where` — both apply together when both are supplied.
  - `IIntegrationManagementService.ListIntegrationsAsync`: add `string? moduleId = null` parameter; thread through to the repository call in both the interface default and `IntegrationManagementService.cs`'s production implementation. Do not change the authz call — it must still run before/around the repository call exactly as today.
  - `IntegrationsController.List`: add `[FromQuery] string? moduleId` and pass it through to `_integrationService.ListIntegrationsAsync(contextFilter, moduleId, ct)` (or whatever parameter order keeps the diff smallest — implementer's call, but keep `moduleId` clearly named, not positional-ambiguous with `contextId`).
  - `IManagementClient.GetIntegrationsAsync` (interface + impl): add `string? moduleId = null` parameter; `ManagementClient.cs:634` appends `&moduleId=...` to the query string when supplied (URL-encode).

- [ ] **Step 2: Write/extend filter tests**
  - Extend the `IntegrationFleetTestHarness`-based tests (pattern from `IntegrationFleetContextFilterTests.cs`) with cases: filter by `moduleId` alone; filter by `resourceContextId` alone; filter by both (must AND, returning only Integrations matching both); filter by neither (unchanged current behavior); a Module with zero matching Integrations returns empty, not an error.
  - If a controller-level test file exists for `IntegrationsController.List`, add a case confirming the `moduleId` query parameter is accepted and forwarded. If no such test file exists, do not create a new one solely for this — the service/repository-level coverage above is the primary gate, consistent with existing conventions.

- [ ] **Step 3: Add the "Integrations using this module" section to `ModuleDetail.razor`**
  - Add a `ManagementSection Title="Integrations using this module"` (or the closest existing page-style wording) after the existing sections, following the same layout conventions as the "Recent activity" section.
  - On page load, call `Client.GetIntegrationsAsync(contextId: null, moduleId: ModuleId)` (or the actual parameter shape after Step 1) to fetch Integrations for this Module. Respect whatever the page's existing loading/error pattern is (mirror how "Recent activity" or "State" handle loading).
  - Render each Integration as a row/item showing `DisplayName` linked to `/integrations/{id}` (URL-escaped), matching `IntegrationsList.razor`'s link convention. Do not show the raw Integration id in visible text.
  - Empty state: when the call returns zero Integrations, show the page's existing `EmptyState`/`EmptyMessage` convention with a message such as "No integrations currently use this module."
  - Do not add Components-contributed-by-Module content, do not relocate Modules under Components, do not redesign Module lifecycle UI — those are explicitly out of scope.

- [ ] **Step 4: Build and test**
  ```
  dotnet build Apperture.slnx
  dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj
  dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj
  dotnet test tests/Apperture.Management.Ui.Client.Tests/Apperture.Management.Ui.Client.Tests.csproj
  ```
  Expected: 0 errors, no regressions.

- [ ] **Step 5: Commit**
  ```
  git add <changed files>
  git commit -m "HSK-A: show Module integrations"
  ```

---

### Task 3: Live local validity — required text fields (batched, mechanical)

**Files:** `IntegrationNew.razor`, `ConnectionNew.razor`, `IntegrationConnections.razor`, `ScheduleCreate.razor`, `NotificationDefinitionAuthoringForm.razor`.

**Confirmed current shape (discovery, re-verified) — all five fields below use plain `@bind` (blur-gated) and feed an already-correct `CanCreate`/`CanSubmit` computation that simply isn't re-evaluated until blur:**

1. `IntegrationNew.razor:104-108` — `<input @bind="_displayName" required />`. `CanCreate()` at `:267-275` already checks `!string.IsNullOrWhiteSpace(_displayName)` correctly.
2. `ConnectionNew.razor:72-77` — `<input @bind="_connectionName" .../>`. `CanCreate` at `:120-128` already checks it correctly (connector-field gap is Task 4, not here).
3. `IntegrationConnections.razor:164-169` — `<input @bind="_inFlowConnectionName" .../>`. Inline `disabled` expression at `:206-211` already checks it correctly (connector-field gap is Task 4).
4. `ScheduleCreate.razor:53-57` (name) and `:137-142` (recurrence) — both `<input @bind="..." required .../>`. `CanSubmit` at `:195-201` already checks both correctly.
5. `NotificationDefinitionAuthoringForm.razor:12-13` — `<input @bind="_model.DisplayName" disabled="@(!CanSave)" />`. `CanSubmit` at `:365-370` already checks it correctly (stale-validation gap is Task 5, not here).

**This task is purely a binding-mode change — the eligibility logic itself is already correct for these five fields.** Change each listed `@bind="..."` to `@bind="..." @bind:event="oninput"` (or the exact syntax the existing Blazor version in this repo uses for live two-way binding — verify against another live-bound field already in the codebase, e.g. `ConnectorConnectionFieldInputs.razor`'s `@oninput` pattern, and prefer whichever form keeps the diff smallest and most consistent with existing live fields). Do not change validation logic, do not add new rules, do not touch any other input on these pages.

- [ ] **Step 1: Apply `@bind:event="oninput"` to the five fields listed above**

- [ ] **Step 2: Manual reasoning check per field** — confirm the surrounding `CanCreate`/`CanSubmit` property is a plain synchronous computed property (not something that needs an explicit `StateHasChanged()` call) so the live binding alone is sufficient to re-render the button state. If any surface needs an explicit re-render trigger, add the minimal one (e.g. `@bind:after` calling nothing but a no-op, or confirm Blazor's automatic re-render after an event handler already covers it) — do not add debouncing, timers, or async work to the keystroke path.

- [ ] **Step 3: Build**
  ```
  dotnet build Apperture.slnx
  ```

- [ ] **Step 4: Commit**
  ```
  git add <changed files>
  git commit -m "HSK-A: make required-field Create eligibility track live input"
  ```

---

### Task 4: Connection create — dynamic required-field correctness

**Files:** `ConnectionNew.razor`, `IntegrationConnections.razor`.

**Confirmed current shape (discovery, re-verified):**
- `ConnectorConnectionFieldInputs.razor` already updates `ConfigValues`/`SecretValues` live via `@oninput` (`:19-23`) — no change needed there.
- Required-field model already exists cleanly: `ConnectionFieldCatalogDto(string Key, string DisplayName, bool IsSecret, string? Description, bool IsRequired = false)` (`IManagementClient.cs:1272-1277`). `IsRequired` is a plain bool — the model can already express this; **do not invent new semantics**.
- `ConnectionNew.razor.CreateAsync()` (`:220-228`) and `IntegrationConnections.razor.CreateConnectionInFlowAsync()` (`:527-535`) each independently compute the same missing-required-field predicate:
  ```csharp
  var missing = SelectedDefinition.Fields
      .Where(f => f.IsRequired && string.IsNullOrWhiteSpace(GetFieldValue(f)))
      ...
  ```
  (with two different value-getters: `GetFieldValue` on the standalone page, `GetInFlowFieldValueForValidation` in-flow.) Today this check runs only at submit time, after the button is already enabled — so the button can appear enabled while a connector-required field is empty, and submission then rejects it.
- `ConnectionNew.razor`'s `CanCreate` (`:120-128`) and `IntegrationConnections.razor`'s inline `disabled` expression (`:206-211`) currently do not consult this predicate at all.

- [ ] **Step 1: Extract the shared "all required connector fields are filled" check**
  - Create one shared predicate — e.g. a `private static bool AllRequiredConnectorFieldsFilled(ConnectorDefinitionCatalogDto? definition, Func<ConnectionFieldCatalogDto, string?> getValue)` (adjust exact type names to match the real `Definition`/`Fields` types found in discovery) that both pages can call. Choose the smallest reuse mechanism that fits the existing codebase's conventions for sharing logic between these two Razor pages (a static helper class colocated with one of them, or a shared partial — do not build a new abstraction/framework for this).
  - Replace both pages' submit-time `missing = ...` computations with a call to the shared predicate (or keep the submit-time detailed missing-fields message but derive it using the same shared field-iteration logic, to avoid a second independent copy of "what counts as filled").

- [ ] **Step 2: Wire the shared check into Create-button eligibility**
  - `ConnectionNew.razor`'s `CanCreate`: add `&& AllRequiredConnectorFieldsFilled(SelectedDefinition, GetFieldValue)` (adjust names to match Step 1).
  - `IntegrationConnections.razor`'s inline in-flow `disabled` expression: add the equivalent check using the in-flow value-getter.
  - Both surfaces must use the same local-completeness semantics — i.e., the same shared predicate, not two independently-reimplemented checks.
  - No network calls in this path — this is purely local dictionary/field-value inspection, matching the existing `@oninput`-driven `ConfigValues`/`SecretValues` updates.

- [ ] **Step 3: Verify button reactivity**
  - Since `ConnectorConnectionFieldInputs` already updates the bound dictionaries on `oninput`, confirm the parent page re-renders `CanCreate`/the inline `disabled` expression after each keystroke in a connector field (this likely already works via Blazor's existing event-driven re-render from the child `EventCallback`, but verify by tracing the callback chain from `ConnectorConnectionFieldInputs` back to the parent).

- [ ] **Step 4: Build and test**
  ```
  dotnet build Apperture.slnx
  dotnet test tests/Apperture.Management.Ui.Client.Tests/Apperture.Management.Ui.Client.Tests.csproj
  ```
  If the shared predicate is extracted into a plain, testable static method, add a focused unit test covering: all required fields filled → true; one required field empty → false; optional fields empty, required fields filled → true; no fields defined → true.

- [ ] **Step 5: Commit**
  ```
  git add <changed files>
  git commit -m "HSK-A: fix Connection create eligibility to reflect connector-required fields"
  ```

---

### Task 5: NotificationDefinition — stale-validation correctness

**Files:** `NotificationDefinitionAuthoringForm.razor`.

**Confirmed current shape (discovery, re-verified):**
- `CanSubmit` (`:365-370`) already correctly requires `_validation?.IsValid == true && !_dirtySinceValidation`.
- `ValidateAsync()` (`:480-505`) is button-triggered only, sets `_validation` and clears `_dirtySinceValidation` on success. **Preserve this exactly — no keystroke-triggered validation.**
- `MarkDirty()` (`:578-583`) sets `_dirtySinceValidation = true` and clears `_validation`/`_preview`. Today it is called only from: `OnIntegrationChanged` (`:419-424`), `SelectEventTypeAsync` (`:426-434`), and the property-filter / content-field add/remove/insert-variable handlers (`:436-478`).
- **Fields that feed the validation/create payload (`BuildRuleJson`/`BuildTemplateJson`) but currently do NOT call `MarkDirty()` on change** (all plain `@bind`, no companion handler):
  - `_model.UseAdvancedRuleJson` (`:137-140`), `_model.AdvancedRuleJson` (`:143-146`)
  - `_model.IndexSeverityOperator` (`:154-160`), `_model.IndexSeverityValue` (`:163-168`), `_model.IndexModuleId` (`:170-174`)
  - `row.DataField` / `row.Operator` / `row.Value` inside `PropertyFilters` (`:181-205`) — only Add/Remove-row currently call `MarkDirty`, editing an existing row's values does not
  - `_model.UseAdvancedTemplateJson` (`:223-226`), `_model.AdvancedTemplateJson` (`:228-231`)
  - `_model.Subject` / `_model.Summary` / `_model.BodyText` / `_model.ActionText` / `_model.ActionUrl` (`:238-270`)
  - `row.Label` / `row.Value` inside `ContentFields` (`:277-278`) — only Add/Remove-row currently call `MarkDirty`
- **Fields that do NOT feed `ValidateNotificationDefinitionAsync`'s payload** and should NOT call `MarkDirty` (confirm during implementation by re-checking `BuildRuleJson`/`BuildTemplateJson`/the validate-request builder — do not guess): `_model.DisplayName`, `_model.Description`, `_model.DefaultSeverity`, `_model.IsEnabled`. These affect the Create payload but not the Validate payload, so editing them alone should not force re-validation. If implementation discovers one of these genuinely does feed validation, mark it dirty too and note the discrepancy in the task's report rather than silently deciding either way.

- [ ] **Step 1: Confirm the exact validation-payload field set**
  - Before editing, re-read `BuildRuleJson`, `BuildTemplateJson`, and whatever method constructs the request passed to `Client.ValidateNotificationDefinitionAsync(...)`, to get the authoritative list of fields that affect validation. Use this as the final list — the bullets above are a strong starting point from discovery, not guaranteed complete.

- [ ] **Step 2: Add dirty-marking to every validation-relevant field's change handler**
  - For each field confirmed in Step 1 that currently lacks it, add a call to `MarkDirty()` on change — using `@bind:after="MarkDirty"` where the existing Blazor version supports it, or an equivalent `@onchange`/`@oninput` handler consistent with how the codebase already wires similar fields. For per-row fields (`PropertyFilters`, `ContentFields`), ensure editing an existing row's value (not just adding/removing a row) also triggers `MarkDirty()`.
  - Do **not** add `MarkDirty()` calls to fields confirmed in Step 1 to be outside the validation payload.
  - Do **not** call the validate API from any of these handlers — `MarkDirty()` only invalidates local state; `ValidateAsync()` remains the sole entry point to the server call, triggered only by its existing button.

- [ ] **Step 3: Add focused tests where practical**
  - If `_dirtySinceValidation`/`MarkDirty`/`CanSubmit` logic can be exercised via a plain unit test against extracted state (consistent with the repo's no-bUnit convention), add cases: successful Validate → CanSubmit true; edit a validation-relevant field after Validate → CanSubmit false until re-Validate; edit a non-validation field (e.g. DisplayName only, if confirmed non-validation-relevant) after Validate → CanSubmit stays true (adjust based on Step 1's actual finding). If the form's state isn't practically extractable for a plain unit test without a larger refactor, skip and note it — do not force a bUnit introduction for this alone.

- [ ] **Step 4: Build and test**
  ```
  dotnet build Apperture.slnx
  dotnet test tests/Apperture.Management.Ui.Client.Tests/Apperture.Management.Ui.Client.Tests.csproj
  dotnet test tests/Apperture.Management.Ui.Shared.Tests/Apperture.Management.Ui.Shared.Tests.csproj
  ```

- [ ] **Step 5: Commit**
  ```
  git add <changed files>
  git commit -m "HSK-A: invalidate stale NotificationDefinition validation on relevant edits"
  ```

---

## Out of scope for this plan

- Any code change to `IAuditService` DI registration (discovery found two separate composition roots — see Global Constraints; report only).
- HSK-B (movement dialog redesign), HSK-C (Context slug routing), Toast/Notification redesign, Components/Services design or implementation, nav redesign, Connection/Schedule lifecycle work, Context IAM.
- Components-contributed-by-Module display, Modules-under-Components relocation, Module lifecycle UI redesign (Task 2 stays additive-only).
- Introducing bUnit or any new UI test framework.
- New required-field rules, new minimum-length rules, or any validation rule not already present.

## Validation for this plan

- `dotnet build Apperture.slnx` — 0 errors, after every task.
- `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj` — full green.
- `dotnet test tests/Apperture.ArchTests/Apperture.ArchTests.csproj` — full green.
- `dotnet test tests/Apperture.Management.Ui.Shared.Tests/Apperture.Management.Ui.Shared.Tests.csproj` — full green.
- `dotnet test tests/Apperture.Management.Ui.Client.Tests/Apperture.Management.Ui.Client.Tests.csproj` — full green.
- `dotnet test tests/Apperture.IntegrationTests/Apperture.IntegrationTests.csproj` — full green (required because Task 2 changes the Integration API/repository path); report exact executed/soft-skipped counts, never present skipped as passed.
- One final whole-branch review after all tasks, per the subagent-driven-development skill, on the most capable available model.
