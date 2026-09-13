# SDK-5B Durable Sync State — Plan

**Goal:** Restart-safe last-seen watermarks for `Sample.ApiPolling` per [SDK-5B spec](../specs/SDK-5B-ApiPolling-Durable-Sync-State.md).

## Done

1. `Sample.ApiPolling.Persistence` (DbContext, entities, SQL store, migration hosted service)
2. `IApiPollingDurableLastSeenStore` + `HybridApiPollingStateStore`
3. Stage/commit watermark semantics in ProcessChanges / Complete
4. Management + Worker Host registration
5. Unit tests (failed does not advance; hybrid restart simulation)
6. SQL soft-skip integration tests
7. Authoring guide + specs updated

## Validation

- `dotnet test --filter FullyQualifiedName~Sample.ApiPolling`
- Integration soft-skip SQL tests when `APPERTURE_TEST_SQL_*` available
- Solution build

## Next

**SDK-5C** — failed-record retry for ApiPolling (reuse SDK-4 patterns).
