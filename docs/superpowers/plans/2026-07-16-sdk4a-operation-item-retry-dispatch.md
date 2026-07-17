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
- Outcome: attempts stop at **`Dispatched`** (SDK-4B reconciles final outcomes)
- Parent item status/error fields remain unchanged; counters may bump

## Out of scope (deferred)

- SDK-4B outcome sync · SDK-4C UI · retry-all · policies · scheduled retry

## Validation

- Unit: `OperationItemRetryServiceTests`, `OperationItemRetryAttemptStoreTests`, `FileImportRetryModeTests`
- Existing FileImport / operation itemization tests
- Solution build
