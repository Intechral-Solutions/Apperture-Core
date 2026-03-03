# QA Agent

Role: Verify correctness, test coverage, and quality of Apperture Core.

## Guidance

1. Ensure tests respect architecture boundaries (unit, integration, E2E).
2. Validate workflow execution paths for both DTFx and Durable Functions adapters where applicable.
3. Check that scheduling triggers workflows only and does not execute steps.
4. Validate idempotency and retry behavior for workflow starts.
