# AUP-1 — Account, Profile, and Personal Preferences

**Status:** complete
**Date:** 2026-08-18
**Supersedes:** the placeholder `/account` and `/preferences` surfaces, and the
`AccountDeferredFeaturesDto` avatar/profile deferral announced by `GET /api/auth/me`.
**Hands off to:** UIX-7 — Visual Language and Theme Overhaul.

---

## 1. Executive summary

AUP-1 replaces Apperture's placeholder account surfaces with a functional personal control plane. A
signed-in operator can now edit their display name, choose a curated avatar illustration or upload a
photo, set appearance, timezone, and clock preferences, and turn email delivery on or off per
notification. Every setting is persisted server-side, scoped to the authenticated operator, and
reflected in the shell without a reload.

Three things drove the design more than anything else:

1. **There was no file or image infrastructure at all.** No storage abstraction, no upload endpoint,
   no image library. Both were introduced deliberately narrow: a profile-image-only store interface
   with a SQL implementation, and SkiaSharp confined to a single normalization class.
2. **The notification pipeline already had exactly one user-controllable dimension.** Email opt-in and
   opt-out are enforced in `SqlNotificationRecipientResolver`; the in-app inbox is materialized from
   the resolved recipient set and has no per-user switch. The preference centre therefore exposes one
   editable channel and states the other as required, rather than rendering a control that would do
   nothing.
3. **A user timezone column already existed and nothing wrote to it.** `Users.DisplayTimeZoneId` was
   added by SWM-5 and read by `OperatorDisplayTimeZoneService`, with a code comment deferring the
   writer to "the profile overhaul". AUP-1 is that writer. No second timezone model was created.

Scope held: no theme redesign, no generic media subsystem, no Microsoft Graph photo dependency, no
per-user key-value settings store, and no platform settings moved into personal preferences.

---

## 2. Existing account state replaced

| Surface | Before | After |
|---|---|---|
| `/account` | Read-only identity, roles, password change, and a "Coming later" panel | Redirects to `/account/profile` |
| `/preferences` | Information-only stub: "Application preferences beyond appearance are not implemented yet." | Redirects to `/account/preferences` |
| `GET /api/auth/me` | `Deferred.AvatarManagementDeferred = true`, `ProfileEditDeferred = true` | `Deferred` is `null`; response carries `avatar`, `appearance`, and `timeFormat` |
| Email preferences | `PersonalNotificationEmailPreferences.razor` embedded in the notification inbox | Moved to `/account/notifications` with channel-aware presentation; the inbox links to it |
| Shell avatar | Initials in a grey circle | Real avatar (photo or curated illustration), initials only until the session resolves |
| Developer hub card | "Preferences (deferred) — Theme and profile preferences stub." | "Account preferences — Personal appearance, timezone, and clock settings" |

Files removed: `Pages/Account.razor`, `Pages/Preferences.razor`,
`Organisms/PersonalNotificationEmailPreferences.razor`.

---

## 3. Final account information architecture

```text
/account                  → redirect to /account/profile
/account/profile          Profile: display name, avatar, identity (read-only)
/account/notifications    Notification preferences: per-notification channels
/account/preferences      Appearance & regional: theme, timezone, clock
/account/security         Security: sign-in methods, password, session
/preferences              → redirect to /account/preferences (legacy path)

/me/notifications         Notification inbox (messages, not preferences) — unchanged
```

Sections are defined once in `AccountUiRoutes.Sections` and rendered by `AccountSectionNav` as a tab
strip with real links. The account area is reached from the top-right user menu and is deliberately
not in the sidebar, matching how it worked before.

`/me/notifications` stays separate: it holds messages, not settings. It previously had **no**
breadcrumb rule at all; AUP-1 added one.

---

## 4. Profile model

**Display name is platform-owned.** Apperture stores it and never re-synchronizes it from the identity
provider after account creation, so an operator edit is authoritative and cannot be silently
overwritten. Validation lives in `DisplayNameRules`: trimmed, 1–128 characters, no control characters,
interior spacing preserved, non-Latin scripts accepted.

**Everything else on the profile is read-only** and labelled as coming from the identity provider:
sign-in method, email, and principal are all read from the current request's claims rather than from
storage, because they are properties of the session. Roles and linked identities sit behind a
`Disclosure`; the internal user id and provider object ids sit behind `TechnicalDetails`.

`Users.EmailAddress` exists in the schema but nothing populates it, so the profile surfaces the email
claim instead of implying the platform owns an email field it does not maintain.

---

## 5. Avatar model and security pipeline

### Curated illustrations

Four platform-owned options in `BuiltInAvatarCatalog`: `neutral` (the default), `neutral-bold`,
`short-hair`, `long-hair`. Labels describe visual style, never identity — a test asserts the catalog
contains no demographic vocabulary. Selection is presentation only.

The artwork is inline SVG in `BuiltInAvatarGlyph.razor`: no network request, crisp at any size, and it
follows the theme through `currentColor`. Platform-owned inline SVG is safe; SVG *uploads* are not, and
are rejected.

### Upload pipeline

`SkiaProfileImageNormalizer` treats every byte as hostile. The declared content type and file name are
used only as a cheap pre-filter in `AccountProfileService`; the normalizer itself never consults them.

| Stage | Behaviour |
|---|---|
| Size gate | Rejected above 4 MB, before any decode |
| Codec identification | `SKCodec` decides the real format; JPEG, PNG, WebP only |
| SVG | Rejected implicitly — Skia's image codecs do not decode it, so no markup is ever parsed |
| Dimension gate | Rejected above 8000 px per side or 40 MP total, **from the header before allocating a decode buffer**, which is what stops a small compressed payload declaring a huge canvas |
| Minimum size | Rejected below 16 px per side |
| Orientation | EXIF orientation baked into the pixels, per-orientation matrices written out explicitly rather than composed from rotations |
| Crop | Largest centered square (server-side; no client cropper) |
| Re-encode | Always PNG at 64, 128, and 256 px |
| Metadata | EXIF, ICC, XMP, embedded thumbnails, and appended trailing data are all dropped, because output is drawn from decoded pixels onto a fresh surface |
| Original | Never persisted |

`ProfileImageLimits` holds these as platform constants, not settings: relaxing them is a security
decision, not an operator preference.

### Keys and cleanup

Keys are `avt_` plus 16 random bytes from `RandomNumberGenerator`. They are never derived from the user
or the file name, so a traversal-shaped upload name has no effect and a replacement can never collide
with the image it supersedes. Replacement points the user row at the new key *first*, then retires
everything else — so a cleanup failure leaves a correct avatar and orphaned rows, not a broken avatar.
Choosing an illustration also deletes any stored photo.

---

## 6. Storage design

No file or blob abstraction existed, so AUP-1 introduces the smallest one that covers the requirement:
`IUserProfileImageStore` with `SaveAsync`, `GetAsync`, and `DeleteForUserAsync`. Every method is scoped
by user id, so a leaked key alone cannot read another account's image.

The implementation is `SqlUserProfileImageStore` over a `UserProfileImages` table holding
`varbinary(max)` rows. Storing bytes in SQL means **no environment-specific path is ever written to a
user record** — the user row holds only a logical key — so a future cloud-backed implementation can
replace the store without changing the `Users` schema. At this platform's scale (single-tenant, one row
per user per size, a few kilobytes each) the database is the simpler and more portable choice, and it
needs no filesystem provisioning across development, on-prem, and container deployments.

**Retention:** only derived images are stored. Replacing or resetting an avatar deletes the previous
renditions immediately, and the `UserProfileImages` foreign key cascades on user deletion. Images are
never sent to any third-party service.

---

## 7. Preference persistence

Two stores, chosen to avoid duplicating what already existed:

| Preference | Where | Why |
|---|---|---|
| Appearance, clock format | New `UserPreferences` table, one row per user | `Users` is identity-focused, and preference growth is likely |
| Display timezone | Existing `Users.DisplayTimeZoneId` | SWM-5 already added the column and the read path; a second timezone model would be the duplicate system the epic forbids |
| Avatar | `Users.AvatarKind`, `AvatarOptionId`, `AvatarImageKey` | Avatar is profile identity, not a preference, and keeps `GET /api/auth/me` a single read |

The API presents all of them as one `UserPreferencesDto`, so the split is invisible to clients.

**Absence means defaults.** No preference row resolves to `System` appearance and `System` clock, so the
migration needs no backfill. A corrupted row also resolves to defaults rather than failing to load, but
request payloads are parsed *strictly* — `UserPreferences.TryParseAppearance` reports failure instead of
substituting a default, which is what lets the API reject an unknown value rather than silently coercing
it. No arbitrary preference keys are accepted.

Optimistic concurrency was **not** added: these are single-operator settings on their own row, and
nothing in the existing account infrastructure uses row versions for self-service edits.

---

## 8. Appearance precedence

Resolved by `AppearancePreferenceResolution`, highest first:

1. **the signed-in operator's stored account preference** — `UserPreferences.Appearance`;
2. **this browser's local preference** — `localStorage['apperture.theme']`, which is all an
   unauthenticated or still-loading session has to go on;
3. **the platform default** — `System`.

The operating system setting is **not** a separate tier. It is what `System` resolves to at paint time,
so it applies whenever the winning preference is `System`. A corrupt value at any tier falls through to
the next rather than short-circuiting to the default.

**Flash avoidance.** The host page's pre-WebAssembly script reads `localStorage` and toggles `html.dark`
before Blazor loads. `AppearancePreferenceCoordinator` applies the account preference through
`ThemeService`, which *also* writes `localStorage` — so mirroring the account value there is what makes
the next visit flash-free. On first sign-in with no stored account preference, the browser's existing
choice is promoted to the account so it is not silently lost.

Both entry points persist: the header `ThemeMenu` writes through to the account when authenticated
(silently, since the header is not the place to report a background save failure), and the preferences
page saves explicitly and reports failure inline.

No colours, typography, spacing, or component visuals were changed. That is UIX-7.

---

## 9. Timezone and regional behaviour

**UTC remains execution and storage truth.** The timezone preference affects operator-facing display
only. Schedule timezones are a separate, integration-scoped concept and were not touched.

`OperatorTimeZoneCatalog` builds the selector from the zones the host actually recognizes, so a
selection can never be rejected by the server that offered it. Identifiers are **canonicalized to the
IANA form** — on Windows, `TimeZoneInfo.GetSystemTimeZones()` yields names like "South Africa Standard
Time", but browsers report IANA and Apperture's schedule model already prefers IANA. Offering the
Windows form would store a value that disagrees with browser detection. A test asserts
`Africa/Johannesburg` is present and `South Africa Standard Time` is not.

The selector shows a short curated list until the operator searches, matches on both identifier and host
description, treats underscores as spaces so "Los Angeles" finds `America/Los_Angeles`, and offers
browser detection as a convenience with a clear message when the browser reports a zone the server does
not know.

**Clock format** is included because the UI can consume it consistently: `System` and `Hour24` render
`HH:mm:ss`, `Hour12` renders `h:mm:ss tt`. The date portion stays ISO regardless, and the UTC detail
line stays 24-hour because it is execution truth. It threads through
`IOperatorDisplayTimeZone.ClockFormat` into the existing `OperatorTimestampFormat`, so every surface
using `TimestampDisplay` picks it up.

**Locale and date-format preferences were deliberately excluded.** The application has no translated UI
and formats dates as ISO everywhere, so neither could be consumed consistently.

---

## 10. Notification preference model

### What is actually enforced

Email opt-in and opt-out are enforced during recipient resolution in
`SqlNotificationRecipientResolver` — the `userOptOut` skip and the subscription `IsEnabled` check. The
account page delegates to the same `IOperatorNotificationSubscriptionService` the pipeline reads, so it
cannot advertise a preference that has no effect. Verified at runtime: toggling email off writes a
global override row (`OperationalTeamId` null) and disables the self-subscription, which are exactly the
rows the resolver consults.

Definitions listed are those matching `SubscribableNotificationDefinitions.IsOperatorSelfSubscribable`
— enabled platform and module built-ins. In the dev instance that is four: expected run missing, and the
three sample-module completion notifications.

### Channels

| Channel | Editable | Presentation |
|---|---|---|
| In-app | No | "Always on", with the reason stated in text |
| Email | Yes | On/off checkbox with an explicit accessible label |

The in-app channel is materialized from the resolved recipient set and has no per-user switch. It is
therefore stated as required **in prose** rather than rendered as a disabled checkbox, which would imply
an editable preference that does not exist. A test asserts no disabled control is bound to
`PlatformEditable`.

### Precedence

```text
administrator notification definition exists and is enabled
  → definition is operator-self-subscribable
    → user opt-out / self-subscription
      → channel preference (email only)
        → provider readiness (SMTP configured)
```

A user preference cannot create a notification that does not exist, re-enable a disabled definition, or
override administrator-assigned team routing. The page says so.

---

## 11. Administrator versus user ownership boundaries

| Administrator-owned | User-owned |
|---|---|
| SMTP host, port, credentials, from-address | Whether *I* get email for a given notification |
| Notification definitions, rules, templates | — |
| Team subscriptions and routing defaults | My own opt-out |
| Pipeline enable/disable toggles | — |
| Integration and platform settings | Appearance, timezone, clock |
| Scheduling defaults | — |
| User creation, roles, identity links, credential reset | My display name and avatar |

The preferences page states this explicitly, and a test asserts no account page references
`PlatformSetting`, `ISecretStore`, SMTP keys, integration settings, or scheduling defaults. Personal
preferences must not become a second, unaudited route into instance configuration.

---

## 12. Security and account integration

Authentication was not rebuilt. The Security page integrates what exists:

- **Sign-in methods** — read-only list with last-used timestamps; administrators add and remove them.
- **Password** — the existing `POST /api/auth/local/change-password` flow, moved here from `/account`.
- **Entra-managed features** — when an Entra link is present, the page states that MFA, passkeys, and
  password recovery are managed by the organization's directory. They are described, never rendered as
  Apperture controls. A test asserts no such control exists.
- **Session** — sign out, plus ending the directory session when Entra sign-in is enabled.

No MFA, passkey, or session-management feature was invented.

---

## 13. API contract

All routes are `[Authorize]` and resolve the acting user from the authentication cookie. **No route,
query string, or body on `AccountController` accepts a user id**, so there is no shape of request that
lets one operator read or mutate another's account.

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/account/profile` | Profile + session-derived identity |
| PUT | `/api/account/profile` | Update display name |
| GET | `/api/account/preferences` | Appearance, clock, timezone |
| PUT | `/api/account/preferences` | Replace preferences |
| GET | `/api/account/avatar/options` | Curated catalog (static) |
| PUT | `/api/account/avatar/selection` | Choose an illustration |
| POST | `/api/account/avatar` | Upload a photo (multipart) |
| DELETE | `/api/account/avatar` | Reset to the default |
| GET | `/api/account/avatar/image?size=&v=` | Serve the caller's own rendition |
| GET | `/api/account/notification-preferences` | Channel-aware preferences |
| PUT | `/api/account/notification-preferences/{definitionId}` | Set email preference |

`GET /api/auth/me` gained `avatar`, `appearance`, and `timeFormat` so the shell paints correctly on first
render, and `deferred` became nullable.

Validation failures return RFC 7807 `application/problem+json` via `ValidationProblem`. Enums,
timezone identifiers, and avatar option ids are all validated server-side.

### Avatar serving

The route takes **no image key**. The image is located from the authenticated user's own record, which
removes both key enumeration and any path-shaped input from the surface. `size` is validated against the
generated set; anything else is 404. `v` is a cache-busting token only — a new avatar produces a new
token, which is why a matching token may be cached as immutable.

Responses set `Content-Type: image/png` explicitly, `X-Content-Type-Options: nosniff`, and
`Cache-Control: private, max-age=31536000, immutable`. A user on the built-in avatar gets 404 so the
client draws the illustration.

Existing `/api/me/notification-subscriptions` endpoints are unchanged for compatibility.

---

## 14. Authorization

Self-service needs no new permission: `[Authorize]` plus `IPrincipalToUserResolver` establishes both
identity and scope, following the pattern already set by `MeNotificationSubscriptionsController`. An
authenticated but unlinked principal gets 403, matching `GET /api/auth/me`.

No administrator avatar-management capability was added, because none existed to extend.

### Privacy

Profile photos are personal data. They are served only to the authenticated owner, through a route with
no guessable key, with `Cache-Control: private`. Metadata is stripped, originals are never retained,
previous renditions are deleted on replacement, and nothing is sent to a third-party service. No
demographic or identity inference is made from an uploaded photo or an avatar choice.

---

## 15. Accessibility

- Avatar illustrations are a real `radiogroup` with visible text labels; the picture is never the only
  label, and selection is conveyed by border, background, ring, **and the word "Selected"** — never by
  colour alone.
- Avatar previews are decorative (`alt=""`, `aria-hidden`) because the operator's name is always
  rendered beside them; alt text would be noise.
- Upload states accepted formats and the size limit in help text wired via `aria-describedby`, uses a
  keyboard-reachable `<InputFile>` with no drag-and-drop dependency, and announces errors with
  `role="alert"`.
- Photo removal is confirmed through `ConfirmActionDialog`; a simple illustration change is not, being
  trivially reversible.
- Display-name validation is associated with its field (`aria-describedby`, `aria-invalid`,
  `role="alert"`) and stays inline. Toasts are used for successful saves only.
- Notification channel controls have explicit `aria-label`s; the required channel is explained in text.
- Section navigation is a `nav` with `aria-current="page"`.
- No client-side cropper was built. Server-side centre-crop avoids an interaction that would be hard to
  make keyboard-accessible.
- Section headings are real `h2`/`h3` elements with `aria-labelledby` on their sections.

### Responsive behaviour

Notification preferences are a list with responsive flex direction, not a table: rows stack on narrow
screens with channel labels repeated per row, so no horizontal scrolling is needed. Account forms are
constrained to `max-w-2xl` with full-width inputs below that. A test asserts no `<table>` and no
`overflow-x-auto` on the notifications page.

---

## 16. Automated test results

All suites run on 2026-08-18 against .NET 10 and SQL Server 2022 in Docker.

| Suite | Result |
|---|---|
| `Apperture.UnitTests` | **1564 passed, 0 failed** |
| `Apperture.ArchTests` | **38 passed, 0 failed** |
| `Apperture.Management.Ui.Shared.Tests` | **670 passed, 0 failed** |
| `Apperture.IntegrationTests` | **194 passed, 1 failed** |
| Full solution build | succeeded, 0 errors |
| Management UI Client publish | succeeded |

The single integration failure is
`ManagementHostGovernanceHttpTests.Post_sftp_test_connect_persists_and_updates_readiness_across_unverified_success_failed`,
which requires an SFTP container at `127.0.0.1:2222`. It is an environment dependency unrelated to
AUP-1 and fails identically without these changes.

### New coverage

| File | Count | Covers |
|---|---|---|
| `SkiaProfileImageNormalizerTests` | 17 | Format acceptance, square PNG output, re-encode, **EXIF stripping verified against output bytes**, orientation verified by sampling pixels, centre-crop, SVG/executable/text rejection, truncated image, empty, oversized bytes, decompression-bomb dimensions, undersized, upscale |
| `AccountProfileServiceTests` | 47 | Profile read/update, display-name validation, preference round-trip and strict rejection, avatar selection and upload, key generation ignoring client file names, replacement cleanup, reset, rendition serving, size validation, notification channel model, audit assertions, and cross-user isolation at every step |
| `AccountDomainModelTests` | 33 | Catalog size and neutrality, **no demographic vocabulary in labels**, avatar storage round-trip and corrupt-row fallback, version-token changes, strict preference parsing, display-name rules |
| `Aup1AccountShellTests` | 44 | Route resolution, breadcrumbs for all sections, appearance precedence at every tier including corrupt values, IANA canonicalization, timezone search, clock formatting |
| `Aup1AccountUiSurfaceTests` | 26 | Route declarations, placeholder removal, **no platform-settings leakage**, no controls for unbuilt notification features, accessibility attributes, labelled inputs, responsive layout, provider-owned features not duplicated, no planning labels |
| `ManagementHostAccountProfileHttpTests` | 44 | Auth on every endpoint, unlinked 403, profile round-trip, **client-supplied user id ignored**, preference validation, avatar upload/serve/replace/reset, cache and sniffing headers, SVG and executable rejection, traversal file name, oversized upload, **cross-user avatar isolation against a real second user**, notification round-trip, audit written for display name, **no audit noise for presentation preferences** |

`S9PersonalNotificationPreferencesUiTests` and `S10NotificationDeliveryDiagnosticsTests` were updated
to follow the preference controls to their new home, preserving their original guarantees.

---

## 17. Runtime smoke results

Executed against live hosts (API `:9000`, UI `:9001`, SQL Server on `:1433`) via
`scripts/dev/maintenance/aup1-account-smoke.ps1`, which is committed so the run is repeatable.
**56 checks passed, 0 failed.**

- **Migration** — `20260818090000_Aup1_AccountProfileAndPreferences` applied cleanly to a live database
  alongside 15 other pending migrations.
- **A. Profile** — display name saved, persisted across reload, and reflected on the session response the
  account menu reads. Blank name returned 400 `problem+json`. A client-supplied `userId` in the body did
  not redirect the mutation.
- **B. Built-in avatar** — catalog returned four options with exactly one neutral default; selection
  saved and appeared on the session response; an out-of-catalog id was rejected; reset returned to
  neutral.
- **C. Custom photo** — PNG uploaded and normalized with a `avt_` key; all three sizes served;
  `image/png` + `nosniff` + `private, max-age=31536000, immutable` confirmed on the wire; unsupported
  size 404; replacement issued a new version; SVG-as-PNG, MZ executable, plain text, and a 5 MB payload
  all rejected; a rejected upload left the stored avatar intact; removal returned 404 so the client
  draws the default.
- **D. Appearance** — Light, Dark, and System each saved, reloaded, and appeared on the session
  response. `Chartreuse` rejected.
- **E. Timezone and clock** — `UTC` and `Africa/Johannesburg` accepted; `Hour12` saved and carried on
  the session response; `Mars/Olympus_Mons`, `../../etc/localtime`, and `Not A Zone` all rejected;
  clearing fell back to the browser.
- **F. Notifications** — four configurable definitions listed; in-app reported enabled and not editable
  with a reason on every row; email editable; enable and disable both persisted; turning email off left
  in-app unaffected; unknown definition returned 404.
- **G. UI routes** — all seven routes returned 200 with the Blazor boot script, including both
  redirects.
- **Database verification** — email opt-out wrote a global override row and disabled the
  self-subscription: exactly the rows `SqlNotificationRecipientResolver` reads. Avatar reset left zero
  `UserProfileImages` rows. `Users.AvatarKind`/`AvatarOptionId` populated.
- **Audit verification** — `account.profile.display_name.updated`,
  `account.notification.email.opted_in`/`opted_out`, and `account.profile.avatar.reset` present with
  readable details. **No records** for the many appearance, timezone, and clock changes made during the
  run, confirming presentation preferences do not generate audit noise.
- **Console gate** — host logs contained zero occurrences of `Unhandled exception`, `parentNode`,
  `Symbol()`, `No element is currently associated with component`, `fail:`, or `crit:`. Request counts
  matched the deliberate calls with no repeated profile or avatar fetch loops, and zero unexpected
  401/403/500 responses after authentication.

**Not verified without a browser:** in-browser theme application and flash-on-load behaviour, DevTools
console output, visual rendering at each breakpoint, and drag-free upload interaction were exercised at
the HTTP and source level but not observed in a live browser session. Responsive layout is asserted
structurally rather than visually.

---

## 18. Known limitations

1. **Avatar bytes live in SQL.** Correct at this scale and fully portable, but a deployment with a very
   large operator population would want the cloud-backed implementation the `IUserProfileImageStore`
   seam exists to allow.
2. **Custom avatars are fetched as data URLs.** The API host is a different origin from the UI host in
   development, and a plain `<img>` request would not carry the authentication cookie. Fetching through
   the API client keeps the request authenticated everywhere, at the cost of HTTP caching for that one
   image. The endpoint still sets correct cache headers for same-origin deployments.
3. **No administrator avatar management.** An administrator cannot set another operator's avatar,
   because no such capability existed to extend.
4. **Clock preference only reaches `TimestampDisplay` consumers.** Surfaces that format dates inline
   rather than through the shared component still render 24-hour. That is a pre-existing inconsistency,
   not one introduced here.
5. **No optimistic concurrency on preferences.** Two browsers editing simultaneously means last write
   wins. Acceptable for single-operator settings.
6. **Entra profile photo synchronization is deferred** — see below.
7. **`Users.EmailAddress` remains unpopulated.** The column predates AUP-1 and nothing writes it; the
   profile surfaces the email claim instead.

---

## 19. Deferred capabilities

### Entra profile photo — deferred

Microsoft Graph *is* already referenced, but only for directory discovery
(`EntraDirectoryDiscoveryService` selects `id`, `displayName`, `userPrincipalName`, `mail`). Fetching a
signed-in user's photo needs `User.Read` on the user's own token, and the OIDC configuration requests
only `openid` and `profile`. Adding that means new delegated scopes, token retention for Graph calls,
a re-consent cycle, and a synchronization and staleness policy. The value does not outweigh that scope,
and the epic explicitly says not to add a Graph dependency solely for photos. **Deferred.** If revisited,
`AccountAvatarDto.Kind` is the natural extension point (an `organization` kind alongside `builtIn` and
`custom`) with fallback to the current behaviour.

### Notification capabilities — deferred

None of these exist functionally, and none is exposed as a control:

| Capability | Current state |
|---|---|
| Daily digest | `NotificationDeliveryModes.DigestOnly` exists and is honoured as a *skip* reason in the resolver, but no digest is ever assembled or sent |
| Journal delivery | Not implemented |
| Quiet hours | No code |
| Scheduled delivery windows | No code |
| Vacation mode | No code |
| SMS | No code; `NotificationChannels` has only `Email` |
| Mobile push | No code |
| Escalation as a personal preference | Escalation exists in administrator-owned attention routing, not as a user preference |
| Per-definition in-app opt-out | The inbox is materialized from the resolved recipient set; there is no per-user switch to expose |

The notifications page names digest, quiet hours, and non-email channels in a short "Not available yet"
section so an operator knows they are absent by design rather than hidden.

### Other exclusions honoured

No application-wide theme overhaul, avatar generation, avatar marketplace, generic media management,
SVG uploads, social profile fields, public user directory, messaging, broad i18n, generic user
key-value store, new authentication provider, unsupported MFA or passkeys, generic file-storage
platform, administrator user-management redesign, platform settings migration, or `BusinessRecord` work.

---

## 20. UIX-7 handoff

AUP-1 deliberately produced functional, semantically correct surfaces and changed no visual language.
These are the recommended flagship references for the theme overhaul:

| Surface | Why it is a good reference |
|---|---|
| `/account/profile` | A form with dirty state, inline field validation, read-only identity rows, and progressive disclosure in one page |
| `/account/notifications` | Repeating rows with a per-row control matrix and a responsive stack — the pattern most Management surfaces need |
| `/account/preferences` | Segmented radio-group selection, a searchable selector, and a live preview |
| `/account/security` | Read-only status rows next to a live credential form, and provider-owned information |
| `AccountSectionNav` | Tab-style section navigation, currently the only one of its kind in the shell |
| `AccountAvatar` / `BuiltInAvatarGlyph` | Themed inline SVG following `currentColor` |

Notes for UIX-7:

- **Selection state is currently carried redundantly** — border, background, ring, and the word
  "Selected". The redundancy is an accessibility requirement, not decoration; keep a non-colour signal
  when restyling.
- **`AccountSectionNav` is a new pattern.** If UIX-7 introduces a canonical tab or segmented-control
  primitive, this should adopt it rather than keeping its own classes.
- **Notification rows repeat channel labels per row** for the narrow-screen stack. A visual table
  treatment must preserve a stacked variant.
- **`BuiltInAvatarGlyph` uses `currentColor`** and inherits `text-surface-muted`/`text-muted`. Theme
  token changes will flow through automatically; the artwork itself should not need editing.
- **Avatar circles are sized by a `SizeClass` parameter**, so spacing and scale changes are parameter
  changes rather than component edits.
- The account pages use plain `rounded border border-default bg-surface p-4` sections rather than a card
  primitive, matching the surrounding codebase. If UIX-7 introduces a card component, these are
  straightforward migration candidates.

UIX-7 owns visual identity, typography, spacing, surfaces, cards, forms, tables, light/dark refinement,
navigation chrome, responsive visual language, and the broader Management UI migration.
