# SDK-3D Run Detail Operation Itemization Panel — Plan

> Spec gate = SDK-3B read APIs + SDK-3C FileImport durable items.

**Goal:** Read-only Management UI panel on Run Detail for durable operation groups/items.

## Placement

- Below `RunSummaryBand`, above investigation/forensics
- Organism: `RunOperationItemizationPanel.razor`
- Helpers: `OperationItemizationUiHelpers` in Ui.Shared

## Behavior

| State | UI |
|-------|-----|
| No groups | Empty copy (not an error); modules must use writer |
| Groups | Kind, source, status, counts, timestamps (`TimestampDisplay`) |
| Expanded group | Items via SDK-3B client; status chips; failed-first when All |
| Pagination | Load more (`skip`/`take`, page size 50) |
| Metadata | `<details>` disclosure (not primary row) |
| API failure | Section-level error + Try again; page stays up |
| Retry | `IsRetryable` indicator only — **no actions** (SDK-4) |

## FileImport example

`Sample.FileImport` `customers-mixed` → one `file_import` group, failed rows visible with errors, retryable Yes, no Retry button.

## Status

- [x] Panel + helpers
- [x] Tests + docs
- [x] Management.Ui.Client build + Ui.Shared tests + solution build
- [ ] Manual AppHost smoke (optional — not run)
- [ ] Follow-up: **SDK-4** per-item retry
