# Decisions Index

Architecture decision records (ADRs) live in [docs/02-decisions/](../../docs/02-decisions/). This index links to them.

| ADR | Title |
|-----|-------|
| [ADR-0001](../../docs/02-decisions/ADR-0001-runtime-boundaries.md) | Runtime boundaries (control vs instance plane, one-module-per-process) |
| [ADR-0002](../../docs/02-decisions/ADR-0002-single-tenant-instance.md) | Single-tenant instance deployment boundary |
| [ADR-0003](../../docs/02-decisions/ADR-0003-runtime-portability.md) | Runtime portability (portable workflow model, DTFx + Durable Functions adapters) |
| [ADR-0020](../../docs/02-decisions/ADR-0020-operator-output-display-policy.md) | Operator output display policy (intent vs capture status; OperatorRunOutput) |
| [ADR-0021](../../docs/02-decisions/ADR-0021-failure-diagnostics-visibility.md) | Failure diagnostics visibility and capture semantics |
| [ADR-0022](../../docs/02-decisions/ADR-0022-declared-execution-result-contracts.md) | Declared execution result contracts (unified success/failure authoring intent) |
| [ADR-0023](../../docs/02-decisions/ADR-0023-execution-contract-author-guidance-and-validation-signals.md) | Author guidance taxonomy and lightweight validation signals for execution contracts |
| [ADR-0027](../../docs/02-decisions/ADR-0027-connector-definitions-not-platform-clients.md) | Connector definitions vs platform-managed clients (Epic 52 correction; supersedes ADR-0026) |
| [ADR-0028](../../docs/02-decisions/ADR-0028-integration-canonical-deployment-unit.md) | Integration as canonical deployment, binding, and scheduling unit (Epic 54) |
| [ADR-0036](../../docs/02-decisions/ADR-0036-extension-taxonomy-connectors-and-execution-modules.md) | Extension taxonomy: Connector Extensions and Execution Modules |
| [ADR-0035](../../docs/02-decisions/ADR-0035-connection-verifier-contributors.md) | Connection verifier contributors (`IConnectionVerifierContributor`; colocate DI with connector packages) |
| [Epic 35](../../docs/02-decisions/Epic35-Run-Inspection-Forensics.md) | Run inspection and execution forensics (`ForensicContext`, origin inference, schedule convention, UI cross-nav) |
| [Epic 73](../../docs/02-decisions/Epic73-execution-hot-path-and-host-roles.md) | Execution hot path, host roles, runtime boundary (SQL dispatch, WorkerHost vs SchedulerHost stub, module DbContext) |
| [Epic 74](../../docs/02-decisions/Epic74-CampusNexus-Student-Proof-Integration.md) | Proof module: CNS OData student sync, module-owned persistence, integration-scoped slots, manual/schedule workflow path |
| [ADR-0037](../../docs/02-decisions/ADR-0037-platform-signals-events-and-notification-system.md) | Platform signals (events/logs/telemetry), notification definitions, templates, staged rules, async delivery (accepted; Epic 77) |
| [ADR-0038](../../docs/02-decisions/ADR-0038-platform-signals-sdk-conventions.md) | Module SDK signal conventions (events/logs/telemetry authoring, unified Signals surface, fail-fast emit) |
| [Epic 77 plan](../../docs/02-decisions/Epic77-Platform-Signals-and-Notification-System-Implementation-Plan.md) | Phased implementation plan for ADR-0037 (delivered milestone) |
| [Epic 103A plan](../../docs/02-decisions/Epic103A-Platform-Signals-SDK-Stabilization-Implementation-Plan.md) | SDK stabilization plan (Signals-02–08; docs-only in 103A) |
| [ADR-0042](../../docs/02-decisions/ADR-0042-notification-to-toast-channel-model.md) | Notification-to-Toast channel model: `NotificationUserPreferences` (email/toast, resolve-at-read), Definition Toast default, delta-polling watcher (Toast-B; supersedes ADR-0037's user-preferences sketch — see its addendum) |
| [ADR-0043](../../docs/02-decisions/ADR-0043-component-catalog-read-model.md) | Component catalog is a read-model projection: `(Kind, NativeId)` identity, Origin-based eligibility, reused family authorization, delegating detail, current usage, no table/registry/`ComponentId` (Components implementation; [closure](../../docs/audits/components-catalog-closure.md)) |
| [ADR-0044](../../docs/02-decisions/ADR-0044-connection-workspace-and-browse-authorization.md) | The Connection workspace is the existing `/connections/{id}` page ("Service" is architectural language, never an entity, route, permission or type); `connection.browse` is the one new action, additive to `connection.read`; a minimal typed tool seam with a SQL structure-only browser (closed parameterized statements, no dynamic SQL, no rows); Connection lifecycle and browse audit (Services implementation; [closure](../../docs/audits/services-connection-workspace-closure.md)) |
| [ADR-0045](../../docs/02-decisions/ADR-0045-browser-session-recovery-and-request-trust.md) | Browser session recovery (one 401 interception point, one probe, one full-page sign-in, no replay), one shared return-target policy, background requests never renew the session, browser request trust (Rule U exact Origin + platform header, Rule G, Rule N; exemptions: enroll and OIDC protocol callbacks), GET safety, `POST /signout`, Development-only CORS, Secure cookies and public `ManagementUiUrl` outside Development/`Testing` (SEC-1; [closure](../../docs/audits/sec1-browser-session-security-closure.md)) |
| [Epic 104](../../docs/02-decisions/Epic104-Module-Authoring-Documentation.md) | Module authoring guide rewrite (104A) and documentation consistency closeout (104B) |
| [Epic 78](../../docs/02-decisions/Epic78-Management-UI-Modernization-Groundwork.md) | Management UI audit, page archetypes, layering/drill-in rules, pilot recommendations (documentation only) |
| [Management UI design system](../../docs/01-architecture/management-ui-design-system.md) | Epic 79: layout/status primitives, semantics, PageMessage conventions |
| [Epic 79](../../docs/02-decisions/Epic79-Management-UI-Design-System-Foundation.md) | Design system foundation (primitives, CSS, `/config` proof) |
| [Epic 80](../../docs/02-decisions/Epic80-Integration-Detail-Redesign-Pilot.md) | Integration Detail layered redesign pilot (`/integrations/{id}`) |
| [Epic 81](../../docs/02-decisions/Epic81-Run-Detail-Redesign-Pilot.md) | Run Detail layered redesign pilot (`/runs/{RunId}`) |
| [Epic 82](../../docs/02-decisions/Epic82-Control-Center-Redesign-Pilot.md) | Control Center layered console redesign pilot (`/control/center`) |
| [Epic 83](../../docs/02-decisions/Epic83-Schedules-List-Detail-Redesign-Pilot.md) | Schedules list/detail layered redesign pilot (`/schedules`, `/schedules/{id}`, `/schedules/new` layout pass) |
| [Epic 84](../../docs/02-decisions/Epic84-Notification-Definition-Authoring-Redesign-Pilot.md) | Notification definition detail/authoring layered redesign pilot (`/config/notifications/definitions/{DefinitionId}` cluster) |
| [Epic 85](../../docs/02-decisions/Epic85-Management-UI-Pilot-Consolidation.md) | Pilot consolidation and design-system hardening (Epics 80–84 learnings) |
| [Epic 86](../../docs/02-decisions/Epic86-Management-UI-State-Branch-Consistency-Sweep.md) | Management UI state branch consistency sweep (non-pilot pages; primitives only) |
| [Epic 87](../../docs/02-decisions/Epic87-Management-UI-Action-Surface-and-Operation-Feedback-Groundwork.md) | Action surface and operation feedback groundwork (inventory + guidance; no operation shell) |
| [Epic 88](../../docs/02-decisions/Epic88-Cooperative-Action-Feedback-Shell-Pilot.md) | Cooperative action feedback shell pilot (`ActionFeedback`; Run + Schedule detail) |
| [Epic 89](../../docs/02-decisions/Epic89-Management-UI-Modernization-Progress-Audit.md) | Modernization progress audit (Epics 78–88 coherence, findings, next epic) |
| [Epic 90](../../docs/02-decisions/Epic90-Management-UI-Action-Feedback-Corrective-Pass.md) | Action feedback corrective pass (COR-001–003 from Epic 89 audit) |
| [Epic 91](../../docs/02-decisions/Epic91-Operation-History-and-Action-Feedback-Groundwork.md) | Operation history and action feedback groundwork (sources of truth, durability, future contract; docs only) |
| [Epic 92](../../docs/02-decisions/Epic92-UI-Modernization-QA-and-Corrective-Stabilization.md) | UI modernization QA and corrective stabilization (Epics 78–90; state-branch fixes) |
| [Epic 93](../../docs/02-decisions/Epic93-Management-UI-Interaction-Primitive-Hardening.md) | Interaction primitive hardening (toolbar, metadata grid, section actions; Epic 93) |
| [Epic 94](../../docs/02-decisions/Epic94-Entity-Local-Recent-Activity-Panels.md) | Entity-local recent activity panels (runs/audit/notifications; existing APIs only) |
| [Epic 95](../../docs/02-decisions/Epic95-Recent-Activity-Filter-and-Drill-In-Completion.md) | Recent activity drill-in completion (`moduleId` on Runs list; audit scope copy) |
| [Epic 96](../../docs/02-decisions/Epic96-Integration-Detail-Action-Feedback-Alignment.md) | Integration Detail ActionFeedback alignment (hub mutations; toasts preserved) |
| [Epic 97](../../docs/02-decisions/Epic97-Management-UI-Visual-Density-and-Polish-Pass.md) | Visual density and polish pass (shared CSS; pilot surfaces) |
| [Epic 97 corrective](../../docs/02-decisions/Epic97-Toolbar-Affordance-Corrective-Patch.md) | Toolbar button affordance + click fix after Epic 97 QA |
| [Epic 98](../../docs/02-decisions/Epic98-Management-UI-Navigation-and-Information-Flow-Audit.md) | Navigation and information-flow audit (routes, menu gaps, future nav model) |
| [Epic 99](../../docs/02-decisions/Epic99-Route-Menu-Truth-Cleanup.md) | Route/menu truth cleanup (remove broken/demo menu entries; Config contacts) |
| [Epic 100](../../docs/02-decisions/Epic100-Management-Shell-Domain-Navigation-Restructure.md) | Shell domain labels, menu grouping, hub orientation copy |
| [Epic 101](../../docs/02-decisions/Epic101-Top-Bar-Breadcrumbs-and-Parent-Context-Links.md) | Top-bar breadcrumbs and parent-context links (route resolver, no entity API) |
| [RC1 program gate](../../docs/01-architecture/rc1-program-gate.md) | **RC1 program contract (2026-10-03):** RC1 definition, decisions D-01 to D-29, Directions, deferrals, epic sequence (IAM-9, AUD-1, IAM-10, IAM-11, QW-1, NAV-2, ACT-1, LST-1, SDK-8, ID-1, OPS-1, RCV-1) and post-RC1 bodies |
| [AUD-1.0 design gate](../../docs/01-architecture/transitions/audit-hardening/aud1-design-gate.md) | **Audit hardening contract (approved 2026-10-06 with conditions):** stable actor identity with an `external` kind (amends IAM-9 R14); attribution provenance and visible defect notices; atomic audit persistence for every required mutation, with operation-lock commit verification (supersedes the LFC-1 and CTX-3B post-commit delete audits); a classification set frozen once complete; outcome-aware routine visibility; legacy outcome provenance; integrity guard plus an optional privileged startup credential (T-B); no automatic purge; page-by-page `UserRef` ownership with LST-1.3. AUD-1.1 implemented on branch `aud-1.1-audit-foundation`, awaiting review ([record](../../docs/01-architecture/transitions/audit-hardening/aud1-1-foundation-record.md)) |
| [ADR-0046](../../docs/02-decisions/ADR-0046-external-organization-access-and-federation-withdrawal.md) | External-organization access through ordinary user identity (local guests, primary-tenant Entra guests; a second tenant is a future option); guests are confined by capability and module, not by Application, in RC1; instance federation (ADR-0005, 0006, 0008 to 0011 and the relationship leg of 0007) is withdrawn |
| [ADR-0041](../../docs/02-decisions/ADR-0041-human-readable-identity-policy.md) | Human-readable identity policy: canonical opaque id, natural key for routing where one exists, display name in UI copy |
| [ADR-0040](../../docs/02-decisions/ADR-0040-operator-information-architecture.md) | Operator information architecture (Control Center / Integrations / Business Data / Config) |
| [Management platform configuration](../../docs/03-operations/management-platform-configuration.md) | Canonical appsettings inventory, dev/deploy setup, topology examples (`deploy/config/`) |
| [ADR-0039](../../docs/02-decisions/ADR-0039-environment-startup-bootstrap-and-platform-settings.md) | Environment, startup, bootstrap, and platform settings layers (105B); Aspire local orchestration (106) |
| [Epic 105A](../../docs/02-decisions/Epic105A-Environment-Startup-Inventory.md) | Appsettings and deploy/config rationalization; startup inventory |
| [Epic 105B](../../docs/02-decisions/Epic105B-Configuration-Layers.md) | Four-layer configuration model (ADR-0039) |
| [Epic 106](../../docs/02-decisions/Epic106-Aspire-Local-Orchestration.md) | Aspire AppHost / ServiceDefaults local orchestration |
| [Epic 107](../../docs/02-decisions/Epic107-Post-Aspire-Assessment.md) | Post-Aspire reassessment ([ops detail](../../docs/03-operations/epic107-post-aspire-assessment.md)) |
| [Epic 108](../../docs/02-decisions/Epic108-File-Backed-Bootstrap-Store.md) | File-backed bootstrap store ([bootstrap CLI](../../docs/01-architecture/bootstrap-cli-invocation.md)) |
| [Epic 109](../../docs/02-decisions/Epic109-Platform-Settings-Registry-Phase1.md) | Platform settings registry Phase 1 ([architecture](../../docs/01-architecture/platform-settings-registry.md)) |
| [Epic 110](../../docs/02-decisions/Epic110-Foundation-Stabilization.md) | Foundation stabilization ([ops runbook](../../docs/03-operations/epic110-foundation-stabilization.md)) |
| [Epic 111](../../docs/02-decisions/Epic111-Platform-Settings-Operator-UI-Polish.md) | Platform settings operator UI polish |
| [Epic 112](../../docs/02-decisions/Epic112-Platform-Settings-MultiSelect-Foundation.md) | StringArray, InputKind, static MultiSelect |
| [Epic 113](../../docs/02-decisions/Epic113-Platform-Settings-Option-Providers-Design.md) | Option providers and scoped settings design ([spec](../../docs/01-architecture/platform-settings-option-providers-and-scopes.md)) |
| [Epic 114](../../docs/02-decisions/Epic114-Platform-Setting-Option-Provider-Prototype.md) | `platform.connectorDefinitions` option provider prototype |
| [Epic 115](../../docs/02-decisions/Epic115-Integration-Scoped-Option-Provider-Foundation.md) | Integration-scoped provider context foundation |
| [Epic 116](../../docs/02-decisions/Epic116-CampusNexus-Campuses-Option-Provider.md) | `campusnexus.campuses` Campus Nexus campuses option provider |
| [Epic 117](../../docs/02-decisions/Epic117-Integration-Scoped-Settings-Design.md) | Integration-scoped settings and Integration Detail settings UI design |
| [Epic 118](../../docs/02-decisions/Epic118-Integration-Settings-API-Foundation.md) | Integration-scoped settings persistence and API foundation |
| [Epic 119](../../docs/02-decisions/Epic119-Integration-Detail-Settings-UI.md) | Integration Detail settings UI |
| [Epic 120](../../docs/02-decisions/Epic120-Settings-Presentation-Demo-Gallery.md) | Settings presentation refinement and Development-gated demo input gallery |
| [IAM-2](../../docs/02-decisions/S2D-iam2-strong-authentication.md) | Strong authentication and Entra-primary local-disable policy |
| [IAM-3](../../docs/02-decisions/S2D-iam3-emergency-access-native-mfa.md) | Emergency Access via system-managed Recovery Access (`breakglass`), native TOTP MFA, recovery codes |
| [IAM-4](../../docs/02-decisions/S2D-iam4-verified-contact-and-credential-lifecycle.md) | Verified contact email, hashed account-action tokens, Apperture setup invitations, password policy, password change/reset |
