# Epic 72B - Worker Runtime Launch Recovery and Workflow Testability

## Status

Accepted

## Context

Dev workflows were blocked because the testing worker runtime could fail to launch with poor diagnosability and no practical retry path short of tearing down the environment.

## Root cause

The worker launch path relied on fragile dev environment assumptions:

- worker startup depended on env propagation and bootstrap SQL configuration being present at launch time
- failures were mostly visible only in ephemeral console windows
- no dedicated recovery action existed to start/restart only the worker runtime after failure.

## Decision

Epic 72B introduces a corrective, minimal recovery path:

1. add dedicated worker start/restart scripts with persisted startup state + logs
2. call worker startup via that script from `scripts/dev/up.ps1`
3. add development-only recovery endpoints to start/restart worker and read worker launch status
4. improve WorkerHost startup logging with explicit begin/success/failure events.

## Consequences

- Workflow testability is restored without full environment teardown.
- Launch failures are visible via structured logs and persisted dev state file.
- Runtime launch recovery remains deliberately lightweight; full runtime topology and operations UX are deferred to Epic 74/75.
