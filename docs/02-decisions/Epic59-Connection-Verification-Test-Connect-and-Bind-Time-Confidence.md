# Epic 59: Connection Verification, Test-Connect, and Bind-Time Confidence

Status: Accepted  
Date: 2026-03-25

## Context

Integration setup now supports creating and binding missing connections in-flow, but metadata completeness is not equivalent to remote reachability. Operators can end up with "configured" integrations that fail at execution time due to bad credentials, endpoint, or access.

## Decision

1. Connection verification is connector-specific and optional.
2. Verification enriches the first-class `Connection` model (no shadow health model).
3. Operators can run explicit test-connect from connection context.
4. Integration readiness includes verification-derived issues as explicit **advisory** issues in Epic 59.
5. A readiness policy seam exists for future per-module/per-slot escalation from advisory to blocking, but escalation is not enabled in this epic.

## Verification model

- Connector capability flag: `SupportsConnectionVerification`.
- Persisted connection fields:
  - `LastVerificationStatus` (`NeverTested`, `Success`, `Failed`, `Unavailable`)
  - `LastVerificationAtUtc`
  - `LastVerificationSummary`
- Management API action:
  - `POST /api/connections/{id}/test-connect`
  - Runs connector-specific verifier and persists latest result.

## Readiness behavior (Epic 59)

- Readiness may emit:
  - `connection_unverified`
  - `connection_verification_failed`
  - `connection_verification_unavailable`
- These are emitted with severity `Advisory` and do not block `IsRunnable`.
- Blocking semantics remain driven by existing hard readiness failures.

## Out of scope

- Recurring dependency health polling.
- Cross-connector generic health observability framework.
- Automatic escalation policies from advisory to blocking.

## Consequences

- Operators get truthful connection confidence without overbuilding health infrastructure.
- UI can clearly communicate when verification is unavailable.
- Future policy evolution can tighten readiness semantics without changing endpoint/DTO shape.
