# IAM-9 — Identity foundation — closure audit

Date: 2026-10-05
Design authority: [IAM-9 design gate](../01-architecture/transitions/identity-foundation/iam9-design-gate.md) (rulings R1–R18, §§9–24, package decomposition §25, carried findings §29).
Program contract: [RC1 program gate](../01-architecture/rc1-program-gate.md) (D-01 to D-07, D-13, D-19, D-27; §2 RC1 definition; §8).
Audited baseline: `master` at `2b5bb52` (merge of PR #10).

## Verdict

**NOT READY TO CLOSE.** The audit found one unmet approved contract row on `master` (`2b5bb52`):

- **§22, "Entra provisioning results: add the assigned `Username`"** (IAM-9.3 scope by §25). Neither the directory-provisioning batch result nor the access-request approval result carried the username. No ruling waives the row.

**Correction status (§6).** The row is implemented on branch `iam-9-provisioning-username-result` (finding C-1). That branch is **not merged and not pushed**. IAM-9 closure stays pending, in this order: the correction merges, post-merge CI is green, and the owner accepts closure. Until the correction merges, `master` still does not meet §22. After it merges, this audit knows of no unmet IAM-9 requirement; implementation is then complete and closure awaits owner acceptance.

Closing IAM-9 does not close RC1. It does not close, accept or schedule away any finding in §4, and it does not narrow RC1 requirement 4.

## 1. Merge and CI checkpoint

| Check | Result |
|---|---|
| PR #10 `IAM-9.5: governance adoption and administrator username rename` | `MERGED` 2026-10-06 02:33 UTC; merge commit `2b5bb5214ed9895a63a756c1ec320453b663128c` |
| Reviewed head `ed38362528ce08733fc67868ca9d1f9493419c60` | Second parent of `2b5bb52`; `git merge-base --is-ancestor` holds; `git diff ed38362 2b5bb52` is empty (the merge adds nothing) |
| `origin/master` | `2b5bb52` |
| Post-merge CI (run `37404724761`, push, `2b5bb52`) | `success`. `build-and-test` 13/13 steps green; `browser-security` 15 steps green, 1 skipped (results upload, which runs only on failure) |
| Earlier `master` run on `f319a35` (PR #9 merge) | `failure`: `build-and-test` cancelled with no steps recorded; `browser-security` green. Superseded by the green run on `2b5bb52`, which contains it |
| IAM-9 packages on `master` | 9.1 #5 (`9d65a4f`), 9.2 #6 (`b86b226`), 9.2a #7 (`c7b89b6`), 9.3 #8 (`a80b439`), 9.4 #9 (`f319a35`), 9.5 #10 (`2b5bb52`). The gate and QW-1 arrived with #4 (`4449586`) |

**What CI ran.**

- The `build-and-test` job ran with a SQL Server 2022 service container, and every test assembly reported zero failures.
- **Whether the soft-skip SQL suites executed against SQL Server in CI is unverified.** The suites are `UsernameFoundationSqlServerTests` (11 tests), `HistoricalActorNormalizationSqlServerTests` (9) and `UserSearchSqlServerTests` (9). When SQL is unreachable they return early and report PASS (see N-1). `dotnet test --verbosity normal` does not print the "EXECUTED against SQL Server" or "NOT EXECUTED" marker they write, the CI log contains neither marker, and CI does not set `APPERTURE_REQUIRE_SQL_TESTS=1`. An earlier version of this audit said their 4–7 s durations showed real execution. That was an inference, not evidence, and it is withdrawn.
- **What CI does show.** The Management Host HTTP suites have no soft skip: their fixture fails without SQL, and a derived class asserts SQL is available. They passed in the same job: `ManagementHostUsernameRenameHttpTests`, `ManagementHostSessionIdentityHttpTests` and `ManagementHostUserSearchHttpTests`. That shows SQL Server was reachable through the governance connection used by those fixtures. It does not prove which connection the soft-skip suites resolved.
- **Explicit local SQL evidence** for the soft-skip suites, run with the skip turned into a failure, is recorded in §6.

**Blocking and opt-in browser evidence.**

- `browser-security` is the blocking SEC-1 suite. It ran 335 tests: 330 passed and 5 were skipped.
- The 5 skips are exactly `GovernanceUserIdentityEvidenceTests`. They are opt-in: they need `APPERTURE_UI_EVIDENCE=1` as well as `APPERTURE_BROWSER_TESTS=1`, and CI sets only the second (`UiEvidenceGate.cs`).
- The real-browser governance evidence is therefore **not** part of CI. Gate §24 does not require browser tests beyond the SEC-1 suite.

## 2. Requirement-to-evidence matrix

UT = `tests/Apperture.UnitTests`, IT = `tests/Apperture.IntegrationTests`, UIC = `tests/Apperture.Management.Ui.Client.Tests`, UIS = `tests/Apperture.Management.Ui.Shared.Tests`. Source paths are abbreviated as in the gate §3. Every named test class passed in run `37404724761`.

### 2.1 IAM-9.1 — username, ledger, generation, backfill

| Requirement | Source | Evidence | Status |
|---|---|---|---|
| R1/R2 format, normalization; one reserved list (R4) | `Core.Domain/Identity/UsernameRules.cs`; `UsernamePolicy.cs`; `EmergencyAccessIdentity.cs` delegates | UT `UsernamePolicyTests` (format table, boundaries, user-id shape, non-ASCII, reserved list exact, route words accepted, grandfathered values), `UsernameRulesTests` | Met |
| R5/R6 generation, collisions, exhaustion | `UsernamePolicy` (`GenerateBase`, `SanitizeCandidate`, `CollisionCandidates`); `UsernameService` | UT `UsernamePolicyTests.Generator_fixed_vectors`, collision tests; `UsernameServiceTests.Generation_exhaustion_fails_and_reserves_nothing`; IT `Concurrent_generation_of_the_same_base_yields_distinct_correctly_owned_names` | Met |
| R3 explicit CI collation, unique index, ledger PK | `AppertureDbContext.cs`; migrations `Iam9_UsernameLedger`, `Iam9_UsernameRequired` | IT `Username_columns_declare_the_explicit_case_insensitive_collation` (`sys.columns`), `Users_username_is_unique_case_insensitively_at_the_database`, `Ledger_username_is_the_case_insensitive_primary_key_even_through_raw_sql` | Met |
| R8 ledger (one current name per user, owner forever) | `Iam9_UsernameLedger`; `SqlUserRepository.cs` | IT `Ledger_allows_one_current_name_per_user…`, `Reclaim_is_atomic_and_never_takes_another_users_historical_name` | Met (see N-4) |
| R9 backfill and barrier; direct migrate fails closed | `AppertureDatabaseMigrator.cs:35-44`; `UsernameBackfill.cs`; `PersistenceMigrationHostedService.cs` | IT `Populated_database_migrates_through_the_backfill_barrier_and_reruns_safely` (every gate case, error 50012, re-run no-op), `Duplicate_local_links_fail_the_migration_closed…` | Met |
| R10 Local link mirrors username; one Local link per user | `SqlUserRepository.AddAsync`/`RequireNewUserIdentity`; `UX_IdentityLinks_Local_UserId` | UT `UsernameFoundationContractTests` (`AddAsync_rejects_a_user_without_an_assignable_username`, mirror, two links); IT `One_local_link_per_user_is_enforced_and_entra_uniqueness_is_unchanged` | Met |
| Every creation path (§4.4) assigns a username | `UserManagementService`, `UserProvisioningService`, `FirstAdminBootstrapService`, `BootstrapPlatformAdminAssignment`, `EmergencyAccessService`, `DevTestUserBootstrapHostedService`, `HarnessSeeding` | One UT per production path (admin create, directory, access request, first admin, config bootstrap Local and Entra, Recovery Access, dev user) | Met (see N-5) |
| Rename and canonicalization atomic with link | `UsernameService.RenameAsync`; `SqlUserRepository.cs:172-302` | IT `Rename_moves_username_ledger_and_local_link_together_or_not_at_all` (fault injected), `Recovery_access_canonicalization_uses_the_same_rename…` | Met |

### 2.2 IAM-9.2 — session, principal and actor identity

| Requirement | Source | Evidence | Status |
|---|---|---|---|
| R11 claims: `NameIdentifier = UserId`, `session_format = 2`, username | `ApplicationSessionClaims.cs`; `LocalSessionIdentity.cs`; `AuthController.cs`; `EntraOpenIdConnectOptionsConfigurer.cs` | UT `ApplicationSessionIdentityTests`, `AuthControllerSessionIdentityTests`, `EntraStepUpCallbackTests.A_normal_entra_sign_in_issues_the_user_id_session…` | Met |
| R12 format gate, no database work, one-time sign-out | `ManagementCookieAuthentication.cs:44-51` | UT `ApplicationSessionFormatGateTests`; IT `Pre_upgrade_local_and_entra_cookies_are_rejected_once_and_touch_no_identity_link` | Met (see N-3) |
| Mapper: no `oid`/`sub`/name fallback | `ClaimsPrincipalToActorMapper.cs:50-71` | UT `ApplicationSessionIdentityTests` (four no-fallback cases); `SqlPrincipalToUserResolverTests.Actor_without_an_identity_source_is_not_resolved_by_any_legacy_fallback` | Met |
| R13 resolver by user id plus link binding; unlink ends session, rename does not | `SqlPrincipalToUserResolver.cs` | IT `An_entra_session_resolves_to_the_same_user_id_as_a_local_session_and_ends_when_its_link_is_removed`, `Removing_the_local_link_ends_the_local_session…`, `A_username_change_does_not_end_an_existing_local_session`, `A_legacy_break_glass_session_authorizes_nothing_once_its_local_link_is_removed` | Met |
| Recovery Access MFA; legacy break-glass; pending-MFA ticket by user id | `NativeMfaService`; `PendingMfaLoginTicket(UserId, …)` | IT `Recovery_access_needs_its_second_factor…`, `The_legacy_break_glass_endpoint_issues_a_user_id_session…`; UT `The_pending_mfa_ticket_identifies_the_user_by_user_id_only` | Met |
| `ActorLiterals` | `Core.Application/Services/ActorLiterals.cs` | UT `ActorLiteralsTests` (incl. every hard-coded production actor id is a known literal) | Met |
| Release notes (§12.4, §20.4) | `deploy/README.md` "Who-did-what history after upgrading", "Sign-in sessions after upgrading" (key deletion, not rotation) | Read | Met |
| SEC-1 regression row | — | `browser-security` 330 passed; `BrowserRequestTrustHttpTests`, `EntraStepUpSecurityGuardTests`, `ReturnTargetCorpusPolicyTests` green | Met |

### 2.3 IAM-9.2a — historical non-audit actors

| Requirement | Source | Evidence | Status |
|---|---|---|---|
| Rules 1–5, ambiguity, literals first | `HistoricalActorClassifier.cs` | UT `HistoricalActorClassifierTests` (15) | Met |
| Exactly the 13 columns; audit log untouched | `HistoricalActorNormalization.cs:63-78` | UT `HistoricalActorNormalizationScopeTests.The_scope_is_exactly_the_thirteen_approved_columns`; IT `Every_approved_column_is_normalized_by_the_gate_rules_and_counted_in_rows` (per-column counts; audit and identity tables unchanged) | Met |
| Re-run safety; failure policy | `PersistenceMigrationHostedService.cs` | IT `A_second_run_changes_nothing…`, `Legacy_values_written_again_after_a_rollback…`, `An_interrupted_run_converges…`, `A_normalization_failure_fails_host_startup_keeps_committed_progress_and_converges_after_repair` | Met |

### 2.4 IAM-9.3 — user reference and search

| Requirement | Source | Evidence | Status |
|---|---|---|---|
| R14 `UserReferenceDto(UserId, Username, DisplayName)`, no status | `IUserReferenceResolver.cs`; client copy `IManagementClient.cs` | UT `UserReferenceContractTests`; UIS `UserRefSourceContractTests.UserRef_does_not_extend_the_reference_contract_with_status_or_avatar` | Met |
| Batch resolver, server-side composition, no bulk lookup endpoint | `UserReferenceResolver.cs`; one query in `SqlUserRepository` | UT `UserReferenceResolverTests`; IT `References_resolve_in_one_query…` | Met. No IAM-9 response embeds `UserReferenceDto` (§22 adds `Username` fields instead); first consumers are LST-1.3 and AUD-1 (N-8) |
| R16 search: prefix or substring, escaping, ordering, clamp, Recovery Access excluded, no email/UPN/id, never audited, `user.read` | `UsersController.cs:47-67`; `SqlUserRepository.cs`; `UserSearchCriteria.cs` | IT `ManagementHostUserSearchHttpTests` (9), `UserSearchSqlServerTests` (8); UT `UserSearchCriteriaTests` | Met (see N-6) |
| §22 `Username` on `UserListDto`, `UserDetailDto`, `MeResponse` (both copies), `RoleAssignmentHolderDto` | `UsersController.cs`; `AuthController.cs`; `GovernanceAdminModels.cs`; client copies | UT `UserReferenceContractTests`; IT `User_detail_and_me_expose_the_current_username` | Met |
| §11 `CreateUserRequest.Username`, `ExternalId` alias, both supplied → `username_mismatch`; Local link name optional and must match | `UsersController.cs`; `UserManagementService.cs` | UT `UsersControllerCreateUserTests`, `UsersControllerIdentityLinkTests`, `UserManagementServiceIdentityLinkTests`; IT `Create_local_user_by_username_by_alias_and_with_a_conflict`, `Local_identity_link_may_omit_the_name_and_must_match_the_username` | Met |
| **§22 Entra provisioning results carry the assigned `Username`** | `UsersEntraProvisioningController.cs:267-276` (`EntraProvisioningPersonResultDto`), client copy `IManagementClient.cs:2193`, `UserProvisioningResult` | None on `master` | **Not met on `master` (C-1).** Implemented on `iam-9-provisioning-username-result`, pending merge; evidence in §6 |

### 2.5 IAM-9.4 — `UserRef`

| Requirement | Source | Evidence | Status |
|---|---|---|---|
| R15 compact and rich modes; accessible name `Display Name (username)`; username visible in rich; link only when the caller grants navigation, through `DrillInLink`; unresolved actor; empty status slot; no tooltip primitive | `Ui.Client/Molecules/UserRef.razor` | UIC `UserRefRenderTests` (28 cases); UIS `UserRefSourceContractTests` | Met. The compact markup was corrected in IAM-9.5 after real-browser evidence showed Chromium computing stray spaces in the name |

### 2.6 IAM-9.5 — governance adoption and administrator rename

| Requirement | Source | Evidence | Status |
|---|---|---|---|
| §23 surfaces: Users list (username, search), User detail (rich header, username metadata, id in `TechnicalDetails`, rename), effective access, role holders, account-menu fallback, New user live validation | `UsersList.razor`, `UserDetail.razor`, `UserEffectiveAccess.razor`, `RoleDetail.razor`, `UserAccountMenu.razor`, `UserNew.razor` + `UsernameField` | UIC `UsersListUiTests`, `UserDetailUiTests`, `UserEffectiveAccessUiTests`, `RoleDetailHoldersUiTests`, `UserAccountMenuUiTests`, `UsernameFieldTests`; UIS `Iam2UserCreationUiSurfaceTests.Targeted_governance_surfaces_do_not_print_the_raw_user_id_outside_technical_details` and five more; `UserRefSourceContractTests.UserRef_is_adopted_only_on_the_governance_surfaces_the_gate_names` | Met |
| `PUT /api/users/{id}/username`, `user.manage`: 200 new, 200 reclaim, 409 `username_taken` (current and retired), 409 `UnsafeOperation` for Recovery Access, 400 `username_invalid`/`username_reserved` incl. a grandfathered reclaim target | `UsersController.cs:272-312`; `UserManagementService.cs`; `UsernameService.cs` | IT `ManagementHostUsernameRenameHttpTests` (one test per case); UT `UsersControllerRenameUsernameTests`, `UserManagementServiceRenameUsernameTests` | Met |
| Audit `governance.user.username.changed` with previous and new names taken from the rename transaction; no audit for a no-op | `GovernanceAuditActionNames.cs:16`; `UsernameRenameResult` | IT `A_successful_rename_writes_one_audit_row_with_the_previous_and_new_names_and_a_no_op_writes_none`; UT `The_audit_takes_the_previous_name_from_the_transaction_that_made_the_change` | Met (structured details: §4, AUD-1) |
| Credentials, roles and sessions unchanged; new name signs in, old name refused | — | IT `Rename_changes_no_password_role_or_other_user_and_keeps_an_existing_session_alive` | Met |
| Concurrency (§20.3) | In-transaction compare of the expected current name; `Users.RowVersion`; both become 409 `username_concurrent_change` | UT `A_rename_that_races_a_concurrent_rename_is_refused_and_never_audits_a_stale_previous_name`; ledger PK and current-name index proven in IT (§2.1) | Met (see N-2) |
| D-19 server-confirmed pending state; stale responses; unknown outcome | `UserDetail.razor` `RenameUsernameAsync`; `UsernameRenameException.OutcomeUnknown` | UIC `UserDetailUiTests` (busy and non-resubmittable while pending; six late-response cases; older rename never clears a newer pending state; page gone; unconfirmed rename explained; failed reload after a confirmed rename not reported as a failed rename); `ManagementClientRenameUserTests` (only a server error or timeout is an unknown outcome) | Met |

## 3. Manual and opt-in evidence

- **Smoke and NVDA retest (user-reported).** The user reported that the manual smoke and the NVDA screen-reader retest of the governance identity surfaces passed. This is user-reported manual evidence. No transcript, NVDA version, browser or browser version was recorded, and this audit claims none. The earlier discrepancy and the first retest are recorded in gate §29.4.
- **Opt-in real-browser evidence (not CI).** The five `GovernanceUserIdentityEvidenceTests` drive real Chromium. They record computed accessible names from the browser accessibility tree (not assistive-technology output), focus order, contrast and light/dark screenshots.
- **Local handback output.** The output is in the git-ignored `artifacts/iam95-visual/` on the development machine: 27 PNGs and `evidence.log`. It is not committed. The latest output (17:29, 2026-10-05) matches the time of `12880aa` and predates `16656c1`, which changed the unknown-outcome rename message. That message is proven by the bUnit tests above, not by a screenshot.

## 4. Findings

### 4.1 Blocking IAM-9 closure

**C-1 — Entra provisioning results omit the assigned username (§22).**

- **Severity:** Low product impact. An administrator sees the generated name only by opening the user afterwards. It blocks closure because it is an unmet approved row.
- **Owner:** IAM-9 (an IAM-9.3 contract row).
- **Status:** corrected on branch `iam-9-provisioning-username-result`, not merged. The correction and its evidence are in §6. The fix as originally proposed:
  - Add the username to `UserProvisioningResult` and populate it from the created user in `UserProvisioningService`, for both the directory and the access-request paths.
  - Add `string? Username` to `EntraProvisioningPersonResultDto`, server and client copies; it is null on a failed row.
  - Set it in `UsersEntraProvisioningController` for the batch and approve responses.
  - Extend `UserReferenceContractTests` with a JSON round-trip.
  - Add an HTTP or controller test asserting that a provisioned person's result carries the generated name, which equals the stored `Users.Username`.
  - Showing it in the provisioning result UI is optional; §23 does not require it.
  - No schema or authorization change.

### 4.2 Open findings carried forward (owner named; not dispositioned by IAM-9)

| Id | Finding | Severity | Owner and tracking | Conflict with IAM-9 acceptance? |
|---|---|---|---|---|
| F-1 | Governance-read capability checks `user.read` on Role; APIs enforce `user.read` on User (gate §29.1) | Medium | IAM-10.0. Program gate §8; gate §29.1 holds reproduction and acceptance criteria. Not yet in the IAM-10 package text of the RC1 audit plan; IAM-10.0 must take it in from §8 | **No.** §15 links a reference when the viewer holds `user.read`. Every surface that adopts `UserRef` sits behind the same capability, so a `user.read`-on-User holder sees the forbidden state, never an unlinked reference. §16 "IAM-9 does not widen `user.read`" holds. The defect predates IAM-9 |
| F-2 | `role.manage` implies `user.manage`, `credential.manage`, `permission.assign`, `role.assign` and `auth.*` for custom roles (X-01; gate §29.2) | High (known escalation path) | IAM-10.0 and IAM-10.2; disposition before RCV-1 closes. Program gate §8; RC1 audit plan X-01, F.2, IAM-10 and IAM-10.2 | **No conflict with IAM-9 criteria.** The rename endpoint requires `user.manage`, as §9.5 rules, and the evaluator grants it through the implication, which IAM-9 did not create. **Conflicts with RC1 requirement 4.** It stays open. IAM-9 closure is not risk acceptance and creates no exception. The specific denial test (a `role.manage`-only role no longer gains `user.manage`, `credential.manage` or `auth.*`) is stated in gate §29.2 and program gate §8 but only implied in the audit plan's IAM-10 test list |
| F-3 | `governance.user.username.changed` details are free text; grandfathered names can make them ambiguous (gate §29.3) | Medium | AUD-1. Program gate §8. Not yet in the AUD-1 package text of the RC1 audit plan | No. §6 excludes audit schema change from IAM-9 |
| F-4 | Audit actor backfill (`ActorKind`, user id, original display) for `AuditLogs` | Medium | AUD-1.1. Gate §6, §17 and §26 ("ship AUD-1.1 in the same release"); audit plan F.3 and the IAM-9.2a note | No. Explicitly AUD-1.1 |
| F-5 | Lifecycle status: additive `UserReferenceDto.Status`, the reserved `UserRef` status slot, the resolver status and session-version check | — (planned scope) | IAM-11. Gate §13, §14, §15; audit plan IAM-11.1 (resolver check). The `UserRef` slot is recorded in the design-system catalogue only | No |
| F-6 | A revoked former Recovery Access user stays an ordinary administrator named `breakglass` / "Recovery Access" (gate §19) | Medium | IAM-11 (emergency-path gate). Previously recorded only in gate §19; now also tracked in program gate §8 | No. Existing behaviour |
| F-7 | Login and forgotten-password timing oracles (gate §21) | Medium | IAM-11. Audit plan section J (throttling in IAM-11.4 bounds both; constant time is a should-fix) | No |
| F-8 | Operational `UserRef` adoption (gate §23 "later adoption" list) | — (planned scope) | LST-1.3 and AUD-1. The LST-1 gate's LST-1.3 package owns "the surfaces IAM-9 §23 assigns to this package". Gate §23 says "LST-1.3 or AUD-1" without a per-surface split | No. §6 and §23 exclude it. **Tracking gap, now assigned:** the planning records imply audit timeline and entity activity under AUD-1.5, the team-members table under LST-1.4, and the rest under LST-1.3. `RunOperationItemizationPanel.razor` is named only in gate §23. **AUD-1.0 must establish the explicit per-page ownership split with LST-1.3** before AUD-1 or LST-1.3 adopts `UserRef` anywhere (program gate §8) |
| F-9 | The `FieldValidationIndicator` icon (invisible validation control) renders at 0x0, yet it is a focusable button: a phantom tab stop on fields using `ValidatedFieldShell` (existing password fields included) | Medium (accessibility) | **RCV-1 accessibility baseline (RC1 requirement 8).** Product-owner decision, recorded 2026-10-05 and in program gate §8. Originally found by IAM-9.5 browser evidence and recorded only as the rationale for `UsernameField` in `management-ui-design-system.md` | No. `UsernameField` avoids it |
| F-10 | `text-message-error` is not a defined class, but five account and run surfaces use it | Low (visual) | **RCV-1 accessibility baseline.** Same decision as F-9 | No |
| F-11 | Entra self-link rows have no UPN, mail or display snapshots; the "already has another Entra link" check ignores tenant (gate §21) | Low | Future external-organization access work (D-27 deferral). Recorded only in gate §21 | No |
| F-12 | Username-based routing and grandfathered names | — | Deferred with no owning epic (gate §9.1, §9.4) | No |

The RC1 audit plan still says, in its IAM-9 non-goals, that rename "ships in IAM-11 if approved". It shipped in IAM-9.5 under Q1. The audit plan is a working record and the gates govern, so it is left unedited.

### 4.3 Non-blocking evidence notes

| Id | Note | Owner |
|---|---|---|
| N-1 | SQL tests soft-pass when SQL Server is unreachable. CI does not set `APPERTURE_REQUIRE_SQL_TESTS=1`, and its log carries no execution marker, so CI execution of the soft-skip IAM-9 SQL suites is unverified (§1). Local execution is evidenced in §6 | RCV-1 (RC1 requirement 8, "no silently passing SQL tests") |
| N-2 | Two renames racing on real SQL Server is proven only with a unit fake. The arbiters (ledger PK, current-name index, fault-injected atomicity) are proven on SQL Server | None required |
| N-3 | "Existing local user signs in after migration" is proven at the repository lookup the sign-in path uses (populated barrier test), not by an HTTP sign-in of a migrated user. The old-cookie test proves the 401 and cookie clearing but does not sign in afterwards | None required |
| N-4 | Ledger `UserId` immutability is a behavioural guarantee tested through every write path. No database trigger blocks a raw `UPDATE`, and the gate does not require one | None required |
| N-5 | `HarnessSeeding` normalizes, but it has no dedicated test; the browser suite exercises it | None required |
| N-6 | The search default (20) and maximum (100) are proven at unit level. UPN non-matching is structural (search reads only `Username` and `DisplayName`) with no dedicated test | None required |
| N-7 | Role holders are composed with one user lookup per holder (from IAM-8). A missing user falls back to the user id as username and display name. That case is unreachable while users are never hard-deleted (D-04) | LST-1.3 when holders adopt the batch resolver |
| N-8 | `IUserReferenceResolver` has no consumer yet | LST-1.3, AUD-1 |

## 5. Next package

After C-1 is fixed and IAM-9 closure is accepted, the program contract (§5, Track A: IAM-9 → AUD-1 → IAM-10 → IAM-11) points to **AUD-1**, beginning with its short design gate, AUD-1.0. AUD-1's hard dependency on IAM-9's actor identity is met.

AUD-1.0 must take in F-3 and F-4, and it must establish with LST-1.3 the explicit per-page ownership split for operational `UserRef` adoption (F-8), starting with the audit timeline. Gate §17 and §26 ask for AUD-1.1 in the same release as IAM-9. Tracks B and C are unaffected:

- QW-1 is on `master`.
- NAV-2, LST-1.1 and SDK-8 have not started.
- Every package starts only on an explicit owner instruction (program gate §9).

## 6. Correction record: C-1, Entra provisioning results carry the assigned username

Branch `iam-9-provisioning-username-result`, created from `2b5bb52` with the audit commit cherry-picked on top. It is **not pushed and not merged**. Scope is gate §22 only: no schema, authorization, identity-matching or status-code change.

### 6.1 What changed

- `UserProvisioningResult` gains `Username`. Both provisioning paths (directory and access-request approval) take it from the `User` that `UsernameService.AddWithGeneratedUsernameAsync` returns after the repository persisted it. The repository stores `Username` verbatim and refuses a non-canonical value, so the reported name is the stored name. Nothing regenerates it, and email, UPN and object id are never substituted.
- `EntraProvisioningPersonResultDto` gains a nullable `Username`, in the server and client copies, serialized as `username`. The field is additive. A client reading an older server gets `null`, and an older client ignores the field.
- `UsersEntraProvisioningController` fills it for the batch endpoint and the approval endpoint.

### 6.2 Behaviour by outcome and endpoint

| Endpoint | Outcome | Status and shape | `username` |
|---|---|---|---|
| `POST /api/users/entra` (batch) | User created | 200. Row `succeeded: true` with `userId` | The stored username |
| | User created and a pending access request reconciled | Same row. The reconciled request id is not part of the DTO | The new user's stored username |
| | Failure: disabled or missing directory user, invalid object id, registration problem, role-assign forbidden, username generation exhausted, or any other provisioning failure | 200. Row `succeeded: false` with `errorCode`; `userId` null | `null` |
| | Identity already a user (`duplicate_identity`) | 200. Failed row; `userId` null | `null`. Unchanged: the row does not identify the existing user |
| | Caller lacks `user.manage` | 403 | n/a |
| `POST /api/users/entra/access-requests/{id}/approve` | User created | 200. `succeeded: true` with `userId` | The stored username |
| | Identity already a user (request is reconciled to that user, then refused) | 409 with an **error body** `{ message, code: duplicate_identity }` and nothing else. It is not a provisioning result and carries no user identity (no `userId`, no `username`) | None. Unchanged |
| | Request not pending or not found, or another provisioning failure | 409 with the same `{ message, code }` error body | None |
| | Caller lacks `user.manage` | 403 | n/a |

**Existing-user outcomes (product-owner clarification, recorded).** Gate §22 asks for "the assigned `Username`" on a result. Neither existing-user outcome returns a result that identifies a user: the batch row has no `userId`, and approval answers 409 with the `{ message, code }` error body, which is not a user identity payload. The product owner ruled that this behaviour is preserved: neither outcome gains the existing user's `username` or `userId`, and no response-contract expansion is requested. Returning that user's identity in a failure would disclose it, which §22 does not ask for. An earlier handback described the approval 409 as having "no body". That wording was imprecise: the response has an error body, and it has no user identity payload. The behaviour is unchanged, and the source (the three `Conflict(new { message, code })` returns in `UsersEntraProvisioningController`) is the reference.

### 6.3 Tests

New and strengthened tests (all pass):

- `UserReferenceContractTests` (serialized contract): `Entra_provisioning_result_carries_the_assigned_username_and_the_client_reads_it`, `A_failed_provisioning_row_has_no_username_on_the_wire`, `The_client_still_reads_a_provisioning_result_from_a_server_that_predates_the_username`, `Entra_provisioning_batch_result_round_trips_usernames_into_the_client_copy`.
- `UsersEntraProvisioningControllerUsernameTests` (controller mapping): batch rows carry the service's username and failures carry none, including the generic-failure branch; approval carries it; both refused-approval forms stay 409 with no result.
- `UserProvisioningServiceTests`: both success paths assert `result.Username` equals the persisted user's username; a lost race for the first candidate reports the stored later candidate (`jane-2`), not the base; a reconciled provision reports the new user's username; approving a request for an existing identity still throws `duplicate_identity`, creates no user and reconciles the request (this path had no test).
- `ManagementHostEntraProvisioningUsernameHttpTests` (real Management Host and SQL Server; fails rather than skips without SQL):
  - Approval returns the stored username.
  - A taken base gets the suffix in the stored value, and the response reports it.
  - An existing identity is 409 with no username, and the request is reconciled to the existing user.
  - A mixed batch reports the stored username for the created person and none for the disabled, existing and missing rows.
  - Each compares the response with the value read back from `Users`.

### 6.4 Positive SQL execution evidence (local, explicit)

Run on the development machine against the dedicated `apperture-test-sql` container, with `APPERTURE_REQUIRE_SQL_TESTS=1` so that any skip fails the run. The SQL server clock (UTC) read 2026-10-06 04:11:25 before the run, and the tests finished about four minutes later.

| Evidence | Result |
|---|---|
| Filtered run: the four new HTTP tests plus `UsernameFoundationSqlServerTests`, `HistoricalActorNormalizationSqlServerTests`, `UserSearchSqlServerTests` | 33 passed, 0 failed, **0 skipped** (4 + 11 + 9 + 9) |
| Execution markers written by the soft-skip suites into the trx | 11 `EXECUTED against SQL Server catalog Apperture_Iam9…` lines (`UsernameFoundationSqlServerTests`) and 9 `EXECUTED against SQL Server scratch catalog` lines (`UserSearchSqlServerTests`); **0** `NOT EXECUTED` lines. `HistoricalActorNormalizationSqlServerTests` prints no positive marker; its 9 passes under `APPERTURE_REQUIRE_SQL_TESTS=1` mean it ran, because an unreachable server would have failed it |
| Rows read back from the server after the run (`sqlcmd` against `Apperture_BootstrapHttpTests`, `AssignedAtUtc` after the 04:11:25 marker) | Six ledger rows written by the new tests, for example `….jane` and `….jane-2` (the suffixed name), `….ada` (batch). Each approved request's `ProvisionedUserId` equals the owner of the ledger name the API reported: `rq-p7a762fd525` → `30280ebaf8e94ca8ba7f4717` = owner of `p7a762fd525.jane`; `rq-p11a693289a` → `f6f2979fde454524b77bc934` = owner of `p11a693289a.jane-2` |

This is **local evidence only**. The CI execution detail for the soft-skip suites remains unverified (§1). The new HTTP tests cannot soft-skip in CI, because they throw when SQL is unavailable, and they compare against rows read from the `Users` table.

### 6.5 Other validation

- `dotnet build Apperture.slnx`, isolated under the repo-local `artifacts/` path: 0 errors. A first attempt failed on one compile error in a new test (an `out` variable inside an assertion expression). It was fixed, and the rebuild passed.
- Focused unit run: 52 passed, 0 failed (`UserProvisioningServiceTests`, `UserReferenceContractTests`, `UsersEntraProvisioningControllerUsernameTests`, `EntraAccessRequestServiceTests`, `UsernameServiceTests`).
- Wider affected packages, all with 0 failed and 0 skipped: `Apperture.UnitTests` Governance, Persistence and ControlPlane namespaces (1,824 passed); `Apperture.ArchTests` (38); `Apperture.Management.Ui.Shared.Tests` provisioning, user-creation and `UserRef` source contracts (24); `Apperture.Management.Ui.Client.Tests` provisioning, Entra and `ManagementClient` tests (30).
- Not rerun, because the correction touches nothing they cover: the full `build-and-test` suite and the SEC-1 `browser-security` suite. Both run in CI on the PR.

### 6.6 Remaining unmet IAM-9 requirements

None known on the correction branch. On `master` §22 remains unmet until the correction merges. IAM-9 closure stays pending that merge, green post-merge CI, and owner acceptance. The findings in §4.2 stay open and are untouched by this correction. In particular the permission and security findings F-1 and F-2 remain open: F-2 conflicts with RC1 requirement 4, and no risk acceptance has been requested or granted.
