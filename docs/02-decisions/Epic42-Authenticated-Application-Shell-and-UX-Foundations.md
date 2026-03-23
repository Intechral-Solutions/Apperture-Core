# Epic 42 — Authenticated Application Shell and UX Foundations

## Status

Implemented.

## Purpose

Establish a truthful, reusable authenticated shell: current user presence from the real API, working theme persistence, toast feedback infrastructure, and light page framing—without redesigning navigation or building full profile/settings/notification centers.

## Summary of implementation

### 1. Management API client (`Apperture.Management.Client`)

- DTOs: `MeResponse`, `LocalLoginRequest`, `LocalLoginResponse` (JSON names aligned with `AuthController`).
- `GetMeAsync`: returns `null` on `401`; throws `HttpRequestException` with `403` when the principal is not linked to a platform user.
- `LocalLoginAsync` / `LocalLogoutAsync`: thin wrappers over `POST auth/local/login` and `POST auth/local/logout`.

### 2. Session and shell presence (`SessionStateService`)

- Scoped service loads `GET auth/me` after first interactive render (from `App.razor`).
- States: `Unknown`, `Loading`, `Unauthenticated`, `Authenticated`, `ForbiddenUnlinked`, `Error`.
- `MainLayout` shows a yellow banner when `ForbiddenUnlinked` is true.
- User identity in the header is limited to `MeResponse` (user id, display name, role ids)—no fabricated profile fields.

### 3. Sign-in / sign-out

- **`/login`**: local username/password when `ShellUi:UseEntraSignIn` is `false` (default). On success, session refresh and redirect to `/`.
- **Entra**: when `ShellUi:UseEntraSignIn` is `true`, sign-in uses a full navigation to `{ManagementHost}/signin` (see configuration).
- **`ShellUi:ManagementHostUrl`**: optional absolute management host base (e.g. `http://localhost:9000`). If empty, derived from absolute `ApiBaseUrl` authority, else SPA `BaseUri`.
- **Sign out**: `POST auth/local/logout` plus session refresh; optional **Sign out (end IdP session)** navigates to `{ManagementHost}/signout` when Entra mode is enabled.

### 4. Theme (`ThemeService` + `wwwroot/js/apperture-shell.js`)

- Preferences: **Light**, **Dark**, **System** (`Apperture.Management.Ui.Shared.ThemePreference`).
- Persistence: `localStorage` key **`apperture.theme`** (`ThemePreferenceParser.StorageKey`).
- **System** follows `prefers-color-scheme` and updates on `matchMedia` change.
- DOM: toggles `class="dark"` on `document.documentElement`; semantic tokens in `wwwroot/css/app.css` (`:root` / `.dark`) drive colors.
- **FOUC**: inline script in `Apperture.Management.Ui.Server/Pages/_Layout.cshtml` runs before Blazor boot using the same key and rules.

### 5. Toasts (`IToastService` / `ToastHost`)

- Singleton `ToastService`; severities: `Success`, `Info`, `Warning`, `Error` (`Apperture.Management.Ui.Shared.ToastSeverity`).
- `ToastHost` in `MainLayout`: fixed stack, auto-dismiss (~6s), manual dismiss.
- Wired on **User detail** (assign/remove role) and **Role detail** (save permissions).

### 6. Page framing

- `PageHeader` (title, subtitle, `ChildContent`, `Toolbar`).
- `PageMessage` + `PageMessageKind` for loading / empty / error / forbidden / info.
- Applied on **Audit timeline**, **User detail (governance)**, **Role detail**, and **Login** / placeholder pages.

### 7. Placeholder routes (honest)

- **`/account`**: explains limited functionality; links to governance users when relevant.
- **`/preferences`**: explains that only header theme is implemented; no fake settings forms.

### 8. Prerender host (`Apperture.Management.Ui.Server`)

- Registers `ShellUiOptions`, `ThemeService`, `IToastService`, `SessionStateService`, `ShellHostNavigation` alongside existing menu/capabilities services.
- Prerender: `ThemeService.InitializeAsync` may no-op under try/catch in `App.razor`; `GetMeAsync` without browser cookies typically yields unauthenticated until the WASM session runs.

## Configuration reference

| Setting | Purpose |
|--------|---------|
| `ShellUi:UseEntraSignIn` | `true` → primary sign-in is host `/signin`; `false` → `/login` (local API). |
| `ShellUi:ManagementHostUrl` | Optional; used for `/signin` and `/signout` when UI and API hosts differ. |

## Testing

- `Apperture.Management.Ui.Shared.Tests`: `ThemePreferenceParserTests`, `AuthMeResponseJsonTests` (sample JSON for `MeResponse`).

## What is intentionally not in this epic

- Full user profile CRUD, settings center, or notification inbox.
- Shell / control-panel navigation redesign.
- Visual pass on every page (only representative surfaces updated).

## Follow-ups (optional)

- Click-outside to close header dropdowns.
- BUnit tests for `ToastHost` / `UserAccountMenu` if the suite gains Blazor component test infrastructure.
- Align prerender `HttpClient` with cookie forwarding if same-site hosted UI should show user on first paint.
