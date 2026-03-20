# Epic 17 - Unified Control Facade and Monitoring

## Status
Completed

## Summary
This epic established the first real application/read-model facade for Apperture’s operator experience.

By this point in the platform, Apperture already had meaningful domain foundations for:
- operation execution
- governance and authorization
- scheduling
- environment/runtime governance
- governed runtime capabilities
- runtime control requests

What was still missing was a cohesive operator-facing control system that sat above those slices without flattening them into one fake domain model.

Epic 17 introduced the **Unified Control Facade and Monitoring** layer so the UI can present a small number of coherent top-level concepts such as:
- things you can run
- things you can control
- recent activity
- items needing attention

A corrective pass was completed to make the facade honest and operationally useful by:
- adding target drill-down contracts
- ensuring all emitted attention kinds are actionable
- removing misleading first-class schedule targets from the top-level target shell
- keeping schedule semantics as truthful activity overlay classification only
- expanding tests around drill-down integrity, schedule overlay parsing, action availability, and runtime shutdown normalization

The result is a real facade spine rather than a stitched dashboard with inflated claims.

## Goals
- Introduce a unified operator-facing control facade above operations, workflows, schedules, runtimes, and control requests
- Create shared read models for top-level control targets, activity, and attention
- Provide server-side action availability projection so the UI does not guess what the user can do
- Normalize top-level status enough for monitoring while preserving domain-specific state underneath
- Support a simple top-level UI facade with drill-down into domain-native detail
- Keep schedule integration truthful rather than forcing premature first-class target semantics

## Scope
### Included
- Unified control target facade model
- Server-side action availability projection
- Unified activity/execution monitoring model
- Cross-domain attention model
- Minimal operator-facing control center surface
- Runtime shutdown request monitoring drill-down
- Corrective pass for drill-down integrity, honest schedule semantics, and stronger facade tests

### Excluded
- Redesign of underlying operation/workflow/runtime persistence
- Replacement of detailed domain pages
- Full final UI implementation
- Giant universal execution storage model
- Permanent flattening of all domain-specific statuses
- First-class schedule target read model in this epic
- Redesign of auth/governance foundations

## Key Problems This Epic Solved
- The UI no longer has to mirror backend slices too literally across:
  - operations
  - workflows
  - runtimes
  - control requests
  - run history
- The platform now has a server-side control facade so the UI can consume:
  - top-level targets
  - activity
  - attention
  - action availability
- Operators now have a more coherent default landing/control surface with drill-down into domain-native pages
- Schedule-related activity can appear in the unified activity model without pretending schedule is already a first-class facade target when it is not

## Major Architectural Outcomes

### 1. Unified control facade layer
A real application/read-model layer now exists above the underlying domains.
This facade does not replace operations, workflows, runtimes, schedules, or control requests as domain concepts.
It provides a coherent top-level shell over them.

### 2. Server-side action availability projection
The platform now computes top-level action availability on the server side using:
- permissions
- capability constraints
- target kind
- target state

This prevents the UI from becoming the place where control rules are reconstructed.

### 3. Unified activity model
A common activity shell now exists for:
- operation/workflow runs
- runtime control requests
- scheduled activity overlay classification where applicable

This enables a coherent “recent activity” surface without destroying domain-specific detail.

### 4. Cross-domain attention model
A minimal but real top-level attention model now exists for important operator-visible issues, and all emitted attention kinds are now actionable after the corrective pass.

### 5. Honest schedule semantics
The corrective pass removed inferred schedule targets from the top-level control target list.
Schedule now remains:
- `TargetKind=schedule`
- `ActivitySource=scheduled`

inside the activity model only.

This keeps the facade truthful until a real first-class schedule target model exists.

### 6. Target drill-down contract
Top-level control targets now have a stable drill-down contract via `DetailLink`, routing into existing domain-native control surfaces rather than requiring a speculative universal target detail system.

## Implemented Changes

### Facade models and query services
Added facade DTOs/models and normalization helpers in the control facade layer.

Implemented query services for:
- unified control center surface
- unified activity feed
- cross-domain attention items
- server-side action availability projection

Key services included:
- `ControlCenterQueryService`
- `ControlActivityQueryService`
- `ControlAttentionQueryService`
- `ControlActionAvailabilityProjection`

### Facade endpoints
Added:
- `GET /api/control/center`
- `GET /api/control/activity`
- `GET /api/control/attention`

Added runtime control monitoring support:
- `GET /api/runtime-instances/{runtimeId}/controls/shutdown/requests`

### Frontend operator surfaces
Added:
- `/control/center` page
- runtime shutdown request drill-down page

Updated:
- operator landing to default to the control center
- sidebar/navigation to include Control Center
- management client to consume facade endpoints

### Activity normalization and schedule overlay
Implemented a unified normalized activity feed that merges cross-domain activity into a shared shell.

Schedule overlay classification was initially derived from correlation-key parsing and retained only inside the activity model after the corrective pass.

### Action availability
Implemented a server-side projection for target action availability that is:
- permission-aware
- capability-aware
- state-aware

### Attention model
Implemented a permission-safe cross-domain attention query service and attention surface.

Corrective pass ensured:
- all emitted attention kinds have valid UI “Open” behavior
- no decorative attention items remain in the top-level control facade

### Target drill-down
Corrective pass added:
- `ControlTargetSummaryDto.DetailLink`

Targets now route to existing domain-native pages such as:
- operations → operation start
- workflows → workflow start
- runtime instances → runtime shutdown request list

### Honest schedule correction
Corrective pass removed inferred schedule targets from the `Targets` list and kept schedule semantics only as activity overlay classification.

## Corrective Pass Outcomes
The corrective pass resolved:
- missing drill-down/navigation on top-level targets
- incomplete attention drill-down for emitted attention kinds
- misleading first-class schedule target semantics
- overly weak schedule overlay parsing protection
- narrow facade seam test coverage

## Final Truthful State
After correction, Epic 17 establishes:

### What is real
- a real backend control facade layer
- a usable control center surface
- unified activity feed
- server-side action availability projection
- meaningful top-level attention model
- target drill-down into domain-native detail
- honest schedule handling as activity overlay only

### What is not yet real
- first-class schedule target modeling in the facade
- a universal target detail page
- full final operator UI
- rich alerting/triage workflow system
- complete cross-domain operational cockpit

## Testing
Added and expanded targeted tests for:
- schedule token extraction and overlay classification
- normalized status behavior
- action-availability cases and state matrix behavior
- attention drill-down integrity
- target drill-down contract generation
- omission of schedule targets from the top-level target shell
- runtime shutdown normalization permutations
- permission-safe target emission
- schedule overlay false positive/negative edge cases

## Documentation / ADRs
Updated:
- `docs/04-api/contracts.md`
- `docs/01-architecture/control-panel-ui.md`
- `docs/01-architecture/unified-control-facade.md`

Added/updated:
- ADRs for the unified control facade model and related normalization/availability decisions
- corrective updates clarifying that schedule remains a transitional activity overlay rather than a first-class top-level target in this epic

## Completion Criteria
Epic 17 is complete because the platform now has:
- a coherent top-level control facade read model
- executable and controllable targets surfaced through a unified shell
- direct execution and runtime control represented in a coherent activity model
- server-side action availability projection
- a meaningful top-level attention surface
- drill-down support into existing domain-native pages
- truthful schedule semantics rather than overclaimed schedule target integration

## Follow-on Impact
Epic 17 created the operator experience spine for the platform.

The next logical epic is to close one of the remaining major seams:
- **first-class schedule and control target unification**, or
- deeper runtime operational visibility/health modeling

Epic 17 deliberately stopped at a truthful, durable facade layer rather than overreaching into a fake universal control system.