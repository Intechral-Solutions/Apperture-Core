# RC1 Architecture Audit and Program Plan

| | |
|---|---|
| Date | 2026-10-03 |
| Baseline | `master` at `5cfa901` (merge of PR #3, IAM-8.5), clean working tree |
| Status | Working record of the audit and the proposed program. Rulings were made on 2026-10-03; the contract is [`rc1-program-gate.md`](../01-architecture/rc1-program-gate.md), which governs where this record differs. Epic and work-package detail in sections H and I remains the reference, as amended by the gate's section 6. |
| Scope | Current-state audit, RC1 definition, requirements traceability, ordered epic program, work packages, deferred architecture |
| Supplement | Read with [`rc1-supplemental-reconciliation-addendum.md`](rc1-supplemental-reconciliation-addendum.md). The addendum governs where the two differ. Section R's rows are recommendations awaiting approval, and section T's recommendation to start RC-0 is replaced by the addendum's section L. |

**How to read this.** Sections A to C give the verdict and the RC1 definition. Section D is the traceability matrix for every product-owner objective. Sections G to I are the program, epics and work packages. Sections J to N hold the detailed findings. Sections R to T list decisions, open questions and the next step.

**Evidence standard.** Nine parallel read-only investigations traced code, tests and documents. Nothing was built or run. I re-verified these claims directly against source: the Runs triage sort, the hamburger morph, role assignment having no escalation check, the Entra unlink guard gap, the absence of session revalidation, the Development-only health endpoints, the null run-cancel requester, the silent missing-step-handler path, the implied permissions of `role.manage`, the anonymous first-admin endpoint, and the raw user-id rendering sites. Everything else is from the investigation reports and is cited by path so it can be checked. Line numbers are as reported at `5cfa901`.

**Precedence used.** Product-owner direction is future intent. Accepted ADRs and design gates describe current decisions unless explicitly superseded. Code and tests are the evidence of what exists.

---

## A. Executive Assessment

### Maturity

Apperture Core is a functionally rich **operator product on a development topology**. The management plane (governance, integrations, connections, schedules, runs, notifications, resource contexts) is deep, well tested at unit level and documented decision by decision. It is not yet a release candidate, and the gap is not the near-term wishlist. The gap is that four foundations under the wishlist are weaker than the surface suggests:

1. **Identity has no username and no user status.** A "username" is `IdentityLink.ExternalId` on a local link. Entra-only users have none. There is no disable, no delete, and no session revalidation.
2. **Authorization has open privilege escalation.** Any holder of `role.assign` can assign themselves the platform administrator role. Any holder of `role.manage` implicitly holds `user.manage`, `credential.manage`, `role.assign`, `permission.assign` and `auth.entra.manage`.
3. **Audit cannot attribute reliably.** `AuditLogs.Actor` holds a username, an Entra object id or a user id depending on the code path. There is no target column. Most configuration changes and all manual run actions are unaudited or unattributed.
4. **There is no production deployment path.** No Windows Service hosting, no installer, no production health endpoint, no explicit migration step, no backup or restore tooling, no release tags.

### Major strengths (preserve these)

- **Decision discipline.** Design gates with rulings, closure audits and ADRs. The plan below reuses that pattern.
- **Connection lifecycle audit** (`ConnectionManagementService`, ADR-0044) is the model for how audit should work: structured details, transactional, field names without values, tested for secret leakage.
- **Browser security** (SEC-1, ADR-0045) is real engineering with a CI gate on three browser engines.
- **The procedure launch pipeline** (`IntegrationProcedureService.LaunchAsync`) is already a general operation envelope: readiness, authorization, schema validation, durable dispatch, idempotency, result contract.
- **Resource contexts** are cleanly separated from authorization, with transactional moves and concurrency proofs.
- **The UI is DTO-driven.** The Blazor client needs no module assemblies. That is the one seam that already suits installable modules.
- **Typed, URL-encoded navigation context** (UIX-3 and UIX-4) is a sound base for drill-through breadcrumbs. It survives refresh, new tab and share, and never carries a URL in the query string.
- **No secrets were found in any audit payload.**

### Major RC1 gaps

| Area | Gap |
|---|---|
| Identity | No username attribute, no user status, no disable or decommission, no admin reset flow, no lockout or throttling on local sign-in |
| Authorization | Self-promotion via `role.assign`; `role.manage` implication; no sensitivity or step-up metadata; permission catalog renders as raw strings |
| Audit | Mixed actor identity, no target columns, unaudited configuration lifecycle, unattributed run actions, outcome column used as a category, scheduler noise |
| Navigation | Metadata in seven places, 97 hand-written breadcrumb matchers, no browser titles, no menu icons, Schedules and Connections in no menu |
| Lists | No paging on Runs, eight paging shapes, no shared async selector, no user search |
| Module runtime | A step with no registered handler records as Completed; no step timeout; a long step can outlive its lease |
| Operations | No install, upgrade, backup, restore or health path outside a developer machine |
| Verification | End-to-end test project is a placeholder; 33 SQL test sites pass silently without SQL; no functional browser tests; accessibility unmeasured |

### Largest architectural risks

1. **Freezing identity attribution wrong.** User delete, username rename and audit integrity all depend on normalizing the actor to a stable user id first. Doing user lifecycle before that would bake in unattributable history.
2. **Shipping the escalation paths.** A "root-level" credential-recovery permission is meaningless while `role.assign` allows self-promotion.
3. **Calling the SDK stable.** Four parallel form schemas, no version check, hand-wired composition per host, and lifecycle verbs ("install", "enable") whose meaning true installation would have to break.
4. **Undefined RC1.** No document defines exit criteria. The roadmap reserves the term for a final track that has five undefined bullets.
5. **Stated invariants that the code does not satisfy.** One module per process (all five modules are compiled into one worker) and "adapters exist for DTFx and Durable Functions" (both are stubs). These are in `AGENTS.md` as non-negotiables.

---

## B. Repository Reality Check

### Actual state

- **Branch:** `master` at `5cfa901`, clean. IAM-8 (custom role lifecycle) is merged.
- **Authoritative sequencing document:** `docs/00-overview/pre-release-product-roadmap.md`. It records "Current next: none selected".
- **`docs/roadmap.md`** contains only federation phases and secrets phases, with no status markers. It is a stale orphan.
- **Topology that runs:** Aspire AppHost in development with three processes (`Management.Host`, `Management.Ui.Server`, `WorkerHost`) and SQL Server. Scheduling runs inside WorkerHost. `SchedulerHost`, `ModuleHost`, `InstanceAgent`, the Azure template and both workflow backend adapters are stubs.
- **Execution engine:** a SQL start-request queue plus `DefaultWorkflowExecutor`. Workflows are linear lists of step names.
- **Modules:** compiled into both hosts by project reference. `AddKnownModules([...])` in `Management.Host/Program.cs`; a hand-built list in `WorkerHost/Program.cs`.
- **Tests:** about 2,700 unit, 1,000 UI source-contract, 300 UI client, 250 integration, 69 browser (security only), 30 architecture, 1 end-to-end placeholder.

### Discrepancies between documents and code

| # | Document claim | Reality | Consequence |
|---|---|---|---|
| 1 | `AGENTS.md` rule 3: one module per compute process | All five modules run in one WorkerHost (`WorkerHost/Program.cs:124-143`, acknowledged in a comment) | Needs a ruling. See decision D-17. |
| 2 | `AGENTS.md` rule 4: adapters exist for DTFx and Durable Functions | `DurableTaskRuntimeAdapterStub` is empty; `DurableFunctionsRuntimeStub` throws | Portability is a design intent, not a capability. State it honestly. |
| 3 | Roadmap and four other documents: IAM-8 "not merged" | `HEAD` is the merge commit | Doc fix. |
| 4 | ADR-0040 rule 2 and 3: Runs belong to Control Center; Connections and Schedules stay off the rail | Product owner now wants Runs and Schedules under Integrations and Connections under a Services domain | Requires explicit supersession. |
| 5 | ADR-0044: "Service" is never an operator-facing noun, route or permission | Product owner wants a Services rail domain | Requires a scoped amendment. See decision D-08. |
| 6 | ADR-0005 to ADR-0011 and `federated-instance-relationships.md` describe federation | All seven ADRs are "Proposed"; no implementation; one seeded permission (`relationship.manage`) gates nothing | Withdraw. Remove the dormant permission from the catalog. |
| 7 | ADR-0012: ownership check, break-glass "deny if not authorized", evaluation uses the registry | `OwnerId` is never consulted; break-glass allows everything; only `ExecutablePermissionEvaluator` uses the registry | Amend ADR-0012 during IAM-10. |
| 8 | Module authoring guide: startup validates every workflow step has a handler | `WorkerHostStartupValidation.ValidateHandlerOwnership` is an empty stub; a missing handler returns `null` and the step completes | Defect. See SDK-8. |
| 9 | Module manifest spec documents `helpText`, `defaultValue`, `order` | Parser reads `help` and `default`; no `order`; sample modules declare `"type":"boolean"` where the vocabulary is `bool` | Defect. Fields render as text and skip type validation. |
| 10 | Epic 26 "install" and "enable" | An activation flag over compiled-in code; several registries only refresh on restart | Naming will collide with real installation. |
| 11 | IAM-3: Recovery Access is omitted from user "search" and rejects "rename, disable, delete" | None of search, rename, disable or delete exists | Doc drift. |
| 12 | `control-panel-ui.md`: a rail button navigates when a domain has one item | It never navigates, except two hard-coded cases | Doc drift. |
| 13 | `current-architecture-tracking.md` and `.ai/playbooks/project-map.md` | Both omit about fifteen real projects and describe stubs as current | Regenerate before RC1. |
| 14 | Two files numbered ADR-0013; two ADR indexes that disagree; IAM-8 and LFC-1 have no ADR and live under a folder described as disposable | | Doc hygiene in RC-0. |
| 15 | `deploy/README.md` and the example appsettings | Reference folders that do not exist; the example production configuration cannot start a host because the credential resolver is a stub | Fix in OPS-1. |

---

## C. RC1 Definition

No repository document defines RC1. This is the proposed definition. It needs product-owner approval (decision D-01).

**RC1 is a feature-complete, upgrade-safe candidate of the single-instance operator platform on one supported topology, with a security-reviewed identity and audit core, a coherent operator experience, and a module SDK explicitly labelled preview.**

Concretely, RC1 is reached when all of these hold:

1. **Supported topology is one thing and it works.** Windows Server, SQL Server, three hosts as Windows Services, Integrated Security. Azure, Docker and Linux are stated as not supported in RC1.
2. **An operator can install, upgrade, back up, restore and health-check** by a documented procedure with scripts, and a fresh instance cannot be claimed by an anonymous caller.
3. **Identity lifecycle is complete:** username, disable, decommission, credential recovery, session termination, sign-in throttling.
4. **Authorization has no known escalation path,** and every permission has a human-readable name, description and sensitivity.
5. **Audit attributes every administrative and operator action to a stable user identity,** with a queryable target, and routine noise is separated from significant events.
6. **Navigation and entity screens are consistent:** one navigation registry, titles on every route, icons, one toolbar model, one list and selector pattern.
7. **Runtime correctness defects are closed:** missing handler, lease expiry during a long step, unvalidated parameter schemas.
8. **Verification is honest:** no silently passing SQL tests, at least one real end-to-end run test, functional browser smoke, an accessibility baseline, an upgrade test, and the outstanding operator smoke runbooks executed.
9. **Documentation matches the code:** roadmap reconciled, federation withdrawn, architecture tracking regenerated, changelog and version stamping in place.

**RC1 is not:** multi-host Azure deployment, module installation from packages, metrics, dashboards, workspaces, a rebuilt CLI, a script runtime, context-scoped authorization, or a stable SDK.

---

## D. Requirements Traceability Matrix

Classification key: **R1** must implement before RC1. **R2** must design or settle before RC1. **R3** should implement if low risk. **R4** post-RC1. **R5** horizon. **R6** already satisfied. **R7** invalidated or superseded.

### D.1 Status, classification and destination

| ID | Requirement | Horizon | Current status and evidence | Class | Destination |
|---|---|---|---|---|---|
| N-01 | Hamburger shows X when the drawer is open | Near | Three-bar CSS morph to an arrow in `Atoms/HamburgerToggle.razor`. No `aria-expanded`. | R3 | QW-1.1 |
| N-02 | Username is the standard short identifier; display name in richer views with username tooltip; ids not user-facing | Near | No username attribute. `UserListDto` has no username. Raw ids shown on about 15 pages (`UsersList.razor:76`, `UserDetail.razor:25,95`, `AuditTimeline.razor:164`, team members, notification recipients). No shared user component. | R1 | IAM-9, LST-1.3 |
| N-03 | Usernames in URLs (deferred by owner) | Near | Routes use opaque ids. ADR-0041 tier 2 allows a natural key later. | R2 | IAM-9.0 decides stability rules only |
| N-04 | Disable user | Near | Absent. Only the local credential can be disabled. Sessions survive 12 hours. ADR-0045 left revalidation undecided. | R1 | IAM-11 |
| N-05 | Delete user | Near | Absent. Six Restrict foreign keys and many loose references. | R1 | IAM-11 (as decommission) |
| N-06 | Admin-triggered password reset under a root-level, role-expressible permission | Near | Only direct password set exists, under `credential.manage`, no step-up. No reset flow for an existing or disabled credential. | R1 | IAM-11 |
| N-07 | Unlink Entra and return a user to local access | Near | Possible only as three or four separate calls under two permissions. Last-admin guard skipped for Entra removal. | R1 | IAM-11 |
| N-08 | Runs sorted by date, with outcome filters | Near | Server returns newest first. Client forces `SortRunsForTriage` in `RunsList.razor:354` and `IntegrationRuns.razor:83`. Status filter exists. | R1 | QW-1.3 |
| N-09 | Team members added by user selector | Near | A textarea with a mini-grammar; save replaces all member rows (`AttentionContactGroupDetail.razor:51`). No add or remove endpoint. | R1 | LST-1.4 |
| N-10 | User detail lists teams and can add the user to a team | Near | Nothing about teams on `UserDetail.razor`. No reverse lookup endpoint. | R1 | LST-1.5 |
| N-11 | Consistent entity controls | Near | Three edit models, nine documented inconsistencies (section L). | R1 | ACT-1 |
| N-12 | Browser titles for every screen | Near | Only two `<PageTitle>` elements exist. `TopBarOrientation.ApplyTitleSubtitle` already computes the right title. | R1 | QW-1.2, then NAV-2.1 |
| N-13 | Notification Diagnostics cleanup | Near | Two free-text integration id boxes, "required for tables" with no tables rendered, pipeline test fixed to one definition. | R3 | QW-1.4 |
| N-14 | Full audit-system review | Near | Done in this audit (section K). Remediation is the work. | R1 | AUD-1 |
| N-15 | UUID v7 audit | Near | Done in this audit (section N). v7 is used nowhere. No migration project is needed. | R3 | ID-1 |
| N-16 | Icon action toolbar with show or hide labels and tooltips | Near | Text-only buttons. `ActionButton` supports icons. No toolbar component. | R1 | ACT-1 |
| N-17 | Preserve existing full-width disclosure rows | Nav | `ControlPanelNavNode` | R6 | Invariant for NAV-2 |
| N-18 | Reusable tree navigation primitive | Nav | None. `SqlStructureBrowser` is the only `role="tree"` and is bespoke. | R1 | NAV-2.5 |
| N-19 | Hover a rail icon to show its menu; click navigates to the domain default | Nav | Hover shows a tooltip only. Default destination exists as two hard-coded cases. Panel is in-flow and unmounted when closed. | R1 for click default, R3 for hover | NAV-2.4 |
| N-20 | Services rail domain containing Connections | Nav | Conflicts with ADR-0044 and ADR-0040. `/connections` is in no menu today. `ViewConnections` capability token already exists. | R1 after ruling | RC-0, NAV-2.3 |
| N-21 | Runs to Integrations as All Runs; add All Schedules; integration trees | Nav | Runs is under Control Center. Schedules is in no menu. `/runs?integrationId=` works. `/schedules` has no integration filter. | R1 | NAV-2.3, NAV-2.6 |
| N-22 | All Connections with a tree of integrations that have connections | Nav | `/connections` has no integration filter. Bindings table gives the relation. | R1 | NAV-2.6 |
| N-23 | All Integrations tree with integrations and their panels | Nav | Integration hub sections are real routes with a descriptor registry (`IntegrationUiRoutes.Sections`). | R1 | NAV-2.6 |
| N-24 | Icons for all navigable things; future module-defined icons | Nav | Zero menu items have icons. `DomainMenuItem.Icon` exists and is never set. No module icon channel. | R1 for platform icons, R2 for the module channel | NAV-2.2 |
| N-25 | Drill-through breadcrumb context stack | Nav | Depth-one typed context for one source type (`demo-customer`) to one destination (`/runs/{id}`). | R2 design, R4 implementation | NAV-3 gate |
| M-01 | Drop federation; use guests and Entra | Mid | Federation is design-only. One active Entra tenant. Guest access requests exist, off by default. | R7 for federation, R2 for the replacement statement | RC-0 |
| M-02 | Typed platform notes on resources and business records with per-integration authority | Mid | No record model, no notes, no mutation path. | R4, with R2 seams | POL-1 gate |
| M-03 | Human-readable policies published by platform and modules | Mid | No bundle concept. Catalog has no names or descriptions. Dead `IsPrivileged` scaffolding exists. | R1 for permission metadata, R2 for sets, R4 for module sets | IAM-10 |
| M-04 | AI verification of policy descriptions | Mid | Nothing | R5 | None |
| M-05 | Metrics as a request-style provider with authorization and throttling | Mid | No seam. `IModuleTelemetry` is a no-op. `DashboardCardFact` is string pairs. | R4, with R2 seam note | Section Q |
| M-06 | Metric drill-through | Mid | Nothing | R4 | Section Q |
| M-07 | Dashboard framework with blocks | Mid | `IDashboardCardProvider` system is orphaned; its controller says not to build on it. No chart library. | R4 | Section Q |
| M-08 | Block-level policy enforcement | Mid | Nothing | R4 | Depends on IAM-10 result type |
| M-09 | Rebuilt interactive PowerShell CLI, environment-aware | Mid | CLI is a bootstrap and emergency console over a file and direct SQL. No API authentication for non-interactive clients. | R4, with R1 operability subset | OPS-1, CLI-2 gate |
| M-10 | "Artisan" pass-through in dev mode | Mid | Not applicable to .NET as named | R4 | Open question Q-10 |
| M-11 | CLI deploys or registers another environment with trust | Mid | Nothing to anchor it. Enrollment code is weak. | R4, dedicated security gate | CLI-2 gate |
| M-12 | PowerShell module runtime | Mid | None. No step timeout or cancellation to build on. | R4, with R1 prerequisites in SDK-8 | Section Q |
| M-13 | PowerShell SDK or connector | Mid | None | R4 | Section Q |
| M-14 | Bash equivalent | Mid | None | R5 | Section Q |
| M-15 | Review module UI capability | Mid | Done in this audit (section M). Four parallel schemas. | R2 | SDK-8.0 |
| M-16 | Simple panels, complex panels, wizards | Mid | Simple about 70 percent; complex and wizards absent. | R4, with R2 schema decision | SDK-8.0, MUI-1 gate |
| M-17 | Module UI attached to business data and resources | Mid | Procedures are integration-scoped only. | R4 | Section Q |
| M-18 | Platform reuse of module UI primitives | Mid | Platform settings already use the best schema. | R6 in spirit | None |
| M-19 | Module installation from a zip | Mid | Compile-time composition only. | R4, with R2 seams | SDK-8, MOD-1 gate |
| M-20 | Platform reboot mechanism | Mid | Three independent processes. Stop-only control request exists. | R4, with R1 correctness of service restart | OPS-1, MOD-1 gate |
| L-01 | Services abstraction | Long | Connection workspace, one-to-one by ruling. | R5, with R2 "do not harden" list | Section Q |
| L-02 | Workspaces | Long | No concept. "Workspace" already has two other meanings. | R4, with R2 seams | Section Q |
| H-01 | Business Rules | Horizon | Only a notification rule evaluator. | R5 | Section Q |
| H-02 | Module authoring tool | Horizon | Nothing | R5 | None |
| H-03 | Workflow designer | Horizon | Roadmap says it is not an active item. | R5 | None |
| H-04 | AI features | Horizon | Evaluator denies all non-human actors. | R5, with R2 principal model | IAM-10.0 |

**Requirements discovered by the audit (not on the owner's list).**

| ID | Requirement | Evidence | Class | Destination |
|---|---|---|---|---|
| X-01 | Close privilege escalation through `role.assign`, `permission.assign` and `role.manage` | `UserManagementService.AssignRoleAsync:602-621`; `RbacAuthorizationPolicyEvaluator.ImpliedByInstanceRoleManage:100-115` | R1 | IAM-10 |
| X-02 | Normalize the audit actor to a stable user identity | `ClaimsPrincipalToActorMapper.cs:62-97`; `LocalSessionIdentity` | R1 | IAM-9, AUD-1 |
| X-03 | Attribute and audit run actions and configuration lifecycle | `WorkflowCancelService.cs:43` (`RequestedBy = null`); section K matrix | R1 | AUD-1 |
| X-04 | Sign-in throttling or lockout, and usable failed sign-in evidence | No limiter on `local/login`, `local/mfa`, `forgot-password` | R1 | IAM-11 |
| X-05 | A missing step handler must fail, not complete | `DefaultWorkflowExecutor.cs:127-129` | R1 | SDK-8.1 |
| X-06 | Step timeout and lease renewal during a step | `WorkflowStartRequestProcessor.cs:368-369` | R1 | SDK-8.2 |
| X-07 | Runs list paging and indexes | No `Take` from `RunsList.razor`; no index on `CreatedAt` | R1 | LST-1.2 |
| X-08 | Fresh instance takeover window | Anonymous `POST /api/bootstrap/first-admin` | R1 | OPS-1.4 |
| X-09 | Production install, upgrade, backup, health | Section B and OPS-1 | R1 | OPS-1 |
| X-10 | Honest test posture | 33 soft-skip sites; placeholder end-to-end project | R1 | RCV-1 |
| X-11 | Enrollment token stored in plaintext and passed in a URL | `SqlRuntimeEnrollmentService` (ControlPlane.Security) | R3 | ID-1.3 |
| X-12 | Module metadata hardening | `ModulePermissionRegistrar.cs:76-79`; connector registry last-write-wins | R3 | SDK-8.3 |
| X-13 | Operational team permission is `schedule.manage` | `RequireScheduleManageAsync` | R2 | LST-1.0 |
| X-14 | Break-glass grant-all branch still live behind one flag | `RbacAuthorizationPolicyEvaluator.cs:40` | R2 | IAM-10.0 |
| X-15 | Host composition hook for premium assemblies | `Management.Host/Program.cs:55-56` | R2 | SDK-8.0 |

### D.2 Dependencies, implications and open questions

| ID | Architectural dependencies | Security | Migration | UX and accessibility | Open questions |
|---|---|---|---|---|---|
| N-01 | None | None | None | Add `aria-expanded` and `aria-controls` | None. Reversible trial. |
| N-02 | X-02; new `Users.Username`; `UserReference` DTO | Usernames become visible to anyone with `user.read`; fine per owner | Backfill usernames for Entra-only users; collision handling | Tooltip must also work on focus and touch | Q-02 |
| N-03 | N-02 stability rules | Username reuse would hijack bookmarks and history | Reserve retired usernames now | None | Deferred by owner |
| N-04 | X-02; session revalidation | Must end live sessions | New status column, default Active | Clear disabled state in lists and selectors | None |
| N-05 | N-04; X-02; AUD-1 actor snapshot | Attribution must survive; bootstrap re-creation trap | Explicit clearing of six Restrict tables | Placeholder rendering through one component | Q-03 |
| N-06 | X-01 first; permission metadata; step-up gate | Highest-privilege action in the product | New permission added by the startup repair | Destructive-style confirm | Q-04 |
| N-07 | N-06; last-admin guard extended | Can strand the last administrator today | None | One guided action | None |
| N-08 | None | None | None | Subtitle text change | Q-06 |
| N-09, N-10 | N-02 user search; X-13 ruling; async selector | Selector must respect `user.read` | Member endpoints are additive | Listbox keyboard pattern | Q-05 |
| N-11, N-16 | NAV-2 icon keys; preferences field | Actions must still be enforced server-side | New typed preference field | Accessible names mandatory; tooltip on focus | None |
| N-12 | NAV-2 registry for static titles | Titles appear in history; no secrets in titles | None | Short, record-first titles | None |
| N-18 to N-24 | NAV-2 registry; ADR supersession | Tree nodes must be permission-filtered server-side | None | Full tree keyboard model | Q-07, Q-08 |
| N-25 | NAV-2 registry with a separate breadcrumb parent | Never carry a URL in the query | None | Ellipsis controls need names and keyboard access | Q-09 |
| M-01 | Context-scoped authorization for confined guests | Guests are instance-wide today | Withdraw ADRs | None | Q-11 |
| M-02, M-03 | Principal model; scope forms; record anchor | Integrations have no principal | New tables later | Readable permission catalog | Q-12 |
| M-09 to M-13 | Non-interactive API credential; step timeout | Long-lived credentials; script trust | None now | None | Q-10 |
| M-19, M-20 | Composition entry point; version check; worker composition report | Package trust; in-process full trust | Module migrations | None | None |
| L-02 | Nav registry with data-driven rail entries; typed default-view preference | Workspace must not imply data authority | Typed preference field | Name collision with existing "workspace" | Q-13 |

---

## E. Architectural Findings by Theme

### E.1 User representation (theme A)

**Finding.** There is no shared representation at any layer. The API returns ids, the UI renders them in monospace, and the only "picker" is a `<select>` of the first 200 users labelled `DisplayName (UserId)`.

**Recommendation.** One contract at each layer:

- **Domain and persistence:** `Users.Username` (unique, normalized, format-validated), `Users.Status`.
- **API:** `UserReferenceDto { UserId, Username, DisplayName, Status, AvatarKey? }`. Every DTO that exposes a user uses it. Historical actors resolve through the same shape, with `Status = Decommissioned` or `Unknown`.
- **UI:** one `UserRef` component with two modes. Compact shows the username. Rich shows the display name with the username as an accessible tooltip. It renders disabled and decommissioned states and never shows the id. One `UserSelector` built on the shared async selector.

This single abstraction covers N-02, N-05, N-09, N-10, audit actor display and notification recipient display.

### E.2 Entity actions (theme B)

**Finding.** The right shape already exists but is narrow. `ControlActionAvailabilityDto(ActionKind, AvailabilityState, ReasonCode)` (ADR-0017) covers runs, start actions and runtime shutdown. Every other entity uses 31 hand-written capability booleans plus client-side state logic.

**Recommendation.** Introduce a client-side `EntityActionDescriptor` whose availability vocabulary is the ADR-0017 one. Render it through one `EntityToolbar`. Do not build a server-side action registry for RC1. The descriptor shape is what module operations and dashboard actions will feed later, so it must not be named or shaped around pages.

### E.3 Navigation metadata (theme C)

**Finding.** Feasible and closer than it looks. Menus are already records. Breadcrumbs are pure functions of the path. `IntegrationUiRoutes.Sections` shows the descriptor pattern. The obstacles are the 97 matchers and the fact that menu parent, breadcrumb parent and owning domain legitimately differ.

**Recommendation.** One `NavigationRegistry` in `Ui.Shared` with separate fields for those three relationships, string icon keys mapped to components client-side, and a title pattern. Migrate in order: static routes and menus, section ownership, titles, then breadcrumb matchers last behind the existing tests.

### E.4 Navigation context (theme D)

**Finding.** Drill-through is a navigation-provenance concept, not a breadcrumb component feature. The existing mechanism already has the right properties. It is typed identity in the URL, validated against an application-owned catalog, with labels and hrefs never read from the query.

**Recommendation.** Keep the URL as the source of truth and extend the codec to a bounded stack of typed contexts. Do not use history state or an in-memory stack as the source of truth. Both lose refresh, new-tab and share behaviour. Details in section L.

### E.5 Policies and capabilities (theme E)

**Finding.** The `(action, resourceType, scopeKey)` descriptor, string scope containment and single evaluator seam are sound and extensible. New product areas can be added as new resource types and actions with no structural change. Three things are structurally missing and cannot be fixed with metadata: non-human principals, scope on the assignment, and data-ownership scope.

**Recommendation.** Do not create a policy engine. Map the owner's "policy" onto the existing model:

- A **permission** gains a definition: display name, description, category, sensitivity, step-up requirement, default grant to platform administrator. This is the human-readable unit. It ships in RC1.
- A **permission set** is a named, described bundle that expands to permissions when granted. Platform and modules can declare sets. Design in RC1, implement after.
- **Principal kinds and scoped assignments** are designed in RC1 and implemented when the first consumer arrives (guests confined to a context, integration authority over shared records, AI identities).

### E.6 Operations (theme F)

**Finding.** A common operation abstraction exists in fact. It is the Procedure plus `IntegrationProcedureService.LaunchAsync`, with `OperatorExecutionResult` as the result contract and `ControlActionAvailabilityProjection` for availability.

**Recommendation.** Do not invent a new Operation or Command abstraction. Later generalize the Procedure along three axes: scope (instance, integration, record), a typed parameter schema, and a third launch kind for script-backed handlers. Nothing in RC1 should add a second launch path.

### E.7 Query and list infrastructure (theme G)

**Finding.** There are several list systems, not one. Eight response shapes, about 38 hand-written tables, debounce re-implemented per page, and `SearchableList` is synchronous and in-memory.

**Recommendation.** Do not build a list framework. Standardize three small things and apply them to the lists RC1 touches: one `PagedResult<T>` and `PageRequest`, one `AsyncSearchSelect` component, and one convention for filter parameters in the URL. Apply to Users, Runs, Schedules, Connections. Leave the other tables until they are next edited.

### E.8 Module metadata (theme H)

| Metadata | Module-defined? | Constraint |
|---|---|---|
| Icons | Later | A key from a platform-owned icon set. Never markup, a URL or a file. |
| Permissions | Yes (today) | Scope forced to `module:{id}`. Fix the `GovernedResources.ScopeKey` passthrough. |
| Permission sets | Later | Only over the module's own permissions. Never grants at install. |
| UI panels | Later | The single field schema. No markup channel. |
| Operations | Yes (procedures) | Through the launch pipeline only. |
| Metrics | Later | Namespaced `module.{id}.*`, authorized through the catalog. |
| Services and connectors | Yes (connectors) | Registry must reject overwrites of platform definitions. |

---

## F. Recommended RC1 Architecture (changes only)

### F.1 Identity

- `Users` gains `Username` (unique index, normalized lower-invariant, validated format), `Status` (`Active`, `Disabled`, `Decommissioned`), `StatusChangedAtUtc`, `StatusChangedByUserId`, and `SessionVersion`.
- **Every user has a username.** Local users keep the one they have. Entra-provisioned users are assigned one at provisioning, defaulted from the UPN local part with a collision suffix.
- **The session identity becomes the user id.** The cookie `NameIdentifier` changes from the username or Entra object id to `UserId`. `SqlPrincipalToUserResolver` already resolves per request; it additionally checks `Status` and `SessionVersion`. This gives immediate disable with no session store.
- **Retired usernames are reserved forever.** This makes usernames stable enough for future URLs and prevents a new person inheriting history.

### F.2 Authorization

- The catalog entry becomes a `PermissionDefinition` with display name, description, category, `Sensitivity` (`Standard`, `Elevated`, `Critical`), `RequiresStepUp` and `GrantedToPlatformAdministratorByDefault`.
- **Grant rules.** A grantor may only place permissions they effectively hold. A grantor may only assign a role whose permissions are a subset of their own. The platform administrator role can only be assigned by a platform administrator.
- **`ImpliedByInstanceRoleManage` is removed.** Existing custom roles that relied on it are expanded to explicit grants by a one-time migration so behaviour is preserved and visible.
- **The evaluator returns a result** (allowed, reason, step-up required) through `IAuthorizationEvaluationService`. The `bool` overload stays for existing callers.
- **Step-up is declarative.** A permission marked `RequiresStepUp` triggers `IStrongAuthenticationGate` at one enforcement point.
- New permission `user.credential.recover` at `Critical` sensitivity. `relationship.manage` leaves the catalog.

### F.3 Audit

- `AuditLogs` gains `ActorUserId`, `ActorKind`, `ActorDisplay` (username snapshot), `TargetType`, `TargetId`, `Category`, `Severity`. `Outcome` gets a closed vocabulary. `Details` becomes JSON for new rows.
- One `IAuditRecorder` with a typed builder replaces the roughly 45 hand-built `new AuditRecord(...)` helpers over time. New and touched call sites use it.
- **Write-mode rule.** Security and lifecycle events are written in the same transaction as the change. Best-effort is reserved for scheduler and recovery telemetry.
- **Noise rule.** Routine events carry `Category = Routine` and are excluded from the default timeline and the dashboard count. Blocked-schedule rows are written on state transition, not every cycle.
- **Tamper stance.** Documented database-level denial of update and delete on `AuditLogs` for the application login, and a written retention position.

### F.4 Navigation and shell

- `NavigationRegistry` as in E.3. Rail domains are data, so a later promoted workspace needs no code change.
- Rail domains become: Control Center, Integrations, Services, Business Data, Config.
- **URLs do not move.** `/runs`, `/schedules`, `/connections` and their children keep their paths. Only menu placement, owning domain and breadcrumb roots change.
- One `NavTree` primitive with the full tree keyboard model and lazy children. Existing disclosure rows are unchanged.
- One `<PageTitle>` driven by the registry pattern and the page-published entity title.

### F.5 Entity actions and lists

- `EntityActionDescriptor` and `EntityToolbar` as in E.2, with a typed `ToolbarLabels` preference.
- `PagedResult<T>`, `PageRequest`, `AsyncSearchSelect`, and an `integrationId` filter on the global Schedules and Connections lists.

### F.6 Module runtime

- A missing step handler fails the step and fails worker startup.
- Steps get a timeout-bearing linked token and the lease is renewed while a step runs.
- Parameter schemas are validated at registration.
- The SDK is labelled preview. The execution subset is declared stable.

### F.7 Operations

- The three hosts run as Windows Services. Health endpoints exist in Production. Migration is an explicit step. Backup and restore are scripted. First-admin is not anonymous outside Development.

---

## G. RC1 Program and Epic Sequence

Three tracks run in parallel after one gate. Arrows show hard dependencies.

```
RC-0  Program gate, rulings, documentation reconciliation
  |
  +-- Track A (security)   IAM-9 --> AUD-1 --> IAM-10 --> IAM-11
  |
  +-- Track B (experience) QW-1 --> NAV-2 --> ACT-1 --> LST-1
  |                                   (LST-1.3 to 1.5 need IAM-9; LST-1.4 needs IAM-11.1)
  |
  +-- Track C (platform)   SDK-8 --> ID-1 --> OPS-1
  |
RCV-1  Release-candidate validation (needs all tracks)
```

| Order | Epic | Why here | Risk | Design gate first |
|---|---|---|---|---|
| 0 | **RC-0** Program gate and reconciliation | Nothing else can start cleanly while ADR-0040 and ADR-0044 forbid the navigation work and RC1 is undefined | Low | It is the gate |
| A1 | **IAM-9** Identity foundation | Username and session identity are prerequisites for audit attribution, user lifecycle and every user-facing surface | High | Yes |
| A2 | **AUD-1** Audit hardening | Needs the stable actor from IAM-9. Must precede user decommission so history is attributable | Medium | Yes (short) |
| A3 | **IAM-10** Authorization hardening and permission metadata | The root permission in IAM-11 is meaningless until escalation is closed | High | Yes |
| A4 | **IAM-11** User lifecycle and credential recovery | Consumes all three predecessors | High | Yes, security |
| B1 | **QW-1** Quick wins | Four independent, visible, low-risk changes that need no architecture | Low | No |
| B2 | **NAV-2** Navigation registry and shell | Icons and registry are prerequisites for the toolbar and titles | Medium | Yes |
| B3 | **ACT-1** Entity action model and toolbar | Needs icon keys from NAV-2 | Medium | Short |
| B4 | **LST-1** Lists, selectors and teams | Needs username search and user status | Medium | Short |
| C1 | **SDK-8** Module contract correctness and preview label | Independent of A and B. Retires runtime correctness risk early | Medium | Yes |
| C2 | **ID-1** Identifier hygiene | Small. Touches the same dispatch code as SDK-8, so it follows it | Low | No |
| C3 | **OPS-1** Operability baseline | Independent, but benefits from SDK-8's worker composition report | Medium | Yes |
| Z | **RCV-1** Release-candidate validation | Last by definition | Medium | Checklist |

Post-RC1 design gates are listed in section Q: NAV-3, POL-1, MUI-1, MOD-1, CLI-2.

---

## H. Detailed Epic Plans

Agent classes used below. **Architect**: Fable-level design and review. **Senior**: Opus-class implementation where security or concurrency reasoning matters. **Standard**: Sonnet-class implementation against a written work package.

Path shorthand. `Sec/` is `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/`. `Api/` is `src/Apperture.ControlPlane/Apperture.Management.Api/`. `UI/` is `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/`. `Shared/` is `src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/`. `Pers/` is `src/Apperture.Persistence/Apperture.Persistence.SqlServer/`.

### RC-0. Program gate, rulings and documentation reconciliation

- **Objective.** Record the rulings this plan depends on and remove documents that would mislead implementation agents.
- **Prerequisites.** Product-owner answers to section S.
- **Scope.** Documentation only: a program gate document under `docs/01-architecture/transitions/rc1-program/`; supersession notes on ADR-0040 (rules 2 and 3) and a scoped amendment to ADR-0044; withdrawal banners on ADR-0005, 0006, 0008, 0009, 0010, 0011 and the relationship leg of ADR-0007; a new ADR for external-organization access; retire `docs/roadmap.md` into the pre-release roadmap; fix IAM-8 merge status in five places; unify the two ADR indexes; renumber the duplicate ADR-0013; promote the IAM-8 and LFC-1 gates to ADRs; an RC1 definition section in the pre-release roadmap.
- **Non-goals.** No code. No regeneration of `current-architecture-tracking.md` yet (that is RCV-1, when the code has settled).
- **Decisions required.** D-01, D-08, D-09, D-13, D-17.
- **Schema, API, UI, authorization, audit impact.** None.
- **Acceptance.** Every document an agent would read first (`AGENTS.md`, playbooks, roadmap, ADR index) is consistent with the rulings. `AGENTS.md` rules 3 and 4 are either reworded or annotated with their actual status.
- **Risk.** Low. **Agent.** Architect drafts the gate; Standard applies the doc edits.

### IAM-9. Identity foundation

- **Objective.** Make username a first-class attribute of every user, make the session and audit identity the stable user id, and give every surface one way to show a user.
- **Why here.** Everything in Track A and half of Track B depends on it.
- **Prerequisites.** RC-0. Decisions D-02, D-03.
- **Scope.** `Users.Username` and its rules; backfill; session `NameIdentifier` to user id; `ClaimsPrincipalToActorMapper` produces the user id; `UserReferenceDto`; `GET /api/users` gains `q`, a clamped `take` and username in the list DTO; `UserRef` component; username shown on Users list and detail in place of the id.
- **Non-goals.** No disable or delete. No username in URLs. No rename UI (the rule is decided; the operation ships in IAM-11 if approved). No multi-tenant Entra.
- **Affected areas.** `Core.Domain/Identity/User.cs`, `IdentityLink.cs`; `Pers/Entities/UserEntity.cs`, `AppertureDbContext.cs`, `SqlUserRepository.cs`; `Sec/LocalSessionIdentity.cs`, `ClaimsPrincipalToActorMapper.cs`, `SqlPrincipalToUserResolver`, `UserManagementService.cs`, `UserProvisioningService.cs`, `FirstAdminBootstrapService.cs`, `BootstrapPlatformAdminAssignment.cs`, `EmergencyAccessService.cs`; `Management.Host` cookie and OIDC configuration; `Api/Controllers/UsersController.cs`, `AuthController.cs`; `Management.Client`; `UI/Pages/Governance/UsersList.razor`, `UI/Pages/Config/Governance/UserDetail.razor`.
- **Schema.** One migration: `Username nvarchar(64)` with a unique index, populated from the local link where present and generated otherwise. A `ReservedUsernames` table or equivalent for retired names.
- **API.** `UserListDto` and `UserDetailDto` gain `Username`. `MeResponse` gains `Username`. `GET /api/users?q=&skip=&take=`.
- **Authorization.** None new. User search stays under `user.read`.
- **Audit.** `user.username.assigned` on backfill is not needed row by row; one summary event. All new audit rows carry the user id as actor once AUD-1 lands.
- **Migration and compatibility.** Existing sessions are invalidated once by the cookie claim change. This must be stated in release notes. The local link's `ExternalId` remains the sign-in key for RC1 and is kept equal to `Username`.
- **Tests.** Unit tests for format and normalization rules. SQL Server test for the unique index under the database collation. Integration tests for sign-in before and after backfill, for Entra-provisioned users receiving a username, and for the search endpoint (paging, permission, clamp). A regression test that a local user and an Entra user produce the same actor shape.
- **Acceptance.** Every user has a username. No page in Governance shows a user id outside a technical-details disclosure. `GET /api/auth/me` returns the username. The audit actor for an Entra session and a local session is the user id.
- **Risk.** High (authentication path). **Agent.** Architect for the gate; Senior for 9.1 to 9.3; Standard for 9.4 and 9.5.

### AUD-1. Audit hardening

- **Objective.** Make the audit log attributable, queryable by target, low-noise and complete for administrative and operator actions.
- **Prerequisites.** IAM-9.2 (user id as actor).
- **Scope.** Schema columns; `IAuditRecorder`; closed outcome and category vocabularies; coverage gaps listed as P1 in section K; noise rules; timeline filters for category and target; tamper and retention stance; a guard test for mutating endpoints.
- **Non-goals.** No export. No hash chaining. No long-term retention shipping. No rewrite of all 45 call-site files in one pass; only those on P1 paths and those touched for the actor change.
- **Affected areas.** `Core.Application/Services/IAuditService.cs`; `Pers/SqlAuditService.cs`, `Entities/AuditLogEntity.cs`; `Sec/Audit/*`; the services named in the coverage matrix; `Api/Controllers/AuditController.cs`; `UI/Pages/Control/AuditTimeline.razor`; `Sec/Dashboard/AuditDashboardCardProvider.cs`; `RuntimePlane.Dispatch.SqlServer/ScheduleDispatchRunner.cs`.
- **Schema.** Additive nullable columns and indexes on `(TargetType, TargetId, At)`, `(ActorUserId, At)`, `(Category, At)`. Old rows stay as they are.
- **API.** `AuditQuery` gains `targetType`, `targetId`, `category`, `actorUserId`. Timeline DTO gains a `UserReferenceDto` actor.
- **Authorization.** `audit.read` unchanged. Record the open question on context scoping.
- **Tests.** One test per newly audited action asserting actor, target, outcome and the absence of secret values. A reflection-based guard test: every `[HttpPost|Put|Patch|Delete]` action is either on an explicit "no audit required" list or reaches a recorder (implemented as a registry of action names per endpoint, the same style as `ControllerAuthorizationCoverageTests`). A details-injection test. SQL test for target queries.
- **Acceptance.** The P1 rows of the coverage matrix read "Yes". The default timeline for a busy schedule shows administrative events without scrolling past dispatch rows. Per-entity history can be queried.
- **Risk.** Medium. **Agent.** Architect for the short gate; Senior for 1.1 and 1.2; Standard for coverage packages.

### IAM-10. Authorization hardening and permission metadata

- **Objective.** Close escalation, make permissions human-readable, and settle the principal and scope model so later features do not need a second security system.
- **Prerequisites.** AUD-1.2 (grant changes must be audited with a diff).
- **Scope.** `PermissionDefinition`; names and descriptions for every seeded permission; module permission declarations gain name and description; grant-subset and assignment-subset rules; removal of the `role.manage` implication with a preserving migration; authorization result type; declarative step-up; role editor shows names, descriptions and sensitivity; removal of `relationship.manage`; a written design for principal kinds, scoped assignments and permission sets.
- **Non-goals.** No context-scoped grants. No service accounts. No permission-set implementation. No deny rules.
- **Affected areas.** `Shared.Kernel/PermissionDescriptor.cs`; `Core.Application/Services/IResourceDescriptorRegistry.cs`; `Sec/InMemoryResourceDescriptorRegistry.cs`, `PlatformPermissionSeeder.cs`, `PlatformAdminRolePermissionRepair.cs`, `RbacAuthorizationPolicyEvaluator.cs`, `PlatformAdministratorGovernancePermissions`, `UserManagementService.cs`, `ModulePermissionRegistrar.cs`; `ModuleSdk.Abstractions/ModuleManifest.cs`; `Api/Models/GovernanceAdminModels.cs`, `GovernanceController.cs`; `UI/Pages/Governance/Role*.razor`.
- **Schema.** One data migration that expands implied permissions into explicit grants on existing custom roles.
- **API.** `PermissionCatalogEntryDto` gains display name, description, category, sensitivity, step-up flag. Role mutation endpoints return a specific error code for a refused grant.
- **Audit.** Grant changes record added and removed permissions, not a count. Refused grants are audited.
- **Migration and compatibility.** The implication removal is behaviour-changing for any deployment that created a custom role with only `role.manage`. The migration preserves effective access and makes it visible.
- **Tests.** Denial tests for each escalation path (self-assign administrator, grant a permission not held, assign a role exceeding own permissions). A property-style test that no sequence of role and grant operations by a non-administrator increases that actor's effective permissions. Migration test for the implication expansion. A catalog test that every seeded permission has a name and description.
- **Acceptance.** The three escalation scenarios in section J are refused and audited. The role editor shows no raw action strings as primary text.
- **Risk.** High. **Agent.** Architect for the gate and for review of 10.2; Senior for 10.2 and 10.3; Standard for metadata and UI.

### IAM-11. User lifecycle and credential recovery

- **Objective.** Disable, decommission, administrative credential recovery, return to local access, sign-in throttling.
- **Prerequisites.** IAM-9, AUD-1, IAM-10. Decisions D-04, D-05, D-06.
- **Scope.** User status and enforcement; session termination; decommission as tombstone; `user.credential.recover`; admin-initiated reset; atomic return-to-local; last-administrator guards on every path; throttling on local sign-in, MFA and forgot-password; failed sign-in evidence; recipient resolvers respect status.
- **Non-goals.** No hard delete. No self-service account deletion. No admin MFA enrolment UI. No username URLs.
- **Affected areas.** `Sec/UserManagementService.cs`, `ForgottenPasswordService.cs`, `AccountInvitationService.cs`, `AccountActionTokenService`, `EntraPrimaryAuthenticationPolicy`, `SqlPrincipalToUserResolver`, `OperationalTeamRecipientExpander`, `SqlNotificationRecipientResolver`, `BootstrapPlatformAdminAssignment.cs`; `Api/Controllers/UsersController.cs`, `AuthController.cs`; `UI/Pages/Config/Governance/UserDetail.razor`, `UsersList.razor`.
- **Schema.** Status columns from F.1. A sign-in throttle store (per account and per source address), or in-memory with documented single-host limits.
- **API.** `POST /api/users/{id}/disable`, `/enable`, `/decommission`, `/credential-recovery/reset`, `/credential-recovery/return-to-local`. The existing direct password-set endpoint moves behind `user.credential.recover`.
- **Authorization.** Disable and enable under `user.manage`. Decommission under `user.manage` with step-up. Both recovery operations under `user.credential.recover`, `Critical`, step-up required.
- **Audit.** `user.disabled`, `user.enabled`, `user.decommissioned`, `credential.recovery.reset_initiated`, `credential.recovery.returned_to_local`, `auth.local.login.throttled`. All in-transaction. Failed sign-in records the resolved target user id where one was resolved, and a source address.
- **Tests.** Disabled user is refused on the next request with a live cookie (integration). Decommission clears every Restrict table and leaves history resolvable (SQL Server). Bootstrap assignment does not resurrect a decommissioned administrator. Return-to-local on the last administrator is refused or leaves a viable sign-in. Throttle engages and is audited. Browser test: a disabled user's open tab lands on sign-in.
- **Acceptance.** Requirements N-04 to N-07 and X-04 are met with the tests above.
- **Risk.** High. **Agent.** Architect for the security gate; Senior throughout; Standard for the UI package.

### QW-1. Quick wins

- **Objective.** Four visible fixes that need no architecture.
- **Prerequisites.** None beyond RC-0 approval of D-07 (Runs).
- **Scope and packages.** Hamburger X; one `<PageTitle>` from `TopBarOrientation`; remove the Runs triage sort; Notification Diagnostics cleanup.
- **Risk.** Low. **Agent.** Standard.

### NAV-2. Navigation registry and shell

- **Objective.** One source of navigation metadata, the new information architecture, icons, rail behaviour and the tree primitive.
- **Prerequisites.** RC-0 rulings D-08 and D-09. QW-1.2.
- **Scope.** `NavigationRegistry`; generated menus and section ownership; icon keys and icons on every item; Services domain; Runs and Schedules under Integrations; domain default destinations; hover peek; `NavTree`; dynamic integration nodes; `integrationId` filter on Schedules and Connections.
- **Non-goals.** No URL changes. No migration of all 97 breadcrumb matchers (only those whose roots change). No rebase of `SqlStructureBrowser` onto `NavTree`. No module-supplied icons. No drill-through stack.
- **Invariants.** Existing disclosure rows look and behave as today. `/connections`, `/runs`, `/schedules` paths are unchanged. The UIX-2 rule against layout state changes during navigation holds. Menu filtering stays server-side.
- **Affected areas.** `Shared/ControlPanelMenuDefinitions`, `SectionOwnerConstants`, `RouteToSectionResolver`, `ManagementBreadcrumbResolver`, `ResourceContextViewRoutes`, `IntegrationUiRoutes`; `UI/Layout/MainLayout.razor`, `UI/Organisms/NavRail.razor`, `ControlPanel.razor`, `UI/Molecules/ControlPanelNavNode.razor`, `NavRailItem.razor`, `UI/Services/DomainMenuService.cs`; `Api/Controllers/MenusController.cs`, `SchedulesController.cs`, `ConnectionsController.cs`.
- **API.** `GET /api/navigation/integrations?contextId=` returning id, name, state, `hasSchedules`, `hasConnections`. `integrationId` on `GET /api/schedules` and `GET /api/connections`.
- **Authorization.** Tree nodes are filtered by the same read permissions as the lists. No new permission.
- **Tests.** Registry consistency tests (every route has a domain, a title pattern and an icon; every menu href resolves). Rewrite of the tests that pin the old menu shape. bUnit tests for `NavTree` keyboard behaviour. Playwright smoke for rail click, hover peek, Escape, tree expand and navigate, and title per route.
- **Acceptance.** N-17 to N-24 met. No route without a title. No menu item without an icon.
- **Risk.** Medium (shell regressions; no existing shell tests). **Agent.** Architect for the gate; Senior for 2.1 and 2.4; Standard for the rest.

### ACT-1. Entity action model and toolbar

- **Objective.** One action model and one toolbar on every entity screen.
- **Prerequisites.** NAV-2.2 (icon keys).
- **Scope.** `EntityActionDescriptor`, `EntityToolbar`, placement and ordering rules, confirm rules, edit-model rule, label preference, adoption on the entity pages listed in section L.
- **Non-goals.** No server-side action registry. No new actions except those needed for consistency (for example Delete on the notification definition edit route).
- **Schema.** One typed field on `UserPreferences` (`ToolbarLabels`).
- **Tests.** bUnit tests for the toolbar (accessible name on every icon button, tooltip on focus, disabled reason exposed, destructive last). A source-contract test that entity pages use `EntityToolbar`. axe check in the browser smoke.
- **Acceptance.** The nine inconsistencies in section L are resolved.
- **Risk.** Medium (wide surface, low depth). **Agent.** Architect for a short gate; Standard for adoption.

### LST-1. Lists, selectors and teams

- **Objective.** Shared paging and selection, paged Runs, human-readable users everywhere, and usable team membership.
- **Prerequisites.** IAM-9 for 1.3 onward; IAM-11.1 for excluding disabled users.
- **Scope.** `PagedResult<T>` and `PageRequest`; `AsyncSearchSelect`; Runs paging and indexes; `UserRef` adoption; `UserSelector`; member add and remove endpoints; teams on user detail; team permission ruling.
- **Non-goals.** No shared table component. No conversion of lists RC1 does not otherwise touch.
- **Schema.** Indexes on `OperationRuns` for `CreatedAt` and `(IntegrationId, CreatedAt)`.
- **API.** `GET /api/runs` paged. `POST` and `DELETE /api/operational-teams/{id}/members`. `GET /api/users/{id}/operational-teams`. `DELETE /api/operational-teams/{id}` if D-10 approves.
- **Authorization.** Per D-10.
- **Audit.** Team create, update, delete and membership changes (closes a P2 gap).
- **Tests.** Paging correctness with authorization filtering. Team membership service and controller tests (none exist today). Concurrency test for simultaneous member adds.
- **Acceptance.** N-09, N-10, X-07 met. No page lists users by id.
- **Risk.** Medium. **Agent.** Standard, with Senior review of the Runs paging change.

### SDK-8. Module contract correctness and preview label

- **Objective.** Fix runtime correctness defects, harden module metadata, and decide which parts of the SDK are stable.
- **Prerequisites.** RC-0. Decision D-11.
- **Scope.** Missing-handler failure; startup handler coverage; step timeout and in-step lease renewal; schema validation at registration; sample schema fixes; id namespace rule; `GovernedResources.ScopeKey` restriction; connector registry overwrite refusal; SDK contract version and `MinimumPlatformVersion` check; worker heartbeat carries module ids and versions; a design note choosing the single field schema and the composition entry point; SDK stability statement.
- **Non-goals.** No dynamic loading. No field-schema consolidation implementation. No wizard subsystem. No rename of lifecycle API verbs (operator-facing copy only).
- **Affected areas.** `RuntimePlane.Engine/DefaultWorkflowExecutor.cs`, `WorkflowStartRequestProcessor.cs`; `RuntimeHost.Windows.WorkerHost/WorkerHostStartupValidation.cs`, `Program.cs`; `Core.Application/Validation/OrchestrationParameterSchemaValidator.cs`; `Sec/ModulePermissionRegistrar.cs`, `ProcedureRegistryService`; `ConnectorResolution/InMemoryConnectorDefinitionRegistry.cs`; `ModuleSdk.Abstractions/*`; sample modules.
- **Tests.** A workflow with an unregistered step fails with a clear outcome. Startup fails on missing handlers. A step exceeding its timeout is cancelled and recorded. A step longer than the lease is not double-executed (SQL Server). Registration rejects an invalid schema. Signature-level public API baseline for the stable subset.
- **Acceptance.** X-05, X-06, X-12 met. `docs/05-modules` states what is stable and what is preview.
- **Risk.** Medium (execution hot path). **Agent.** Architect for the gate; Senior for 8.1 and 8.2; Standard for the rest.

### ID-1. Identifier hygiene

- **Objective.** Time-ordered ids where volume justifies it, random where secrecy requires it, and no dead generation.
- **Scope.** `EntityIds.NewOrdered()` and `NewRandom()` in `Shared.Kernel`; route run, instance, operation item and group, platform event, notification instance, outbox and inbox ids through `NewOrdered`; remove the 69 discarded `AuditRecordId` generations (done with AUD-1.1 if it lands first); hash the enrollment token.
- **Non-goals.** No change to existing ids. No column type changes. No change to low-volume entity ids.
- **Tests.** Format tests updated (two tests assert `^[0-9a-f]{32}$` and `^role-[0-9a-f]{32}$`; the latter is unaffected). An ordering test for the helper. A test that token-like ids use `NewRandom`.
- **Risk.** Low. **Agent.** Standard.

### OPS-1. Operability baseline

- **Objective.** A supported way to install, upgrade, back up, restore and observe one instance.
- **Prerequisites.** Decision D-12 (supported topology).
- **Scope.** Windows Service hosting for three hosts; publish layout; production health endpoints; explicit migration step and worker schema guard; backup and restore scripts and runbook (database, bootstrap file, protector keys); first-admin hardening; working production SQL credential path; public-origin configuration (forwarded headers, Data Protection key persistence, `ManagementUiUrl`); version stamping, changelog and release tagging.
- **Non-goals.** No CLI rebuild. No Azure. No Docker images. No maintenance mode. No automatic rollback. No remote management.
- **Affected areas.** The three host `Program.cs` files; `ServiceDefaults/Extensions.cs`; `Pers` migration hosted service; `Cli.Host`; `Bootstrap`; `scripts/deployment`; `deploy/config`; `.github/workflows`; `docs/03-operations`.
- **Tests.** A Windows CI leg that publishes, installs the services, starts them, hits readiness and uninstalls. An upgrade test from the previous tagged schema. A restore rehearsal recorded in the runbook.
- **Acceptance.** X-08 and X-09 met. A person following the runbook on a clean Windows Server reaches a signed-in administrator.
- **Risk.** Medium. **Agent.** Architect for the gate; Senior for migration and first-admin; Standard for scripts and docs.

### RCV-1. Release-candidate validation

- **Objective.** Evidence that RC1 meets section C.
- **Scope.** Convert soft-skips to hard failures or real skips; one real end-to-end test (schedule to run to notification); functional browser smoke; axe baseline; execute the SEC-1, IAM-3, IAM-4 and IAM-5 operator smoke runbooks; whole-product security review; triage of parked findings (Toast-B gap 3, NAV-1 review findings, CTX-3B debts); regenerate `current-architecture-tracking.md` and `project-map.md`; remove tracked `poc/` and stray scripts; tag.
- **Risk.** Medium. **Agent.** Architect for the security review and the final go or no-go; Standard for test conversion and docs.

---

## I. Work-Package Breakdown

Each package states: **Goal**, **Reuse**, **Areas**, **Invariants**, **Tests**, **Handback**, **Do not change**, **Depends on**. Every package also inherits these standing rules:

- Follow `AGENTS.md`, including rule 6 (no epic ids or planning labels in operator-visible text).
- Use Edit and Write for source changes. Build into the repo-local `artifacts/` path. Do not put SQL credentials on a command line.
- SQL Server tests must fail, not pass, when SQL is unavailable.
- Register any new client script in both `Ui.Server/_Layout.cshtml` and `Ui.Client/index.html`.
- Do not create branches or merge without asking.

### QW-1

**QW-1.1 Hamburger X.**
- Goal: when open, the three bars form an X; add `aria-expanded` and `aria-controls`.
- Reuse: the existing CSS-transform approach in `UI/Atoms/HamburgerToggle.razor`.
- Invariants: click behaviour and `aria-label` text unchanged; reduced-motion respected.
- Tests: a bUnit or source-contract test for the ARIA attributes in both states.
- Handback: before and after screenshots in light and dark themes.
- Do not change: `MainLayout` state fields.
- Depends on: nothing.

**QW-1.2 Browser titles.**
- Goal: one `<PageTitle>` rendered from the title `TopBarOrientation.ApplyTitleSubtitle` already computes, formatted "Record name - Area" and truncated to about 60 characters. No product name.
- Reuse: `ShellOrientationService`, `ManagementBreadcrumbResolver`.
- Areas: `UI/Organisms/TopBar*.razor`, `Routes.razor`.
- Invariants: no per-page edits; the title updates when the page publishes its entity name; sign-in pages get explicit titles.
- Tests: unit tests for the formatter; Playwright check on three routes including one entity page.
- Handback: a table of 15 representative routes and their titles.
- Do not change: breadcrumb matchers.
- Depends on: nothing. NAV-2.1 later moves the static part into the registry.

**QW-1.3 Runs chronological.**
- Goal: remove `SortRunsForTriage` from `RunsList.razor` and `IntegrationRuns.razor`; keep the status filter and "Needs attention" filter; update the subtitle.
- Reuse: server ordering in `RunQueryService.ListRunsAsync`.
- Tests: replace the assertions in `S6RunScheduleOperatorTriageTests` that pin the sort; add a unit test asserting server order is `CreatedAt` descending.
- Handback: note whether Schedules and the Integrations fleet keep their attention sort (see Q-06).
- Do not change: `RunAttentionRank` (still used for badges).
- Depends on: D-07.

**QW-1.4 Notification Diagnostics.**
- Goal: one integration selector; a short purpose statement; SMTP test and health visible; pipeline test inside `ManagementAdvancedSection` with each input explained and a warning that a live run creates a real notification; either render the four tables the API returns or remove the "required for tables" wording and link to Delivery health; state which definition is tested.
- Reuse: `ManagementSection`, `ManagementAdvancedSection`, `Disclosure`, the integrations list already available client-side.
- Areas: `UI/Pages/Config/Notifications/NotificationDiagnostics.razor` only.
- Invariants: every existing function still reachable; no API change.
- Tests: update `S10NotificationDeliveryDiagnosticsTests` source-contract assertions.
- Do not change: `NotificationDiagnosticsController`.
- Depends on: nothing.

### IAM-9

**IAM-9.0 Design gate.** Architect. Rules for username format, normalization, uniqueness under collation, generation for Entra users, reservation of retired names, rename policy, the cookie claim change and its one-time sign-out, and the `UserReferenceDto` contract. Output: a gate document with rulings.

**IAM-9.1 Username attribute and backfill.**
- Goal: add `Users.Username`, populate it, enforce format and uniqueness, keep the local link `ExternalId` equal to it.
- Reuse: `LocalAuthHelpers.NormalizeUsername`, `EmergencyAccessIdentity.IsReservedLocalUsername`, the migration pattern from `Epic72MigrationHardeningTests`.
- Areas: `User.cs`, `UserEntity.cs`, `AppertureDbContext.cs`, a new migration, `SqlUserRepository.cs`, every user-creation path listed under IAM-9.
- Invariants: `breakglass` stays canonical; one local link per user is enforced; existing sign-in keeps working.
- Tests: format, normalization, collision generation, migration on a populated database, unique index under default collation.
- Handback: migration script review, list of creation paths updated with file and line.
- Do not change: Entra link identity key.
- Depends on: 9.0.

**IAM-9.2 Session and actor identity.**
- Goal: cookie `NameIdentifier` is the user id for local and Entra sessions; `ClaimsPrincipalToActorMapper` returns the user id; `SqlPrincipalToUserResolver` resolves by id.
- Reuse: `LocalSessionIdentity`, `EntraOpenIdConnectOptionsConfigurer`.
- Invariants: step-up claims, return-target policy and request-trust middleware untouched; Recovery Access still works; break-glass session flag preserved.
- Tests: resolver tests; `ManagementHostAccountHttpTests` and `ManagementHostEntraLinkingHttpTests` updated; the SEC-1 browser suite must pass unchanged.
- Handback: confirmation that both CI jobs pass; a note for release notes about the one-time sign-out.
- Do not change: cookie lifetime, `SameSite`, scheme names.
- Depends on: 9.1.

**IAM-9.2a Historical actor backfill.**
- Goal: a one-time data migration that maps legacy actor strings (local username or Entra object id) to the user id through `IdentityLinks`, for every column that stores an actor. It must run in the same release as 9.2, before any username can change.
- Columns that store the session actor string today, not the user id: `AuditLogs.Actor`; `RuntimeControlRequests.RequestedBy`; `WorkflowExternalControlRequests.RequestedBy`; `PlatformSettingValues.UpdatedByPrincipalId`; `EntraAccessRequests.ReviewedByUserId` (misnamed); `AttentionIncidents.AcknowledgedBy`; `OperationItemAttentionAcknowledgements.AcknowledgedByUserId` and `ReopenedByUserId` (misnamed); `OperationItemRetryAttempts.RequestedByActorId`; `NotificationDefinitions` and `NotificationSubscriptions` `CreatedByActorId` and `UpdatedByActorId` (admin path; the self-service path already writes the user id, so the column is mixed).
- Invariants: literals (`anonymous`, `system`, `recovery`, `emergency-cli`, worker ids) are left untouched; unresolvable values are left as they are and render as an unknown actor; for `AuditLogs` the original string is preserved in `ActorDisplay` once AUD-1.1 adds the column, so sequence the audit part of the backfill with AUD-1.1.
- Tests: SQL Server migration test with local, Entra, literal and unresolvable actors.
- Handback: counts of resolved and unresolved values per column on a development database.
- Depends on: 9.2.

**IAM-9.3 User reference contract and search.**
- Goal: `UserReferenceDto`; `Username` on list, detail and `me` DTOs; `GET /api/users?q=` searching username and display name, ordered by username, with `take` clamped to 100.
- Areas: `Api/Controllers/UsersController.cs`, `Management.Client/IManagementClient.cs`, `SqlUserRepository.GetPageAsync`.
- Invariants: Recovery Access stays hidden; permission stays `user.read`.
- Tests: search, paging, clamp, 403 without `user.read`.
- Depends on: 9.1.

**IAM-9.4 `UserRef` component.**
- Goal: compact and rich modes, accessible tooltip (hover, focus, touch), states for disabled, decommissioned and unknown, optional link to user detail when the viewer has `user.read`.
- Reuse: `AccountAvatar`, `DrillInLink`.
- Tests: bUnit render tests for each mode and state.
- Do not change: page layouts (adoption is 9.5 and LST-1.3).
- Depends on: 9.3.

**IAM-9.5 Governance adoption.**
- Goal: Users list, User detail, Role holders and Role-derived access show username and display name; the id moves into `TechnicalDetails`.
- Tests: update `Iam2UserCreationUiSurfaceTests` and related source-contract tests.
- Depends on: 9.4.

### AUD-1

**AUD-1.0 Design gate (short).** Architect. Vocabularies for `Category`, `Severity`, `Outcome`, `TargetType`; the write-mode rule; the noise rule; the tamper and retention position; the guard-test mechanism.

**AUD-1.1 Schema and recorder.**
- Goal: add the columns and indexes; introduce `IAuditRecorder` with a typed builder that writes structured JSON details and never interpolates caller text into keys; `SqlAuditService` persists target fields in columns; remove the discarded `AuditRecordId` generation.
- Reuse: `ConnectionAuditDetails` as the model for structured details; `AuditCategoryResolver` as the fallback for old rows.
- Invariants: old rows remain readable; `IAuditService.AppendAsync` keeps working for unmigrated call sites; ambient-transaction behaviour preserved.
- Tests: `AuditTimelineSqlServerTests` extended; injection test with a role named `x; runId=1`.
- Handback: migration, before and after of two converted call sites.
- Do not change: the test `AU8_...no_resource_or_connection_column` must be consciously replaced, with the gate cited.
- Depends on: 1.0, IAM-9.2.

**AUD-1.2 Identity and governance coverage.**
- Goal: audit sign-out, Entra remote failure, auth UI policy changes, Entra tenant registration create, patch, delete and activate, permission-set diffs, and break-glass marking on every service (move the marker into the recorder).
- Areas: `Api/Controllers/ConfigAuthUiPolicyController.cs`, `ConfigEntraTenantsController.cs`, `Management.Host/ManagementAuthEndpoints.cs`, `EntraSignOut.cs`, `UserManagementService.cs`.
- Tests: one per event.
- Depends on: 1.1.

**AUD-1.3 Operator action attribution.**
- Goal: manual workflow and operation start, procedure launch (parameter names, not values), cancel, retry, park, resume and single-item retry all record the acting user in both the request row and the audit log.
- Areas: `Sec/WorkflowCancelService.cs`, `OperationCancelService.cs`, `WorkflowRetryService.cs`, `WorkflowExternalControlService.cs`, `OperationItemRetryService.cs`, `IntegrationProcedureService.cs`, `Api/Controllers/WorkflowsController.cs`, `OperationsController.cs`.
- Invariants: no parameter values in audit; durable dispatch unchanged.
- Tests: one per action; update `WorkflowRetryServiceTests`.
- Depends on: 1.1.

**AUD-1.4 Configuration lifecycle coverage.**
- Goal: integration create, update, activate, deactivate, retire; schedule create, update, enable, disable; binding changes; notification definition create, update, enable; subscriptions; managed contacts; attention routing.
- Reuse: the connection lifecycle pattern (field names changed, never values).
- Depends on: 1.1. Team events are in LST-1.4.

**AUD-1.5 Noise and timeline.**
- Goal: `Category = Routine` for dispatch rows; blocked rows on transition only; dashboard count excludes Routine; timeline gains category options for identity, connections, settings and governance, a target filter and an actor rendered with `UserRef`.
- Areas: `ScheduleDispatchRunner.cs`, `AuditDashboardCardProvider.cs`, `AuditTimeline.razor`, `AuditController.cs`.
- Depends on: 1.1, IAM-9.4.

**AUD-1.6 Guard test and stance.**
- Goal: the mutating-endpoint guard test; a documented script that denies update and delete on `AuditLogs` to the application login; a retention statement in `docs/03-operations`.
- Depends on: 1.2 to 1.4.

### IAM-10

**IAM-10.0 Design gate.** Architect. Rulings on: `PermissionDefinition` fields; the two subset rules and their exact semantics for module-scoped permissions; the implication removal and migration; result type; declarative step-up; the future principal model (kinds: User, ServiceAccount, Integration, Agent), scoped assignments (`context:{id}`, `integration:{id}` reserved), permission sets; the fate of the break-glass grant-all branch; ADR-0012 amendments.

**IAM-10.1 Permission definitions.**
- Goal: replace the `(PermissionDescriptor, ResourceFamily?)` tuple with `PermissionDefinition`; names and descriptions for every seeded permission; module declarations accept name and description and honour `IsPrivileged`.
- Reuse: the dead `GovernedActionDescriptor.IsPrivileged` and `ActionCategory` (either use or delete them).
- Invariants: permission identity triple unchanged; evaluation unchanged.
- Tests: every seeded permission has a name and description; registrar tests.
- Depends on: 10.0.

**IAM-10.2 Anti-escalation.**
- Goal: enforce the subset rules in `SetRolePermissionsAsync`, `CreateRoleAsync` and `AssignRoleAsync`; remove `ImpliedByInstanceRoleManage`; ship the expanding migration; ensure the platform administrator role holds every permission explicitly through `PlatformAdminRolePermissionRepair`.
- Invariants: the platform administrator is never locked out; last-administrator guards unchanged; Recovery Access unaffected.
- Tests: as listed under IAM-10. Replace the test that pins the implication.
- Handback: a table of the escalation scenarios with the refusing code path.
- Do not change: scope containment.
- Depends on: 10.1, AUD-1.2.

**IAM-10.3 Result type and declarative step-up.**
- Goal: `AuthorizationEvaluationResult`; one enforcement helper that calls `IStrongAuthenticationGate` when the permission requires step-up.
- Reuse: `IStrongAuthenticationGate`, `EntraStepUpService`, the `ControlActionAvailabilityDto` reason codes.
- Depends on: 10.1.

**IAM-10.4 Catalog and role editor UI.**
- Goal: names, descriptions, categories and a sensitivity marker in the permission catalog and role editor; a refused-grant message.
- Tests: update `RoleLifecycleUiRenderTests`.
- Depends on: 10.1, 10.2.

**IAM-10.5 Dormant surface removal.**
- Goal: remove `relationship.manage` from the seeder; annotate the dormant trust types; decide and apply the break-glass ruling from the gate.
- Depends on: 10.0, RC-0.

### IAM-11

**IAM-11.0 Security design gate.** Architect. Tombstone field-by-field policy (what is cleared, what is retained, the personal-data erasure variant); admin reset mechanics; return-to-local sequence and guards; throttling thresholds and storage; whether direct password set survives; MFA interaction; threat model.

**IAM-11.1 Status and enforcement.**
- Goal: `Status`, `SessionVersion`; resolver refuses non-Active users and stale session versions; disable and enable endpoints; sign-in refuses disabled users on local and Entra paths with the same generic failure.
- Reuse: `SqlPrincipalToUserResolver` (already per request), `UserManagementService` last-administrator guard.
- Invariants: cannot disable the last platform administrator or Recovery Access; background requests still do not renew sessions.
- Tests: as listed under IAM-11.
- Depends on: 11.0, IAM-9.2.

**IAM-11.2 Decommission.**
- Goal: one transactional operation that removes identity links, credentials, MFA, tokens, role assignments, team memberships, contact links and preferences, sets `Status = Decommissioned`, bumps `SessionVersion`, reserves the username and retains the row.
- Invariants: history resolves through `UserReferenceDto`; `BootstrapPlatformAdminAssignment` skips decommissioned principals; default lists exclude decommissioned users with an explicit filter to include them.
- Tests: SQL Server test over all six Restrict tables; concurrency test (decommission during role assignment) following the IAM-8.4 pattern.
- Depends on: 11.1, AUD-1.1.

**IAM-11.3 Credential recovery.**
- Goal: `user.credential.recover`; admin-initiated reset that issues a reset token to the verified contact email, works on a disabled credential, ends the target's sessions and never shows the administrator a password; atomic return-to-local.
- Reuse: `AccountActionTokenService`, `ForgottenPasswordService`, `AccountInvitationService`, `EntraPrimaryAuthenticationPolicy` exceptions, the `connection.browse` precedent for adding a permission through the repair arrays.
- Invariants: last-administrator guard applies to Entra link removal; Recovery Access refused; step-up required.
- Tests: full flow integration tests; guard tests; audit assertions.
- Depends on: 11.1, IAM-10.2, IAM-10.3.

**IAM-11.4 Throttling and sign-in evidence.**
- Goal: rate limits on `local/login`, `local/mfa`, `forgot-password` and `break-glass`; failed sign-in rows record the resolved user id where one exists and a source address; pre-validation failures are not audited.
- Reuse: the rate-limiting approach in `EntraAccessRequestService`.
- Invariants: uniform external responses; no username echoed for unknown accounts.
- Depends on: AUD-1.1.

**IAM-11.5 UI.**
- Goal: status on Users list and detail; Disable, Enable, Decommission, Send password reset and Return to local access through `EntityToolbar` if ACT-1 has landed, otherwise through the existing toolbar classes and converted later.
- Depends on: 11.1 to 11.3.

**IAM-11.6 Recipient resolution.**
- Goal: disabled and decommissioned users receive no notifications and are flagged in team membership.
- Areas: `OperationalTeamRecipientExpander`, `SqlNotificationRecipientResolver`, `SqlAttentionRecipientResolver`.
- Depends on: 11.1.

### NAV-2

**NAV-2.0 Design gate.** Architect. Registry record shape; the three parent relationships; icon key set; rail domain data model; hover-peek interaction model (overlay or in-flow, intent delay, focus equivalent, Escape, touch behaviour); tree keyboard model; the choice between filtered global lists and hub tabs for tree children; the list of pinned tests to rewrite.

**NAV-2.1 Registry and generated menus.**
- Goal: `NavigationRegistry` in `Shared`; `ControlPanelMenuDefinitions`, `RouteToSectionResolver` and static titles derive from it.
- Reuse: `DomainMenuItem`, `IntegrationUiRoutes.Sections` as the pattern, `DomainMenuCapabilityTokens`.
- Invariants: the rendered menu is identical to today's before 2.3 changes it (prove with a snapshot test); unknown capability tokens deny, not allow.
- Tests: registry consistency; menu snapshot.
- Depends on: 2.0, QW-1.2.

**NAV-2.2 Icons.**
- Goal: a platform icon-key enum mapped to Heroicons in the client; an icon on every menu item and tree node class (integration, connection, schedule, run, panel, and reserved keys for service, schema, table, entity, operation, workspace).
- Invariants: icons are decorative next to text (`aria-hidden`); no icon-only navigation without a name.
- Depends on: 2.1.

**NAV-2.3 Information architecture move.**
- Goal: Services domain with All Connections; Integrations domain with All Integrations, All Runs, All Schedules; Control Center keeps Control Center, Execute and Audit; breadcrumb roots and section ownership updated; the `StripAutomationRunsRoot` label match updated.
- Invariants: no URL changes; `ResourceContextViewRoutes` scoped paths unchanged.
- Tests: rewrite `Nav1OperatorInformationArchitectureTests`, `Epic100ShellDomainNavigationTests`, `ShellSectionOwnerTests` and the affected breadcrumb tests, citing the RC-0 rulings.
- Depends on: 2.1, RC-0.

**NAV-2.4 Rail behaviour.**
- Goal: `IsDomainDefault` destinations; rail click navigates to the default and opens the menu; hover peek per the gate; `aria-current` on the active domain; `<nav aria-label>`; the href-less group row becomes a real button.
- Invariants: the UIX-2 mount rules; no reflow on hover if the gate chooses overlay.
- Tests: Playwright for click, hover, keyboard and Escape.
- Depends on: 2.1.

**NAV-2.5 `NavTree` primitive.**
- Goal: a reusable tree with `role="tree"`, `treeitem`, `group`, `aria-level`, single `aria-expanded` owner, roving tabindex, arrow, Home, End and type-ahead keys, an async children provider with loading, retry and truncation states, and a node that can be both a link and expandable.
- Reuse: the lazy node lifecycle from `SqlStructureBrowser`; keyboard handling patterns from `SearchableList` and `ViewSelector`.
- Invariants: disclosure glyph is a chevron or plus and minus per the gate, with the state exposed through ARIA and not the glyph.
- Tests: bUnit keyboard tests.
- Do not change: `SqlStructureBrowser`.
- Depends on: 2.0.

**NAV-2.6 Dynamic integration nodes and list filters.**
- Goal: the navigation summary endpoint; trees under All Integrations (integrations, then their hub sections), All Runs, All Schedules and All Connections; `integrationId` filter on Schedules and Connections lists with a visible, removable filter chip; nodes refresh on `ResourceContextViewState.Changed`.
- Reuse: `IBindingRepository`, `ScopedListContextFilter`, `Epic33UiDisplayHelpers.GetRunsListIntegrationFilterHref`.
- Invariants: only Active integrations are listed; server-side permission filtering; a cap with a "show all" link to the list.
- Tests: endpoint tests with context and permission filtering; list filter tests.
- Depends on: 2.3, 2.5.

### ACT-1

**ACT-1.0 Short gate.** Action kinds, ordering rule (navigation, primary, lifecycle, move, destructive last, refresh at the end), confirm rule (every destructive and lifecycle-ending action confirms), edit-model rule (in-place edit with Save and Cancel in the same toolbar; separate edit routes keep the same lifecycle actions), label preference semantics.

**ACT-1.1 Descriptor and toolbar.**
- Goal: `EntityActionDescriptor` and `EntityToolbar`; icon buttons with visible focus, accessible name, tooltip on hover and focus, disabled reason exposed through `aria-describedby`; overflow menu on narrow widths.
- Reuse: `ActionButton`, `ConfirmActionDialog`, `PageHeader` toolbar slot, `ControlActionAvailabilityDto` vocabulary.
- Depends on: NAV-2.2.

**ACT-1.2 Label preference.**
- Goal: typed `ToolbarLabels` in `UserPreferences` with a control on `/account/preferences`.
- Reuse: the AUP-1 preferences path.

**ACT-1.3 to 1.5 Adoption** in three batches: Governance and Attention (Users, Roles, Teams, Contacts); Integrations, Connections, Schedules, Runs; Notifications, Applications, Modules, Components, Platform Settings. Each batch resolves its rows in the section L inventory and updates source-contract tests.

### LST-1

**LST-1.0 Short gate.** Paging contract (skip and take with total, or keyset); filter parameter naming (`contextId` everywhere); team permission ruling (D-10).

**LST-1.1 Paging and selector primitives.** `PagedResult<T>`, `PageRequest`, `AsyncSearchSelect` (async source, 300 ms debounce, loading and empty states, listbox keyboard model).

**LST-1.2 Runs paging.** Server paging with a maximum page size, authorization applied before the page is cut, indexes, a load-more control, wired `createdFrom` and `createdTo` filters. Senior review.

**LST-1.3 `UserRef` adoption outside Governance.** Audit timeline, entity activity panels, Run detail, runtime requests, schedule diagnostics, notification recipients and subscriptions, managed contact link (with `UserSelector`).

**LST-1.4 Team membership.** Add and remove member endpoints; replace the textarea with `UserSelector` plus existing contact and email entry; resolved names in the member table; team audit events; optional team delete.

**LST-1.5 Teams on user detail.** Reverse lookup endpoint; Teams section; add-to-team control gated by the D-10 permission.

### SDK-8

**SDK-8.0 Design gate.** Architect. The single field schema (extend `PlatformSettingDefinition` vocabulary; typed parameter fields in the SDK); the composition entry point shape with a host-role parameter; the host extension hook for non-Core assemblies; SDK stability statement; lifecycle verb wording; timeout defaults.

**SDK-8.1 Handler correctness.** Missing handler fails the step with a distinct outcome; `ValidateHandlerOwnership` verifies every declared step has a handler and fails startup. Senior.

**SDK-8.2 Timeouts and lease renewal.** Linked cancellation token with a configurable step timeout; lease heartbeat while a step runs; operator cancel reaches the handler token. Senior. SQL Server test for no double execution.

**SDK-8.3 Registration validation.** Parameter schema validated at registration; unknown types rejected; samples corrected; ids must be prefixed with the module id (warning in RC1, documented as an error later); `GovernedResources.ScopeKey` restricted to the module scope; connector registry refuses to overwrite.

**SDK-8.4 Versioning and composition report.** SDK contract version constant; `MinimumPlatformVersion` populated and checked at discovery; worker heartbeat includes module ids and versions; Module detail shows a mismatch.

**SDK-8.5 Stability statement.** `docs/05-modules` marks the execution subset stable and the configuration, form and composition surface preview; signature-level API baseline for the stable subset.

### ID-1

**ID-1.1 Helper.** `EntityIds.NewOrdered()` and `NewRandom()` in a `net10.0` project, with documentation of when each is allowed.
**ID-1.2 High-volume adoption.** Run, instance, operation group and item, platform event, notification instance, outbox, inbox. Keep the 32-character form; do not truncate ordered ids below 32.
**ID-1.3 Enrollment token.** Generate with `RandomNumberGenerator`, store a hash, look up by hash, remove the hard-coded base URL.

### OPS-1

**OPS-1.0 Design gate.** Architect. Supported topology; service accounts; migration policy (explicit step, and what the host does when it finds pending migrations); first-admin mechanism; backup scope including protector keys; health endpoint exposure.

**OPS-1.1 Windows Service hosting and publish.** `UseWindowsService` in three hosts; publish layout for all three; install and uninstall scripts.
**OPS-1.2 Health.** Liveness and readiness in Production, readiness including the migrations check, a Management heartbeat row.
**OPS-1.3 Migration step.** A `migrate` verb in `Cli.Host` listing and applying pending migrations for the platform and module contexts; hosts refuse to start on a schema mismatch outside Development; worker schema guard. Senior.
**OPS-1.4 First-admin.** Outside Development the anonymous endpoint requires a one-time setup secret generated at install, or is replaced by a CLI verb. Senior.
**OPS-1.5 Backup and restore.** Scripts and a runbook covering the database, bootstrap file and protector keys, with a recorded restore rehearsal.
**OPS-1.6 Production configuration.** Integrated Security path made single-step; `deploy/config` examples that actually start; forwarded headers; Data Protection key persistence; `ManagementUiUrl`.
**OPS-1.7 Release engineering.** Version stamping from tags, changelog, a release workflow that publishes artifacts, fixed CLI exit codes.

### RCV-1

Packages follow the scope list in section H. The security review and the go or no-go are Architect work.

---

## J. Security and IAM Findings

### Findings that need a dedicated security design gate

| Topic | Gate |
|---|---|
| User decommission, admin credential recovery, Entra unlink, disabled-user sessions | IAM-11.0 |
| Escalation rules, sensitivity, step-up, principal model | IAM-10.0 |
| First-admin, migration and service accounts | OPS-1.0 |
| Module installation and package trust | MOD-1 (post-RC1) |
| Script execution | With M-12 (post-RC1) |
| Environment-management trust and non-interactive credentials | CLI-2 (post-RC1) |
| Business Data mutation authority and cross-context authority | POL-1 (post-RC1) |

### Escalation scenarios open today

1. **Self-promotion.** A user whose role holds `role.assign` calls `POST` to assign `role-platform-administrator` to themselves. `AssignRoleAsync` checks only the permission, that the user exists and that the role exists.
2. **Unbounded grant.** A user with `permission.assign` adds any catalog permission to a custom role they hold.
3. **`role.manage` as super-administrator.** A custom role with only `role.manage` implicitly holds `credential.manage` and can set a platform administrator's password. `SetLocalPasswordAsync` has no target-privilege check and no step-up. A test pins the implication as intended.

### Admin password reset authority

- **Today:** direct password set under `credential.manage`, no step-up, target sessions survive.
- **Recommendation:** the administrator initiates a reset; the user chooses the password through a single-use token sent to the verified contact email. The administrator never knows the password. Direct set either goes away or stays as an emergency path under the same critical permission with step-up and a forced change at next sign-in (decision D-06).
- **Users without a verified contact email** cannot receive a token. The gate must rule on this case. The safe default is a one-time setup link displayed once to the administrator, with the same expiry and audit.

### Entra unlinking

- **Today:** `user.manage`, no step-up, no last-administrator guard for the Entra source, and the Entra-primary policy has already disabled the local credential. An administrator can strand any user, including the last platform administrator.
- **Recommendation:** remove the raw unlink path for Entra links from ordinary user management. Offer one "Return to local access" operation that is atomic and guarded.

### User deletion

- **Recommendation: tombstone, not hard delete.** Reasons: six Restrict foreign keys; loose references in at least twelve columns across audit, notifications, settings, run control requests and acknowledgements; the audit log would lose attribution; a reused username would inherit history; `BootstrapPlatformAdminAssignment` would re-create a deleted configured administrator at the next start.
- **Privacy.** The tombstone keeps user id, username and status. An "erase personal data" variant replaces the display name with a standard placeholder and removes email, avatar and Entra snapshots. Whether the username itself counts as personal data to erase is a product-owner call (Q-03).

### Actor references are strings, and they are never resolved

- Beyond the audit log, about ten columns store the session actor string (username or Entra object id) with no foreign key. Three are named `...UserId` but do not hold a user id. The full list is in package IAM-9.2a.
- No code anywhere resolves an actor string to a name. Every surface prints the stored string. There is no "unknown user" fallback because nothing was ever looked up.
- Notification recipient resolution silently drops a missing user and does not record a skipped recipient. A team whose only members are missing users is not reported as empty. IAM-11.6 must make both cases visible.
- `PlatformEvents.ActorId` is never set, and start requests, runs and schedules have no actor column at all. AUD-1.3 covers the run side.

### Username enumeration

- Usernames are not secret by the owner's ruling. That removes the need to hide them from authenticated operators.
- Two unauthenticated timing oracles remain: `local/login` returns faster for unknown usernames, and `forgot-password` sends mail inline only for eligible accounts. Throttling (IAM-11.4) bounds both. Constant-time behaviour is a should-fix.

### Disabled-user sessions

- No `OnValidatePrincipal`, security stamp or session store exists. Because the principal resolver already runs per request, a status and session-version check there gives immediate effect without new infrastructure.

### Other findings

| Finding | Evidence | Action |
|---|---|---|
| No lockout or throttling on local sign-in | No limiter anywhere except access requests | IAM-11.4 |
| Break-glass grant-all branch is live behind one configuration flag | `RbacAuthorizationPolicyEvaluator.cs:40`; `BreakGlassAuthOptions.Enabled` | Ruling in IAM-10.0. Recovery Access already replaces it. |
| Static recovery token compared with `string.Equals` | `ConfigRecoveryAuthorizer` | Constant-time compare; review in RCV-1 |
| Anonymous first-admin endpoint | `BootstrapController.cs:16,47` | OPS-1.4 |
| Enrollment token in plaintext, in a URL, with a hard-coded base | `SqlRuntimeEnrollmentService` | ID-1.3 |
| Runtime identity validation is a string comparison with no callers | `IRuntimeIdentityService.ValidateAsync` | Document as dormant; do not build on it |
| Scheduler and worker run as string actor ids with no authorization evaluation | `ScheduleDispatchRunner`, `WorkflowStartRequestProcessor` | Accept for RC1; the principal model in IAM-10.0 gives them a home |
| Module code is fully trusted in-process | ADR-0001 | State it in the SDK documentation; it bounds what module installation can promise |
| `TrustServerCertificate` defaults to true on SQL connections | Services closure | RCV-1 security review |
| Secret material stays active when a connection is retired | LFC-1 R10 | RCV-1 triage |
| PBKDF2 at 100,000 iterations with no rehash path | `LocalPasswordHasher.cs` | Should-fix: rehash on sign-in |
| `audit.read` is all-or-nothing across contexts | `AuditController` | Open question for context-scoped authorization |

### AI authorization assumptions

The evaluator denies every non-human actor and role assignments are keyed on a user id. An AI identity today could only act as a human user. That is acceptable for RC1 only because no such feature exists. The principal model in IAM-10.0 is where AI identities, API clients and integrations acquire first-class, auditable authority.

---

## K. Audit-System Findings

### Infrastructure summary

- One real writer: `IAuditService.AppendAsync` into `AuditLogs`. A separate `SecretAudits` table has no reader.
- Three dead or stub contracts: `IAuditEventSink`, `IEnrollmentAuditWriter`, `IBootstrapAuditWriter` (bound to a no-op, so bootstrap changes are unaudited).
- About 45 files hand-build audit records, so conventions diverge.
- Three write modes coexist: same transaction, post-commit with a propagating exception, and best-effort.
- No retention, no tamper protection, no target columns, no severity or category column.

### Coverage matrix

| Action family | Today | RC1 target | Package |
|---|---|---|---|
| Local sign-in success | Yes | Yes, actor as user id | AUD-1.1 |
| Local sign-in failure | Partial: anonymous, no target, no source | Target user id when resolved, source address; no pre-validation rows | IAM-11.4 |
| Entra sign-in success and unlinked rejection | Yes | Unchanged | |
| Entra remote failure | No | Yes | AUD-1.2 |
| Sign-out | No | Yes | AUD-1.2 |
| Throttling or lockout | Not applicable (no feature) | Yes | IAM-11.4 |
| Password change, reset request and completion | Yes | Unchanged | |
| Admin password set | Yes, success only | Replaced by recovery events | IAM-11.3 |
| Setup invitation | Yes, but actor is the target | Actor is the inviting administrator | AUD-1.2 |
| MFA enrol, reset, recovery codes | Yes, best-effort | In-transaction | AUD-1.2 |
| Emergency access and break-glass | Yes; break-glass marked only in user management | Marked on every action in the session | AUD-1.2 |
| Step-up | Yes | Unchanged | |
| Entra link and unlink | Yes | Plus return-to-local | IAM-11.3 |
| Entra provisioning and access requests | Yes | Unchanged | |
| Entra tenant registration changes | No, except the policy toggle | Yes | AUD-1.2 |
| Sign-in experience policy change | No | Yes | AUD-1.2 |
| User create | Yes | Unchanged | |
| User disable, enable, decommission | Not applicable | Yes | IAM-11 |
| Role create, update, delete | Yes | Unchanged | |
| Role permission change | Partial: count only | Added and removed permissions | IAM-10.2 |
| Role assignment | Yes | Plus refused-grant events | IAM-10.2 |
| Startup permission repair | No | One summary event when it changes anything | IAM-10.2 |
| Operational teams and membership | No | Yes | LST-1.4 |
| Managed contacts, attention routing | No | Yes | AUD-1.4 |
| Resource context create, update, archive | Yes | Outcome vocabulary normalized | AUD-1.1 |
| Ownership moves | Yes, transactional | Unchanged (model behaviour) | |
| Integration create, update, activate, deactivate, retire | No | Yes | AUD-1.4 |
| Integration delete | Yes | Unchanged | |
| Binding changes | No | Yes | AUD-1.4 |
| Integration and platform settings | Yes | Unchanged; re-check `IsSensitive` flags | RCV-1 |
| Connection create, update, rotate, retire, test, browse | Yes | Unchanged (model behaviour) | |
| Schedule create, update, enable, disable | No | Yes | AUD-1.4 |
| Schedule delete | Yes | Unchanged | |
| Scheduled dispatch | Yes, one row per fire | Routine category; blocked rows on transition | AUD-1.5 |
| Manual workflow or operation start | Partial: blocked only, no actor on success | Yes, with actor | AUD-1.3 |
| Procedure launch | No | Yes, parameter names only | AUD-1.3 |
| Run cancel | No; requester hard-coded null | Yes, with actor | AUD-1.3 |
| Run retry, park, resume, single-item retry | Partial or no | Yes, with actor | AUD-1.3 |
| Bulk retry, attention acknowledge | Yes | Unchanged | |
| Notification definition create, update, enable; subscriptions | No | Yes | AUD-1.4 |
| Notification definition delete | Yes | Unchanged | |
| Notification diagnostics actions | No | Yes for live sends | AUD-1.4 |
| Own profile and notification preferences | Yes | Routine category | AUD-1.5 |
| Module lifecycle | Yes | "Discover" becomes Routine | AUD-1.5 |
| Runtime shutdown request, enrollment invitation | Yes | Plus the enrollment itself | AUD-1.2 |
| Secret store operations | Partial: separate table, actor always `system`, unread | Real actor; surfaced under a Secrets category | AUD-1.4 |
| Bootstrap and CLI | Partial: file changes unaudited | CLI verbs added in OPS-1 are audited; file-store audit recorded as a known gap | OPS-1 |
| Business data reads | No | No (deferred with the feature) | |
| Authorization denials | Partial | Denials of Elevated and Critical permissions audited | IAM-10.3 |
| Audit reads | Denials only | Unchanged | |

### Over-logging to remove or demote

- Per-fire dispatch rows and repeated blocked rows.
- Module "Discover modules".
- Avatar and notification preference changes.
- Failed sign-in rows for missing credentials or disabled local auth, which any anonymous caller can generate without limit.
- The dashboard "Audit (24h)" count, which currently counts all of the above.

### Sensitive data

- No secret, password, token or secret reference was found in any payload.
- Non-sensitive setting values are logged in clear, so safety depends on every setting definition's `IsSensitive` flag. Review the flags in RCV-1.
- User-controlled names are interpolated unescaped into `key=value` details and then regex-parsed for links. The structured recorder removes this.

---

## L. Navigation and Shell Findings

### Current structure

- State lives in `MainLayout.razor` (`isControlPanelOpen`, `isControlPanelLocked`, `currentDomainName`). Open and locked are always set together, so there is no peek state.
- `ControlPanel` is unmounted while closed, by a UIX-2 constraint.
- Menus come from `ControlPanelMenuDefinitions.GetDefaultMenus()`, are filtered server-side by `MenusController`, and fall back to an unfiltered client copy if the call fails.
- Section ownership is a hard-coded if-chain in `RouteToSectionResolver` that runs before any menu lookup.
- Breadcrumbs are 97 ordered matchers in `ManagementBreadcrumbResolver` (1,391 lines).

### Entity action inconsistencies (input to ACT-1)

1. Three edit models: separate route (Notification Definition), in-place toggle (Role, Schedule, Connection), always-editable (Application, Team, Contact).
2. Notification Delete on detail but not on edit.
3. Lifecycle actions in the toolbar (Integration, Schedule, Connection), in a body section (Module, Application) or on a list row (Integrations list).
4. Destructive-last rule broken on Integration, Schedule and Role.
5. Three different stylings for the primary "New" action.
6. Two stylings for Save and Cancel.
7. Enable and Disable as two buttons (Schedule, Module) or one swapped button (Integration).
8. Five different Back labels; none on Run or Integration.
9. Uneven confirmation: Integration Retire has none, Connection Retire does.

### Accessibility gaps in the shell

- The rail `<nav>` has no label; nothing sets `aria-current`.
- A parent menu row whose link was filtered out renders as a click-only `<div>` with no role, tab stop or key handler.
- Tooltips appear on hover only.
- The panel has no Escape handling and no focus management.
- Two level-one headings per page (top bar and `PageHeader`).
- No automated accessibility tests exist.

### Hover-to-open (N-19)

The request is sound but touches the most fragile part of the shell.

- The panel is in-flow. Opening on hover would shift the page unless the peek is an overlay.
- Mounting and unmounting on hover risks the failures UIX-2 fixed.
- Touch devices have no hover. Keyboard users need the same reveal on focus.

**Recommendation.** When the drawer is already open (the owner's stated condition), hovering or focusing another rail icon swaps the menu content after a short intent delay. No mount or unmount happens, so the UIX-2 risk does not arise. Click navigates to the default destination. On touch, the first tap swaps the menu and the second navigates. Escape returns focus to the rail. This is narrower and safer than a general hover-to-open and matches the wording of the request.

### Tree disclosure glyph (N-18)

Plus and minus to the left of the node is acceptable and distinguishes trees from the existing right-hand chevron rows, which is the stated intent. State is exposed through `aria-expanded`, never through the glyph. The node label remains a link; the glyph is a separate button with its own accessible name.

### Runs and Schedules under Integrations (N-21)

- Not every run or schedule belongs to an integration (`/execute` runs; schedules with a null `IntegrationId`). All Runs and All Schedules still list them. The trees list only integrations.
- The integration hub already has `/integrations/{id}/runs` and `/schedules` tabs. **Recommendation:** tree children under All Runs and All Schedules open the global list filtered by integration, with a visible filter chip and a link to the integration. This keeps the user in "all runs, narrowed" and avoids two different pages both called Runs for the same integration. Under All Integrations, the panel nodes open the hub tabs.

### Drill-through context (N-25)

**What exists.** `?from=<type>&fromKey=<key>` validated against `ManagementNavigationContextCatalog`, one registered type, one contextualizable destination, depth one. `DrillInLink` already exists as a component.

**Recommended model: navigation provenance in the URL.**

- **Declaration.** A drill-through is a link rendered with `DrillInLink` (or an equivalent parameter) from a detail surface. The link appends the current page's typed identity to a bounded provenance list in the query. Ordinary links and all menu, rail and tree links never carry provenance, which is the reset rule.
- **Encoding.** A compact list of typed identities, capped in depth. Labels and hrefs are always resolved from the catalog, never read from the URL, so the security property of UIX-4 holds.
- **Behaviour for free.** Refresh, deep link, share and new tab all work because the URL is the state. Back and Forward work because each drill-through is a real navigation.
- **Display.** Current and previous context shown; earlier contexts collapse behind a left ellipsis that expands in place.
- **Proposed simplification.** The right-hand ellipsis for "contexts ahead" is replaced by the browser Forward button. Contexts ahead only exist after the user goes back, and browser history already holds them. Modelling them in the URL would mean a URL that describes pages the user is not on. This needs owner approval (Q-09).
- **Catalog growth.** Each entity type that can be a source registers a context definition (integration, connection, schedule, run, application, user, role).

**Cost.** This is not a breadcrumb component enhancement. It needs the registry (so breadcrumb parents are data), a generalized codec, about seven context definitions, and changes to every detail page that links across hierarchies. **Classification: design gate before RC1, implementation after.** The RC1 seam is that NAV-2 keeps breadcrumb parent separate from menu parent and does not remove the typed-context catalog.

### Icons (N-24)

Icon keys are strings from a platform-owned set, mapped to components in the client. That same key is what a module or connector will later supply, and what an operator will pick for a workspace. No arbitrary SVG, URL or upload path is created.

---

## M. Extensibility and Module Findings

### What a module is today

A class implementing `IModule`, compiled into both hosts, plus between one and four hand-written `AddXxx()` calls per host. Adding a module means editing two project files, two `Program.cs` files and an architecture-test list, then rebuilding and restarting both hosts.

### Module UI capability (M-15, M-16)

Four parallel field schemas exist:

| Schema | Used for | State |
|---|---|---|
| `PlatformSettingDefinition` with `InputKind` and option providers | Platform and integration settings | Most mature; the foundation |
| `IntegrationSettingDefinition` | SDK-declared integration settings | Lossy mapping onto the first; no options |
| `ParameterSchema` JSON string | Procedure and operation parameters | Untyped; not validated at registration; samples already violate it |
| `ConnectionFieldDescriptor` | Connection forms | Text and password only |

| Target | Assessment |
|---|---|
| Simple metadata-defined panels | About 70 percent. Needs one typed schema and a "launch with these values" binding. |
| Complex panels | Absent. No layout, conditional visibility, cross-field validation or module option providers. |
| Wizards | Absent. `WizardFlowController` is client-only navigation state. No server-held state and no module handler points. All module code runs only as worker step handlers. |

**Conclusion.** The existing work can carry simple panels after consolidation. Complex panels and wizards need a control-plane handler contract that does not exist. RC1 should choose the single schema and reserve the handler seam, and build neither.

### Module installation and "platform reboot" (M-19, M-20)

- There is no platform-level thing to reboot. There are three independent processes and one database.
- The only cross-process control is a shutdown request that a worker polls. Nothing starts a process.
- Starting the Management host applies migrations silently.
- The control plane cannot tell which module builds a worker is running.

**Honest meaning of the request.** A coordinated restart: drain, stop workers, apply migrations, start the Management host, start workers, confirm each worker reports the expected module set. That needs a host adapter (the Windows Service control manager for RC1's topology) and a composition report. RC1 delivers the composition report (SDK-8.4), explicit migration (OPS-1.3) and services that restart correctly. The orchestration is post-RC1 (MOD-1).

### PowerShell runtime and SDK (M-12, M-13)

- **Recommendation: protocol first.** The platform exposes operations, results and prompts over an API contract. PowerShell is one client and one handler runtime. Bash follows the same contract.
- A script is an operation handler kind launched through the Procedure pipeline. It is not a new scheduled job type. The enum value `ScheduledJobType.PowerShellScriptExecution` should be removed or marked reserved.
- Prerequisites that RC1 does deliver: step timeout, cancellation that reaches the handler, lease renewal. Prerequisites it does not: a non-human principal, an interactive prompt channel, output capture limits, secret injection policy.

### SDK stability

The execution core is coherent and tested: `IStepHandler`, signals, operation items and retry, result contracts, testing toolkits. The rest is not ready to freeze: namespaces that contradict the assembly, an obsolete member on the main context interface, two settings contracts, no version check, no handler dependency injection, hand-wired composition, lifecycle verbs with provisional meaning. **RC1 ships the SDK as preview with the execution subset declared stable.**

### Open-core boundary

- The licence is AGPL-3.0 and the README states the enterprise intent. `CONTRIBUTING.md` has no contributor licence agreement, which matters for dual licensing of contributed code. That is a legal decision for the owner (Q-14).
- No licensing, edition or entitlement hook exists in code, and none should be added to Core for RC1.
- A premium assembly can plug in today only by editing the host's startup code. The smallest seam is a host extension hook that loads configured assemblies and lets them register services and modules. Design it in SDK-8.0.

| Capability | Recommended home | Reason |
|---|---|---|
| Permission catalog, audit, navigation registry, entity actions, field schema, procedure pipeline | Core | Neutral primitives every extension needs |
| Metric provider interface, dashboard block host, basic blocks | Core | Without them modules cannot contribute to the core experience |
| Business Rules: definition model and simple evaluation | Core | The owner's stated intent |
| Business Rules: visual builder, time-dimensional engine, warehouse outputs, projection to external systems | Premium | Advanced analytics |
| Workflow designer, module authoring tool | Premium candidates | Tooling over stable Core contracts |
| AI capabilities | Premium candidate | Must act through Core authorization, never around it |
| Additional Entra tenants, SCIM, group-to-role sync | Owner decision | Enterprise identity is a common premium line, but guest access is a Core need |

---

## N. Identifier and UUID Findings

- **UUID v7 is used nowhere.** `Guid.CreateVersion7` is available (the SDK is .NET 10).
- **Ids are strings, not `uniqueidentifier` columns.** `EntityId` wraps a string. About 42 entities have an `nvarchar(64)` clustered primary key holding random hex. Eight append-heavy log tables use integer identity keys.
- **No central generator.** About 150 call sites in shipping code. 69 of them generate an `AuditRecordId` that `SqlAuditService` discards.
- **SQL Server ordering.** In a `uniqueidentifier` column a v7 value is not insert-ordered, because SQL Server sorts on the last bytes first. In a string column, v7 formatted as hex sorts in creation order. The platform's string keys are therefore the case where time-ordered ids help.
- **A blanket replacement would be unsafe.** 25 sites truncate to 24 characters, which under v7 leaves about 42 random bits. One truncates to 16 (12 random bits). One truncates to 8 for DOM ids, which would collide for about a minute. Tokens and unguessable handles must stay random.

| Group | Examples | Recommendation |
|---|---|---|
| High-volume keys | Run, workflow and operation instance, operation group and item, platform event, notification instance, outbox, inbox | New ids time-ordered, full length |
| Low-volume entity keys | User, integration, connection, binding, schedule, context, role, team, contact | Keep. No benefit at this volume. |
| Must stay random | Enrollment token, access request id, profile image id, avatar key, secret id, account action token secret | Random; hash stored where it is a bearer value |
| Not a UUID | Schedule idempotency key, worker id, correlation id | Keep as is |
| Dead | `AuditRecordId` at 69 sites | Delete |

**Verdict.** No post-RC1 migration project is warranted. Existing ids are opaque and can coexist with new ones. The change is small, affects only new rows and belongs before RC1 as ID-1. Risks: ordered ids reveal creation time in URLs such as `/runs/{id}` (acceptable in a single-tenant operator UI); tie-breakers that sort by id become chronological, which may shift tests that assert an arbitrary order.

---

## O. Test and Validation Strategy

| Layer | Use it for | RC1 additions |
|---|---|---|
| Unit | Rules, evaluators, formatters, validators, recorder | Username rules, subset rules, title formatter, schema validation, id helper |
| Architecture | Dependency and convention rules | Mutating endpoints reach an audit recorder; entity pages use `EntityToolbar`; registry consistency |
| Integration over HTTP with SQL Server | Authentication, authorization denial, lifecycle flows | Disable ends a live session; escalation refusals; recovery flows; paging; team membership |
| SQL Server focused | Migrations, concurrency, indexes | Username backfill; implication expansion; decommission across Restrict tables; decommission versus role assignment; lease renewal without double execution; upgrade from the previous tag |
| bUnit | Component behaviour and accessibility contract | `NavTree` keyboard model; `EntityToolbar`; `UserRef`; `AsyncSearchSelect` |
| Playwright | Things only a browser proves | Rail click and hover; tree navigation; titles; history and refresh on filtered lists; disabled-user redirect; axe baseline on six representative pages |
| End to end | One real path | Schedule fires, worker runs, run completes, notification is delivered |
| Manual smoke | Environment-dependent | SEC-1 with real Entra, Safari and HTTPS; IAM-3, IAM-4, IAM-5 runbooks; install and restore rehearsal |

**Where focused tests are enough.** Quick wins, icons, `UserRef` adoption, diagnostics cleanup, identifier helper, documentation.

**Where a full CI checkpoint is warranted.** After IAM-9.2 (session identity), IAM-10.2 (escalation), IAM-11.2 (decommission), NAV-2.3 (information architecture), LST-1.2 (Runs paging), SDK-8.2 (timeouts and leases), OPS-1.3 (migration). Both CI jobs must pass at each.

**Tests to avoid.** Screenshot comparison of the whole shell. Per-page browser tests for every entity. Load tests. Source-text assertions that pin markup; they made the navigation work expensive and should not grow.

**Policy changes.** SQL tests fail when SQL is unavailable. The 33 soft-skip sites are converted in RCV-1 or earlier when a package touches them.

---

## P. Roadmap Reconciliation

| Category | Items |
|---|---|
| Still valid | The pre-release roadmap structure. Deferrals of Systems, the Track 5 remainder, Dashboard Panels and the shared Wizard. Secrets phase 2 (Key Vault store) and a non-DPAPI protector as prerequisites for any non-Windows host. |
| Already complete | Stabilization S1 to S13; SWM-1 to SWM-12; NAV-1; UIX-1 to UIX-7; AUP-1; SDK-1 to SDK-7; JOB-1; BDS-1 to BDS-6; CTX-1 to CTX-3B; HSK-A to D; Toast-A and B; Components; Services; SEC-1 engineering; LFC-1; IAM-8; secrets phase 1. |
| Stale | `docs/roadmap.md`; `current-architecture-tracking.md`; `.ai/playbooks/project-map.md`; both ADR indexes; `CHANGELOG.md`; IAM-8 "not merged" in five places; wording that presents DTFx as the current runtime; `rr1-v1-release-readiness-audit.md` (a stub pointing at a superseded audit). |
| Superseded by current direction | Federation phases 1 to 4 and ADR-0005 to 0011. ADR-0040 rule 2 (Runs under Control Center) and rule 3 (Connections and Schedules off the rail). The ADR-0044 ruling that "Service" never appears in navigation. |
| Missing RC1 work | Everything in section G. In particular: identity lifecycle, authorization hardening, audit hardening, operability, module runtime correctness, and a definition of RC1. |
| Incorrectly ordered | Track 9 puts the security review last. Escalation and attribution defects should be fixed before more surface is added. Item 8 (cross-context grouping) is sequenced "so the rail does not move twice", yet NAV-2 will move the rail now. Record that item 8 inherits the registry and adds no second rail change. |
| Federation changes | Withdraw the six federation ADRs and the relationship leg of ADR-0007. Banner on `federated-instance-relationships.md`. Remove `docs/roadmap.md` phases 1 to 4. Addendum to ADR-0037 section 12. Annotate the Relationship evaluation path as reserved. Remove the seeded `relationship.manage`. Write one ADR: external-organization access is local guest users, guests in the primary Entra tenant, or later an additional mapped tenant. |
| Explicitly post-RC1 | Track 7 (Azure and deployment beyond Windows). Item 8 grouping. Track 8 module UI and wizard. Context-scoped authorization. Metrics, dashboards, workspaces, CLI rebuild, script runtime, module installation, Services abstraction, drill-through implementation. |

**Note on external access.** Guests are instance-wide today because authorization has no context scope. A guest confined to one Application needs context-scoped grants, which the roadmap defers "until a concrete need appears". External-organization access is that need. Record it as the trigger, and schedule it after RC1 with POL-1.

---

## Q. Deferred Architecture and Seams RC1 Should Preserve

### Mid-term

**Policies and data authority (POL-1 gate).**
- Build on: permission definitions and sets from IAM-10; the principal model from IAM-10.0.
- Needs: a principal kind for integrations; `context:{id}` and `integration:{id}` scope forms; a record anchor independent of operation items; a record-type registry with an owner; a publisher-scoped notes store keyed by record type, business key and publishing integration; a distinct permission for modifying shared data.
- The rule the owner wants falls out directly: an integration always writes its own note set; writing shared data requires an explicit grant; installing a module grants nothing.
- RC1 must not: treat `integration.read` at instance scope as the permanent business-data security model; rename the `api/demo/business-records` routes to look final; freeze `SharedRecordIdentity` comparison rules as universal.

**Metrics.**
- Shape: a request-style `IMetricProvider` contributed by platform and modules, returning typed values with unit and window, authorized through the permission catalog, with caching and throttling in the platform host.
- Drill-through: a metric definition optionally returns a navigation target (a list route plus filter criteria) or a provider-defined detail query. The target is the same typed-identity shape used by navigation provenance, so metric drill-through and breadcrumb drill-through share one mechanism.
- RC1 must not: let `DashboardCardFact(string, string)` or `IModuleTelemetry` names become the metric contract. Mark `IModuleTelemetry` as best-effort in the SDK statement. Mark `api/dashboard/default` internal or delete it.

**Dashboards.**
- Shared primitives: metrics for data; the field schema for forms; the Procedure pipeline for operations; `EntityActionDescriptor` for item actions; the authorization result type for disabled and restricted states; navigation provenance for drill-down.
- Nothing dashboard-specific is built in RC1. No chart library exists in the repository and none should be chosen now.

**CLI and environment trust (CLI-2 gate).**
- Foundation to keep: `FileBootstrapConfigurationStore`, `BootstrapCliService`, `EmergencyAccessService`, the stdin-only secret contract.
- To rebuild: the shell. A binary PowerShell module or a structured command-line host, an environment profile store, object output, confirmation gates.
- Prerequisite: a non-interactive API credential mapped to a principal. Direct SQL stays for local break-glass only.
- Threat model notes: an environment that can deploy to another makes the operator's workstation part of the target's trust boundary. Require target-side approval, scoped capabilities, expiry and revocation. Never reuse the enrollment token code or the static recovery token.

**Module UI (MUI-1 gate) and module installation (MOD-1 gate).** As described in section M.

### Long-term

**Services.**
- RC1 adds a navigation domain labelled Services and nothing else.
- RC1 must not: add `service.*` permissions, a `/services` route, a Service type, or new columns on `Connections` that assume one connector per connection forever.
- Known future obstacles, recorded so they are not deepened: `Bindings.ConnectionId` (one connection per slot); `IConnectionResolver.ResolveAsync<T>(Connection)`; the tool seam keyed by connector definition and composed only in the Management host; every consumer assuming one row per connection.

**Workspaces.**
- The word already means two other things (the Connection workspace and Application views). Choose the product term before any API uses it.
- Seams RC1 preserves: rail domains are registry data, so promotion needs no code change; a typed default-view preference with an "explicitly chosen" flag, which is exactly what the team-default rule needs; teams gain settings only when the feature arrives.
- A workspace is a presentation, not a grant. It never widens data access.

### Horizon

- **Business Rules.** Do not grow `NotificationRuleDocument` into a general rules engine. Decide whether the linear `WorkflowDefinition.StepNames` shape is provisional in the SDK statement.
- **Workflow designer.** Depends on the typed parameter schema and the Procedure envelope.
- **AI.** Depends on the principal model. An AI acts as its own principal with its own grants and its own audit trail.
- **Module authoring tool.** Depends on the single field schema and the SDK stability statement.

---

## R. Decision Register

### Decisions this audit resolves (recommendations)

| ID | Decision | Alternatives considered | Why it fits | Consequences | Record as |
|---|---|---|---|---|---|
| D-01 | RC1 is defined as in section C | Leave undefined; define by feature list only | The roadmap reserves the term and defines nothing | Operability and verification become RC1 scope | RC-0 gate |
| D-02 | Username is a first-class attribute of every user; retired usernames are never reused | Keep it on the identity link; allow reuse | Entra-only users have none today; reuse would inherit history and break future URLs | Migration; generated usernames for Entra users | IAM-9 gate |
| D-03 | Session and audit identity is the user id | Keep username or object id in the cookie | One person appears under three actor values today | One-time sign-out at upgrade | IAM-9 gate, ADR |
| D-04 | "Delete user" is implemented as decommission (tombstone) | Hard delete with placeholder | Six Restrict foreign keys, loose references, audit attribution, bootstrap re-creation | Row retained; personal data erasable | IAM-11 gate, ADR |
| D-05 | Credential recovery is a new `Critical` permission with step-up, granted to the platform administrator by default and assignable only by a holder | Reuse `credential.manage`; restrict to the administrator role only | Matches the owner's wording; the repair arrays already provide the default-grant mechanism | Depends on closing escalation first | IAM-10 and IAM-11 gates |
| D-06 | Administrators initiate resets; they do not set passwords | Keep direct password set | The administrator should not know another user's password | Needs a path for users with no verified email | IAM-11 gate |
| D-07 | Runs are chronological by default on both Runs surfaces | Keep triage sort with a toggle | Owner request; server already returns this order | A pinned test changes | QW-1 |
| D-08 | "Services" is permitted as a navigation domain label only. No Service entity, route, permission or type. | Full reversal of ADR-0044; a Connections domain | Achieves the owner's intent without undoing a sound ruling | ADR-0044 amended, not superseded | RC-0, ADR amendment |
| D-09 | Runs and Schedules move to the Integrations domain; Connections to Services; URLs do not move | Move URLs too | URL moves break scoped-route tables, return targets and deep links for no gain | ADR-0040 rules 2 and 3 superseded | RC-0 |
| D-11 | The SDK ships as preview with the execution subset stable | Declare it stable; hold RC1 until it is | Four form schemas and no versioning cannot be frozen honestly | External module authors get a clear contract | SDK-8 gate |
| D-14 | "Policy" maps to permission definitions plus permission sets on the existing catalog | A separate policy engine | The evaluator and catalog already express it; a second system would diverge | Sets are designed now and built later | IAM-10 gate, ADR-0012 amendment |
| D-15 | No new Operation abstraction; generalize the Procedure later | New command bus | The launch pipeline already has identity, authorization, validation, dispatch and results | None in RC1 | Section Q note |
| D-16 | No UUID migration project; ordered ids for new high-volume rows only | Convert everything; change column types | Ids are opaque strings; only string keys benefit | Small change in ID-1 | ID-1 |
| D-18 | Drill-through is URL-encoded provenance, designed before RC1 and implemented after | History state; in-memory stack; implement now | Keeps refresh, share and new-tab behaviour and the no-URL-in-query property | NAV-2 must keep breadcrumb parent separate | NAV-3 gate |
| D-19 | Federation is withdrawn | Keep as deferred | No code; all ADRs are Proposed; owner direction | Dormant types annotated; one permission removed | RC-0, new ADR |
| D-20 | Audit gets target, category and structured details, and security events are written in-transaction | Keep free-text details | Connection audit already proves the model | Additive migration | AUD-1 gate, ADR |

### Decisions that cannot responsibly be made from current evidence

| ID | Decision | What is missing |
|---|---|---|
| D-10 | Which permission governs operational teams | Owner intent: are teams a governance object or an operations object? |
| D-12 | The supported RC1 topology | Owner's target customers and hosting |
| D-13 | Whether an additional Entra tenant is in scope at all, and whether it is Core | Owner's commercial intent |
| D-17 | What to do about "one module per process" | Whether the invariant is a deployment option or a requirement. A worker setting that selects which modules it hosts would satisfy it by deployment at low cost. |
| | Paging contract (offset or keyset) | Expected run volumes |
| | Whether context-scoped authorization is needed for the first external-organization customer | A concrete guest scenario |
| | Whether the username counts as personal data to erase on decommission | Legal and regulatory context |
| | The metric and dashboard contracts | No consumer exists yet |
| | The Service model | No second example beyond the SQL structure browser |

---

## S. Open Questions for the Product Owner

| ID | Question | Default if unanswered |
|---|---|---|
| Q-01 | Is the RC1 definition in section C acceptable, including operability and one supported topology (Windows Server, SQL Server, Windows Services)? | Proceed with it |
| Q-02 | For Entra-provisioned users, may the platform generate the username from the sign-in name, with administrators able to change it before first use? | Yes |
| Q-03 | On decommission, should the display name be replaced by a standard placeholder by default, or retained for history with a separate "erase personal data" action? | Retain; separate erase action |
| Q-04 | Should direct administrator password set remain as an emergency path, or be removed in favour of reset links only? | Keep, under the critical permission, with a forced change at next sign-in |
| Q-05 | Which permission should govern operational teams: a new team permission, the existing schedule management permission, or user management? | A new `operational_team.manage`, granted to roles that hold `schedule.manage` today |
| Q-06 | Should the Schedules list and the Integrations fleet also drop their attention-first default sort for consistency with Runs? | Yes for Schedules; keep fleet attention as a labelled sort option |
| Q-07 | Do you accept the narrower hover behaviour: hover or focus swaps the menu only while the drawer is already open? | Yes (it matches your wording) |
| Q-08 | Under All Runs, All Schedules and All Connections, should an integration node open the global list filtered to that integration, or the integration's own tab? | Filtered global list |
| Q-09 | For drill-through breadcrumbs, do you accept the browser Forward button in place of a right-hand ellipsis, and implementation after RC1? | Yes to both |
| Q-10 | "Artisan support" names Laravel's tool, and this platform is .NET. Do you mean pass-through to the equivalent tooling (`dotnet`, EF migrations, Aspire), or is a PHP service planned? | Treat as .NET tooling pass-through |
| Q-11 | Is a guest who can see the whole instance acceptable for the first external-organization use, or must guests be confined to one Application? | Acceptable for RC1; confinement after |
| Q-12 | Are "platform notes" free text, structured fields, or both? | Decide in POL-1 |
| Q-13 | What should Workspaces be called, given "workspace" already names the Connection page and Application views? | Decide before any API uses the word |
| Q-14 | Do you intend to dual-license contributed code? If so a contributor agreement is needed before outside contributions. | No change until decided |
| Q-15 | Should `AGENTS.md` rule 3 (one module per process) be reworded to describe a deployment option, or enforced? | Reword, and add the worker module-selection setting |

---

## T. Recommended Immediate Next Step

1. **Answer Q-01, Q-05, Q-07, Q-08, Q-15** and confirm decisions D-02 to D-09 and D-19. These unblock all three tracks.
2. **Run RC-0** as a documentation-only body: the program gate, the ADR amendments and withdrawals, and the roadmap reconciliation. One to two sessions.
3. **Start two things in parallel once RC-0 is approved:**
   - **IAM-9.0**, the identity foundation design gate (Architect).
   - **QW-1**, the four quick wins (Standard agent), which need no further design.

### Closing statements

1. **Is the proposed RC1 program architecturally sound?** Yes, with one condition. It is sound because it retires attribution and escalation risk before adding lifecycle features, changes navigation metadata once, and adds no parallel security, list or operation system. The condition is that RC1 includes operability (OPS-1) and honest verification (RCV-1). Without them the result is a feature milestone, not a release candidate.

2. **Decisions that need your approval before implementation.** D-01 (RC1 definition), D-02 and D-03 (username and session identity), D-04 (decommission instead of hard delete), D-05 and D-06 (credential recovery model), D-08 and D-09 (Services label and the navigation move, which amend ADR-0044 and supersede parts of ADR-0040), D-11 (SDK preview), D-19 (federation withdrawal), plus the open decisions D-10, D-12 and D-17.

3. **First epic or design gate.** RC-0, then IAM-9.0 in parallel with QW-1.

4. **Where another Architect-level review is warranted.**
   - The design gates: IAM-9.0, AUD-1.0, IAM-10.0, IAM-11.0, NAV-2.0, SDK-8.0, OPS-1.0.
   - Review of four implementations before merge: IAM-9.2 (session identity), IAM-10.2 (escalation rules and the preserving migration), IAM-11.2 and 11.3 (decommission and recovery), SDK-8.2 (timeouts and leases).
   - The whole-product security review and the go or no-go in RCV-1.
   - Everything else (QW-1, AUD-1.2 to 1.6, IAM-10.1 and 10.4, NAV-2.2 to 2.6, ACT-1, LST-1, SDK-8.3 to 8.5, ID-1, OPS-1.1, 1.2, 1.5 to 1.7) can be delegated to less expensive agents working from the packages in section I.
