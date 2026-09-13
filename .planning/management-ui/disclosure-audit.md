# Progressive disclosure audit

Candidates for hiding secondary or forensic content. **Do not** hide required actions or permission-gated primary tasks.

Criteria:

| Level | Use when |
|-------|----------|
| **always** | Orientation, primary actions, blocking status |
| **section** | Operational detail the user edits often |
| **advanced** | IDs, payloads, lineage, low-frequency diagnostics (`ManagementAdvancedSection`, `TechnicalDetails`) |
| **drill-in** | Large secondary workflows (diagnostics routes, authoring sub-flows) |

---

## Signed off / done

| Location | Current | Target | Status |
|----------|---------|--------|--------|
| Account Profile | `Disclosure` for roles/links | section | OK |
| Account Profile | `TechnicalDetails` for ids | advanced | OK |
| Account Preferences | Platform settings card | always (summary) | OK — not personal prefs |

---

## High priority (next batches)

| Location | Issue | Proposal | Batch |
|----------|-------|----------|-------|
| `IntegrationDetail.razor` | Many inline forms + bordered pockets on one scroll | Summary band + grouped `ManagementSection`; inline schedule create → section or modal; bindings advanced | Integrations |
| `IntegrationScheduleDiagnostics.razor` | Dense diagnostic wall | Route already separate; collapse evidence blocks into `ManagementAdvancedSection` | Integrations |
| `IntegrationNotifications.razor` | Cutover + diagnostics + subscriptions | Tab-like sections or anchored subsections; diagnostics → advanced | Integrations |
| `NotificationDefinitionAuthoringForm.razor` | Long single scroll | Validate/preview in section; raw payload → advanced | Config notifications |
| `ExecutionStart.razor` | All parameters visible | Group by procedure sections; optional advanced JSON view | Execute |
| `RoleDetail.razor` | Permission matrix always expanded | Matrix in section; effective summary always visible | Governance |
| `AuditTimeline.razor` | Filters always visible | Filters in `ui-action-row`; export/advanced filters → collapsible | Control |

---

## Medium priority

| Location | Issue | Proposal |
|----------|-------|----------|
| `ControlCenter.razor` | Multiple summary + activity sections | Already layered; tune advanced evidence only |
| `RunDetail.razor` | Investigation sections | Keep actionability first; payload panels already advanced |
| `PlatformSettings.razor` | Long category lists | Category tabs exist; row help → disclosure per row (candidate) |
| `PersonalNotifications.razor` | Custom tabs | Align with `AccountSectionNav` pattern or document as inbox-specific | **Done** — inbox uses `SegmentedControl` + `ui-inbox-list` (filter tabs, not account nav) |
| `ConnectionsPage.razor` | Two tabs | OK as drill-in; connector vs instance separation is IA |

---

## Low priority / defer

| Location | Notes |
|----------|-------|
| Governance list pages | Legacy tables; disclosure after visual harmonization |
| Demo routes | Low traffic |
| `RuntimeShutdownRequests*.razor` | Legacy master/detail; full restructure later |

Update rows when a batch ships.
