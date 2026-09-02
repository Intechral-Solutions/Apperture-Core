# Emergency access live smoke test

Use only supported UI, API, and CLI/PowerShell paths. Do **not** open SQL Server and insert or edit users, hashes, or MFA rows. Do not pass passwords or authenticator secrets on the command line.

You do **not** need a second environment. Provisioning writes a new emergency user into the same instance you already use. Your existing Entra Platform Administrator stays; it is not replaced.

## What “without SQL” means

The emergency CLI uses the bootstrap SQL connection (the same database Management.Host already talks to). You never write T-SQL. You still need that database running, just as you do for normal Management.Host.

## Same-instance setup

1. Restart **Management.Host** (or the Aspire app) so migration `20260831120000_Iam3_EmergencyAccessNativeMfa` applies. The host migrates on startup.
2. Point the CLI at **that same database**. The CLI is a separate process and does not inherit Aspire’s injected connection unless you set it.

From the repo root, for the usual Aspire SQL (port **14333**, database **Apperture**):

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__DefaultConnection = "Server=localhost,14333;Database=Apperture;User Id=sa;Password=<your-aspire-sql-password>;TrustServerCertificate=True"

dotnet run --project src/Apperture.Cli/Apperture.Cli.Host -- emergency status
```

Use the Aspire `sql-password` parameter (see AppHost `Parameters:sql-password`), not a password you invent for the emergency user.

If `status` reports LocalDB (`(localdb)\mssqllocaldb`) or a different catalog than Management.Host, stop and fix the connection string. Provisioning into the wrong database looks like “it worked” and then the UI will not see the user.

Confirm `Healthy: False` and `Fallback safeguard required: True` before you enroll MFA.

## What will change on this instance

| Step | Effect on your current admin |
|---|---|
| `emergency provision` (password only) | Adds a **new** user. Fallback still protects your last ordinary local admin. |
| `emergency enroll-mfa` (healthy) | Emergency posture becomes valid. **The next proven Entra sign-in** of an ordinary Platform Administrator will disable **that** user’s local password. Entra sign-in itself still works. |
| `emergency revoke --confirm-fallback` | Clears designation. IAM-2 last-admin local protection returns. |

Do not designate your daily Entra admin as the emergency account if you want to keep that person as an ordinary administrator.

## Procedure

1. **IAM-2 fallback before emergency setup**  
   With `emergency status` showing `Healthy: False` and `Fallback safeguard required: True`, sign in as the only Entra-independent Platform Administrator with Microsoft, then confirm Account → Security still shows local sign-in retained for platform recovery.

2. **Provision without writing SQL**  
   ```text
   dotnet run --project src/Apperture.Cli/Apperture.Cli.Host -- emergency provision --display-name "Emergency Administrator" --username breakglass
   ```  
   Type the new local password, Enter, then confirm it (stdin; nothing on the command line). Then:
   ```text
   dotnet run --project src/Apperture.Cli/Apperture.Cli.Host -- emergency enroll-mfa --username breakglass
   ```  
   Add the printed `otpauth://` URI (or manual key) to an authenticator app, enter the 6-digit code when prompted, and store the printed recovery codes. `emergency status` should show `Healthy: True`.

3. **Emergency local login requires native MFA**  
   Sign out. Sign in with username `breakglass` and the password you set. The UI must ask for an authenticator code. Password alone must not open the application.

4. **Recovery code works exactly once**  
   Sign out. Sign in with password, then a recovery code. The session succeeds. Repeat with the same code; it must fail.

5. **Usable without Entra**  
   With Microsoft sign-in unused (or the directory unreachable), the emergency local + MFA path still signs in.

6. **Ordinary last admin no longer kept for recovery**  
   Sign in as the ordinary Platform Administrator through proven Entra. Account → Security (or Governance → Users) should show that administrator’s local sign-in **disabled**. The emergency account remains enabled.

7. **Emergency access remains protected**  
   In Governance, disabling the emergency local credential or removing Platform Administrator from that user is rejected.

8. **Microsoft login/logout still work**  
   Ordinary Entra sign-in and sign-out on Management.Host succeed as in IAM-2.

9. **Ordinary local login has not regressed**  
   A non-emergency local user without MFA enrollment still signs in with username and password only.

## Undo (still this instance)

```text
dotnet run --project src/Apperture.Cli/Apperture.Cli.Host -- emergency revoke --confirm-fallback
```

That clears emergency designation. It does not delete the `breakglass` user. After revoke, sign in with Entra again if you need IAM-2 to restore your ordinary admin’s local credential for recovery.
