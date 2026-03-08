# ADR-0004: Instance-owned scheduling (control-plane-managed, host-portable execution)

## Status

Accepted

## Context

Apperture needs a scheduling model that works consistently for Windows/on-prem and Azure-hosted deployments without changing product semantics by host. Existing boundaries already state that control plane manages and instance plane executes, but scheduling ownership needs to be explicit.

If scheduling is modeled around Azure timer mechanics or centralized in control plane runtime, the system becomes harder to run on-prem, weaker during control plane outages, and less aligned with the instance execution boundary.

We also need a model that supports scheduled workflows, module operations, connector polling, and PowerShell jobs with durable run history, retry and concurrency policy, and restart recovery.

## Decision

1. **Schedules are assigned to an instance and evaluated by that instance runtime.**
   - The instance is responsible for due-time evaluation and local execution dispatch.
   - Scheduling semantics are host-agnostic domain contracts, not host trigger primitives.

2. **Control plane is schedule source of truth and governance plane, not tenant job executor.**
   - Control plane owns schedule definition, assignment, policy, enable/disable, desired-state distribution, and run visibility.
   - Control plane does not normally execute scheduled tenant jobs or workflow steps.
   - Existing schedules continue firing locally when control plane is temporarily unavailable.

3. **Scheduling triggers starts only; dispatch targets are executed by instance runtime boundaries.**
   - Scheduler dispatches to workflow/operation/script execution services.
   - Scheduler does not inline business-step execution logic.
   - Idempotency and duplicate prevention are required parts of the runtime contract.

4. **Azure timers are adapter details, not core model.**
   - Azure-hosted deployments may use native timer capabilities to drive evaluations.
   - Domain entities and policies remain identical across Azure and Windows.

5. **PowerShell is a first-class scheduled target type, not a default instance-per-script topology.**
   - Scripts run as scheduled jobs within an instance runtime by default.
   - Dedicated instances are used selectively for strict isolation, regulatory separation, or resource contention boundaries.

## Consequences

### Positive

- Uniform scheduling mental model across hosts.
- Stronger runtime resilience because local scheduling does not require control plane availability for every firing event.
- Clear boundaries: control plane governs and observes; instance evaluates and executes.
- Portable implementation path that allows host-specific optimization without domain drift.
- PowerShell becomes manageable and observable as part of the same scheduling fabric as workflows and operations.

### Negative

- Instance runtimes must implement durable schedule state/run tracking and recovery logic.
- Additional synchronization mechanics are needed between control plane desired state and instance applied state.
- Multi-node future requires lease/coordination strategy beyond single-node lock assumptions.

### Neutral

- This ADR clarifies scheduling ownership and supersedes ambiguous interpretations that scheduler runtime could live in management host.
- Azure-native schedule features remain valid as implementation optimizations under adapter boundaries.
