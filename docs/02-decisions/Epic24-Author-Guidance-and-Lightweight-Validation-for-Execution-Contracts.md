# Epic 24: Author guidance and lightweight validation for execution contracts

## Overview

Epic 24 strengthens module-author adoption of declared execution contracts without changing runtime semantics or breaking compatibility.

## Implemented

- Added ADR-0023 taxonomy for execution-contract authoring:
  - preferred (`OperatorExecutionResult.Success*` / `Fail*`),
  - supported fallback (plain returns, ordinary exceptions),
  - discouraged (new implicit usage when explicit intent is feasible).
- Consolidated author guidance in the module authoring guide with a success/failure selection matrix.
- Added lightweight runtime signals in canonical processors that classify success/failure paths as `declared` or `legacy` for guidance and drift visibility.
- Added focused tests verifying legacy and declared path signal behavior while preserving runtime parity.
- Added a minimal HelloWorld declared-failure proof path via `OperatorExecutionResult.FailSummaryOnly(...)`.

## Compatibility and scope

- No persistence schema, API route, or UI contract changes.
- No runtime outcome/output/diagnostics behavior redesign.
- Plain return and ordinary exception fallback paths remain supported.
