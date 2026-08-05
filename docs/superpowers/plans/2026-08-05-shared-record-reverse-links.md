# Shared-record reverse links Implementation Plan

> **For agentic workers:** Implement tasks in order. Do not invent a platform entity model.

**Goal:** Affected-record items with `recordType=customer` link to Demo Customer detail via a reusable catalog.

**Architecture:** `SharedRecordLinkCatalog` in `Apperture.Management.Ui.Shared` parses metadata and maps known types to href/label. Panel consumes `TryResolve`.

## File map

| File | Role |
| --- | --- |
| `.../SharedRecordLinkCatalog.cs` | Catalog + parse/resolve |
| `.../RunOperationItemizationPanel.razor` | Render reverse link |
| `tests/.../SharedRecordLinkCatalogTests.cs` | Unit + integrity coverage |
| Spec above | Design record |

## Task 1: Catalog + tests

1. Add failing tests for customer match, SubjectKey fallback, unknown type, missing metadata, WithoutContext.
2. Implement `SharedRecordLinkCatalog`.
3. Run shared tests until green.

## Task 2: Panel wiring

1. In item actions, if `TryResolve(item.MetadataJson, item.SubjectKey, out var link)`, render `<a href="@link.Href">@link.Label</a>`.
2. Add source-integrity assertion that the panel calls the catalog.
3. Run shared tests.
