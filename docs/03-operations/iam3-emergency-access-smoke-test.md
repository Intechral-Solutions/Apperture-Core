# Emergency Access live smoke test

Use only supported UI, API, and CLI/PowerShell paths. Do **not** open SQL Server and insert or edit users, hashes, or MFA rows. Do not pass passwords or authenticator secrets on the command line.

You do **not** need a second environment. Provisioning writes the system-managed Recovery Access user into the same instance you already use. Your existing Entra Platform Administrator stays; it is not replaced.

## Canonical identity

```text
Emergency Access
  → system-managed account: Recovery Access
  → username: breakglass
```

Username and display name are not configurable. Do not pass `--username` or `--display-name` to provision.

The account is omitted from Config → Governance → Users. That is an operational boundary, not a secrecy guarantee — assume the username is publicly guessable. Use a strong unique password and native MFA. Do not use Recovery Access as a daily administrator account. Do not link it to Microsoft Entra. Do not put it through ordinary account-recovery or onboarding flows.

## What “without SQL” means

The emergency CLI uses the bootstrap SQL connection (the same database Management.Host already talks to). You never write T-SQL. You still need that database running, just as you do for normal Management.Host.

## Same-instance setup

1. Restart **Management.Host** (or the Aspire app) so migration `20260831120000_Iam3_EmergencyAccessNativeMfa` applies if it has not already, and so the Recovery Access canonicalization hosted service can upgrade an already-designated emergency user **in place** (same `UserId`, credentials, TOTP, and recovery codes). It does not revoke or recreate the account.
2. Point the CLI at **that same database**. The CLI is a separate process and does not inherit Aspire’s injected connection unless you set it.

From the repo root, for the usual Aspire SQL (port **14333**, database **Apperture**):

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__DefaultConnection = "Server=localhost,14333;Database=Apperture;User Id=sa;Password=<your-aspire-sql-password>;TrustServerCertificate=True"

dotnet run --project src/Apperture.Cli/Apperture.Cli.Host -- emergency status
```

Use the Aspire `sql-password` parameter (see AppHost `Parameters:sql-password`), not a password you invent for the emergency user.

If `status` reports LocalDB (`(localdb)\mssqllocaldb`) or a different catalog than Management.Host, stop and fix the connection string. Provisioning into the wrong database looks like “it worked” and then the UI will not see the user.

If an emergency account already exists, `status` should show `Account: Recovery Access` and `Username: breakglass` after canonicalization. If it reports a username conflict, another user already owns `breakglass` — do not continue until that conflict is resolved; the previous recovery path is left unchanged.

If no emergency account exists yet, confirm `Healthy: False` and `Fallback safeguard required: True` before you enroll MFA.

## What will change on this instance

| Step | Effect on your current admin |
|---|---|
| `emergency provision` (password only; skipped if Recovery Access already exists) | Adds the canonical Recovery Access user, or recognizes the existing designated account without changing its password. Fallback still protects your last ordinary local admin until MFA is healthy. |
| Host restart / `emergency status` (existing designated user) | Renames display name to Recovery Access and local username to `breakglass` in place when there is no collision. Credentials and MFA are preserved. |
| `emergency enroll-mfa` (healthy) | Emergency posture becomes valid. **The next proven Entra sign-in** of an ordinary Platform Administrator will disable **that** user’s local password. Entra sign-in itself still works. |
| `emergency revoke --confirm-fallback` | Clears designation. IAM-2 last-admin local protection returns. Do **not** run this as part of the system-managed identity refinement. |

Do not designate your daily Entra admin as the emergency account if you want to keep that person as an ordinary administrator.

## Procedure

1. **IAM-2 fallback before emergency setup**  
   With `emergency status` showing `Healthy: False` and `Fallback safeguard required: True`, sign in as the only Entra-independent Platform Administrator with Microsoft, then confirm Account → Security still shows local sign-in retained for platform recovery. Skip this step if Recovery Access is already healthy on this instance.

2. **Provision or recognize Recovery Access**  
   ```text
   dotnet run --project src/Apperture.Cli/Apperture.Cli.Host -- emergency provision
   ```  
   If the account already exists, the command prints the existing user id and does not prompt for a new password. Otherwise type the new local password, Enter, then confirm it (stdin; nothing on the command line). Then, if authenticator enrollment is not already complete:
   ```text
   dotnet run --project src/Apperture.Cli/Apperture.Cli.Host -- emergency enroll-mfa
   ```  
   Add the printed `otpauth://` URI (or manual key) to an authenticator app, enter the 6-digit code when prompted, and store the printed recovery codes. `emergency status` should show `Healthy: True`, `Account: Recovery Access`, `Username: breakglass`.

3. **Emergency local login requires native MFA**  
   Sign out. Sign in with username `breakglass` and the password. The UI must ask for an authenticator code. Password alone must not open the application. Failed login must not disclose that this is the emergency account.

4. **Recovery code works exactly once**  
   Sign out. Sign in with password, then a recovery code. The session succeeds. Repeat with the same code; it must fail.

5. **Usable without Entra**  
   With Microsoft sign-in unused (or the directory unreachable), the Recovery Access local + MFA path still signs in.

6. **Ordinary last admin no longer kept for recovery**  
   Enabling Recovery Access does **not** immediately disable `tenny` (or any other ordinary admin). Sign **out**, then sign in as that ordinary Platform Administrator through **Microsoft Entra** (not local). That proven Entra sign-in disables **that** user’s local password. Recovery Access remains enabled and is **not** listed under Governance → Users. Config → Authentication → Overview still shows Emergency Access status.

7. **Recovery Access remains protected**  
   Ordinary Governance user administration cannot open, disable, or unlink Recovery Access. Creating a user named `breakglass` (any casing) is rejected. Account → Security for Recovery Access does not offer Connect Microsoft account or a self-service password form.

8. **Microsoft login/logout still work**  
   Ordinary Entra sign-in and sign-out on Management.Host succeed as in IAM-2.

9. **Ordinary local login has not regressed**  
   A non-emergency local user without MFA enrollment still signs in with username and password only.

## Leave it active

After a successful setup, **leave Recovery Access designated**. That is the production recovery path. Do not revoke unless you intend to undo IAM-3 and return the installation to IAM-2 last-administrator local protection.

## Undo (still this instance)

Only if you deliberately want IAM-2 fallback again:

```text
dotnet run --project src/Apperture.Cli/Apperture.Cli.Host -- emergency revoke --confirm-fallback
```

That clears emergency designation. It does not delete the `breakglass` user. After revoke, sign in with Entra again if you need IAM-2 to restore your ordinary admin’s local credential for recovery.
