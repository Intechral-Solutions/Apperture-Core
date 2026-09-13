# SDK-5C ApiPolling Failed-Record Retry — Plan

**Goal:** FileImport-parity per-item retry for `Sample.ApiPolling` per [SDK-5C spec](../specs/SDK-5C-ApiPolling-Failed-Record-Retry.md).

## Done

1. `sequence` / `includeDeleted` in operation item metadata
2. `ApiPollingOperationItemRetryHandler` + DI registration (Worker + Management)
3. Retry-mode path in Validate / Poll / Compare / ProcessChanges / Complete
4. `api_poll_retry` child groups with linkage metadata
5. Single-record durable watermark update on success only
6. Unit + control-plane retry service tests
7. Authoring guide + specs

## Next

Cross-integration / business entity demo, or configuration metadata epic (SDK roadmap).
