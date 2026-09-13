# SDK-5A Sample.ApiPolling Implementation Plan

**Goal:** Ship portable `Sample.ApiPolling` per [SDK-5 design audit](../../audits/sdk5-api-polling-demo-strategy-and-design.md) and [SDK-5A spec](../specs/SDK-5A-Sample-ApiPolling-Demo.md).

## Done in this epic

1. Contracts + Module projects (no Persistence / `.Ui`)
2. Five embedded JSON snapshots
3. `ISampleDirectoryApiClient` + embedded loader
4. `IApiPollingStateStore` in-memory (process-local caveat documented)
5. Workflow steps + procedure + settings contributor
6. Durable `api_poll` operation groups/items via SDK-3A writer
7. Exactly one fail-loud `sync.*` signal per Complete
8. Notification seed for `sync.completed.with.errors` + migration
9. Management Host + Worker Host registration (`AddSampleApiPolling`)
10. Unit/harness tests + authoring guide update

## Validation

- `dotnet test --filter FullyQualifiedName~Sample.ApiPolling`
- Related notification seed tests
- Solution build

## Next

- **SDK-5B** — durable SQL watermark / sync state (optional)
- **SDK-5C** — failed-record retry using SDK-4 patterns
