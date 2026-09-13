function Get-AppertureDoctor {
    <#
    .SYNOPSIS
        Stub for apperture doctor command.
    #>
    [CmdletBinding()]
    param()
    Write-Output "Apperture doctor - stub. Not implemented."
}

function Get-AppertureInstanceStatus {
    <#
    .SYNOPSIS
        Stub for apperture instance status command.
    #>
    [CmdletBinding()]
    param()
    Write-Output "Apperture instance status - stub. Not implemented."
}

#region Bootstrap Functions

function Invoke-AppertureBootstrapHelper {
    <#
    .SYNOPSIS
        Internal helper to invoke apperture bootstrap subcommands.
    .DESCRIPTION
        Tries to run 'apperture bootstrap' from PATH. If not found, outputs guidance.
    #>
    [CmdletBinding()]
    param([string[]]$Args)
    $apperture = Get-Command apperture -ErrorAction SilentlyContinue
    if ($apperture) {
        & apperture bootstrap @Args
    } else {
        Write-Warning "Apperture CLI not found. Run 'dotnet run --project src/Apperture.Cli/Apperture.Cli.Host -- bootstrap <subcommand>' from the repo root, or install the apperture CLI."
        Write-Output "Not implemented (apperture not in PATH)."
    }
}

function Get-AppertureBootstrap {
    <#
    .SYNOPSIS
        Shows bootstrap configuration (no secrets).
    .DESCRIPTION
        Maps to 'bootstrap show'. Returns non-secret profile data; never outputs credentials.
    .OUTPUTS
        Bootstrap profile summary.
    #>
    [CmdletBinding()]
    param()
    Invoke-AppertureBootstrapHelper -Args @('show')
}

function Test-AppertureBootstrap {
    <#
    .SYNOPSIS
        Validates bootstrap configuration.
    .DESCRIPTION
        Maps to 'bootstrap validate'. Returns validation result.
    .OUTPUTS
        Validation result.
    #>
    [CmdletBinding()]
    param()
    Invoke-AppertureBootstrapHelper -Args @('validate')
}

function Test-AppertureBootstrapSqlConnection {
    <#
    .SYNOPSIS
        Tests SQL connection using bootstrap config.
    .DESCRIPTION
        Maps to 'bootstrap sql test'. Validates SQL connectivity.
    .OUTPUTS
        Connection test result.
    #>
    [CmdletBinding()]
    param()
    Invoke-AppertureBootstrapHelper -Args @('sql', 'test')
}

function Set-AppertureBootstrapSqlProfile {
    <#
    .SYNOPSIS
        Sets SQL server and database in bootstrap profile.
    .DESCRIPTION
        Maps to 'bootstrap sql set-profile'. Updates server and database only.
    .PARAMETER Server
        SQL server name or host.
    .PARAMETER Database
        Database name.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Server,
        [Parameter(Mandatory = $true)]
        [string]$Database
    )
    Invoke-AppertureBootstrapHelper -Args @('set-profile', '--server', $Server, '--database', $Database)
}

function Set-AppertureBootstrapSqlAuthMode {
    <#
    .SYNOPSIS
        Sets SQL authentication mode in bootstrap profile.
    .DESCRIPTION
        Maps to 'bootstrap sql set-auth-mode'.
    .PARAMETER AuthMode
        Auth mode: SqlPassword, WindowsIntegrated, ManagedIdentity, KeyVaultReference.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('SqlPassword', 'WindowsIntegrated', 'ManagedIdentity', 'KeyVaultReference')]
        [string]$AuthMode
    )
    Invoke-AppertureBootstrapHelper -Args @('set-auth-mode', $AuthMode)
}

function Set-AppertureBootstrapSqlPassword {
    <#
    .SYNOPSIS
        Sets SQL password in bootstrap (secure; no echo).
    .DESCRIPTION
        Maps to 'bootstrap sql set-password'. Accepts SecureString; never logs or outputs plaintext.
    .PARAMETER SecurePassword
        SecureString password. If not provided, prompts securely.
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [SecureString]$SecurePassword
    )
    if (-not $SecurePassword) {
        $SecurePassword = Read-Host -AsSecureString -Prompt "Enter SQL password"
    }
    Invoke-AppertureBootstrapHelper -Args @('set-password')
}

function Set-AppertureBootstrapSqlKeyVaultBinding {
    <#
    .SYNOPSIS
        Binds Key Vault secret URI for SQL credential.
    .DESCRIPTION
        Maps to 'bootstrap sql bind-keyvault-secret'.
    .PARAMETER SecretUri
        Key Vault secret URI (e.g. https://vault.azure.net/secrets/name/version).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$SecretUri
    )
    Invoke-AppertureBootstrapHelper -Args @('bind-keyvault-secret', $SecretUri)
}

function Set-AppertureBootstrapSqlManagedIdentityBinding {
    <#
    .SYNOPSIS
        Binds Managed Identity for SQL auth.
    .DESCRIPTION
        Maps to 'bootstrap sql bind-managed-identity'.
    #>
    [CmdletBinding()]
    param()
    Invoke-AppertureBootstrapHelper -Args @('bind-managed-identity')
}

function Set-AppertureBootstrapSqlIntegratedSecurityBinding {
    <#
    .SYNOPSIS
        Binds Windows Integrated Security for SQL auth.
    .DESCRIPTION
        Maps to 'bootstrap sql bind-integrated-security'.
    #>
    [CmdletBinding()]
    param()
    Invoke-AppertureBootstrapHelper -Args @('bind-integrated-security')
}

function Invoke-AppertureBootstrapSqlCredentialRotation {
    <#
    .SYNOPSIS
        Rotates SQL credential in bootstrap.
    .DESCRIPTION
        Maps to 'bootstrap rotate-sql-credential'.
    #>
    [CmdletBinding()]
    param()
    Invoke-AppertureBootstrapHelper -Args @('rotate-sql-credential')
}

function Repair-AppertureBootstrap {
    <#
    .SYNOPSIS
        Repair path for bootstrap config.
    .DESCRIPTION
        Maps to 'bootstrap repair'. Distinct from routine admin; use for repair scenarios.
    #>
    [CmdletBinding()]
    param()
    Invoke-AppertureBootstrapHelper -Args @('repair')
}

function Unlock-AppertureBootstrapRecovery {
    <#
    .SYNOPSIS
        Recovery unlock for bootstrap (recovery only).
    .DESCRIPTION
        Maps to 'bootstrap recovery-unlock'. Use only in recovery scenarios; not routine admin.
    .PARAMETER Request
        Recovery unlock request (implementation-specific).
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [hashtable]$Request
    )
    Invoke-AppertureBootstrapHelper -Args @('recovery-unlock')
}

#endregion Bootstrap Functions

#region Emergency Access Functions

function Invoke-AppertureEmergencyHelper {
    [CmdletBinding()]
    param([string[]]$Args)
    $apperture = Get-Command apperture -ErrorAction SilentlyContinue
    if ($apperture) {
        & apperture emergency @Args
    } else {
        Write-Warning "Apperture CLI not found. Run 'dotnet run --project src/Apperture.Cli/Apperture.Cli.Host -- emergency <subcommand>' from the repo root, or install the apperture CLI."
        Write-Output "Not implemented (apperture not in PATH)."
    }
}

function Get-AppertureEmergencyAccess {
    <#
    .SYNOPSIS
        Shows whether a valid emergency administrator is configured.
    #>
    [CmdletBinding()]
    param()
    Invoke-AppertureEmergencyHelper -Args @('status')
}

function New-AppertureEmergencyAdministrator {
    <#
    .SYNOPSIS
        Creates the system-managed Recovery Access account (username breakglass) if it does not exist.
    .DESCRIPTION
        The password is prompted by the CLI (stdin). Do not pass the password as an argument.
        Username and display name are not configurable.
    #>
    [CmdletBinding()]
    param()
    Invoke-AppertureEmergencyHelper -Args @('provision')
}

function Set-AppertureEmergencyAdministrator {
    <#
    .SYNOPSIS
        Designates an existing local Platform Administrator as emergency access.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Username
    )
    Invoke-AppertureEmergencyHelper -Args @('designate', '--username', $Username)
}

function Set-AppertureEmergencyPassword {
    <#
    .SYNOPSIS
        Sets the Recovery Access local password. The CLI prompts on stdin.
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [string]$Username
    )
    if ($Username) {
        Invoke-AppertureEmergencyHelper -Args @('set-password', '--username', $Username)
    } else {
        Invoke-AppertureEmergencyHelper -Args @('set-password')
    }
}

function Register-AppertureEmergencyAuthenticator {
    <#
    .SYNOPSIS
        Enrolls native authenticator MFA for Recovery Access.
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [string]$Username,
        [switch]$Reset
    )
    if ($Reset) {
        $cmd = 'reset-mfa'
    } else {
        $cmd = 'enroll-mfa'
    }
    if ($Username) {
        Invoke-AppertureEmergencyHelper -Args @($cmd, '--username', $Username)
    } else {
        Invoke-AppertureEmergencyHelper -Args @($cmd)
    }
}

function New-AppertureEmergencyRecoveryCodes {
    <#
    .SYNOPSIS
        Regenerates one-time recovery codes. Previous unused codes are invalidated.
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [string]$Username
    )
    if ($Username) {
        Invoke-AppertureEmergencyHelper -Args @('regenerate-codes', '--username', $Username)
    } else {
        Invoke-AppertureEmergencyHelper -Args @('regenerate-codes')
    }
}

function Remove-AppertureEmergencyAdministrator {
    <#
    .SYNOPSIS
        Clears emergency designation and returns to last-administrator fallback protection.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [switch]$ConfirmFallback
    )
    Invoke-AppertureEmergencyHelper -Args @('revoke', '--confirm-fallback')
}

#endregion Emergency Access Functions

