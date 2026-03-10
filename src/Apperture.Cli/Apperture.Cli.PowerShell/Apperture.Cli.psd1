@{
    RootModule        = 'Apperture.Cli.psm1'
    ModuleVersion     = '0.1.0'
    GUID              = '00000000-0000-0000-0000-000000000001'
    Author            = 'Apperture'
    Description       = 'Apperture CLI - management and scaffolding'
    PowerShellVersion = '5.1'
    FunctionsToExport = @(
        'Get-AppertureDoctor',
        'Get-AppertureInstanceStatus',
        'Get-AppertureBootstrap',
        'Test-AppertureBootstrap',
        'Test-AppertureBootstrapSqlConnection',
        'Set-AppertureBootstrapSqlProfile',
        'Set-AppertureBootstrapSqlAuthMode',
        'Set-AppertureBootstrapSqlPassword',
        'Set-AppertureBootstrapSqlKeyVaultBinding',
        'Set-AppertureBootstrapSqlManagedIdentityBinding',
        'Set-AppertureBootstrapSqlIntegratedSecurityBinding',
        'Invoke-AppertureBootstrapSqlCredentialRotation',
        'Repair-AppertureBootstrap',
        'Unlock-AppertureBootstrapRecovery'
    )
    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @()
}
