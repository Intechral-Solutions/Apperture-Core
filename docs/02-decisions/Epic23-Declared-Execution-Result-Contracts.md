# Epic 23: Declared execution result contracts

## Overview

Epic 23 adds an explicit authoring contract over the existing Epic 20-22 runtime model so module authors can declare operator-facing success output and failure diagnostics intent consistently.

## Implemented

- Added `OperatorExecutionResult` (preferred authoring helper):
  - success: `SuccessInline`, `SuccessSummaryOnly`, `SuccessWithheld`
  - failure: `FailInline`, `FailSummaryOnly`, `FailWithheld`
- Added `OperatorExecutionFailureException` and `OperatorDiagnosticsDisplayPolicy`.
- Updated `RunOutputCapture` to recognize `OperatorExecutionResult` success wrappers and map to existing output capture semantics.
- Updated `RunFailureCapture` to recognize `OperatorExecutionFailureException` and honor declared diagnostics policy.
- Updated handler interface remarks (`IStepHandler`, `IWorkflowExecutor`) to point authors to Epic 23 explicit contract usage.
- Updated HelloWorld success handlers to use `OperatorExecutionResult` success helpers.
- Added tests for declared success/failure mapping and declared failure processor path.

## Compatibility and scope

- Plain `object?` success returns remain supported.
- Non-declared exceptions remain supported via default failure capture behavior.
- No persistence schema redesign, no API route churn, and no parallel result model introduced.
