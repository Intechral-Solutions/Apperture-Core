# SEC-2: Configuration secret ownership and operator boundary (SEC-2.0 design gate)

| | |
|---|---|
| Date | 2026-10-13 |
| Status | **Proposed. Awaiting product-owner and architect review.** No decision in this document is approved. Every "P-" item is a proposal and every "SR-" item is a ruling the owner has not yet made (§10). |
| Baseline | `master` at `65d95f0` (PR #27, the AUD-1.4 slice 8 merge). The source was read at `fdcf4fe`, whose tree is identical to `65d95f0`. |
| Nature | Design only. No production code, test, migration or contract has changed. SEC-2.1 has not started. |
| Origin | The five findings of the [AUD-1.4 record](../audit-hardening/aud1-4-configuration-lifecycle-record.md) §19.7 that need an owner: F1, F2, U-1, U-2 and U-3. F3 to F5 and the carried items stay with their owners. |
| Inputs | [RC1 program gate](../../rc1-program-gate.md) and its [addendum](../../../audits/rc1-supplemental-reconciliation-addendum.md); the [AUD-1.0 gate](../audit-hardening/aud1-design-gate.md) (§11 vocabulary, §13 write classes); AUD-1.4 record §11 (ruling H1 and its classifier), §16.10 (the connection reference rule) and §19; the [SEC-1 gate](sec1-browser-session-request-forgery-design-gate.md) (precedent, and F12, which turned the Graph searches into POSTs); and the source cited below. Where a document and the code disagree, the code wins and the disagreement is recorded. |

**Labels.** **Code fact** means traced in source at the baseline. **Proposed (P-n)** means a design this gate recommends and the owner has not approved. **Ruling needed (SR-n)** means the owner must choose (§10). Paths are abbreviated: `API/` is `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/`, `SEC/` is `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/`, `PERS/` is `src/Apperture.Persistence/Apperture.Persistence.SqlServer/`, and `CR/` is `src/Apperture.ConnectorResolution/`.

---

## 0. Summary

Each of the five findings sits on one of two boundaries:

- **The operator-action boundary (F1, F2).** These are writes made by an authenticated principal, outside any audited unit, under a weaker permission than the effect warrants.
- **The secret-holder boundary (U-1, U-2, U-3).** A configuration row names a secret by id. Nothing on the read path proves that the row owns that secret, and one read model hands the ids out.

The source shows that four of the five reach further than §19.7 records (§2):

- **F1.** A principal with only `integration.read` can suppress an incident's reminders and escalations.
- **U-1.** There are six resolution entry points, not three, and two of them run in the Management Host.
- **U-2.** The setting provisioner ignores the secret's provider as well as its name, and clear and reset can disable a same-key secret that belongs to another integration.
- **U-3.** All five connection responses carry the references, not only `GET`.

The gate recommends four packages after SEC-2.0:

| Package | Finding | Proposed RC1 class |
|---|---|---|
| SEC-2.1 | F1 | **Release blocker** |
| SEC-2.2 | U-1 and U-3 | **Release blocker** (U-1); U-3 ships with it |
| SEC-2.3 | U-2 | **Required for RC1**, unless the owner records a risk acceptance (SR-15) |
| SEC-2.4 | F2 | Not a blocker |

F2 is a fourth package because the brief assigned it none (SR-1). IAM-10 stays separate; the interfaces the two genuinely share are listed in §4.5. SEC-2 adds no permission constant.

## 1. Scope and non-goals

**In scope:**
- The five findings.
- The enforcement points they need.
- One common resolution boundary for secret holders.
- Read-only inventories of existing rows.
- The catalog actions and contracts the fixes require.

**Not in scope:**
- **A secrets redesign.** No owner column in `SecretDescriptors`, no new protector, no Key Vault work, no audit of successful resolutions.
- **Changing who may verify a connection or search a directory** (§4.5, input to IAM-10).
- **An attention-incident reopen route.** Gate §13.1 lists one, but none exists.
- **The Entra registration's `ClientSecretRef`.** This gate did not assess it, beyond noting it as a candidate consumer of the same primitive (§9, K-3).
- F3 to F5, AUD-1.5, AUD-1.6, IAM-10, IAM-11 and LST-1.4.

## 2. Finding register (corrected against source)

| # | §19.7 statement | What the source adds (code fact) |
|---|---|---|
| F1 | The acknowledgement writes no audit row and is gated only by `integration.read` | **Confirmed.** `API/IntegrationsController.cs:426-459` authorizes only through `GetIntegrationAsync` (`integration.read`, `SEC/IntegrationManagementService.cs:91-112`). `PERS/SqlAttentionIncidentTracker.cs:169-189` makes a tracked `SaveChangesAsync` with no unit, no lock and no status check. **Also:** the write overwrites an earlier acknowledgement, acknowledges a resolved incident, and does not validate the note (the column is 1024 characters, so an over-long note is an unhandled 500). Acknowledgement suppresses reminders and escalation when the routing asks for it (`SEC/NotificationInstanceCoordinator.cs:537-543`, `:601-602`). The UI shows the button to anyone who can see the page and always sends a null note (`IntegrationScheduleDiagnostics.razor:258-267`, `:579-601`). The only test is an InMemory one |
| F2 | The probe results are written outside any unit, with `UpdatedUtc` | **Confirmed.** `PERS/SqlEntraInstanceRegistrationRepository.cs:153-193`. **Also:** each probe write also replaces the row's `rowversion`, which is the token `PATCH` checks (`API/ConfigEntraTenantsController.cs:261`). So a probe that runs while an administrator has the edit form open makes their save fail with a 409. The directory searches are POST since SEC-1B2. They need only `auth.configuration.read`, resolve the stored client secret and call Graph (`:566-612`, `:797-819`). `validate-openid` needs `auth.entra.manage` and uses no secret. The directory pages search automatically on load, and the governance UserDetail page uses the same route. `UpdatedUtc` has no reader beyond the response mapping |
| U-1 | A poisoned legacy connection resolves a foreign secret at run time and in verification | **Confirmed; the scope is wider.** The six entry points are listed in §3.3. None checks ownership. Two verifiers (Sftp and CampusNexus) copy the material loop instead of calling `CR/ResolvedConnectionMaterialBuilder.cs`. The builder also runs in the Management Host: test-connect, structure browse and the CampusNexus campus options used by the integration settings. Binding a poisoned connection and moving its ownership are not checked either. Resolution writes no audit row, and `PERS/SqlSecretResolver.cs:24,28` puts the reference GUID in its exception messages, which `DefaultConnectionResolver` passes on unmasked |
| U-2 | `ReplaceAsync` versions the stored reference without an ownership check; names are not scoped per integration | **Confirmed; stronger than stated.** `SEC/PlatformSettingSecretMaterialProvisioner.cs:30-53` checks neither the name nor the **provider**, so a connection or Entra secret would get the version. Names are `platform-setting:{key}` for both scopes (`:19-20`). `TryDisableOwnedSecretAsync` (`:80-103`), which clear and reset use, therefore cannot tell integration A's secret for key K from B's, and could disable A's. The runtime accessor (`RuntimePlane.Engine/Integration/IntegrationSettingsAccessor.cs:71-101`) and the SMTP resolver resolve any GUID. A further, narrow path: an ordinary string setting can hold any GUID, and if a module later reclassifies that key as sensitive, the text becomes the secret reference. That needs a module definition change, not an API call. Today no in-tree module declares a sensitive integration setting, so the integration-scope path is exercised only by tests |
| U-3 | `GET` of a connection returns the raw references | **Confirmed; the scope is wider.** One mapper (`API/ConnectionsController.cs:262`) feeds list, get, create (201), `PATCH` and test-connect. The UI uses the map only to show presence and to list undeclared legacy keys (`ConnectionWorkspacePresentation.cs:74-89`), and never sends it back on `PATCH`. There is no CLI consumer and no OpenAPI document |

## 3. Trust boundaries and threats

Each row states the boundary, who can cross it, the precondition, the impact, and whether exploitation needs anything beyond the API.

### 3.1 F1: attention acknowledgement

- **Boundary:** read access to an integration versus changing how it alerts.
- **Actor:** any principal with `integration.read` on the integration, such as a read-only operator role.
- **Precondition:** none beyond the permission. The incident must be active for suppression to matter, but the API does not check.
- **Impact:** the actor stops reminders and escalation on any route that sets `SuppressRemindersWhenAcknowledged` or `SuppressEscalationWhenAcknowledged`, and can overwrite a colleague's acknowledgement. No audit row records it. `AcknowledgedBy` names the actor, but it is overwritable and in no audit record.
- **RC1 requirements reached:** 4 (an escalation path from read to a change in operational behaviour) and 5 (an operator action not attributed in audit).
- **Not involved:** no secret, no notification content. The incident row holds no payload; its only free text is the operator's note.

### 3.2 F2: Entra probe results

- **Boundary:** reading the authentication configuration versus changing it.
- **Actor:**
  - Directory search: `auth.configuration.read`.
  - OpenID validation: `auth.entra.manage`.
- **Impact:**
  1. A reader rewrites the diagnostic columns and bumps `UpdatedUtc` and `rowversion` on a configuration row. This is an integrity nuisance: an administrator's concurrent edit is refused with a 409, and no configuration value changes.
  2. A reader makes the instance's application credential query Graph and receives directory names, UPNs and mail addresses. The credential never leaves the server. The Graph host is fixed by the library default, and the tenant comes from stored configuration that only `auth.entra.manage` can change.
- **Assessment:** no further exploit path was found. Reading the directory under an administrative read permission is a permission-design question (§4.5), not a vulnerability this gate fixes.

### 3.3 U-1: connection secret resolution

- **Boundary:** a connection's credential versus every other secret in the store.
- **Precondition:** a row whose `SecretRefsJson` names a secret it does not own. Since slice 5 (§16.10) no API path can create one, so it must be a pre-rule row, a hand-written row, or one left by an earlier defect.
- **Impact:** credential disclosure. The foreign secret is resolved and sent to the poisoned connection's configured destination, and not only by a worker run. Any `connection.read` holder can trigger it through test-connect.

**Resolution entry points (code fact).** None of them checks ownership.

| # | Path | Host | Trigger and permission |
|---|---|---|---|
| a | `CR/DefaultConnectionResolver.cs:41`, through `SlotBoundResolver` | Worker | A step's `ResolveSlotAsync`; the sample module's connection accessor |
| b | `CR/DefaultConnectionResolver.cs:41`, through `SEC/CampusNexusCampusesOptionProvider.cs:60` | Management | Integration settings options and validation |
| c | `CR/PlatformSqlConnectionVerifier.cs:39` | Management | `POST /api/connections/{id}/test-connect`, `connection.read` |
| d | `CR/PlatformSqlStructureBrowser.cs:129` | Management | Structure browse, `connection.read` and `connection.browse` |
| e | `Connectors.Sftp/SftpConnectionVerifier.cs:70-85` (its own loop) | Management | test-connect, `connection.read` |
| f | `Connectors.CampusNexus/CampusNexusConnectionVerifier.cs:100-115` (its own loop) | Management | test-connect, `connection.read` |

### 3.4 U-2: setting secrets

- **Boundary:** one setting holder's credential (an instance setting, or one integration's setting) versus another's.
- **Precondition:** a setting row naming a secret it does not own. No API path writes one for a sensitive setting. The reclassification case in §2 is the only route that needs no database access, and it requires a module change.
- **Impact:**
  - **Integrity of another holder's credential:** a replace adds a version to it, and a clear or reset can disable it.
  - **Disclosure to the module that reads the setting:** the runtime resolves the secret whatever owns it.
  - **Collisions built into the naming:** every integration's secret for key K has the same name, so even a correct ownership check cannot distinguish them today. This is why U-2 cannot be fixed by checking alone.

### 3.5 U-3: connection read model

- **Boundary:** a secret's identifier versus anyone who may read connections.
- **Impact on its own:** an unnecessary exposure of secret ids. Once U-1 is enforced, an id cannot be used to read material.
- **Impact combined with U-1:** the response hands a reader the foreign id. It is also the source of the GUIDs that the U-2 reclassification path would need.

### 3.6 The common pattern

In U-1 and U-2 the holder row is treated as proof of ownership. Slice 5 already rejected that for connection writes: "the proof is the secret's own stored descriptor, never the connection row". The fix is to apply the same proof on the **read** path and to make the names unique per holder. It needs no owner column.

## 4. Proposed decisions and alternatives

### 4.1 F1

| # | Proposal | Alternatives considered |
|---|---|---|
| P-1 | **Acknowledging requires `integration.update`.** It is checked before the unit, then again inside it after the integration row lock is taken. This follows the slice 7 routing pattern: `RequireIntegrationManageAsync` (`SEC/IntegrationSettingsAuthorization.cs:33-39`) and the `IntegrationRowLock.AcquireAsync` reauthorize callback (`PERS/IntegrationRowLock.cs:17-36`). The incident row is then read with `UPDLOCK` and its state re-checked under the lock | **Keep `integration.read`.** Rejected: acknowledging changes alerting behaviour. **A new `attention.acknowledge` permission.** Not proposed: it pre-empts IAM-10's catalog work. SR-2 |
| P-2 | **One W1 audited unit** writes the three acknowledgement columns and one `attention_incident.acknowledged` row. The row's actor is the unit's actor, and `AcknowledgedBy` takes the unit actor's recorded value, as in the failed-item precedent (`SEC/OperationItemAttentionService.cs:167-238`). A lost acknowledgement is verified and reconstructed from the committed row | **An AUD-1.3 extension, or the AUD-1.6 inventory** (both options in §19.7). Not proposed: neither is scheduled before RC1 and F1 reaches requirements 4 and 5 |
| P-3 | **Idempotent re-acknowledgement.** Acknowledging an active incident that is already acknowledged is a no-op: 204, nothing written, no row, and the first acknowledger, time and note are kept. Of two concurrent acknowledgements, one wins and the other is the no-op | **Last writer wins, recorded each time** (today's write, now audited). SR-3 |
| P-4 | **A resolved incident cannot be acknowledged:** 409 with code `attention_incident_not_active`, and nothing is written. Unknown incidents and incidents of another integration stay 404 | **A silent 204 no-op.** SR-4 |
| P-5 | **The note is validated.** It is trimmed, a blank note is stored as null, and a note over 1024 characters is a 400 (today it is a 500). The note is never recorded in audit details, logs or exception messages | |
| P-6 | **UI:** the Acknowledge button is shown only with `CanUpdateIntegrations`, and the client maps the 409 | |

### 4.2 F2

| # | Proposal | Alternatives |
|---|---|---|
| P-7 | **Probe outcomes are operational telemetry, not configuration.** They write no audit row and leave the registration's configuration identity untouched: no `UpdatedUtc` change and no `rowversion` change | **Convert the probes to W1 `external_attempt` rows, as connection verify does.** Not proposed: verify records a connection's operational state in the configuration family, whereas a directory search is a read whose search value may never be audited (program gate §6.5). Adding a `data_access` writer needs a gate amendment |
| P-8 | **The eight diagnostic columns move to a 1:1 `EntraRegistrationDiagnostics` table.** The migration copies the current values. A probe then never updates the registration row, so it cannot cause a 409 | **Keep the columns and stop setting `UpdatedUtc` only.** Cheaper, but SQL Server replaces `rowversion` on any update of the row, so the 409 nuisance remains. SR-8 |
| P-9 | **No change to who may search.** Directory search stays at `auth.configuration.read` or `auth.entra.manage`. That is the legitimate browsing the configuration and UserDetail pages rely on. The question is recorded as an IAM-10.0 input (§4.5) | **Require `auth.entra.manage`.** That removes reader browsing and breaks the UserDetail lookup for non-managers. SR-9 |
| P-10 | **No additional attempt evidence** is recorded for probes. The telemetry row and the response are the evidence | **A W4 telemetry action per probe.** SR-8 |

### 4.3 U-1 and U-3: the owned-resolution boundary

| # | Proposal |
|---|---|
| P-11 | **One definition of ownership per holder kind.** A value `SecretOwner(Provider, Name)` is derived from the holder: for a connection field, `ConnectionSecretOwnership.For(connectionId, field)`. Today the name format lives in `ConnectionSecretMaterialProvisioner`; it moves to this one place, so the write rule (slice 5) and the read rule cannot drift apart |
| P-12 | **Owned resolution.** A new resolver call `ResolveOwnedAsync(SecretRef, SecretOwner)` returns material only if the stored descriptor has exactly that provider and name and is usable. The SQL implementation does this in **one query** that joins `SecretDescriptors` to `SecretVersions`, so the payload of a non-owned secret is never read out of the database or unprotected. A resolver that cannot verify ownership fails closed: the Key Vault resolver outside RC1 must declare it, not resolve. **Alternative:** a descriptor `GetAsync` followed by the existing `ResolveAsync` (two round trips, no new resolver method). Rejected as the primary design because it leaves the unchecked call available to the next caller |
| P-13 | **One connection-material boundary.** `IConnectionMaterialResolver` (public, beside the existing builder) is the only way to turn a `Connection` into `ResolvedConnectionMaterial`. Paths a to d already use the builder. The Sftp and CampusNexus verifiers (e, f) receive the boundary instead of `ISecretResolver` and lose their copied loops. An identifier-based guard (the S8-1 style) fails if any production file outside the boundary builds a `SecretRef` from `Connection.SecretRefs` or hands a connection reference to `ISecretResolver` |
| P-14 | **Fail closed, identically.** Missing, another provider, another owner, another field, disabled and unreadable all give one typed refusal, `ConnectionSecretUnavailableException`. It names only the field key and never the reference, the owner or which condition failed. The verifiers and the browser keep their existing masked "secret could not be resolved" result, and a run step fails with the same fixed text. Separately, `SqlSecretResolver`'s messages stop echoing the GUID (`:24`, `:28`), a two-line hardening |
| P-15 | **Each refusal writes one W3 row**, `connection.secret.resolution_refused` (§5.1). The actor is the principal on Management paths and the system on worker runs. A failed write does not change the result, which is the W3 rule |
| P-16 | **U-3 read model.** `secretRefs` is removed from every connection response and replaced by `secretFields: [{ key, configured }]`. The list includes undeclared stored keys, which the UI needs (`ConnectionWorkspacePresentation.cs:74-78`). "Configured" means a non-blank reference is stored. Whether to also show an ownership state ("needs attention") is SR-12 |
| P-17 | **U-3 write side.** Once a client can no longer learn a reference, `PATCH secretRefs` (which replaces the whole map) loses its purpose, and it is today the only repair route. It is replaced by two field operations. `clearSecretFields: [key]` removes a reference. `secretValues[key]` on a field whose stored reference is not owned creates a new owned secret and repoints the field, and **never** touches the referenced secret. Today that request is refused; the new behaviour neither adopts nor modifies another holder's secret. Create keeps refusing `secretRefs` (400). A `PATCH` that still sends `secretRefs` is refused with a clear message (SR-13) |

### 4.4 U-2: setting secret ownership

**Assessment of the H1 classifier** (`SEC/IntegrationSettingSecretClassifier.cs`, `SEC/IntegrationSettingSecretInventory.cs`):

- **Reusable:** its evidence model (provider, name, single holder, surviving-reference scan across settings, connections and Entra) and its lenient GUID reader. Both are the right basis for the U-2 inventory.
- **Not reusable as an enforcement check:**
  - Its API is shaped for the integration delete: it takes a whole integration and assumes the overrides are being removed.
  - Its scan is a full read and is not stable under READ COMMITTED or snapshot isolation (its own remarks, `:24-31`).
  - Its notion of "owned" cannot prove which integration created a secret, because the names do not say.
- **Conclusion:** it is reused for the inventory and the one-time legacy disposition, and not for per-request checks.

| # | Proposal | Alternatives |
|---|---|---|
| P-18 | **Ownership rule.** An instance setting K is owned by provider `platform-setting` and name `platform-setting:{K}`, which is today's format, so existing instance secrets such as the SMTP password stay valid. An integration setting K of integration I is owned by a **new provider** `integration-setting` and name `integration-setting:{I}:{K}`. Because the provider differs, an integration secret can never satisfy an instance rule or the reverse. SEC-2.3 confirms that integration ids cannot contain `:`; if they can, the rule compares the parsed parts | **Keep one provider and add the integration id to the name.** That leaves a legacy integration secret (`platform-setting:{K}`) indistinguishable from an instance secret |
| P-19 | **Enforced on every write and read:** `ReplaceAsync`, clear, reset, the integration delete's retirement, the runtime accessor, and the SMTP resolver for a row-sourced reference. Replace on a reference that is not owned creates an owned secret and repoints the row, leaving the referenced secret untouched. Clear and reset disable only an owned secret. A read of a reference that is not owned fails closed through the same `ResolveOwnedAsync` (P-12), with a fixed message and a W3 row (§5.1). A module sees "setting not configured", never a foreign value | |
| P-20 | **The reclassification hazard is closed by P-19 itself.** A GUID typed into an ordinary setting does not meet the rule once the key becomes sensitive, so it is refused at read and never versioned | |
| P-21 | **Legacy integration-scope secrets** (provider `platform-setting` on an integration row) are **not adopted automatically.** The inventory lists them. Each is refused at read until an operator re-enters the value, which creates an owned secret under P-19. The legacy secret is disabled afterwards only if the H1 sole-holder test proves no other row needs it. Expected population: none outside tests (§2) | **Normalize at startup:** rename each sole-holder legacy secret to the new owner name, keeping it working across upgrade. More machinery, and it adopts on evidence that cannot prove the creating integration. SR-14 |
| P-22 | **Deployment configuration stays trusted.** A reference supplied by deployment configuration (`Notifications:Email:SmtpPasswordRef`) is not a setting row and stays outside the row ownership rule. This is documented and pinned by a test | SR-14 |

### 4.5 Shared primitives, and the IAM-10 boundary

**Genuinely shared inside SEC-2:**
- `SecretOwner`, `ResolveOwnedAsync` and the refusal shape (P-11, P-12, P-14). SEC-2.2 builds them and SEC-2.3 reuses them.
- The inventory reader. SEC-2.3 reuses H1's; SEC-2.2's rule is simpler (§6.1).

**Shared with SDK-8.3:** P-13 removes `ISecretResolver` from the connector verifiers. That is the same boundary as the addendum's S-14, which adds the secrets assembly to the module architecture test. SEC-2.2 should land first or alongside, so the test can forbid the reference outright.

**Not combined with IAM-10, but handed to IAM-10.0 as inputs:**
1. Three routes make an outbound call with a stored credential under a read permission:
   - connection verify (`connection.read`);
   - Entra directory search (`auth.configuration.read`);
   - structure browse (`connection.read` and `connection.browse`).

   RC1 requirement 4 asks for a sensitivity on every permission. Whether "initiates a call with a stored credential" warrants a distinct permission or sensitivity is IAM-10's question. SEC-2 changes none of the three.
2. P-1 uses the existing `integration.update` through the existing evaluator. If IAM-10.0 changes `integration:{id}` semantics (question 5), the one callback in SEC-2.1 follows it.
3. SEC-2 adds no permission constant.

## 5. Action catalog and authorization contracts

### 5.1 Proposed catalog actions

All follow the conventions in `Audit/AuditActionCatalogEntries.cs`:
- `IntroducedIn` is a package label and `Writer` a file name, as in the AUD-1.4 entries.
- Each has a declared schema and a typed producer in `AuditEvents.*.cs` (guard G2).
- No field is named for a secret, credential or token (the schema refuses such names).

| Action | Category | Severity | Target | Basis | Write class | Schema fields | Never recorded |
|---|---|---|---|---|---|---|---|
| `attention_incident.acknowledged` | `operations` | standard | `integration` (SR-5) | `change` | W1 | `name` (integration name snapshot), `incidentId` (Id), `conditionKey` (the existing condition-key field), `incidentStartedAt` (Timestamp), `noteProvided` (Flag) | The note text; recipients; notification content |
| `connection.secret.resolution_refused` | `secrets` | elevated | `connection` | `attempt` | W3 | `fieldKey` (Id), `path` (Enum: `run`, `verify`, `browse`, `settings_options`) | The reference, the foreign owner, which condition failed |
| `integration.settings.secret.resolution_refused` | `secrets` | elevated | `integration_setting` (`{integrationId}:{key}`, as for `.stored`) | `attempt` | W3 | `path` (Enum: `run`, `management`) | As above |
| `platform.settings.secret.resolution_refused` | `secrets` | elevated | `platform_setting` | `attempt` | W3 | `path` | As above |

**Notes on the table:**
- **Target for the acknowledgement.** The closed target vocabulary has no `attention_incident` (gate §11.1). The proposal follows the failed-item precedent, which targets the run and carries item ids as fields. The alternative is a gate amendment adding `attention_incident` to the Operations row, plus a check-constraint migration (the slice 4 `binding` precedent). That is SR-5.
- **Severity of the refusals.** Gate §11.3 makes refusals that concern secrets elevated.
- **The W3 refusals have default visibility** and no routine outcome: each one means a row that the inventory should have found.

### 5.2 Authorization matrix

| Route or path | Today | Proposed |
|---|---|---|
| `POST /api/integrations/{id}/attention-incidents/{incidentId}/acknowledge` | `integration.read`, once | `integration.update`, before the unit and again under the integration row lock (P-1) |
| `POST .../entra-tenants/{id}/directory/users` and `/groups` | `auth.configuration.read` or `auth.entra.manage` | Unchanged (P-9, SR-9); the call no longer writes the registration row |
| `POST .../entra-tenants/{id}/validate-openid` | `auth.entra.manage` | Unchanged |
| Connection verify, browse, options; worker slot resolution | Their current permissions; no ownership check | Unchanged permissions; **plus** ownership at resolution (P-12 to P-14) |
| Connection list, get, create, `PATCH`, test-connect responses | Return `secretRefs` | Return `secretFields` (P-16) |
| Setting replace, clear, reset; runtime and SMTP read | Current permissions; no ownership check | Unchanged permissions; **plus** ownership (P-19) |

### 5.3 Response and compatibility contracts

| Change | Status codes | Compatibility |
|---|---|---|
| F1 | 204 success or no-op (P-3); **403** without `integration.update`, including after an ownership move under the lock; **404** unknown or another integration's incident; **409** `attention_incident_not_active` (P-4); **400** note too long; **500** `outcome_unknown` or `committed_result_unavailable`, as the other converted routes | A behaviour change for read-only principals |
| U-3 | Unchanged | `secretRefs` is removed from responses, a breaking change to the response shape. The known consumers are the Management client DTO and the UI, both updated in the same package. A compatibility note goes in `docs/04-api/contracts.md`. Unknown external scripts are a risk (§9, K-6) |
| U-1 and U-2 refusals | A run step fails with a fixed message; test-connect returns its existing masked failure; a setting reads as not configured | No new status code |

## 6. Legacy data inventory and rollout

### 6.1 U-1 inventory (read-only)

**Form.** A read-only T-SQL script under `scripts/`, runnable by an operator with read access to the instance database. It uses `OPENJSON` over `Connections.SecretRefsJson`, joined to `SecretDescriptors`. A SQL Server test seeds every class below and pins the script's output. It writes nothing and needs no Management Host. An alternative, a Management-host diagnostic endpoint, is SR-11.

**Output.** Connection id, connection state, field key and class. For a reference with another owner, it also gives the owner implied by the foreign descriptor's name, because that tells the operator whose credential may have been sent. It never outputs material or the reference value.

| Class | Meaning | Disposition |
|---|---|---|
| `owned` | Provider `platform-connection`, name `connection:{id}:{field}` | None. Not listed |
| `dangling` | No descriptor, or disabled or revoked | Clear the field or re-enter its value. The connection already fails today |
| `unreadable` | Not a GUID | Clear the field |
| `foreign_provider` | A settings or Entra secret | **Incident.** Repair the connection, and rotate the foreign credential if the connection was ever verified, browsed or bound |
| `foreign_owner` | Provider `platform-connection` with another connection's or field's name | **Incident**, as above. The foreign connection's credential is rotated |

The disposition of every listed row is recorded in the SEC-2.2 record. An empty result on each supported instance database is the evidence for the historical assessment. **The owner names those databases (SR-11).**

### 6.2 U-2 inventory (read-only)

**Form.** H1's evidence model (§4.4), run as a read-only report over every `PlatformSettingValues` row with no running writer. It is a script, or a one-off read-only C# tool in the test or tooling tree. It is not wired into the product.

| Class | Disposition |
|---|---|
| `owned` (the P-18 rule) | None |
| `legacy_integration` (provider `platform-setting` on an integration row) | Refused at read after SEC-2.3. Re-enter the value (P-21). The legacy secret is disabled only if it is a sole holder |
| `shared` (a reference held by more than one row) | No adoption. Each holder re-enters its value, and the secret is retained |
| `key_mismatch`, `foreign_provider` | Refused at read. **Incident** for `foreign_provider` (a module may have read another holder's credential). The foreign secret is never modified |
| `ordinary_guid_text` (a non-sensitive key whose value resolves to a secret) | Reported only. Harmless until the key is reclassified, and refused by P-19 after |
| `dangling` | As today: a replace creates a new owned secret |

### 6.3 Rollout order

1. **SEC-2.2 first:**
   1. Build `SecretOwner` and `ResolveOwnedAsync`.
   2. Ship the U-1 inventory script and run it on each named instance.
   3. Dispose of any rows it lists.
   4. Then enable read-side enforcement in the same package.

   Enforcement does not wait on the inventory result: the inventory exists so operators are not surprised. U-3 and the write-side field operations (P-16, P-17) ship together, because the field operations are the repair route once the references are hidden.
2. **SEC-2.1 is independent** and can run in parallel. It touches no secret.
3. **SEC-2.3 after SEC-2.2**, reusing the primitive:
   1. New names for new secrets.
   2. The inventory.
   3. Read and write enforcement.
   4. The legacy disposition.

   The SMTP instance secret keeps working throughout, because P-18 keeps the instance name.
4. **SEC-2.4 at any point.** Its migration is additive and copies data.
5. **Upgrade documentation (OPS-1).** The upgrade runbook gains the two inventories as a pre-upgrade step for instances that existed before SEC-2.

## 7. Work packages and acceptance criteria

The rules below follow the AUD-1.4 rules for every slice:
- Real SQL Server tests run on both isolation modes, and a SQL Server suite fails rather than skips without SQL Server.
- M6 means the five obligations: audit-insert failure leaves nothing; a lost acknowledgement is verified and reconstructed; a confirmed rollback reruns once; an unknown outcome is wholly committed or wholly absent; the operation lock is not granted.
- Sentinel tests prove that a secret, a reference or a note never appears in audit details, logs, exceptions or responses.

### SEC-2.0 Design gate (this document)

| # | Criterion |
|---|---|
| A0.1 | The owner records a ruling on each SR item in §10, or defers it explicitly |
| A0.2 | The program gate's §8 drift table, or its successor, gains the SEC-2 rows with their RC1 classes (§8) |

### SEC-2.1 Attention acknowledgement (F1)

| # | Criterion |
|---|---|
| A1.1 | A principal with `integration.read` and without `integration.update` gets 403. No column changes and no audit row is written (unit and SQL) |
| A1.2 | **Ownership race:** the integration is moved, after the pre-check and before the lock, to a context where the actor lacks `integration.update`. The result is 403 with nothing written. The test asserts the server's lock view, as `IntegrationAttentionRoutingAuthorizationTests` does |
| A1.3 | A first acknowledgement commits the three columns and exactly one `attention_incident.acknowledged` row. The row's actor and `AcknowledgedBy` are the unit's actor, and its target and fields match the schema. A sentinel note never appears in the row |
| A1.4 | A repeated acknowledgement is 204 with the original acknowledger, time and note unchanged and no row. Two concurrent acknowledgements give one winner and one row (SQL race). The expected results follow SR-3 |
| A1.5 | A resolved incident is 409 `attention_incident_not_active` with nothing written. An acknowledgement that races the observation worker's resolution ends either acknowledged then resolved, or 409. The acknowledgement columns are never lost. The expected results follow SR-4 |
| A1.6 | M6 holds on both isolation modes, including a lost acknowledgement whose verified commit answers 204 with one row |
| A1.7 | A note over 1024 characters is 400; a blank note is stored as null |
| A1.8 | The UI hides Acknowledge without `CanUpdateIntegrations` and reports a 409. The existing suppression tests still pass unchanged |
| A1.9 | The closure guards are updated: the tracker's write is inside a unit, G1 and G2 pass for the new action, and `docs/04-api/contracts.md` states the new statuses |

### SEC-2.2 Connection ownership and read model (U-1, U-3)

| # | Criterion |
|---|---|
| A2.1 | All six paths of §3.3 resolve through the boundary. The identifier-based guard fails on a new production file that builds a `SecretRef` from `Connection.SecretRefs` or passes one to `ISecretResolver`. A self-test proves the guard detects it |
| A2.2 | **Real SQL Server:** a poisoned row of each class (another connection's secret, another provider, another field, dangling, unreadable), driven through worker slot resolution, test-connect for each of the three verifiers, structure browse and the campus options, is refused. A recording protector proves the foreign version is never unprotected, a recording destination proves nothing is sent, and the store snapshot is unchanged |
| A2.3 | The refusal is identical for every class. No GUID appears in the exception, log, response or audit row, including the resolver's own messages |
| A2.4 | One W3 `connection.secret.resolution_refused` row is written per refusal, with the right actor and `path`. A failed W3 write leaves the result unchanged |
| A2.5 | An owned connection resolves exactly as before on every path (regression), with one query per field |
| A2.6 | The inventory script, on a seeded catalog, returns exactly the seeded rows with the right classes. On clean data it returns nothing. It succeeds under a read-only login |
| A2.7 | The inventory has been run on each instance the owner named, and its results and each row's disposition are recorded |
| A2.8 | No connection response (list, get, create, `PATCH`, test-connect) contains a seeded reference anywhere in its serialized body. `secretFields` lists declared and undeclared keys with the right `configured` value |
| A2.9 | `clearSecretFields` and `secretValues` repair a poisoned field without knowing any reference. The foreign secret's descriptor and versions are unchanged (SQL). A `PATCH` with `secretRefs` is refused as SR-13 rules |
| A2.10 | The UI renders Set and Not set and the undeclared legacy fields from `secretFields`; its source and render tests are updated. The contracts and the compatibility note are written |

### SEC-2.3 Settings secret ownership and legacy remediation (U-2)

| # | Criterion |
|---|---|
| A3.1 | A new integration secret has provider `integration-setting` and name `integration-setting:{I}:{K}`. Two integrations setting the same key get two distinct owned secrets. Instance secrets keep their name |
| A3.2 | A replace on a reference that is not owned (another integration's secret, a connection secret, an Entra secret, a dangling one) never versions that secret: the victim's SQL snapshot is unchanged. The setting is repointed to a new owned secret |
| A3.3 | A clear or reset by integration B never disables integration A's same-key secret (SQL) |
| A3.4 | The runtime accessor and the SMTP resolver refuse a reference that is not owned. The module sees "not configured", the W3 row is written, and nothing is revealed |
| A3.5 | **Reclassification:** a foreign GUID stored as an ordinary value, after the key becomes sensitive, is refused at read and not versioned at replace |
| A3.6 | The H1 classifier and retirement accept the new names, and the existing disposition SQL suite passes with legacy and new fixtures |
| A3.7 | The inventory classifies a seeded catalog correctly and is empty on clean data. Results and dispositions are recorded for each named instance |
| A3.8 | The legacy disposition behaves as SR-14 rules (refusal and re-entry tests, or normalization tests). A configuration-sourced SMTP reference keeps working and is pinned (P-22) |

### SEC-2.4 Entra probe telemetry (F2)

| # | Criterion |
|---|---|
| A4.1 | A probe changes no configuration column, no `UpdatedUtc` and (under P-8) no `rowversion` of the registration row. An edit form opened before a probe saves without a 409 (SQL) |
| A4.2 | The migration copies existing diagnostics, and the list, overview and directory pages show the same status and summary |
| A4.3 | The closure guard reclassifies the probe writes from "excluded, unaudited" to telemetry, naming the new table |
| A4.4 | The directory-search permission behaves as SR-9 rules, with a 403 test for a principal without it |

## 8. RC1 release classification (proposed)

| Finding | Proposed class | Basis |
|---|---|---|
| F1 | **RC1 release blocker** | RC1 requirement 4 (read reaches a change in alerting behaviour) and requirement 5 (an unattributed operator action). Reachable through the API with no precondition |
| U-1 | **RC1 release blocker** until enforcement ships and every named instance's inventory is recorded | Credential disclosure across the connection boundary, triggerable by a `connection.read` holder. The precondition (a poisoned row) cannot be ruled out for pre-rule data without the inventory |
| U-3 | Not independently a blocker; **ships with SEC-2.2** | It compounds U-1, and P-17 depends on it |
| U-2 | **Required for RC1** (recommended); a release blocker unless the owner records a risk acceptance with conditions before RCV-1 | No API exploit today. But the naming collision is structural, and it becomes reachable as soon as a module declares a sensitive integration setting, which the preview SDK allows |
| F2 | **Not a blocker** | An integrity nuisance and a permission-design question. No exploit path found (§3.2) |

These classes are proposals. Program gate §1 says: "promotion needs an explicit ruling" (SR-15).

## 9. Dependencies and risk register

| # | Risk or dependency | Effect | Mitigation |
|---|---|---|---|
| K-1 | Enforcement breaks a connection or setting that "works" on a foreign secret | An operational outage after upgrade | The inventory runs first (§6.3); a refusal is explicit and audited, and repair needs no reference (P-17, P-19) |
| K-2 | An ownership check on every resolution adds a database round trip in the worker | Latency per step | One joined query that returns the payload only when the owner matches (P-12) |
| K-3 | Other holders resolve unchecked (Entra `ClientSecretRef`, deployment-configured references) | A residual of the same class | Out of scope (§1). The Entra holder is a candidate consumer of `ResolveOwnedAsync`; recorded for the owner, not assessed |
| K-4 | Worker runs on a poisoned row write a W3 row each time | Audit volume | Rows are default-visibility and expected to be about zero after disposition. Transition-only writing is the fallback (SR-10) |
| K-5 | The observation worker updates incidents without the integration lock | A race with acknowledgement | The incident row is locked with `UPDLOCK` and its state re-checked (P-1); EF writes only changed columns; A1.5 pins it |
| K-6 | Unknown external consumers of `secretRefs` (operator scripts) | A breaking response change | A compatibility note. The API is instance-local and has no published OpenAPI |
| K-7 | Read-only operators who acknowledge incidents today lose the ability | A product change | Owner ruling SR-2; the UI hides the action |
| K-8 | SDK-8.3 module architecture test (S-14) | Ordering | SEC-2.2 removes `ISecretResolver` from the connector verifiers first (§4.5) |
| K-9 | AUD-1.6 M4, which flags unaudited mutating endpoints | Overlap | SEC-2.1 removes F1 before M4. SEC-2.4 reclassifies F2 so M4 does not flag it |
| K-10 | IAM-10.0 scope and permission design | A possible change to `integration.update` semantics | SEC-2 adds no permission and uses the existing evaluator (§4.5) |
| K-11 | OPS-1 upgrade runbook | Where the inventories run | §6.3, step 5 |
| K-12 | Integration ids containing `:` | Ambiguous setting secret names | Verified in SEC-2.3 (P-18) |
| K-13 | The `IntegrationSettingSecretInventory` remark (`:~29`) still says connections accept operator references | A stale comment | Corrected in SEC-2.2 |

## 10. Owner rulings requested

None is approved. Each lists the recommendation first.

| # | Ruling | Recommendation | Alternative |
|---|---|---|---|
| SR-1 | Adopt SEC-2 with packages 2.0 to 2.4, F2 as SEC-2.4 | Adopt | Fold F2 into SEC-2.1 |
| SR-2 | Permission to acknowledge an attention incident | `integration.update`, reauthorized under the row lock (P-1) | A new permission (via IAM-10) |
| SR-3 | Repeated acknowledgement | A no-op that keeps the first (P-3) | Last writer wins, audited |
| SR-4 | Acknowledging a resolved incident | 409 `attention_incident_not_active` (P-4) | A silent 204 no-op |
| SR-5 | Target of `attention_incident.acknowledged` | `integration`, with `incidentId` as a field (§5.1) | Amend gate §11.1 to add `attention_incident` |
| SR-6 | Action name, `operations` category, `change` basis, W1, standard severity, and the §5.1 schema (no note text; `noteProvided` flag) | As proposed | Drop `noteProvided` |
| SR-7 | Probe outcomes are telemetry, with no audit row (P-7, P-10) | Telemetry | W1 `external_attempt` or W4 rows |
| SR-8 | Where the probe outcomes are stored | A separate diagnostics table (P-8) | Same table, `UpdatedUtc` only (the 409 remains) |
| SR-9 | Directory-search permission | Unchanged, as an IAM-10.0 input (P-9) | Require `auth.entra.manage` |
| SR-10 | U-1 refusal evidence | One W3 row per refusal (P-15) | Transition-only rows; or logs and run failure only |
| SR-11 | U-1 inventory form and scope | A read-only SQL script; the owner lists every instance database to run it on (§6.1) | A Management-host diagnostic endpoint |
| SR-12 | U-3 indicator | `configured` only (P-16) | Also an ownership state, "needs attention" |
| SR-13 | `PATCH secretRefs` after U-3 | Refused; replaced by `clearSecretFields` and repointing `secretValues` (P-17) | Accepted for a deprecation window |
| SR-14 | U-2 naming and legacy disposition | The P-18 names; legacy integration secrets refused until re-entered (P-21); deployment configuration trusted (P-22) | Startup normalization that renames sole-holder secrets |
| SR-15 | RC1 classes in §8 | As proposed | A risk acceptance for U-2 with recorded conditions |
| SR-16 | Sequencing | SEC-2.2 then SEC-2.3; SEC-2.1 and SEC-2.4 in parallel (§6.3) | |

## 11. What may start

Nothing. SEC-2.1 to SEC-2.4 start only on an explicit owner instruction after the rulings in §10. This gate changes no code, test, contract or catalog entry.
