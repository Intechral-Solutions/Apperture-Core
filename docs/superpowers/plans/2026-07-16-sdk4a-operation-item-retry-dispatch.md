# SDK-4A — Operation Item Retry Attempt Persistence & FileImport Dispatch

**Status:** Implemented  
**Date:** 2026-07-16  
**Design:** [sdk4-per-item-retry-design-audit.md](../../audits/sdk4-per-item-retry-design-audit.md)

## Delivered

- Table `OperationItemRetryAttempts` (migration `20260716160000_Sdk4A_OperationItemRetryAttempts`)
- Stores: `IOperationItemRetryAttemptStore` (SQL + in-memory); `IOperationItemStore.GetItemAsync` / `RecordRetryDispatchAsync`
- Service: `IOperationItemRetryService` / `OperationItemRetryService`
- API: `POST /api/runs/{runId}/operation-items/{itemId}/retry`, `GET .../retries`
- Contract: `IOperationItemRetryHandler`; FileImport `FileImportOperationItemRetryHandler` + ProcessRows `retryMode`
- Outcome: attempts stop at **`Dispatched`** at dispatch time; **SDK-4B** reconciles to `Succeeded`/`Failed`
- Parent item status/error fields remain unchanged; counters may bump

## Out of scope (deferred from 4A)

- ~~SDK-4B outcome sync~~ → done in SDK-4B
- SDK-4C UI · retry-all · policies · scheduled retry

## Follow-on

See [SDK-4B plan](2026-07-17-sdk4b-retry-attempt-outcome-reconciliation.md).

## Validation

- Unit: `OperationItemRetryServiceTests`, `OperationItemRetryAttemptStoreTests`, `FileImportRetryModeTests`
- Existing FileImport / operation itemization tests
- Solution build
