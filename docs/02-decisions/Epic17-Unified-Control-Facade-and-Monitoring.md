# Epic 17 - Unified Control Facade and Monitoring

## Status
Planned

## Summary
This epic establishes the application/read-model layer needed to turn Apperture’s growing domain foundations into a cohesive operator control system.

By this point, the platform has real underlying slices for:
- operation execution
- governance and authorization
- scheduling
- environment/runtime governance
- governed runtime capabilities
- runtime control requests

What is still missing is a simple, unified operator experience that sits above those slices without flattening them into one fake domain model.

Epic 17 introduces the **Unified Control Facade and Monitoring** layer so the UI can present a small number of coherent top-level concepts such as:
- things you can run
- things you can schedule
- things you can control
- things currently running or recently run
- things that need attention

The goal is not to replace the underlying domains.
The goal is to create a server-side, permission-aware facade that:
- hides unnecessary top-level complexity
- supports drill-down into domain detail
- unifies direct execution, scheduled execution, runtime control, and monitoring into one operational story

## Goals
- Introduce a unified operator-facing control facade above operations, workflows, schedules, runtimes, and control requests
- Create shared read models for top-level control targets, activity, and attention
- Provide server-side action availability projection so the UI does not guess what the user can do
- Normalize top-level status enough for monitoring while preserving domain-specific state underneath
- Integrate schedules into the operator control story without collapsing them into direct execution
- Support a simple top-level UI facade with drill-down into domain detail

## Scope
### Included
- Unified control target facade model
- Action availability projection
- Unified activity/execution monitoring model
- Schedule association at the facade level
- Minimal cross-domain attention model
- One or two minimal operator-facing facade surfaces enabled by the new models
- Docs, ADRs, and targeted tests for the facade layer

### Excluded
- Redesign of underlying operation/workflow/runtime persistence
- Replacement of detailed domain pages
- Full final UI implementation
- Giant universal execution storage model
- Permanent flattening of all domain-specific statuses
- Redesign of auth/governance foundations

## Key Problems This Epic Solves
- The UI currently risks mirroring backend slices too literally:
  - operations
  - workflows
  - schedules
  - runtimes
  - control requests
  - run history
- Operators need a simpler top-level experience that still respects:
  - permissions
  - capabilities
  - target-specific action constraints
  - drill-down detail
- Direct execution, scheduled execution, runtime control, and activity history need to feel like one coherent control story

## Planned Architectural Outcomes

### 1. Unified control target facade
A common shell for “things the operator can act on,” spanning:
- operations
- workflows
- schedules
- runtime resources / runtime control surfaces

This shell should unify:
- identity
- target kind
- display info
- grouping
- summary state
- available actions

without erasing domain distinctions.

### 2. Server-side action availability projection
The platform will project what actions are available on each target based on:
- permissions
- capabilities
- target state
- runnability / scheduleability / controllability

This keeps the UI from becoming the place where permission-critical logic is guessed.

### 3. Unified activity and monitoring shell
A common top-level activity model will be introduced for:
- operation runs
- workflow instances
- runtime control requests
- scheduled activity where appropriate

This allows a coherent “Recent Activity” / “Monitoring” surface while preserving links to detailed domain pages.

### 4. Facade-level schedule integration
Schedules will become part of the control story at the top level so users can move naturally between:
- viewing a thing
- running it now
- scheduling it
- seeing recent activity

### 5. Minimal attention model
A small cross-domain attention model will surface important operational issues such as:
- failed runs
- stale/offline runtimes
- stuck control requests
- repeated schedule failures
- disabled/unavailable targets

## Planned Phases

### Phase 1 — Top-level control target facade model
Define the unified shell for executable/schedulable/controllable targets and decide what remains domain-specific for drill-down.

### Phase 2 — Action availability projection
Define the server-side model for what actions are currently available on each target.

### Phase 3 — Unified activity / execution monitoring model
Create a common shell for runs, workflow instances, runtime control requests, and related activity.

### Phase 4 — Schedule integration at the facade level
Associate schedules with top-level control targets and unify “run now,” “scheduled,” and “recent activity” concepts in the operator experience.

### Phase 5 — Attention / issue model
Create a small cross-domain model for attention-worthy operational conditions.

### Phase 6 — Minimal facade surfaces
Enable one or two top-level operator surfaces such as:
- Control Center / Catalog
- Activity / Recent Runs / Requests
- Attention / Needs Review

### Phase 7 — Tests, docs, and hardening
Add targeted tests, docs, and ADRs for the facade model and its normalization rules.

## Recommended Contracts and Concepts
Likely concepts in this epic include:
- `ControlTargetSummary`
- `ControlTargetKind`
- `ControlActionAvailability`
- `ControlActivitySummary`
- `ControlActivityKind`
- `NormalizedControlStatus`
- `ControlAttentionItem`
- `IControlFacadeQueryService`
- `IControlActivityQueryService`
- `IScheduleAssociationQueryService`
- `IControlAttentionQueryService`

These names may change based on the codebase, but the role of these contracts should remain.

## Architectural Guardrails
- Do not collapse all domains into one persistence model
- Do not compute permission-critical action availability only in the UI
- Do not expose raw governance/action sprawl as the top-level user experience
- Do not flatten domain-specific state so aggressively that drill-down becomes meaningless
- Do not leave schedules disconnected from executable/control targets in the operator experience
- Do not hide important failures behind an overly clean facade
- Do not let the control facade become a bloated universal DTO monster

## Test Expectations
Targeted tests should cover:
- facade target identity/kind mapping
- action availability projection correctness
- normalized status mapping across domains
- activity summary correctness across operations, workflows, and runtime controls
- schedule association projection
- attention item generation
- unauthorized action leakage prevention
- drill-down link integrity and detail preservation

## Documentation / ADR Expectations
This epic should produce or update:
- architecture docs for the unified control facade model
- API/query docs for facade, activity, and attention surfaces
- operator-facing docs for top-level control center concepts
- UI architecture notes for top-level vs drill-down behavior
- ADRs for normalized facade status and action availability projection

## Completion Criteria
Epic 17 should be considered complete when:
- the platform has a coherent top-level control facade read model
- executable and controllable targets can be surfaced through a unified shell
- direct execution, scheduled execution, and runtime control can be monitored through a coherent activity model
- action availability is projected server-side in a permission-aware and capability-aware way
- top-level statuses are normalized enough for the facade without destroying domain-specific detail
- at least one simple operator-facing facade surface can be built cleanly from the resulting models

## Follow-on Impact
Epic 17 is intended to create the operator experience spine for the platform.

If done well, it will make future work on:
- richer monitoring
- UI simplification
- deeper scheduling integration
- runtime control expansion
- attention/alerting improvements

far easier and far more coherent.