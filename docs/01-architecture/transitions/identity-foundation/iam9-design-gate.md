# IAM-9 — Identity foundation — design gate

Date: 2026-10-03
Status: **IAM-9.0 design gate. Approved by the product owner (2026-10-03) with amendments.** Section 27 records the answers to Q1 to Q3 and the amendments to the username ledger and reserved names. Every ruling is binding on IAM-9.1 onward. No IAM-9 implementation has started.
Branch at writing: `rc1-foundation` (documentation-only commit; may be cherry-picked onto the dedicated IAM-9 branch)
Baseline HEAD: `d8cc7e5` (`docs: establish RC1 program contract and roadmap reconciliation`)
Governing contract: [rc1-program-gate.md](../../rc1-program-gate.md), decisions D-01 to D-07, D-10, D-11, D-13, D-27.

This gate is the IAM-9 identity contract. It does not reopen any RC1 decision. Section 7 records that no RC1 decision needs amendment.

Labels:

- **Code fact** — verified in the repository at the baseline HEAD.
- **Ruling** — IAM-9.0 decision, binding on implementation packages.
- **Contract** — what an implementation package must build and prove.

---

## 1. Status

| Item | State |
|---|---|
| RC-0 program contract | Present and authoritative: `docs/01-architecture/rc1-program-gate.md` (commit `d8cc7e5`) |
| RC1 identity decisions D-01 to D-07 | Present in the gate §3.1 and consistent with repository reality (§7) |
| IAM-9.0 | This document. Approved with amendments (§27) |
| IAM-9.1 onward | Not started. Section 25 decomposes them |
| Product code changed by IAM-9.0 | None |

## 2. Baseline

- Branch `rc1-foundation`, HEAD `d8cc7e5`, clean working tree when the investigation started.
- RC-0 documentation is committed as its own commit (`d8cc7e5`).
- SQL Server: stock `mcr.microsoft.com/mssql/server:2022-latest` in AppHost (`src/Apperture.AppHost/AppHost.cs:33-36`) and LocalDB for integration tests (`tests/Apperture.IntegrationTests/ManagementHost/ManagementGovernanceTestConstants.cs:10-11`). Neither sets a collation, so both use the case-insensitive server default.
- Migrations are applied in-process at host start by `PersistenceMigrationHostedService` (`src/Apperture.Persistence/Apperture.Persistence.SqlServer/PersistenceMigrationHostedService.cs:48`), which calls `MigrateAsync`.

## 3. Repository evidence

Paths are relative to the repository root. `CP.Sec` means `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/`. `Api` means `src/Apperture.ControlPlane/Apperture.Management.Api/`. `Host` means `src/Apperture.ControlPlane/Apperture.Management.Host/`. `UI` means `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/`.

| Area | Primary evidence |
|---|---|
| Domain user | `src/Apperture.Core/Apperture.Core.Domain/Identity/User.cs:15-25` |
| Identity link | `src/Apperture.Core/Apperture.Core.Domain/Identity/IdentityLink.cs`, `IdentitySource.cs` (`Entra = 0`, `Local = 1`) |
| Persistence | `UserEntity`, `IdentityLinkEntity`; `AppertureDbContext.cs:674-746` |
| Identity-link uniqueness | Migration `20260909180000_Iam5_EntraTenantScopedIdentityUniqueness` |
| Repository | `src/Apperture.Persistence/Apperture.Persistence.SqlServer/SqlUserRepository.cs`; interface `src/Apperture.Core/Apperture.Core.Application/Services/IUserRepository.cs` |
| Local username normalization | `LocalAuthHelpers.NormalizeUsername` in `CP.Sec/FirstAdminBootstrapService.cs:100-103` |
| Local session | `CP.Sec/LocalSessionIdentity.cs:18-25`; `Api/Controllers/AuthController.cs:466-533`, `539-622`, `706-726` |
| Legacy break-glass session | `AuthController.cs:628-704` |
| Entra sign-in | `Host/EntraOpenIdConnectOptionsConfigurer.cs:49-216`; `CP.Sec/EntraTokenClaims.cs`; `CP.Sec/EntraIdentityAssociationService.cs` |
| Entra access requests | `CP.Sec/EntraAccessRequestAuthentication.cs`; `CP.Sec/EntraAccessRequestService.cs` |
| Entra provisioning | `CP.Sec/UserProvisioningService.cs:53-249` |
| Step-up | `Host/ManagementAuthEndpoints.cs:89-140`; `CP.Sec/EntraStepUpService.cs:69-145`; `CP.Sec/AuthenticationAssuranceClaims.cs` |
| Actor mapping | `CP.Sec/ClaimsPrincipalToActorMapper.cs:65-97`; `CP.Sec/HostPrincipalContext.cs:21-26` |
| Principal resolution | `CP.Sec/SqlPrincipalToUserResolver.cs:30-90` |
| Recovery Access | `src/Apperture.Core/Apperture.Core.Application/Services/EmergencyAccessIdentity.cs`; `CP.Sec/EmergencyAccessService.cs`; `CP.Sec/NativeMfaService.cs:51-56`; ADR `docs/02-decisions/S2D-iam3-emergency-access-native-mfa.md` |
| Bootstrap | `CP.Sec/FirstAdminBootstrapService.cs:31-87`; `CP.Sec/BootstrapPlatformAdminAssignment.cs:36-164`; `CP.Sec/DevTestUserBootstrapHostedService.cs` |
| Governance API | `Api/Controllers/UsersController.cs`; `CP.Sec/UserManagementService.cs`; `Api/Controllers/UsersEntraProvisioningController.cs` |
| SEC-1 | [sec1-browser-session-request-forgery-design-gate.md](../security/sec1-browser-session-request-forgery-design-gate.md) §3, D5; ADR-0045 |
| Human-readable identity policy | `docs/02-decisions/ADR-0041-human-readable-identity-policy.md` |

## 4. Current-state identity model

### 4.1 Users and links

**Code facts.**

| Fact | Where |
|---|---|
| `User` has `Id`, `DisplayName`, `IdentityLinks`, `AssignedRoleIds`, `IsBreakGlassEligible`, time zone, avatar, `EmailAddress` ("never an authentication key"), `EmailVerifiedAtUtc`, `PendingEmailAddress`. There is **no username and no status**. | `User.cs:15-25` |
| `Users.Id` is `nvarchar(64)`; ids are `Guid.NewGuid().ToString("N")[..24]` (24 lowercase hex characters) on every creation path | `UserEntity`; every path in §4.4 |
| `Users.DisplayName` is `nvarchar(256)` with no index | `AppertureDbContext.cs:674-695` |
| `UX_Users_EmergencyAccess`: unique `IsBreakGlassEligible` filtered `= 1`, so at most one Recovery Access user | `AppertureDbContext.cs:691-694` |
| `IdentityLinks.ExternalId` is `nvarchar(256)`: the Entra `oid`, or the normalized local username | `IdentityLink.cs`, `IdentityLinkEntity` |
| `UX_IdentityLinks_Local_Source_ExternalId`: unique `(Source, ExternalId)` filtered `[Source] = 1` | `AppertureDbContext.cs:737-744` |
| `UX_IdentityLinks_Entra_Source_TenantId_ExternalId`: unique `(Source, TenantId, ExternalId)` filtered `[Source] = 0 AND [TenantId] IS NOT NULL`; check `CK_IdentityLinks_EntraRequiresTenant` | same; migration `Iam5_EntraTenantScopedIdentityUniqueness` |
| Nothing in the model enforces one Local link per user | `AppertureDbContext.cs:721-746`; `SqlUserRepository.AddIdentityLinkAsync:290` checks only global key existence |
| No column or index sets a collation. Local uniqueness across case depends on the implicit CI server default plus app lower-casing | repository-wide search; `SqlUserRepository.cs:71,345,451` compare with `==` |
| `LocalAuthHelpers.NormalizeUsername` is `Trim().ToLowerInvariant()` with no format, character or length rule | `FirstAdminBootstrapService.cs:100-103` |
| Email-shaped local usernames are a real pattern (`jane@contoso.com` in tests) | `tests/Apperture.UnitTests/Governance/EntraProvisioningDirectoryServiceTests.cs:109` |

### 4.2 Sessions

| Path | Application-cookie claims today | Where |
|---|---|---|
| Local password, with or without native MFA | `NameIdentifier = normalizedUsername`, `Name = DisplayName`, `identity_source = Local`, plus assurance claims after MFA | `LocalSessionIdentity.cs:18-25`; `AuthController.cs:706-726` |
| Recovery Access | Same ordinary local path. MFA is always required (`NativeMfaService.cs:51-56`). No distinguishing claim | `AuthController.cs:514,528` |
| Legacy break-glass endpoint (off by default) | Local claims plus `http://schemas.apperture/break_glass = 1` | `AuthController.cs:628-704` |
| Entra OIDC | The **whole validated id_token principal** (`MapInboundClaims = false`, so `oid`, `tid`, `sub`, `name`, `preferred_username`, …) plus `identity_source = Entra`. No `NameIdentifier`. `http://schemas.apperture/*` claims from the token are stripped first (SEC-1 I-1) | `EntraOpenIdConnectOptionsConfigurer.cs:124-216` |
| Local step-up re-issue | `LocalSessionIdentity.Create(user.DisplayName, localLink.ExternalId)` plus assurance | `EntraOpenIdConnectOptionsConfigurer.cs:311-341` |
| Entra access request | Separate scheme and cookie (`.Apperture.EntraAccessRequest`, 20 min). Not a platform user. | `EntraAccessRequestAuthentication.cs` |
| Pending MFA ticket | Carries user id and normalized username | `AuthController.cs:521` |

There is no `OnValidatePrincipal`, security stamp or session store anywhere in `src`. SEC-1 D5 explicitly defers revalidation.

### 4.3 Actor and principal resolution

- `HostPrincipalContext` maps `HttpContext.User` with `ClaimsPrincipalToActorMapper.ToActor`. It never consults the database.
- `ToActor` picks the actor id as `oid` → `NameIdentifier` → `sub` → `Name` → `preferred_username`. So the persisted actor is the **local username** for local and Recovery Access sessions and the **Entra oid** for Entra sessions.
- `SqlPrincipalToUserResolver` turns that actor back into a `UserId` on every request: Entra by `(tid, oid)`, Local by username. With no source claim it uses tenant + oid if a tenant is present, otherwise it treats the id as a local username. It writes `LastAuthenticatedAt` (throttled to one minute) on every call and has no cache.
- Callers include `RbacAuthorizationPolicyEvaluator.cs:48` (every authorization check), `/api/auth/me`, account, governance, step-up and link endpoints.
- A missing user resolves to null. `/me` returns 403 and authorization denies.

### 4.4 User-creation paths

All go through `IUserRepository.AddAsync` (`SqlUserRepository.cs:89`).

| # | Path | Link created | Display name |
|---|---|---|---|
| 1 | Admin create: `UserManagementService.CreateUserAsync` (`:376-456`) via `UsersController:167` | Local, normalized; rejects `breakglass`, rejects Entra | Request |
| 2 | Entra directory provisioning: `UserProvisioningService.ProvisionFromEntraDirectoryAsync` (`:53-161`) | Entra, pending, with tenant; Graph snapshots | Graph display name |
| 3 | Access-request approval: `UserProvisioningService.ProvisionFromAccessRequestAsync` (`:163-249`) | Entra, proven, with tenant; UPN and mail snapshots | Snapshot or "Microsoft user" |
| 4 | First admin: `FirstAdminBootstrapService.TryCreateFirstAdminAsync` (`:31-87`) | Local only | Request |
| 5 | Config bootstrap admins: `BootstrapPlatformAdminAssignment` (`:36-164`) | Entra (tid + oid only) or Local | Default `Bootstrap administrator ({externalId})` |
| 6 | Recovery Access: `EmergencyAccessService.ProvisionAsync` (`:114-160`), via CLI `EmergencyCli.cs:139-164` | Local `breakglass` | `Recovery Access` |
| 7 | Development test user: `DevTestUserBootstrapHostedService` | Local | Option or raw username |
| 8 | Browser-test seeding: `tests/Apperture.BrowserTests/Infrastructure/HarnessSeeding.cs:20,73-77` | Local (**not normalized**) and proven Entra | Test values |

There is also one existing **rename** path. `EmergencyAccessService.EnsureCanonicalIdentityAsync` (`:163-190`) calls `SqlUserRepository.ApplyCanonicalLocalIdentityAsync` (`:140-173`). Designating an ordinary Platform Administrator as Recovery Access renames their local link to `breakglass` and their display name to `Recovery Access`. It audits `previousUsername=…`. Revoke (`:193-213`) clears the designation but leaves `breakglass` on the same row. A later designation of a different user then fails with `emergency.access.username_conflict`.

### 4.5 Persisted actor strings

Every human-initiated control-plane write takes `actor.Id.Value` from `IPrincipalContext`, so it stores the session's provider key, not a user id.

| Table.Column | Length | Value today | Writer |
|---|---|---|---|
| `AuditLogs.Actor` (required) | 256 | Username, oid, user id (Recovery Access designation and blocked-unsafe audits), or a literal | `SqlAuditService.cs:29`, every audit writer |
| `RuntimeControlRequests.RequestedBy` | 256 | Mapper id | `RuntimeControlService.cs:66,74` |
| `WorkflowExternalControlRequests.RequestedBy` | 256 | Mapper id or null | `WorkflowExternalControlService.cs:178,189` |
| `PlatformSettingValues.UpdatedByPrincipalId` | 128 | Mapper id | `PlatformSettingsService.cs:185,300`; `IntegrationSettingsService.cs:198,321` |
| `EntraAccessRequests.ReviewedByUserId` | 64 | Mapper id (**misnamed**) | `EntraAccessRequestService.cs:293`; `UserProvisioningService.cs:199,249,304` |
| `AttentionIncidents.AcknowledgedBy` | 64 | Mapper id | `IntegrationsController.cs:407-410` |
| `OperationItemAttentionAcknowledgements.AcknowledgedByUserId` / `ReopenedByUserId` | 128 | Mapper id (**misnamed**) | `OperationItemAttentionService.cs:164-165,292-293` |
| `OperationItemRetryAttempts.RequestedByActorId` | 128 | Mapper id or null | `OperationItemRetryService.cs:181,195,219` |
| `NotificationDefinitions.CreatedByActorId` / `UpdatedByActorId` | 64 | Mapper id, or null for seeds | `NotificationDefinitionService.cs:276-279` |
| `NotificationSubscriptions.CreatedByActorId` / `UpdatedByActorId` | 64 | **Mixed**: mapper id (admin) or user id (self-service) | `NotificationSubscriptionService.cs:289-292`; `OperatorNotificationSubscriptionService.cs:60-76` |
| `NotificationSubscriptionUserOverrides.CreatedByActorId` | 64 | Mixed, as above | `SqlNotificationSubscriptionStore.cs:164` |

Already user ids (no migration needed): `NotificationUserPreferences.UserId` and `.UpdatedByActorId`, `NotificationUserInboxItems.PrincipalUserId`, `OperationalTeamMembers.PrincipalUserId`, `ManagedContacts.LinkedUserId`, `EntraAccessRequests.ProvisionedUserId`, and every `UserId` foreign key.

Not human actors (left alone): `RuntimeControlRequests.AppliedBy`, `WorkflowExternalControlRequests.AppliedBy`, `*StartRequests.ClaimedBy`, `ClaimedByHostId` (worker or host ids); `SecretAudit.Actor` (`"system"` provider); `*CancelRequests.RequestedBy` (always null); `PlatformEvents.ActorId` (never set).

Literal actor values in use: `"anonymous"` (`AuthController.cs` failure paths, `BootstrapController.cs:149`, `ForgottenPasswordService.cs:47`), `"system"` (fallbacks in `AuditController.cs:125`, `RecoveryController.cs`, `NotificationDefinitionService.cs:296`, `WorkflowRetryService.cs:122`, `ModuleCompositionBootstrapHostedService.cs:61`), `"recovery"` (`RealBootstrapRecoveryService.cs:87,115,164`), `"emergency-cli"` (`EmergencyAccessService.CliActorId`), and worker ids (default `"worker"`). None is centralized.

The only code that relied on "CreatedByActorId equals UserId means self-service" is two applied migrations (`20260922075738_TstB1_NotificationTogglePreferences`, `20260922102920_TstB2_RetireLegacyEmailOptOutOverrides`). No runtime code depends on it.

### 4.6 Read and UI surfaces

- `GET /api/users` pages by `skip`/`take`, ordered by `Id`, excludes Recovery Access (`SqlUserRepository.cs:257-265`). There is no search.
- `UserListDto`, `UserDetailDto`, `MeResponse` and `RoleAssignmentHolderDto` have no username.
- The audit read side returns `Actor` verbatim and `AuditTimeline.razor:164` renders it in monospace.
- There is no reusable user-display component. `AccountAvatar` renders only the signed-in operator. The only tooltip is `SettingInfoAffordance`, which uses the `title` attribute and is not shown on focus or touch.
- Pickers: the notification "principal" picker is a native `<select>` of the first 200 users by id, labelled `DisplayName (UserId)`. Operational team members are typed as raw ids in a textarea.

## 5. Problems being solved

1. Entra-only users have no short human identifier. Local users have one only by accident of the link model.
2. One person can appear under three actor values: username, oid and user id.
3. Three columns named `…UserId` hold usernames or oids.
4. Renaming a local username, including the existing Recovery Access canonicalization, breaks resolution of live sessions and of historical actor strings.
5. Retired usernames are reusable today. After canonicalization, a new user can take the old name and inherit its history.
6. Raw user ids are rendered inline on governance and operational pages.
7. There is no safe user search for selectors.
8. Local username uniqueness across case rests on an undeclared server collation.

## 6. Explicit non-goals

- No user status, disable, decommission, session version or revocation (IAM-11).
- No credential recovery changes; the direct password-set endpoint is untouched (IAM-11, D-07).
- No sign-in throttling or timing equalization (IAM-11, §21).
- No username-based URLs. Routes keep opaque ids.
- No audit schema change. `ActorKind`, `ActorDisplay` and the audit actor backfill belong to AUD-1.1 (§17).
- No context-scoped identity or usernames (D-10, D-11).
- No change to the Entra identity key, tenant model or additional tenants (D-27).
- No change to cookie name, lifetime, `SameSite`, scheme names, return-target policy or request-trust middleware (SEC-1).
- No whole-product `UserRef` adoption. Only Governance adopts it in IAM-9 (§23).
- No new authorization model. Search uses `user.read`.

## 7. Approved RC1 decisions inherited by IAM-9

| ID | Decision | Repository fit |
|---|---|---|
| D-01 | Username is a first-class attribute of every user | Implementable. Requires generation for Entra-only users (§10) |
| D-02 | Stable user id is the session and audit identity | Implementable. The resolver already runs per request; the cookie and mapper change (§12, §13) |
| D-03 | Retired usernames are never reused | Implementable. Interpreted, by product-owner ruling, as **never reassigned to a different user**: a normalized username is permanently bound to one user id, and that user may reactivate it. Needs a username ledger (§9.6). Recovery Access already keeps `breakglass` on one row after revoke, so it is compatible |
| D-04, D-05 | Decommission retains id, username and presentation; erasure is separate | IAM-9 keeps username on the `Users` row and never frees it, so IAM-11 needs no extra structure |
| D-06, D-07 | Recovery permission and initiate-only reset | Untouched. Username rename does not touch credentials |
| D-10, D-11 | No context-scoped IAM in RC1 | Respected: usernames are instance-wide |
| D-13 | Audit gains stable actor identity | IAM-9 provides the user id; AUD-1 owns the audit schema |
| D-27 | No additional mapped Entra tenants in RC1 | Used in §17 to bound historical oid resolution safely |

**Ruling.** No RC1 decision needs amendment. No accepted security invariant is violated by the RC1 direction.

## 8. IAM-9.0 rulings (summary)

| # | Question | Ruling | Section |
|---|---|---|---|
| R1 | Format | 3 to 64 characters; ASCII `a-z`, `0-9`, `.`, `-`, `_`; starts with a letter; ends alphanumeric; no consecutive separators; not the 24-hex user-id shape | 9.1 |
| R2 | Normalization | Trim, then lower-case invariant; then validate. Stored form is the canonical form | 9.2 |
| R3 | Case and uniqueness | Case-insensitive. Column declares an explicit CI collation; unique index plus ledger primary key | 9.3 |
| R4 | Reserved names | Identity and actor names only (`breakglass`, `system`, `anonymous`, `recovery`, `emergency-cli`, `worker`); no route words; no reserved prefixes | 9.4 |
| R5 | Entra generation | UPN local part → mail local part → display name → `user` | 10 |
| R6 | Collisions | `-2` to `-20`, then `-` plus six random characters, five attempts; database is the arbiter | 10.3 |
| R7 | Rename | Approved (Q1). `user.manage` only, audited, no session effect. The old name stays bound to the same user; that user may reclaim it later. Ships in IAM-9.5 | 9.5 |
| R8 | Retired names | `UsernameLedger` table: every name ever assigned, primary key on the name, permanently bound to one owning user id. Never assignable to another user | 9.6 |
| R9 | Backfill | Local verbatim (grandfathered, approved in Q2), then generated for Entra-only; one deterministic order; migration barrier | 20 |
| R10 | Local link | `Users.Username` is authoritative; the Local link `ExternalId` mirrors it in the same transaction | 11 |
| R11 | Session claims | `NameIdentifier = UserId` on every application session, plus a session-format claim | 12 |
| R12 | Old cookies | One-time forced sign-out through a format check; no compatibility bridge | 12.4 |
| R13 | Resolution | Session user id → `Users` by id → session-link binding check → user | 13 |
| R14 | UserReference | `UserId`, `Username`, `DisplayName` now; `Status` and avatar reserved for later additive fields | 14 |
| R15 | Presentation | Compact shows username; rich shows display name with the username visible beside it. No hover-only identity | 15 |
| R16 | Search | `GET /api/users?q=`; username prefix or display-name substring; `user.read`; take ≤ 100; no email or UPN | 16 |
| R17 | Actor migration | IAM-9.2a rewrites the non-audit columns; AUD-1.1 backfills audit rows into new columns without rewriting `Actor` | 17 |
| R18 | Unresolvable actors | Left verbatim; rendered as an unrecognized actor with the original string; never fabricated users | 18 |

## 9. Username contract

### 9.1 Format (R1)

**Ruling.** A newly assigned username must satisfy all of these:

| Rule | Value |
|---|---|
| Length | 3 to 64 characters |
| Characters | ASCII lowercase letters `a-z`, digits `0-9`, and the separators `.` `-` `_` |
| First character | A letter |
| Last character | A letter or digit |
| Separators | Never two in a row |
| Whitespace | None anywhere after trimming |
| Unicode | Not allowed. Non-ASCII input is rejected, not transliterated, for typed names. Generation transliterates (§10) |
| User-id shape | Must not match `^[0-9a-f]{24}$`, so a username can never be mistaken for a user id in mixed historical data |
| Reserved prefixes | None |

`@` is excluded. Email addresses as usernames copy personal data into URLs, audit and support conversations, and the domain is already known for Entra users.

Reference expression for implementers:

```
^(?=.{3,64}$)(?![0-9a-f]{24}$)[a-z](?:[a-z0-9]|[._-](?=[a-z0-9]))*$
```

The format is chosen for operator display, typing and support. Every allowed character is an RFC 3986 unreserved character, so a conforming name needs no URL encoding. That property holds only for names assigned under this policy, not for grandfathered names.

**Grandfathering (approved, Q2).** Existing local usernames that fail this format are **kept verbatim** by the backfill (§20.2), because the local link key is the sign-in name and changing it would lock users out. Tests already use `jane@contoso.com`, so the pattern is real.

The contract is:

- Every user has exactly one canonical username.
- Every **new assignment** must satisfy the current `UsernamePolicy`. Creation, generation, rename and reclaiming a retired name (§9.5) are all new assignments.
- A grandfathered username remains a valid identity even though it does not satisfy the current assignment policy. Nothing changes a person's sign-in name merely to satisfy the grammar.
- A grandfathered username changes only through a deliberate administrator rename, and the new name must satisfy the current policy.
- The format rules describe what may be **assigned**, not what every stored value looks like. No code may assume that every stored `Users.Username` matches the expression above, is reserved-word-free, or is safe as a URL path segment. Display, search, sign-in and the ledger must accept any stored canonical value.
- Username-based routing remains deferred. Its design must explicitly handle grandfathered usernames before relying on route-safe usernames.

### 9.2 Normalization (R2)

**Ruling.**

1. Trim leading and trailing whitespace (`string.Trim()`).
2. Lower-case with `ToLowerInvariant()`.
3. Validate the result against §9.1.

The stored value is the canonical form. Comparison is ordinal on canonical forms in the application and case-insensitive in the database. Because new names are ASCII, no Unicode normalization form is needed. Grandfathered names were already produced by step 1 and 2 of the existing helper.

`LocalAuthHelpers.NormalizeUsername` stays as the single normalization function. IAM-9.1 adds a `UsernamePolicy` (validation, reserved names, generation) next to it in `Apperture.Core.Application` so that every creation path and the CLI share one rule. Sign-in keeps normalizing input with the same helper.

### 9.3 Case sensitivity and database uniqueness (R3)

**Code fact.** No collation is declared. Today `Alice` and `alice` collide only because the app lower-cases and the default server collation is case-insensitive.

**Ruling.**

- `Alice` and `alice` are the same username.
- `Users.Username` and `UsernameLedger.Username` declare an explicit collation, `Latin1_General_100_CI_AS`, through EF `UseCollation`. Uniqueness therefore stays case-insensitive even on a server or Azure SQL database created with a case-sensitive default.
- There is no separate normalized column. `Username` itself is canonical.
- Unique indexes: `UX_Users_Username` on `Users.Username`, and the primary key on `UsernameLedger.Username`.
- The database constraint is the final authority for every assignment path.
- `IdentityLinks` collation is **not** changed in IAM-9, because the Entra key columns depend on it and the Local mirror (§11) is already covered by the `Users.Username` constraint.

**Contract.** A real SQL Server test must insert `alice`, then attempt `ALICE` directly through SQL (bypassing the application), and observe error 2601 or 2627 on both `Users` and `UsernameLedger`. It must also assert the declared collation through `sys.columns`.

### 9.4 Reserved usernames (R4)

**Code fact.** `breakglass` is the only reserved name (`EmergencyAccessIdentity.IsReservedLocalUsername`), enforced on create, link add, first admin, bootstrap and the dev user.

**Ruling.** The reserved list for new assignments is:

| Name | Reason |
|---|---|
| `breakglass` | Recovery Access canonical username (existing) |
| `system`, `anonymous`, `recovery`, `emergency-cli`, `worker` | Literal actor values persisted today (§4.5). Reserving them stops a person being confused with a non-human actor in historical displays |

No route words are reserved (product-owner amendment). Username URLs are deferred and their route namespace is not designed. When username routing is designed, it handles static route segments and grandfathered names deliberately. The reserved vocabulary is not expanded for hypothetical routing.

`admin` is **not** reserved. It is the most natural first-admin name.

The list lives in one place, `UsernamePolicy.ReservedNames`, and `EmergencyAccessIdentity.IsReservedLocalUsername` delegates to it for `breakglass`. Reservation applies to new assignments, including reclaiming a retired name. An existing user who already holds `system` keeps it as a grandfathered name, and the backfill report lists such users.

### 9.5 Rename policy (R7)

**Ruling (approved, Q1).**

| Question | Ruling |
|---|---|
| Are active usernames renamable? | Yes, by an administrator, in RC1. Generated Entra names and grandfathered names otherwise have no correction path |
| Who? | Callers with `user.manage`. No self-service rename in RC1 |
| Step-up? | No. Rename changes no credential and no authorization |
| Recovery Access? | Refused with the existing 409 `UnsafeOperation` pattern. Only canonicalization assigns `breakglass` |
| Audited? | Yes: `governance.user.username.changed`, details `previousUsername` and `newUsername`. Usernames are not secret |
| Local link | `ExternalId` changes to the new name in the same transaction (§11) |
| Sessions | Unaffected, because sessions are keyed by user id after IAM-9.2. Rename ships only after IAM-9.2 |
| Historical displays | Render the **current** username, because history stores the user id. Original legacy strings stay where AUD-1 keeps them |
| Old username | Retired in the ledger and **permanently bound to the same user id**. It can never be assigned to a different user |
| Target is another user's current or retired name | Refused: 409 `username_taken` |
| Target is one of this user's own retired names | Allowed (**reclaim**). The current name is retired and the historical ledger entry is reactivated in one transaction (§9.6). The target must satisfy the current policy, so a reserved name or a grandfathered non-conforming name cannot be reclaimed |
| Credentials and authorization | Unchanged. Rename touches no password, MFA, token, role or grant |
| Password | Not re-evaluated against the new name |
| Concurrency | `Users.RowVersion`, the ledger primary key, and the one-current-name-per-user index |

The service method (`UsernameService.RenameAsync`) ships in IAM-9.1, because Recovery Access canonicalization is a rename and must use it. The administrator API and UI ship in IAM-9.5, after IAM-9.2 has made sessions user-id based.

**Recovery Access canonicalization** becomes a system rename through the same service. The previous name is retired (still bound to that user), `breakglass` is assigned, and the existing `Canonicalized` audit keeps its details. Revoke keeps today's behaviour: `breakglass` stays on the same row. `breakglass` is reserved, so it is never assigned or reclaimed through ordinary governance; only provision and canonicalization assign it. After a revoke, the former holder may be renamed by an administrator and may reclaim their own previous personal name.

### 9.6 Retired username reservation (R8)

**Ruling.** Add one table, `UsernameLedger`:

| Column | Type | Notes |
|---|---|---|
| `Username` | `nvarchar(256)`, collation per §9.3, **primary key** | Every name ever assigned |
| `UserId` | `nvarchar(64)`, FK to `Users`, `ON DELETE RESTRICT` | Owner, forever |
| `AssignedAtUtc` | `datetimeoffset` | |
| `RetiredAtUtc` | `datetimeoffset` null | Null means current |
| `RetiredReason` | `nvarchar(32)` null | `rename`, `canonicalization`, `backfill_previous`. Cleared on reclaim |

Filtered unique index `UX_UsernameLedger_Current_UserId` on `UserId` where `RetiredAtUtc IS NULL` guarantees one current name per user.

**Ledger semantics (product-owner amendment).**

- A normalized username is permanently bound to **one** user id. A ledger row's `UserId` is never updated.
- A retired name cannot be assigned to another user. Any assignment for a different user collides on the primary key.
- The owning user may reclaim a retired name. In one transaction: set `RetiredAtUtc` on the current row, then clear `RetiredAtUtc` and `RetiredReason` on the target row with `WHERE Username = @name AND UserId = @self AND RetiredAtUtc IS NOT NULL`, then update `Users.Username` and the Local link. Zero rows affected means the name is not this user's, and the request is refused.
- `AssignedAtUtc` keeps the first assignment time. Reclaim history lives in the audit row, not the ledger.
- Historical attribution is stable because every name a user has held resolves to the same user id.
- Recovery Access follows its canonicalization rules (§9.5, §19).

Why this shape:

- A single primary key binds each name to one owner across active and retired names. A separate retired table could not share one constraint with `Users`.
- Inserting the ledger row is the concurrency arbiter for generation and for rename to a new name. Error 2627 means "taken, try the next candidate" for generation, and `username_taken` for rename. Reclaim is arbitrated by the owner-conditioned update plus the one-current-name index.
- Failed provisioning reserves nothing, because the ledger insert rolls back with the user insert.
- Decommission (IAM-11) keeps the `Users` row and its current ledger row, so the name stays reserved with no extra work.
- The ledger also resolves historical actor strings after a rename (§17).

`Users.Username` remains a denormalized copy of the current ledger row for joins, ordering and search. One repository method writes both in one transaction, and a SQL test asserts they agree.

No alias routing, no history UI and no username-change timeline are added.

## 10. Entra username-generation contract (R5, R6)

### 10.1 Sources, in order

1. The local part of the UPN, unless the UPN contains `#EXT#` (guest accounts carry the home email inside the UPN).
2. The local part of the mail address.
3. The display name.
4. The literal `user`.

Inputs by path:

| Path | UPN | Mail | Display name |
|---|---|---|---|
| Directory provisioning | Graph `userPrincipalName` | Graph `mail` | Graph `displayName` |
| Access-request approval | Request `UserPrincipalNameSnapshot` (from `preferred_username`) | `MailSnapshot` | `DisplayNameSnapshot` |
| Config bootstrap Entra admin | None | None | Skipped when it is the default `Bootstrap administrator (…)` text |
| Backfill | Link `UserPrincipalNameSnapshot`, then the provisioning access request's snapshot | Link `MailSnapshot`, then access request | `Users.DisplayName`, unless default bootstrap text |

`oid` is never used, so generated names leak no directory identifiers.

### 10.2 Sanitizing a candidate

1. Unicode normalize to form KD and drop combining marks, so `José` becomes `jose`.
2. Lower-case invariant.
3. For the display-name source only, map runs of whitespace to `.`.
4. Drop every character outside `a-z 0-9 . - _`.
5. Collapse runs of separators to the first separator in the run.
6. Trim separators and leading digits from the start, and separators from the end.
7. Truncate to 56 characters and re-trim trailing separators.
8. Reject the candidate if it is shorter than 3, matches the user-id shape, or is reserved. Then try the next source.

The generator is a pure function in `UsernamePolicy` and must be unit-tested with fixed inputs and outputs.

Generated names are assigned once. They do **not** follow later UPN or display-name changes.

### 10.3 Collisions

1. Try the base.
2. Then `base-2` through `base-20`.
3. Then `base-` plus six random characters from `a-z0-9`, up to five attempts.
4. Then fail the operation with `username_generation_exhausted`.

Each attempt is an insert of the ledger row inside the user-creation transaction. A unique-key violation moves to the next candidate. Any existing ledger row, current or retired, counts as taken, because a new user owns no history to reclaim. No in-memory reservation or pre-check is trusted; a pre-check may be used only to skip obvious collisions. The longest result is 56 + 7 = 63 characters.

Numeric suffixes reveal only that another name with the same base exists, and only to administrators who can already list users.

## 11. Local IdentityLink relationship (R10)

**Ruling.**

- `Users.Username` is authoritative.
- When a user has a Local link, its `ExternalId` **equals** `Users.Username`. They never intentionally differ.
- Local sign-in keeps resolving through the Local identity link (`GetByIdentityLinkAsync(Local, normalized)`), so `LocalCredentials`, MFA and the link's `LastAuthenticatedAt` keep working unchanged.
- Rename and canonicalization update both values in one transaction.
- At most one Local link per user. IAM-9.1 adds a filtered unique index on `IdentityLinks(UserId)` where `[Source] = 1`. The migration fails closed with a clear message if any user has two.
- `POST /api/users/{id}/identity-links` with `Local`: `ExternalId` may be omitted, and then defaults to the user's username. If supplied, its normalized form must equal the username, otherwise 400 `username_mismatch`. This is a documented behaviour change.
- `CreateUserRequest.ExternalId` for `Local` keeps meaning "the requested username". IAM-9.3 adds an optional `Username` field as the preferred name and keeps `ExternalId` as an accepted alias.
- **Both fields supplied (product-owner ruling, IAM-9.3).** For a `Local` create request:
  - `Username` only: use `Username`.
  - `ExternalId` only: accept it as the legacy Local username alias.
  - Both: normalize each through the one username normalization (`UsernamePolicy.Normalize`, which `LocalAuthHelpers.NormalizeUsername` delegates to). If the normalized values are equal, proceed. If they differ, return 400 `username_mismatch`.
  - Neither field silently wins over a conflicting value. This is the same rule as the Local identity-link rule above, so `User.Username` and the Local `IdentityLink.ExternalId` keep one canonical meaning.
  - `Entra` creation is unchanged and ignores `Username`.

Rejected alternative: resolving local sign-in through `Users.Username` and dropping the Local link key. It would remove the existing filtered unique index's protection and change the credential model without benefit.

## 12. Session identity contract (R11, R12)

### 12.1 Claims on every application-cookie session

| Claim | Value | Purpose |
|---|---|---|
| `ClaimTypes.NameIdentifier` | `UserId` | The canonical session identity |
| `http://schemas.apperture/session_format` | `2` | Marks a post-IAM-9 cookie (§12.4) |
| `ClaimTypes.Name` | `DisplayName` | Unchanged meaning |
| `http://schemas.apperture/username` | Username at sign-in | Display convenience only. May be stale after rename. Never used for identity |
| `http://schemas.apperture/identity_source` | `Local` or `Entra` | Unchanged. Used for step-up and link binding |
| `http://schemas.apperture/break_glass` | `1` | Unchanged, legacy break-glass endpoint only |
| Assurance and recent-verification claims | Unchanged | SEC-1 and step-up |

The `http://schemas.apperture/` namespace is chosen because the Entra handler already strips that namespace from incoming tokens (SEC-1 I-1). An identity provider can therefore never inject a user id or session format.

### 12.2 Local and Entra become identical at the application layer

- `LocalSessionIdentity.Create` takes the user id, username and display name. It is used by local sign-in, MFA completion, Recovery Access and local step-up re-issue.
- The legacy break-glass endpoint builds the same claims plus `break_glass`.
- **Entra:** `OnTokenValidated` already resolves the user through `CompleteSignInAsync`. After success it adds the claims in §12.1 to the validated principal. The raw Entra claims (`oid`, `tid`, `sub`, `name`, `preferred_username`, …) stay on Entra sessions for diagnostics, `/me` `UserPrincipal`, the account email claim, and link binding (§13). They are never the actor.
- Entra step-up re-issue keeps the same claims, because it re-signs the existing principal.
- The pending-MFA ticket carries the user id only.

### 12.3 Actor mapping

`ClaimsPrincipalToActorMapper.ToActor` for an application-cookie principal:

- Requires `session_format = 2` and a `NameIdentifier`. The actor id is the `NameIdentifier`.
- Does not fall back to `oid`, `sub`, `Name` or `preferred_username`.
- Keeps `identity_source`, `break_glass` and tenant id on `HumanActor` for diagnostics and policy.

IAM-9.2 must inventory any other authentication scheme that reaches the mapper and record it in the handback. The Entra access-request scheme does not reach it.

### 12.4 Existing sessions (R12)

**Ruling. Clean one-time transition.**

- The application cookie gains an `OnValidatePrincipal` handler that rejects any principal without `session_format = 2` (`RejectPrincipal` plus sign-out). It does **no** database work, so it is a format gate, not the per-request revalidation SEC-1 D5 deferred.
- The browser receives 401 on its next API call and follows the SEC-1 recovery path to sign-in.
- No compatibility bridge maps old username or oid cookies to users. That would keep the old resolver logic alive and preserve the problem IAM-9 removes.
- Cookie name, lifetime, sliding expiry, `SameSite`, `Secure` and scheme names are unchanged.
- Session versioning and revocation stay in IAM-11. The same `OnValidatePrincipal` hook, or the resolver (§13), is the seam IAM-11 uses.
- **Release note (required):** "After upgrading, everyone is signed out once and must sign in again. Recovery Access is affected the same way."

## 13. Principal-resolution contract (R13)

**Post-IAM-9 request path.**

```
application cookie (session_format = 2)
  -> NameIdentifier = UserId
  -> IUserRepository.GetByIdAsync(UserId)
  -> session-link binding check
  -> touch LastAuthenticatedAt on the bound link (existing one-minute throttle)
  -> UserId to callers (authorization, /me, governance)
```

**Session-link binding check.** Today, removing an identity link ends sessions that came through it, because resolution goes through the link. Keying sessions by user id would silently lose that property. IAM-9.2 therefore keeps it:

- Entra session: the link `Entra(tid, oid)` from the session's own `tid` and `oid` claims must exist and belong to this user.
- Local session (including Recovery Access): the user must still have a Local link. The check is on link existence, not on the link's `ExternalId`, so a username rename does **not** end the user's Local session, while removing the Local link does.

Approved without change by the product owner (2026-10-03). The one-Local-link-per-user invariant (§11) is kept.

The check reuses the links already loaded by `GetByIdAsync`, so it adds no query. The Entra-primary policy that the resolver applies for pending links keeps running.

The binding applies to authorization as well as to `/me`. A legacy break-glass session bypasses role evaluation only after it has resolved to its user through this check, so removing its Local link ends its permissions too (IAM-9.2 independent review).

| Case | Result |
|---|---|
| User id not found | Null. `/me` 403 (existing terminal account-unavailable surface); authorization denies |
| Binding link removed | Null, as above |
| Cookie without `session_format = 2` | Never reaches the resolver; rejected in §12.4 |
| Recovery Access | Ordinary local session by user id. Canonicalization rename no longer orphans its live session |
| Disabled or decommissioned (IAM-11) | Not implemented. IAM-11 adds a status and session-version check at this exact point, after `GetByIdAsync` |

The resolver keeps its no-cache behaviour. The "no source claim means local username" fallback is removed.

## 14. UserReference contract (R14)

**Ruling.** IAM-9.3 ships:

```csharp
public sealed record UserReferenceDto(string UserId, string Username, string DisplayName);
```

- `Status` is **not** shipped. IAM-11 adds it as an additive, nullable field (`active`, `disabled`, `decommissioned`). Clients treat a missing status as active.
- An avatar key is not shipped. It is additive later.
- Unknown historical actors are **not** `UserReferenceDto` values. AUD-1 defines an actor reference for audit (kind `user`, `system`, `anonymous`, `worker`, or `unresolved`, plus the original string). It embeds `UserReferenceDto` for kind `user`. IAM-9 fixes only the rendering rule (§18).
- References are composed **server-side** into the response that needs them, through an `IUserReferenceResolver.ResolveAsync(IEnumerable<UserId>)` batch service. There is no general bulk user-lookup endpoint. A page shows a user reference because its own permission allows the page, not because the viewer can read the user directory.

## 15. User presentation rules (R15)

**Ruling.** One `UserRef` component in the Management UI design system with two modes.

| Mode | Visible text | Accessible name |
|---|---|---|
| Compact (tables, inline mentions) | `username` | "Display Name (username)" |
| Rich (headers, detail pages, holder lists) | **Display Name** with `username` beside it in secondary text | Same |

- Identity is never available only through hover. Rich mode shows the username as visible text, not in a tooltip. This follows `.ai/playbooks/anti-patterns.md` ("essential facts stay inline; no required information in tooltips") and refines the earlier tooltip suggestion. Approved by the product owner (Q3, §27).
- Compact mode may add a native `title` with the display name as a mouse convenience. The display name is never the only way to identify the user, because the username is visible.
- When the viewer holds `user.read`, the reference links to the user detail page (`DrillInLink`). Otherwise it is plain text.
- Unresolved actor: the original string in monospace with the visible label "Unrecognized actor".
- The component reserves a status slot that renders nothing until IAM-11.
- No new popover or tooltip primitive is introduced (`Uix6InteractionSystemTests`).

## 16. User search contract (R16)

**Ruling.** Extend the existing list endpoint:

```
GET /api/users?q={text}&skip={n}&take={n}
```

| Aspect | Rule |
|---|---|
| Authorization | `user.read`, unchanged |
| Matching | `q` trimmed and lower-cased. Match a **username prefix** or a case-insensitive **display-name substring**. `LIKE` wildcards in `q` are escaped |
| Empty `q` | Unfiltered list, as today |
| Ordering | Exact username match first, then `Username` ascending. The unfiltered list also orders by `Username` (was `Id`; documented change) |
| Paging | Existing `skip`/`take` and `TotalCount`; `take` defaults to 20 and is clamped to 1–100. LST-1 owns later paging contracts |
| Excluded | Recovery Access, as today |
| Not matched | Email, UPN, mail snapshots and user id. Search is not a directory or personal-data lookup |
| Audit | Search terms are never written to audit details (RC1 §6.5) |

Selectors for callers without `user.read`, such as operational-team editors under `operational_team.manage` (D-08), are an LST-1.3 and IAM-10 decision. IAM-9 does not widen `user.read`.

## 17. Historical actor migration (R17)

**Ruling. Split responsibility, explicit sequence.**

| Step | Package | What |
|---|---|---|
| Future writes | IAM-9.2 | Every human write stores the user id, because the mapper returns it |
| Non-audit columns | IAM-9.2a | Re-runnable startup data step rewrites legacy values in place to user ids (see the 9.2a package for why it runs on every start) |
| Audit rows | AUD-1.1 | Adds actor columns (kind, user id, original display) and backfills them using the same resolution rules. `AuditLogs.Actor` is never rewritten |

IAM-9.2a columns: `RuntimeControlRequests.RequestedBy`, `WorkflowExternalControlRequests.RequestedBy`, `PlatformSettingValues.UpdatedByPrincipalId`, `EntraAccessRequests.ReviewedByUserId`, `AttentionIncidents.AcknowledgedBy`, `OperationItemAttentionAcknowledgements.AcknowledgedByUserId` and `.ReopenedByUserId`, `OperationItemRetryAttempts.RequestedByActorId`, `NotificationDefinitions.CreatedByActorId` and `.UpdatedByActorId`, `NotificationSubscriptions.CreatedByActorId` and `.UpdatedByActorId`, `NotificationSubscriptionUserOverrides.CreatedByActorId`.

**Resolution rules, applied in order to each non-null value:**

1. A known literal (`ActorLiterals.IsNonHumanLiteral`: `system`, `anonymous`, `recovery`, `emergency-cli`, `system-worker` and the `system:` family such as `system:scheduler`; or a value equal to a recorded worker or host id) is left unchanged.
2. A value equal to an existing `Users.Id` is already a user id. Leave it.
3. A value equal to a `UsernameLedger.Username` (current or retired) resolves to that ledger row's user. The ledger includes previous Recovery Access names (§20.2).
4. A value equal to the `ExternalId` of exactly **one** Entra link resolves to that link's user. Zero or several matches leave it unresolved. Historical strings carry no tenant, so this rule is the only safe oid match. Under D-27 an instance has one mapped tenant, so ambiguity is not expected, but it is checked.
5. Otherwise leave the value unchanged and count it as unresolved.

A value matching more than one rule with different users is left unchanged and reported as ambiguous.

**Implementation notes (IAM-9.2a).**

- Every rule is evaluated before deciding, so the order above never hides a second meaning. Zero candidate users is unresolved, one is already a user id or resolved, several are ambiguous.
- A literal is left unchanged even when a grandfathered username has the same spelling (rule 1 wins).
- An object id with links of several users is counted as ambiguous. Several links of one user stay unresolved, because rule 4 requires exactly one link.
- A value is rewritten only to a user whose id means that user alone. If the id is also another user's username or object id, the original stays and is counted as ambiguous, so every written value reads back as a user id.
- Usernames compare case-insensitively (the ledger key); object ids compare case-insensitively (GUIDs); user ids compare exactly; nothing is trimmed. Values are matched and rewritten byte-exactly, so case and trailing-space variants are separate values.
- "A recorded worker or host id" needs no data source for these columns. Every writer of the 13 columns is an authenticated Management API request, and worker ids are stored only in `AppliedBy` and `ClaimedBy`, which are not normalized. AUD-1.1 decides this again for audit rows.
- Rule order is a contract shared with AUD-1.1: the pure rules live in `HistoricalActorClassifier` (`Apperture.Core.Application.Services`).
- **Failure policy (independent review).**
  - The step runs on every Management Host start, after migrations and before the host reports persistence ready.
  - Unresolved, ambiguous and literal values are results, not failures, and never block startup.
  - An execution failure (connectivity, timeout, schema mismatch, unexpected SQL error) fails the start exactly like a failed migration: nothing is reported ready.
  - Values already rewritten stay committed. The operator fixes the underlying problem and starts the host again, and the next run resumes and converges.
  - A backup restore is only for deliberately reversing the release, never for recovering from a failed run.

**Sequencing.** IAM-9.2a runs after IAM-9.2 in the same release. Because the ledger keeps every name, a later rename does not make historical usernames unresolvable. AUD-1.1 should still ship in the same release as IAM-9, so that Entra links removed in between do not lose oid resolution.

No mapping log table is kept. Reversal relies on a pre-upgrade database backup, which the release note requires (`deploy/README.md`, "Who-did-what history after upgrading").

## 18. Historical actor fallback (R18)

- An unresolved value is never mapped to a user and never causes a user, link or ledger row to be created.
- It stays verbatim in its column (IAM-9.2a) or in `AuditLogs.Actor` (AUD-1.1).
- Read models expose it as an unresolved actor with its original string. `UserRef` renders "Unrecognized actor" plus the string (§15).
- Literals render as non-human actors (for example "System"), not as users. The literal list moves to one constants class in IAM-9.2 (`ActorLiterals`), shared by the mapper's callers, the migration and AUD-1. It lives in `Apperture.Core.Application.Services` beside `UsernamePolicy`, because the persistence layer (where a startup data step runs) cannot reference `Apperture.ControlPlane.Security`.

## 19. Recovery Access treatment

| Concern | Treatment |
|---|---|
| Username | `breakglass`, which conforms to §9.1; reserved; assigned only by provision or canonicalization |
| Reservation | `breakglass` is a reserved name and its ledger row is bound to the Recovery Access user forever. Revoke keeps it on the same row (existing behaviour). It is never assigned or reclaimed through ordinary governance |
| Canonicalization | System rename through `UsernameService`; previous name retired with reason `canonicalization`; existing fail-closed conflict behaviour unchanged |
| Session | Ordinary local session keyed by user id; MFA still mandatory (`NativeMfaService`); legacy `break_glass` claim unchanged |
| Resolver | By user id plus Local-link binding |
| UserReference | Resolves normally (`breakglass`, "Recovery Access") wherever it is the actor of history |
| Search and list | Still excluded (`!IsBreakGlassEligible`); detail still 404 |
| Mutations | Rename refused with 409 `UnsafeOperation`, like every other mutation |
| Audit identity | User id after IAM-9.2. Pre-upgrade `breakglass` strings resolve through the ledger |
| Unauthenticated disclosure | Unchanged: login does not reveal whether `breakglass` exists (ADR S2D-IAM3) |

Finding for IAM-11, not IAM-9: a revoked former Recovery Access user remains an ordinary administrator named `breakglass` / "Recovery Access". That is existing behaviour and the emergency-path gate should rule on it.

## 20. Migration and concurrency strategy

### 20.1 Migration barrier

Username generation needs C# (Unicode normalization and the shared generator), and the column must be `NOT NULL`. IAM-9.1 therefore ships two migrations with a data step between them:

1. `Iam9_UsernameLedger`: create `UsernameLedger`; add nullable `Users.Username` with the declared collation; add the one-Local-link-per-user index (fails closed on violations); backfill Local users in T-SQL (§20.2 A, C, D).
2. **Data step** `UsernameBackfill`: C# generation for every user still without a username, plus ledger rows for previous Recovery Access names.
3. `Iam9_UsernameRequired`: make `Users.Username` `NOT NULL` and create `UX_Users_Username`.

`PersistenceMigrationHostedService` migrates to `Iam9_UsernameLedger`, runs the data step, then migrates to the end. The host does not report migration completion until all three finish. Any other entry point that bypasses the barrier, such as `dotnet ef database update`, fails closed at step 3 with a message to start the host. IAM-9.1 must find every migration entry point, including test fixtures, and record them in its handback.

The data step is idempotent and safe to re-run after a crash.

### 20.2 Backfill rules (R9)

Processing order is fixed: Local-linked users first, then Entra-only users. Within each group, order by earliest `IdentityLinks.LinkedAt` (nulls first), then `Users.Id`.

| Case | Username |
|---|---|
| A. Local link | `ExternalId` trimmed and lower-cased, **verbatim**, even if it fails §9.1. If lower-casing changes the stored link, the link is updated too. Sign-in is unaffected because input is already normalized and the old collation was case-insensitive |
| B. Entra only | Generated per §10, collisions per §10.3 |
| C. Local and Entra | As A. The Local name wins |
| D. Recovery Access | As A (`breakglass`) |
| E. Bootstrap and dev users | Local: as A. Entra-only config admins: as B, skipping the default bootstrap display text, which usually yields `user`, `user-2`, … |
| Previous Recovery Access names | Parsed from `AuditLogs` rows with the emergency `Canonicalized` action (`previousUsername=…`). Added to the ledger as retired for that user, unless the name is already held by someone; held names are reported and skipped |

Local names cannot collide with each other, because the existing Local index already enforces case-insensitive uniqueness. A grandfathered Local name is never changed by the backfill.

The data step logs counts: local verbatim, local grandfathered (non-conforming), generated, generated with suffix, reserved-word holders, and skipped previous names. It writes one audit row per generated name (`identity.username.assigned`, actor `system`).

### 20.3 Concurrency

| Race | Arbiter |
|---|---|
| Two Entra provisions generate the same base | Ledger primary key; loser moves to the next candidate |
| Admin creates `alice` while generation picks `alice` | Same |
| Two renames of different users to the same new name | Same; the loser gets `username_taken` |
| Rename of user B to user A's retired name | Primary key: the name is already owned by A; `username_taken` |
| Two concurrent renames of the same user (for example a reclaim and a rename) | `Users.RowVersion` and the one-current-name-per-user index; one fails and rolls back |
| Rename versus another edit of the user | `Users.RowVersion` |
| Two creations with the same Local name | Existing Local link index plus the ledger key |
| Rename racing sign-in | Sign-in reads the link by name; whichever commits first wins; the session is keyed by user id either way |

No application-memory lock or cache is relied on.

### 20.4 Rollback

- **IAM-9.1** `Down` migrations drop the index, column and table. Rolling back after renames is unsafe for local sign-in only if a rename happened, and renames ship in IAM-9.5, so 9.1 alone rolls back cleanly.
- **IAM-9.2** changes cookie semantics. Rolling back the binary makes post-upgrade cookies unresolvable by old code (403 for local, works for Entra). A rollback therefore needs a second forced sign-out, and it is **mandatory**, not a convenience: old code reads a local cookie's `NameIdentifier` (now a user id) as a Local username, and a grandfathered username may have the 24-hex user-id shape (§9.1), so a new cookie could resolve to a different account. Before starting the earlier binary, the operator stops the host and deletes (or revokes) every Data Protection key it used. Rotating or adding a key is **not** sufficient, because existing keys still decrypt existing cookies. Recovery Access and native MFA secrets use the secret protector, not this key ring, so they survive. The release note says so (`deploy/README.md`, "Sign-in sessions after upgrading").
- **IAM-9.2a** rewrites data one way. Rollback means restoring the pre-upgrade backup.
- Entra sign-in continuity does not depend on any IAM-9 schema change, because the Entra key is untouched.

## 21. Security considerations

| Topic | Finding | Ruling |
|---|---|---|
| Username secrecy | Usernames are not secret | They may appear in admin UI, audit details and logs |
| Unauthenticated enumeration | Local login returns a generic 401, and forgotten password returns a generic acknowledgement | IAM-9 adds no unauthenticated endpoint that reveals whether a username exists. No "is this name available" check without `user.manage` |
| Login timing | **Code fact:** unknown users return before any password hash (`AuthController.cs:466-533`), so timing reveals existence | Recorded for IAM-11, which owns sign-in throttling. IAM-9 does not make it worse |
| Forgotten-password timing | **Code fact:** the eligible path issues a token and sends mail synchronously | Recorded for IAM-11 credential recovery |
| Search | `user.read` only; no email or UPN matching; terms never audited | §16 |
| Identity-provider injection | Apperture claims use the stripped `http://schemas.apperture/` namespace | §12.1 |
| Unlink ends sessions | Preserved by the binding check | §13 |
| Global oid assumption | Not reintroduced. Live resolution is by user id, then tenant-scoped link. Historical oid matching requires a unique match | §13, §17 |
| Generated names | No `oid` or directory id in names; guest UPNs skipped | §10.1 |
| Step-up claims | Still not bound to a user id inside the cookie (existing). The cookie as a whole is now bound to one user id | No change in IAM-9 |

Minor Entra finding, outside IAM-9 scope: `EntraIdentityAssociationService.cs:179-180` checks "user already has another Entra link" by `ExternalId` only, ignoring tenant. With one mapped tenant (D-27) it is unreachable, because the token tenant must match the active registration. Record it for the future external-organization access work.

Minor Entra finding: the self-link path builds its assertion from `oid`, `tid` and `sub` only, so `DisplayNameSnapshot`, `UserPrincipalNameSnapshot` and `MailSnapshot` are null on self-linked rows. This only weakens backfill inputs (§10.1). IAM-9 does not change it.

## 22. API changes

All changes are additive unless marked.

| Contract | Change |
|---|---|
| `UserListDto` | Add `Username` |
| `UserDetailDto` | Add `Username` |
| `MeResponse` (server and client copy) | Add `Username` |
| `RoleAssignmentHolderDto` | Add `Username` |
| `GET /api/users` | Add `q`; clamp `take`; **order by `Username`** (behaviour change) |
| `CreateUserRequest` | Add optional `Username`; `ExternalId` remains an alias for Local. Both supplied: equal after normalization proceeds, otherwise 400 `username_mismatch` (§11) |
| `POST /api/users/{id}/identity-links` (Local) | `ExternalId` optional and must equal the username (**behaviour change**, 400 `username_mismatch`) |
| `PUT /api/users/{id}/username` (IAM-9.5) | New: `{ "username": "…" }`, `user.manage`. 200 for a new name or a reclaim of this user's own retired name. 409 `username_taken` when the name is, or was, owned by another user. 409 `UnsafeOperation` for Recovery Access. 400 `username_invalid` / `username_reserved` when the target fails the current policy, including reclaim targets |
| `UserReferenceDto` | New shared record (§14) |
| Entra provisioning results | Add the assigned `Username` |
| Actor-bearing DTOs (`AuditTimelineEntryDto.Actor`, `RunTimelineEventDto.Actor`, `RequestedBy`, `AcknowledgedByUserId`, …) | No change in IAM-9. Values become user ids for new writes. LST-1.3 and AUD-1 add references |

## 23. UI boundaries

**IAM-9.4** builds `UserRef` only, with bUnit coverage. It changes no page.

**IAM-9.5 changes these surfaces:**

| Surface | Today | IAM-9.5 |
|---|---|---|
| `UI/Pages/Governance/UsersList.razor:76` | First column is the raw id | Username (link) and display name; search box using `q` |
| `UI/Pages/Config/Governance/UserDetail.razor:25` | Raw id under the header | `UserRef` rich in the header |
| `UserDetail.razor:95` | `User ID` metadata | Username metadata; id moves to `TechnicalDetails`; rename action |
| `UI/Pages/Governance/UserEffectiveAccess.razor:62` | `User ID` metadata | Username; id to `TechnicalDetails` |
| `UI/Pages/Governance/RoleDetail.razor:226,301` | Display name only | `UserRef` compact |
| `UI/Molecules/UserAccountMenu.razor:88` | Falls back to the user id | Falls back to the username |
| `UserNew.razor` | Local "username" field writes `ExternalId` | Same field, validated live against §9.1 |

**Later adoption (LST-1.3 or AUD-1), not IAM-9:** `AuditTimeline.razor:164`, `EntityRecentActivityPanel.razor:46`, `RunDetail.razor:474`, `RunOperationItemizationPanel.razor:421`, `IntegrationScheduleDiagnostics.razor:250`, `RuntimeShutdownRequests.razor:46,48`, `RuntimeShutdownRequestsList.razor:47,49`, `NotificationRecipientResolutionDisplay.razor:46`, `NotificationRecipientSnapshotDisplay.razor:96`, `ManagedContactEditor.razor:97`, the team-members table in `AttentionContactGroupDetail.razor`, and the notification principal pickers in `NotificationDefinitionDetail.razor` and `IntegrationNotifications.razor` (replaced by `AsyncSearchSelect`).

**Legitimately technical, may stay:** `AuthOverview.razor:126`, `AccountProfile.razor:197`, `RunDetail.razor:343` and `:605-607` (all inside `TechnicalDetails` or advanced sections).

## 24. Test matrix

| Layer | Package | Tests |
|---|---|---|
| Unit | 9.1 | Format accept/reject table, including boundaries, separators, user-id shape and non-ASCII; normalization; reserved names (the six actor names, and route words such as `me` and `new` accepted); grandfathered stored values accepted by display and lookup code; generator fixed vectors (UPN, `#EXT#`, mail, accented display name, empty, too short, reserved, user-id shape); suffix sequence and exhaustion |
| Unit | 9.1 | Every creation path assigns a username: one test per path in §4.4, plus an architecture test that `IUserRepository.AddAsync` rejects a user without a username |
| SQL Server | 9.1 | Case-insensitive uniqueness through raw SQL on `Users` and `UsernameLedger`; declared collation via `sys.columns`; the existing `UX_IdentityLinks_*` indexes (no test exists today); one-Local-link index |
| SQL Server | 9.1 | Populated-database migration through the barrier: local, grandfathered `jane@contoso.com`, Entra-only with and without snapshots, mixed, Recovery Access, bootstrap Entra admin, a generated collision, a previous canonical name. Re-run is a no-op. Use the existing migration-hardening test pattern (`Epic72MigrationHardeningTests`) |
| SQL Server | 9.1 | Concurrent generation of the same base from two connections yields two distinct names; rename plus local link change is atomic (fault injected between writes); reclaim of an own retired name reactivates the row and retires the current one atomically; an attempt by another user to take a retired name fails on the primary key, even through raw SQL; ledger `UserId` never changes |
| SQL Server | 9.2a | Each column with local, Entra, user-id, literal, ledger-retired, ambiguous and unresolvable values; counts match |
| Integration / HTTP | 9.2 | Existing local user signs in after migration; Entra user signs in (stub provider); local and Entra sessions produce the same actor shape (user id); old-format cookie gets 401 then signs in; unlinking an Entra link ends that session; Recovery Access signs in with MFA; legacy break-glass endpoint still works when enabled |
| Integration / HTTP | 9.3 | Search prefix and substring, ordering, clamp, escaping, Recovery Access excluded, 403 without `user.read` |
| Integration / HTTP | 9.5 | Rename: success; another user's current name 409; another user's retired name 409; own retired name reclaimed 200; reserved and invalid 400, including a grandfathered reclaim target; Recovery Access 409; audit row; credentials and roles unchanged; existing local session still works after rename; local sign-in with the new name; old name refused at sign-in |
| Security regression | 9.2 | Full SEC-1 browser suite unchanged and green; `BrowserRequestTrustHttpTests`; `EntraStepUpSecurityGuardTests`; return-target corpus |
| UI / bUnit | 9.4 | `UserRef` compact and rich; accessible name; link only with `user.read`; unresolved state; no hover-only content |
| UI / source contract | 9.5 | Update `Iam2UserCreationUiSurfaceTests`; raw user id absent from the targeted governance surfaces outside `TechnicalDetails` |

Browser tests beyond the existing SEC-1 suite are not required. Lower layers prove the behaviour.

## 25. Implementation decomposition

Packages run in this order: 9.1 → 9.2 → 9.2a, and 9.1 → 9.3 → 9.4 → 9.5. 9.3 may run in parallel with 9.2. 9.5 needs 9.2, 9.3 and 9.4.

### IAM-9.1 — Username attribute, ledger, generation and backfill

- **Goal.** Every user has one canonical, unique username. Every new assignment satisfies the current policy. No name is ever assigned to a second user.
- **Prerequisites.** IAM-9.0 approved.
- **Scope.** `UsernamePolicy` (format, reserved names, generator); `UsernameLedger`; `Users.Username`; migration barrier and data step; every creation path in §4.4 assigns a username; `UsernameService` with create-time assignment and `RenameAsync`; Recovery Access canonicalization through `RenameAsync`; Local link mirror; one-Local-link index; `HarnessSeeding` normalizes.
- **Non-goals.** Cookie, mapper, resolver, DTOs, UI, admin rename API.
- **Affected areas.** `User.cs`, `UserEntity.cs`, `AppertureDbContext.cs`, two migrations, `PersistenceMigrationHostedService.cs`, `SqlUserRepository.cs`, `IUserRepository.cs`, `UserManagementService.cs`, `UserProvisioningService.cs`, `FirstAdminBootstrapService.cs`, `BootstrapPlatformAdminAssignment.cs`, `EmergencyAccessService.cs`, `EmergencyAccessIdentity.cs`, `DevTestUserBootstrapHostedService.cs`, `EmergencyCli.cs`, `HarnessSeeding.cs`.
- **Invariants.** Entra key untouched; `breakglass` canonical; existing sign-in unchanged; grandfathered names untouched.
- **Migration concerns.** §20. Real SQL Server proof required.
- **Tests.** §24 rows for 9.1.
- **Handback.** Migration SQL for review; list of creation paths with file and line; list of migration entry points; backfill counts on a populated development database.
- **Model.** Opus-class.

### IAM-9.2 — Stable session, principal and actor identity

- **Goal.** Session identity and actor identity are the user id for every path.
- **Prerequisites.** 9.1.
- **Scope.** §12 and §13: `LocalSessionIdentity`, Entra `OnTokenValidated`, legacy break-glass, local step-up re-issue, pending-MFA ticket, `OnValidatePrincipal` format gate, mapper, resolver with link binding, `ActorLiterals`.
- **Non-goals.** Historical data; DTOs; status or session version.
- **Affected areas.** `LocalSessionIdentity.cs`, `AuthController.cs`, `EntraOpenIdConnectOptionsConfigurer.cs`, `ManagementCookieAuthentication.cs`, `ClaimsPrincipalToActorMapper.cs`, `SqlPrincipalToUserResolver.cs`, `HostPrincipalContext.cs`.
- **Invariants.** SEC-1 behaviour unchanged; step-up still works for both sources; Recovery Access MFA mandatory; Entra `(tid, oid)` resolution.
- **Migration concerns.** One-time sign-out; rollback note (§20.4).
- **Tests.** §24 rows for 9.2. Full CI checkpoint.
- **Handback.** Both CI jobs green; SEC-1 suite output; inventory of schemes reaching the mapper; release-note text.
- **Model.** Opus-class.

### IAM-9.2a — Historical non-audit actor migration

- **Goal.** Legacy actor strings in non-audit columns become user ids.
- **Prerequisites.** 9.2 (so no new legacy values are written), same release. Begins on its own branch as soon as 9.2 merges.
- **Scope.** §17 columns and rules; one data normalization.
- **Non-goals.** `AuditLogs` (AUD-1.1); column renames.
- **Invariants.** Literals and unresolvable values untouched; no fabricated users.
- **Re-run safety (IAM-9.2 independent review).** Rolling the release's binary back lets the earlier version write username and oid actor strings again, and a later upgrade must normalize those too. The normalization is therefore idempotent and safe to run again: prefer a restart-safe startup data step (like the IAM-9.1 username backfill) over a one-shot migration that can never run a second time. The value-based rules make a re-run a no-op for values already converted (rule 2).
- **Rule inputs.** Rule 1 takes its literals from `ActorLiterals`. A Local username that equals a different user's id (possible only for a grandfathered name, §9.1) is an ambiguous value: count and report it, never guess.
- **Sequencing.** Part of the current RC1 release sequence. AUD-1 sequencing (§17) is unchanged.
- **Tests.** §24 row for 9.2a.
- **Handback.** Resolved, unresolved, ambiguous and literal counts per column on a development database.
- **Model.** Opus-class for the rules review; Sonnet-class can implement once the rules are fixed.

### IAM-9.3 — UserReference contract and user search

- **Goal.** Usernames on user DTOs, a shared reference record, and search.
- **Prerequisites.** 9.1.
- **Scope.** §14, §16 and the §22 DTO rows; `IUserReferenceResolver`; `IManagementClient` copies.
- **Non-goals.** UI; selectors for non-`user.read` callers.
- **Tests.** §24 row for 9.3; DTO JSON contract tests.
- **Handback.** API diff; query plan note for the search query.
- **Model.** Sonnet-class.

### IAM-9.4 — `UserRef` component

- **Goal.** One accessible user-presentation primitive.
- **Prerequisites.** 9.3.
- **Scope.** §15; reuse `DrillInLink` and design-system tokens.
- **Non-goals.** Page adoption; new tooltip primitive.
- **Tests.** bUnit per mode and state.
- **Model.** Sonnet-class.

### IAM-9.5 — Governance adoption and administrator rename

- **Goal.** Governance surfaces stop showing raw ids, and administrators can rename.
- **Prerequisites.** 9.2, 9.3, 9.4. Q1 is approved.
- **Scope.** §23 IAM-9.5 table; `PUT /api/users/{id}/username`; rename UI on user detail with server-confirmed pending state (D-19).
- **Non-goals.** Operational surfaces listed for LST-1.3.
- **Tests.** §24 rows for 9.5.
- **Handback.** Screenshots of the changed surfaces; closure audit against this gate.
- **Model.** Sonnet-class.

## 26. Risks and rollback notes

| Risk | Mitigation |
|---|---|
| Migration barrier bypassed by a tool or test fixture | Step 3 fails closed; 9.1 handback lists every entry point |
| Generated names are poor where snapshots are missing | Administrator rename (Q1); sources include access-request snapshots |
| Grandfathered names keep `@` and other characters | They work for sign-in; rename available; counts reported |
| One-time sign-out surprises operators | Release note; Recovery Access works immediately after sign-in |
| Losing "unlink ends session" | Binding check, with an HTTP test |
| Mixed actor columns misread after migration | Rule 2 recognizes user ids; the user-id shape is barred from usernames |
| Audit actor backfill delayed past an unlink | Ship AUD-1.1 in the same release |
| Rollback after 9.2 | Mandatory second forced sign-out by deleting the Data Protection keys (not rotating them); documented (§20.4) |
| Rollback after 9.2a | Database restore only; backup is a release prerequisite |

## 27. Open questions requiring product-owner approval

| # | Question | Recommendation |
|---|---|---|
| Q1 | Should administrators be able to rename active usernames in RC1 (IAM-9.5)? | **Approved (2026-10-03).** Holders of `user.manage` may rename. No self-service rename in RC1. Recovery Access is not renamable through ordinary governance. Rename is audited, transactional with the Local link, changes no credential or authorization, and ships only after user-id sessions (IAM-9.2) |
| Q2 | Should non-conforming existing local usernames be grandfathered rather than forcibly converted? | **Approved (2026-10-03).** Contract in §9.1: new assignments obey the current policy; grandfathered names stay valid identities; no code assumes stored usernames are route-safe |
| Q3 | Rich mode shows the username as visible secondary text, not a tooltip | **Approved (2026-10-03).** Rich: display name primary, username visible secondary. Compact: username primary. Username never tooltip-only |

**Product-owner amendments recorded with the approval.**

| Amendment | Effect |
|---|---|
| Username ledger | A normalized username is bound forever to one user id and is never assigned to another user. The owner may reclaim their own retired name. D-03 is read as "never reassigned to another user". §8, §9.5, §9.6, §20.3, §22, §24 updated |
| Reserved names | Route words (`me`, `new`, `requests`, `from-entra`) removed. Only identity and actor names remain. §8, §9.4 updated |
| Session-link binding | Approved without change (§13) |
| Historical actor split | Approved without change (§17) |

No open product-owner questions remain.

## 28. Explicit prerequisites for IAM-9.1

1. Product-owner approval of this gate: **satisfied** (2026-10-03, §27). An explicit instruction to start IAM-9.1 is still required.
2. A dedicated IAM-9 branch whose base includes the RC-0 commit (`d8cc7e5`), with this gate commit cherry-picked onto it.
3. A populated development database snapshot, including an Entra-only user and an email-shaped local user, for the migration proof.
4. SQL Server or LocalDB available for the SQL tests. SQL-dependent tests that skip must be reported as skipped, not passed.
5. No other package is changing `SqlUserRepository`, `AuthController` or the Entra configurer at the same time.
