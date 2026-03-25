# Epic 57 — Integration Authoring, Binding, Readiness Remediation, and Scheduling Workflow

> **Status:** Proposed / planned  
> **Implementation state:** Not yet implemented

## Summary

Epic 57 is intended to make the platform's **integration-centered operator workflow** coherent and usable.

Epics 52–56C established the underlying architectural model:

- connector definitions are code-contributed configuration artifacts
- connections are first-class persisted platform entities
- integrations are the canonical deployment, binding, and scheduling unit
- readiness exists as a real projection
- procedures are explicit module-contributed operator actions
- procedure provenance and parameter visibility are now truthful and proven

What is still missing is a clean operator journey that ties those pieces together. Today, the platform risks feeling like a set of correct but disconnected capabilities.

Epic 57 is meant to close that gap by making **Integration** feel like the true control-plane center.

## Problem statement

The current risk is operator fragmentation:

- an integration may be created in one place
- required slot bindings may be managed somewhere else
- readiness may be inspected in another surface
- schedules may live behind a separate mental model
- procedures may only become discoverable after several disconnected steps

Even if the underlying architecture is correct, that still creates a weak operator experience.

## Intended outcome

Epic 57 should produce a more coherent workflow in which an operator can:

1. create an integration from a module
2. immediately see required slots, current bindings, and readiness
3. understand what is blocking readiness
4. remediate those blockers in context
5. attach or review schedules in the same integration story
6. discover and launch procedures from the context of that configured integration

## Planned scope

This epic is intended to focus on the operator workflow around integrations, specifically:

- integration creation flow
- integration-centered slot binding workflow
- actionable readiness and remediation cues
- schedule attachment and visibility in integration context
- procedure discovery in context
- API and UI consistency across the flow

## Planned deliverables

### 1. Integration creation flow

Operators should be able to create an integration from an eligible module and land directly in a useful configuration/remediation view.

The post-create flow should make the next steps obvious.

### 2. Integration-centered binding workflow

The integration detail experience should make it easy to see:

- the module backing the integration
- required slots
- current bindings
- expected connector/connection shape for each slot
- whether a slot is missing, satisfied, or invalid

Operators should be able to bind or change connections from the integration workflow without relying on detached mental models.

### 3. Actionable readiness remediation

Readiness should be more than a passive status label.

The integration view should project blockers in operator language and provide clear remediation cues such as:

- bind a missing slot
- create or select a compatible connection
- fix an incompatible binding
- attach or adjust a schedule

This should remain deterministic and evidence-based rather than heuristic.

### 4. Schedule ownership in context

Schedules should feel like part of the integration, not a separate subsystem.

Operators should be able to:

- see schedules associated with the integration
- add or edit them in integration context
- understand whether schedule presence or absence affects readiness or expected behavior

### 5. Procedure discovery as the downstream action surface

Procedures should appear as the intentional actions available on an integration once configuration is in place.

The platform should make it clear that procedures are:

- contributed by the module
- integration-scoped
- governed by readiness and permissions
- downstream of configuration, not a parallel subsystem

## Architectural intent

Epic 57 is intended to reinforce these truths:

- **Integration** is the canonical deployable control-plane unit.
- **Bindings** and **schedules** are part of the integration story.
- **Readiness** is evidence-based and actionable.
- **Procedures** are the intentional action surface exposed by modules through integrations.
- The management API and UI should tell the same story.

This epic should not add a new abstraction layer. It should make the existing model operationally coherent.

## Non-goals

Epic 57 should **not**:

- redesign connectors
- redesign procedures
- implement gateways
- introduce a sensitive-field model for procedure parameters
- build a workflow designer
- add generic connector or client action surfaces
- prioritize cosmetic polish over operator clarity

## Definition of success

Epic 57 should be considered successful when the platform clearly reflects:

- an **integration-centered authoring and configuration workflow**
- **actionable readiness remediation**
- a coherent flow from **module selection → integration creation → slot binding → schedule attachment → procedure launch**
- no reintroduction of platform-managed client operations or other parallel abstractions

## Acceptance criteria

Epic 57 should be considered complete only if all of the following are true:

1. An operator can create an integration and immediately understand how to finish configuring it.
2. Slot bindings are visible and manageable in integration context.
3. Readiness blockers are actionable, not merely descriptive.
4. Schedules are visible and manageable as part of the integration workflow.
5. Procedures are presented as the intentional downstream action surface of the integration.
6. The management API and UI tell one coherent integration-centered story.
7. No generic connector or client operation surface is introduced.

## Follow-up considerations

Likely follow-up areas after this epic may include:

1. UI refinement once the workflow is structurally sound.
2. Stronger remediation guidance for more advanced readiness cases.
3. A future sensitive-field model for procedure parameters if operators need to submit secret-bearing values.
