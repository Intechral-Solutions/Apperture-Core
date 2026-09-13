# ADR: Operation blocked-work recovery is inspection-only

**Status:** Accepted  
**Date:** 2026-08-05  
**Context:** JOB-1 / Track 6 closure  

## Decision

Platform recovery does **not** implement whole-operation retry for operation-kind blocked work. The API continues to return `inspectionOnly: true` with `finalDecision: true` and guides operators to Run Detail → Affected records for item-level retry and acknowledgement.

## Rationale

- Operations that use itemization already record per-item success and failure.
- Whole-operation retry would re-execute successful items without a safe “retry failed only” contract at the operation boundary.
- Per-item retry (SDK-4) and bulk/selective retry (JOB-1) provide bounded, idempotent recovery with truthful child-run lineage.
- Leaving “not yet supported” wording implied an unfinished product; JOB-1 replaces that with an intentional final decision.

## Consequences

- Blocked-work UI and API language describe supported alternatives.
- Future whole-operation retry would require a new ADR proving preservation of successful work, idempotency, and authorization.
