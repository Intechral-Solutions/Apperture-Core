# RC1 Program Gate (RC-0)

| | |
|---|---|
| Status | **Approved program contract.** Product-owner rulings recorded 2026-10-03. |
| Baseline | `master` at `5cfa901` |
| Nature | Documentation and architecture reconciliation only. No application code changed. |
| Evidence | [Part 1 audit](../audits/rc1-architecture-audit-and-program-plan.md) and its [supplemental addendum](../audits/rc1-supplemental-reconciliation-addendum.md). Both are working records. Where either differs from this gate, this gate governs. |

This document is the RC1 program contract. It records what was decided, what was left as a Direction or Candidate, what is deferred, the RC1 definition, and the epic sequence. Epic scope and work packages are in Part 1 sections H and I, as amended by addendum section F and by section 6 below.

It lives in `docs/01-architecture/` and not under `transitions/`, because it is a standing contract and not a disposable working record.

## 1. Status vocabulary

| Status | Meaning |
|---|---|
| **Decision** | Deliberately settled product or architecture direction. Binding on implementation. |
| **Direction** | A preferred direction. Not committed architecture. A design gate must confirm it before anything is built. |
| **Candidate** | Worth investigating. No direction chosen. |
| **Deferred** | Considered and intentionally postponed. Carries a trigger where one is known. |
| **Rejected** | Considered and not adopted. |

A Candidate or Direction is never promoted to a Decision by implementation. Promotion needs an explicit ruling.

## 2. RC1 definition (Decision)

RC1 is a real release-candidate milestone. It is a feature-complete, upgrade-safe candidate of the single-instance operator platform on one supported topology, with a security-reviewed identity and audit core, a coherent operator experience, and a module SDK labelled preview.

RC1 is reached when all of these hold:

1. **One supported production topology works:** Windows Server, SQL Server, and the Management Host, Management UI Server and Worker Host running as Windows Services.
2. **An operator can install, upgrade, back up, restore and health-check** by a documented procedure, and a fresh instance cannot be claimed by an anonymous caller.
3. **Identity lifecycle is complete:** username, disable, decommission, credential recovery, session termination, sign-in throttling.
4. **Authorization has no known escalation path,** and every permission has a human-readable name, description and sensitivity.
5. **Audit attributes every administrative and operator action to a stable user identity,** with a queryable target, and routine noise is separated from significant events.
6. **Navigation and entity screens are consistent:** one navigation registry, titles on every route, icons, one toolbar model, shared list state and paging behaviour.
7. **Runtime correctness defects are closed:** missing step handler, lease expiry during a long step, unvalidated parameter schemas.
8. **Verification is honest:** no silently passing SQL tests, one real end-to-end run test, functional browser smoke, an accessibility baseline, an upgrade test, and the outstanding operator smoke runbooks executed.
9. **Documentation matches the code.**

**Topology wording.** Azure, Docker or Linux production hosting and other topologies are not RC1-supported deployment targets. They remain architectural goals. Nothing in RC1 should foreclose them.

**RC1 is not:** module installation from packages, metrics, dashboards, workspaces, a rebuilt CLI, a script runtime, platform search, protected lookup, a platform calendar, context-scoped authorization, or a stable SDK.

## 3. Decisions

### 3.1 Identity

| ID | Decision |
|---|---|
| D-01 | Username is a first-class attribute of every user. |
| D-02 | The stable internal user id is the session identity and the audit identity. |
| D-03 | Retired usernames are never reused. |
| D-04 | "Delete user" is implemented as decommission (tombstone), not hard deletion. Default behaviour: retain the user id; retain and reserve the username; retain enough presentation information for understandable audit and history; remove active identity links, credentials, MFA, tokens, grants, team memberships and personal operational relationships as the IAM-11 gate specifies. |
| D-05 | Personal-data erasure is a separate future operation for regulatory needs. Ordinary decommission does not destroy historical attribution. |
| D-06 | A new Critical credential-recovery permission exists, with declarative step-up. |
| D-07 | Ordinary administrators **initiate** a secure password reset or a one-time account setup flow. Ordinary governance does not let an administrator choose or directly set another user's password. Any truly exceptional direct reset is emergency or Recovery Access tooling. The IAM-11 gate rules on the exact emergency path. |
| D-08 | Operational teams are governed by a dedicated permission, `operational_team.manage`, not by `schedule.manage` or `user.manage`. Roles intended to manage teams receive it explicitly through migration or repair. A user's view of their own team memberships must not require team-management authority; that self-view is designed separately if needed. |

### 3.2 Authorization

| ID | Decision |
|---|---|
| D-09 | The owner's "policy" concept extends the existing permission system: human-readable permission definitions now, permission sets later. There is no second policy engine. |
| D-10 | Context-scoped authorization is not implemented in RC1. The IAM-10.0 gate must still settle scope composition well enough that RC1 does not freeze an incompatible model. Its design scope is the six questions in section 6.3. |
| D-11 | No context scope constant is added to shipping code in RC1, including as a reservation. |
| D-12 | For RC1, guests cannot be confined to one Application. Confinement is by capability and by module. This is documented honestly. Application-confined guest access is the concrete trigger for scoped authorization. |

### 3.3 Audit

| ID | Decision |
|---|---|
| D-13 | Audit records gain a stable actor identity, target information, a category and severity vocabulary, and structured details. |

### 3.4 Navigation and experience

| ID | Decision |
|---|---|
| D-14 | "Services" may be an operator-facing navigation-domain label. RC1 introduces no Service entity, route family or permission. Recorded as an amendment to ADR-0044. |
| D-15 | Runs and Schedules move under the Integrations domain. Connections moves under Services. Existing URLs stay stable. Recorded as a partial supersession of ADR-0040. |
| D-16 | Runs use chronological ordering by default. |
| D-17 | Drill-through navigation is navigation provenance, with the URL as the durable state. Browser Back and Forward own future-history traversal. The first implementation models no right-hand "future context" control. Design happens before RC1; implementation after. The UI affordance may be revisited if usability testing shows browser Forward is insufficient. |
| D-18 | List work splits into LST-1 (RC1) and LST-2 (post-RC1), with the contents in section 6.1. |
| D-19 | Server-confirmed pending state is the default for lifecycle mutations. `ActionButton` gains a compact default pending treatment, `aria-busy`, reduced-motion behaviour and re-entrancy protection. Content stays visible during a refresh. Custom descriptive pending text remains available. |
| D-20 | "All" may describe the unfiltered scope of a dataset. "Load more", pagination and continuous loading concern how many records are retrieved. "Show All" is never interpreted as loading every row into the browser. There is no universal rule that "Show All" means "clear filters"; existing controls may keep or improve their wording. |

### 3.5 Modules, runtime and identifiers

| ID | Decision |
|---|---|
| D-21 | The SDK ships as preview. Only the execution subset is declared stable. |
| D-22 | No new generic Operation or Command abstraction. The Procedure and `IntegrationProcedureService` remain the operation launch envelope, to evolve later. |
| D-23 | "One module per compute process" is not a currently enforced invariant. Module or process isolation is a supported future deployment option. A worker module-selection mechanism can provide that topology. The runtime is not restructured to make the old statement literally true. |
| D-24 | `BusinessKey`, `SubjectKey` and `DisplayLabel` are **non-sensitive operational identifiers and labels**. Modules must not place secrets, authentication material, protected identifiers, regulated identifiers or sensitive personal data in them. They may be stored, indexed, displayed and searched as ordinary operational metadata. An integration that needs a sensitive external identifier uses an opaque ordinary record key and a future protected-lookup mechanism. SDK-8.5 documents this. SDK samples and RCV-1 validate that the platform's own modules follow it. |
| D-25 | No UUID migration project. Ordered identifiers are used selectively for new high-volume rows where justified. |

### 3.6 Scope and withdrawals

| ID | Decision |
|---|---|
| D-26 | Federation is withdrawn from the active roadmap. See [ADR-0046](../02-decisions/ADR-0046-external-organization-access-and-federation-withdrawal.md). |
| D-27 | Additional mapped Entra tenants are not in RC1. The capability stays a future external-organization access option. No Core-versus-premium ruling is made. |
| D-28 | Contribution policy is unchanged. Apperture Core remains AGPL. A contributor agreement is revisited only if a future commercial model requires proprietary relicensing of contributed Core code. Separate proprietary assemblies do not by themselves require that. |
| D-29 | "Artisan support" in the CLI direction was a mistaken framework reference. The intent is Development-mode pass-through to the platform's actual .NET tooling, including `dotnet` and EF tooling. No PHP or Laravel service is implied. |

## 4. Directions, Candidates, Deferred and Rejected

### 4.1 Directions (not committed architecture)

| Theme | Direction |
|---|---|
| Platform search | Post-RC1. The first search design gate begins by evaluating federation over existing authorized family reads. Business Data may later need a selected searchable projection once POL-1 provides a real record model. Integrations explicitly declare or project searchable Business Data; arbitrary physical fields are not indexed. |
| Protected exact lookup | Separate from ordinary search. Needs a dedicated security gate. |
| Platform notes | For POL-1, begin from typed, namespaced free-text annotations with provenance and authorization. Structured note data only when a concrete use case requires it. The schema is not settled. |
| List presentation (LST-2) | Shared composable presentation primitives with query-specific data strategies. No single smart table. |
| Views, quick filters, advanced filters | A view is a different dataset. A quick filter restricts the same dataset. Long-tail filters sit behind a filter control. Conventions are settled in LST-2. |
| Loading animation assets | Settle the component contract first. Specific assets are chosen later, with a licence check. |
| Workspaces | Ruling 2026-10-03. The preferred long-term product direction for cross-context operational experiences. A Workspace is a curated experience around a role, responsibility, team or use case. It may surface resources, dashboards, operations and related views drawn from several ResourceContexts. Operational Teams may later define or default to a Workspace. A Workspace is presentation, not a grant. It may be associated with a ResourceContext to decide where it is surfaced, listed or presented. That association does not grant data access and does not make ResourceContext an authorization boundary. It is not described as resource ownership. The Workspace design gate settles persistence and ownership. The Workspace direction supersedes pre-release roadmap item 8 as the planned product approach to cross-context operational experiences. Departments, responsibilities, operational areas and similar organizational concepts are expected to be addressed here, not through a new ResourceContext Kind. |

### 4.2 Not adopted at present

These are not selected. They are not rejected forever.

- SQL Server full-text search. If it is reconsidered, its environment and support implications must be verified first. No availability claim is recorded here as fact.
- An `ISearchIndex` provider abstraction.
- Elasticsearch or OpenSearch.
- A centralized search projection for every platform resource.

### 4.3 Deferred

| Item | Status | Trigger or dependency |
|---|---|---|
| Workspace implementation | Deferred | Its own design gate and a product trigger. The direction is in 4.1. The word "Workspace" is not frozen into any API or domain contract until the naming collision with the Connection workspace and Application views is deliberately resolved. No Workspace body is in RC1 or in section 7. |
| Cross-context organizational grouping | No longer an independently planned body (2026-10-03) | Kept only as a possible underlying primitive that the Workspace design may discover it needs. If that design proves a reusable, persistent grouping or membership model is independently necessary, it gets its own design gate then. No such primitive is ResourceContext ownership or authorization. |
| Context-scoped authorization (implementation) | Deferred | Application-confined guest access. Design scope is settled in IAM-10.0. |
| Platform calendar (CAL-1) | Deferred Candidate | A concrete first consumer. No calendar seam is built in RC1. It is never conflated with ScheduleDefinitions. |
| Protected lookup (PLK-1) | Deferred, dormant | A real customer requirement for lookup by a protected identifier. None exists today. |
| Personal-data erasure | Deferred | A regulatory or privacy requirement. |
| Additional Entra tenants | Deferred | An external-organization customer that needs its own tenant. |
| Systems grouping (ADR-0034), Dashboard Panels, shared Wizard, Track 5 remainder | Deferred, unchanged | As recorded in the pre-release roadmap. |
| Optimistic notification read and unread | R3 | May ship in RC1 only if it does not delay RC1. |

### 4.4 Rejected

- Broad optimistic mutation.
- A second policy engine.
- A new generic Operation or Command abstraction.
- Hard deletion of users.
- A UUID migration project.
- Sophisticated instance-to-instance federation at this stage.

### 4.5 Naming notes

- **"Calendar"** already names three deferred items in existing documents: calendar or date-period schedule authoring with a schedule designer; a module "calendar / event signal SDK"; and a UI temporal-filter convention. A business-time platform calendar would be a fourth meaning and needs its own term before anything is written about it.
- **"Schedule"** means a cron-triggered workflow start only.
- **"Service"** is a navigation-domain label and architectural language. It is not an entity.
- **"Workspace"** currently means the Connection workspace and, informally, Application views. The Workspaces direction (4.1) would be a further meaning, which is why the term is not yet frozen.
- **ResourceContext Kind.** The current ownership kinds are Shared and Application. Department is not an anticipated Kind. References to a future Department kind in the CTX design gates are historical provenance, not current direction. Any proposal for another Kind needs its own design gate and an independent ownership use case.

## 5. Program sequence (Decision)

Three tracks run in parallel after this gate, then converge.

```
RC-0  this gate (complete)
  |
  +-- Track A (security)    IAM-9 --> AUD-1 --> IAM-10 --> IAM-11
  |
  +-- Track B (experience)  QW-1 --> NAV-2 --> ACT-1 --> LST-1.3 to 1.5
  |                         LST-1.0, 1.1, 1.2 and 1.6 run alongside NAV-2
  |
  +-- Track C (platform)    SDK-8 --> ID-1 --> OPS-1
  |
RCV-1  release-candidate validation
```

| Epic | Objective | Design gate first |
|---|---|---|
| IAM-9 | Identity foundation: username, session and actor identity, user reference, user search | Yes |
| AUD-1 | Audit hardening: schema, recorder, coverage, noise, stance | Yes (short) |
| IAM-10 | Authorization hardening, permission definitions, scope-composition design | Yes |
| IAM-11 | User lifecycle and credential recovery | Yes (security) |
| QW-1 | Quick wins: drawer toggle glyph, browser titles, Runs ordering, Notification Diagnostics | No |
| NAV-2 | Navigation registry, information architecture move, icons, rail behaviour, tree primitive, typed locators | Yes |
| ACT-1 | Entity action model, toolbar, pending and re-entrancy behaviour | Yes (short) |
| LST-1 | Paging contracts, query lifecycle, list state, pager, selector, correctness fixes, teams | Yes (short) |
| SDK-8 | Module runtime correctness, metadata hardening, preview statement | Yes |
| ID-1 | Identifier hygiene | No |
| OPS-1 | Operability baseline on the supported topology | Yes |
| RCV-1 | Release-candidate validation | Checklist |

Hard dependencies: AUD-1 needs IAM-9's actor identity. IAM-10 needs AUD-1's recorder for grant diffs. IAM-11 needs all three. ACT-1 needs NAV-2's icon keys. LST-1.3 to 1.5 need IAM-9; LST-1.4 also needs IAM-11's user status and D-08.

*Sequencing clarification (product-owner ruling, 2026-10-06; [AUD-1.0 design gate](transitions/audit-hardening/aud1-design-gate.md) §3 OR-11, §26 P-3).* The hard dependencies above are unchanged. IAM-10.0 design planning may proceed independently of AUD-1. IAM-10 implementation packages that record grant changes begin once the AUD-1 contracts they need (AUD-1.1 and AUD-1.2) are delivered and verified. They do not wait for the audit timeline UI (AUD-1.5b). Every package still starts only on an explicit owner instruction (§9).

## 6. Scope amendments to Part 1

Part 1 sections H and I describe each epic and package. These amendments apply.

### 6.1 LST-1 and LST-2

**LST-1 (RC1) includes:** offset and keyset paging contracts as appropriate; a query lifecycle helper; debounce, cancellation and latest-wins for queries; no false empty state while a request is pending or unsearched rows remain; shared list-state presentation; a pager footer; `AsyncSearchSelect`; a URL-state convention; live correctness fixes; truncation disclosure; and the team membership and user-selector work from Part 1.

**LST-2 (post-RC1) owns:** column definitions; sortable headers; richer quick and advanced filter controls; default-row-action conventions; local instant pre-filtering; virtualization; continuous loading where later evidence supports it.

**Runs paging (LST-1.2), binding requirement.** Keyset pagination alone does not fix the current defect, because authorization is applied after the row cap.

- Authorization filtering must be incorporated before the page boundary wherever feasible.
- Where authorization cannot be expressed directly in the query, use a safe over-fetch and fill strategy that keeps fetching until the authorized page is full or the source is exhausted.
- Use a stable cursor such as creation time plus id.
- No exact total is required.

### 6.2 ACT-1

Adds to Part 1: the `ActionButton` changes in D-19; pending state and double-submit prevention on integration lifecycle actions; confirmation on integration Retire; the content-stays-visible rule; explicit supersession of the two lines in `management-ui-design-system.md` that exclude toolbar commands from `ActionButton`; repair or removal of the unreachable cancel-on-hover path. `EntityToolbar` renders `ActionButton`. Under D-07 the toolbar offers no direct password-set action.

### 6.3 IAM-10

IAM-10.0 must rule on six questions. Implementation scope is otherwise as in Part 1.

1. **Scope algebra.** How context composes with module under a prefix-only evaluator. Instance grants stay universal.
2. **Target-scope derivation.** Whether governed identities gain a context dimension; current ownership versus historical snapshot; which tests and rulings would be deliberately superseded.
3. **Assignment shape.** Scope on the assignment or on the role grant; the assignment key consequence; the role-provider return shape.
4. **Principal kinds.** User, ServiceAccount, Integration, Agent; the fate of the dormant actor kinds.
5. **`integration:{id}` semantics.** A containment scope, or a principal and owner identity.
6. **One list-filtering contract.** A single "authorized contexts for this principal and permission" query.

IAM-10 also adds `operational_team.manage` (D-08) to the catalog and the repair arrays, and ensures sensitivity and step-up flags apply to module-declared permissions.

### 6.4 IAM-11

Under D-07, the administrative path is initiate-reset or one-time setup only. The existing direct password-set endpoint leaves ordinary governance. The gate rules on the emergency path. Under D-04 and D-05, decommission retains username and presentation information by default.

### 6.5 AUD-1

Adds: a reserved data-access category; the rule that a searched value or any digest of it never appears in audit details; correction of the audit keyset cursor; an optional per-row audit route in the UI; timeline filter state following the LST-1 URL convention.

### 6.6 NAV-2

Adds: NAV-2.0 owns one typed locator convention, resolved to routes in the client. No new server-built href strings.

### 6.7 SDK-8

Adds: the D-24 contract in SDK-8.5; the secrets assembly in the module architecture test; module contributions stay additive. Under D-23, SDK-8 may add a worker module-selection setting if it is cheap; it does not restructure the runtime.

### 6.8 OPS-1

Scoped to the topology in section 2. Version and health work is unchanged from Part 1.

### 6.9 RCV-1

Adds: no list shows an empty state while a query is pending or rows are unsearched; a reduced-motion check on pending indicators; validation that the platform's own modules follow D-24.

## 7. Post-RC1 bodies

| Body | Kind | Depends on or triggered by |
|---|---|---|
| NAV-3 Navigation provenance | Design gate before RC1; implementation after | NAV-2 registry and typed locators |
| POL-1 Policies and data authority | Design gate | IAM-10.0 principal and scope design. Starts from the platform-notes Direction. |
| LST-2 List presentation layer | Design gate | LST-1 and ACT-1 shipped |
| MUI-1 Module UI panels and wizards | Design gate | SDK-8.0 single field schema |
| MOD-1 Module installation and coordinated restart | Design gate (security) | SDK-8 composition report, OPS-1 |
| CLI-2 Operator CLI and environment trust | Design gate (security) | A non-interactive API credential; IAM-10 principal kinds |
| SRCH-1 Platform search | Design gate | POL-1 record model; AUD-1 |
| PLK-1 Protected lookup | Trigger-based, dormant | A real customer requirement. Dedicated security gate. |
| CAL-1 Platform calendar | Trigger-based, deferred | A concrete first consumer |

## 8. Documentation state after this gate

Corrected in RC-0: federation withdrawal; the navigation rulings in ADR-0040 and ADR-0044; the "one module per process" and adapter wording in agent instructions and playbooks; IAM-8 merge status; the roadmap; index gaps for recent ADRs; several small drifts.

Deliberately left to later work, with owner:

| Drift | Owner |
|---|---|
| ADR-0012 describes ownership and break-glass behaviour the evaluator does not implement | IAM-10.0 |
| `current-architecture-tracking.md` and `.ai/playbooks/project-map.md` are stale | RCV-1, once the code has settled |
| Two files share the number ADR-0013 | Not renumbered, to avoid breaking links. Both are listed in the index. |
| IAM-8 and LFC-1 design gates have no ADR | Their gates remain the contract. Promotion to ADRs is RCV-1 documentation work. |
| `CHANGELOG.md` | OPS-1.7 |
| Module manifest spec and parameter-schema vocabulary | SDK-8.3 |
| The UI governance-read capability checks `user.read` on Role while the catalog and the APIs use `user.read` on User (found in the IAM-9.5 review; reproduction and acceptance in the IAM-9 gate §29.1) | IAM-10.0 |
| `role.manage` implies `user.manage`, `credential.manage`, `permission.assign`, `role.assign` and `auth.*` for custom roles (audit X-01; effective privileges in the IAM-9 gate §29.2). **Open. It closes only when IAM-10.2 (or the design IAM-10.0 rules in its place) is shipped and verified, with the security and product owners' disposition recorded before RCV-1 closes. Scheduling it is not a disposition. No risk acceptance has been requested or granted, and this row creates no exception to RC1 requirement 4 (§2)** | IAM-10.0, IAM-10.2; disposition at RCV-1 |
| Structured audit details for `governance.user.username.changed` (its details are free-form text; IAM-9 gate §29.3) | AUD-1 |
| A revoked former Recovery Access user remains an ordinary administrator named `breakglass` / "Recovery Access" (existing behaviour; IAM-9 gate §19; [IAM-9 closure audit](../audits/iam9-identity-foundation-closure.md) F-6) | IAM-11 (emergency-path gate) |
| The invisible validation control (`FieldValidationIndicator` renders at 0x0 but is a focusable button) and the undefined `text-message-error` class (closure audit F-9, F-10). Product-owner decision 2026-10-05 | RCV-1 accessibility baseline (requirement 8) |
| Last-administrator guards race: removing Platform Administrator, removing the last viable link and disabling the credential read an unlocked count, so two concurrent operations can leave no Platform Administrator, or none who can sign in. The Entra-primary "last Entra-independent administrator" count has the same race (found in the AUD-1.2 review; impact, evidence limits and acceptance in the [AUD-1.2 record](transitions/audit-hardening/aud1-2-identity-governance-record.md) §8.1, F-10A and F-10B). Distinct from audit N-07. **Open until the acceptance tests pass on shipped code or a disposition is recorded; an owner is not a disposition** | IAM-10 |
| A live Entra session that outlived its link's removal proves the link when an administrator re-adds it, without a fresh sign-in (pre-existing; AUD-1.2 record §8.1, F-11A). **Open; IAM-11's session revalidation rules on it** | IAM-11 |
| Operational `UserRef` adoption has no per-page split between AUD-1 and LST-1.3 (IAM-9 gate §23 says "LST-1.3 or AUD-1"; closure audit F-8) | AUD-1.0 establishes the split with LST-1.3. **Split approved by the product owner (2026-10-06)**: [AUD-1.0 design gate](transitions/audit-hardening/aud1-design-gate.md) §18 assigns each surface to AUD-1.5b, LST-1.3 or LST-1.4, or marks it as no adoption. AUD-1.0 was approved on 2026-10-06 with conditions |

## 9. What may start

IAM-9.0, QW-1 and LST-1.0 need no further roadmap work. They start when the product owner instructs. Nothing in this gate begins them.
