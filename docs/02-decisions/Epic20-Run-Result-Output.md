# Epic 20: Canonical Run Execution Result and Operator-Visible Output

## Summary

Introduce run-grain persisted capture of the final step return value on successful completion, with explicit output availability, bounded serialization, and server-side projection into `RunDetailDto`. Update `RunDetail` to separate outcome, failure, and output. Prove the path with HelloWorld.

## Implemented

- ADR-0019: Run output capture semantics
- `OperationRuns` migration: `CapturedOutputJson`, `CapturedOutputSummary`, `CapturedOutputStatus`, `CapturedOutputVersion`
- `RunOutputCapture` helper (serialize, truncate, status vocabulary)
- `WorkflowStartRequestProcessor` and `OperationStartRequestProcessor` persist capture on successful terminal completion
- `RunResultDto` extended with `OutputAvailability`
- `RunQueryService.BuildResult` projects from persisted fields
- `RunDetail.razor`: outcome, failure, output availability, inline payload rules
- HelloWorld `Complete` and `DoWork` steps return serializable proof payload
- Unit tests: `RunOutputCaptureTests`, `RunQueryServiceTests` (captured output projection)
- `docs/04-api/contracts.md`: Run result and output section

## Out of Scope

- Per-step output history
- Artifact store or download endpoint
- Rich redaction policy engine
