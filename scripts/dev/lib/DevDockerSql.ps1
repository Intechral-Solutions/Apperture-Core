# Docker SQL and User Secrets sync for native dev mode.
Set-StrictMode -Version Latest

function Get-DevEnvEffectiveValue {
    <#
    .SYNOPSIS
        The value the test configuration reader sees for the text after the "=" of an env line.
    .DESCRIPTION
        Mirrors TryParseEnvLine and UnquoteEnvValue in tests/Apperture.IntegrationTests/SqlServerTestConfiguration.cs: surrounding
        whitespace is dropped, an inline comment starts at the first " #" (not at the first character), and one pair of matching
        single or double quotes is removed. It exists only so an assignment that already means the requested value is left alone;
        keep it in step with that reader. Limitation shared with the reader: a value that itself contains " #" cannot be
        written back, because the reader would cut it there.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Rest)

    $raw = $Rest.Trim()
    if ($raw.Length -eq 0) { return $raw }

    $commentIndex = $raw.IndexOf(' #', [System.StringComparison]::Ordinal)
    if ($commentIndex -gt 0) { $raw = $raw.Substring(0, $commentIndex).TrimEnd() }

    if ($raw.Length -ge 2 -and (($raw[0] -eq '"' -and $raw[$raw.Length - 1] -eq '"') -or ($raw[0] -eq "'" -and $raw[$raw.Length - 1] -eq "'"))) {
        return $raw.Substring(1, $raw.Length - 2)
    }

    return $raw
}

function Set-DevEnvFileValue {
    <#
    .SYNOPSIS
        Sets one KEY=value assignment in an env file and leaves every other byte of the file as it was.
    .DESCRIPTION
        The repo-root .env also carries the test SQL settings (APPERTURE_TEST_SQL_CONNECTION_STRING and friends), so it must
        never be rewritten wholesale. Only active assignments of exactly KEY are changed (an optional leading "export" and
        indentation are kept; comments, look-alike names and every other line are not touched). If KEY is missing it is appended.
        Line endings (including mixed ones), the UTF-8 byte order mark and a missing final newline are preserved. When the value
        is already current the file is not rewritten at all, and "current" means what the test configuration reader would read
        (see Get-DevEnvEffectiveValue): a quoted, padded or inline-commented form of the same value is left exactly as written.
        A relative Path is resolved against the PowerShell location. A changed file is written to a temporary sibling and swapped in, so
        an interrupted run cannot leave a half-written file.

        A file that cannot be updated safely is refused and left exactly as it was: invalid UTF-8, a NUL byte (UTF-16 or binary),
        a directory, an invalid KEY, or a value containing a line break. The value is never written to any output.
    .OUTPUTS
        'created', 'added', 'updated' or 'unchanged'.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Key,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Value
    )

    if ($Key -cnotmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
        throw "Refusing to update env file '$Path': '$Key' is not a valid variable name."
    }
    if ($Value -match '[\r\n]' -or $Value.IndexOf([char]0) -ge 0) {
        throw "Refusing to update env file '$Path': the value for $Key contains a line break or NUL character."
    }
    if (Test-Path -LiteralPath $Path -PathType Container) {
        throw "Refusing to update env file: '$Path' is a directory."
    }

    # Not [System.IO.Path]::GetFullPath: that resolves against the process directory, which Set-Location does not change.
    $full = $PSCmdlet.GetUnresolvedProviderPathFromPSPath($Path)

    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
        $plainUtf8 = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($full, "$Key=$Value" + [Environment]::NewLine, $plainUtf8)
        return 'created'
    }

    $bytes = [System.IO.File]::ReadAllBytes($full)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    if ([Array]::IndexOf($bytes, [byte]0) -ge 0) {
        throw "Refusing to update env file '$full': it contains a NUL byte (UTF-16 or binary), so it was left untouched."
    }
    $offset = if ($hasBom) { 3 } else { 0 }
    $strictUtf8 = New-Object System.Text.UTF8Encoding($false, $true)
    try {
        $text = $strictUtf8.GetString($bytes, $offset, $bytes.Length - $offset)
    }
    catch {
        throw "Refusing to update env file '$full': it is not valid UTF-8, so it was left untouched."
    }

    # Split on line endings, keeping each separator, so the file is reassembled byte for byte.
    $parts = [regex]::Split($text, '(\r\n|\n|\r)')
    $pattern = '^(?<lead>\s*(?:export\s+)?)' + [regex]::Escape($Key) + '(?<eq>\s*=)(?<rest>.*)$'
    $found = 0
    $changed = $false
    for ($i = 0; $i -lt $parts.Length; $i += 2) {
        $match = [regex]::Match($parts[$i], $pattern)
        if (-not $match.Success) { continue }
        $found++
        $rest = $match.Groups['rest'].Value
        if ($rest -ceq $Value -or (Get-DevEnvEffectiveValue -Rest $rest) -ceq $Value) { continue }
        $parts[$i] = $match.Groups['lead'].Value + $Key + $match.Groups['eq'].Value + $Value
        $changed = $true
    }

    if ($found -gt 0) {
        if (-not $changed) { return 'unchanged' }
        $newText = -join $parts
        $status = 'updated'
    }
    else {
        $newline = if ($parts.Length -gt 1) { $parts[1] } else { [Environment]::NewLine }
        $lastLine = $parts[$parts.Length - 1]
        $newText = $text
        if ($lastLine.Length -gt 0) { $newText += $newline }
        $newText += "$Key=$Value" + $newline
        $status = 'added'
    }

    $encoding = New-Object System.Text.UTF8Encoding($hasBom)
    $body = $encoding.GetBytes($newText)
    $output = if ($hasBom) { [byte[]]($encoding.GetPreamble() + $body) } else { $body }

    $temp = $full + '.tmp-' + [guid]::NewGuid().ToString('N')
    try {
        [System.IO.File]::WriteAllBytes($temp, $output)
        # [NullString]::Value, not $null: PowerShell turns $null into "" for a .NET string argument, which Replace rejects.
        [System.IO.File]::Replace($temp, $full, [NullString]::Value)
    }
    finally {
        if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue }
    }

    return $status
}

function Sync-DevEnvFromUserSecrets {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [string]$DotNetExe = 'dotnet'
    )

    $hostProject = Join-Path $RepoRoot 'src\Apperture.ControlPlane\Apperture.Management.Host\Apperture.Management.Host.csproj'
    $userSecretsId = 'a1b2c3d4-5e6f-4a5b-8c9d-0e1f2a3b4c5d'
    $secretsPath = Join-Path $env:APPDATA "Microsoft\UserSecrets\$userSecretsId\secrets.json"
    $envPath = Join-Path $RepoRoot '.env'

    if (-not (Test-Path -LiteralPath $hostProject)) {
        throw "Management.Host project not found: $hostProject"
    }

    if (-not (Test-Path -LiteralPath $secretsPath)) {
        Write-Host 'User Secrets not initialized. Setting SqlServer:Password...' -ForegroundColor Yellow
        & $DotNetExe user-secrets set 'SqlServer:Password' 'YourStrong!Passw0rd' --project $hostProject | Out-Null
        Write-Host '  Set SqlServer:Password to default. Change with dotnet user-secrets.' -ForegroundColor Gray
    }

    $secretsJson = Get-Content -LiteralPath $secretsPath -Raw
    $secrets = $secretsJson | ConvertFrom-Json
    $password = $secrets.'SqlServer:Password'

    if ([string]::IsNullOrWhiteSpace($password)) {
        throw 'SqlServer:Password not found in User Secrets.'
    }

    # Change only MSSQL_SA_PASSWORD. The repo-root .env also holds the test SQL settings, which a wholesale rewrite would delete.
    $result = Set-DevEnvFileValue -Path $envPath -Key 'MSSQL_SA_PASSWORD' -Value $password
    Write-Host "Synced .env from User Secrets (SqlServer:Password -> MSSQL_SA_PASSWORD: $result; other settings left as they were)" -ForegroundColor Green
    return $password
}

function Start-DevDockerSql {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$DotNetExe
    )

    $password = Sync-DevEnvFromUserSecrets -RepoRoot $RepoRoot -DotNetExe $DotNetExe

    Write-Host 'Starting SQL Server (Docker)...' -ForegroundColor Cyan
    Push-Location $RepoRoot
    try {
        & docker compose up -d
        if ($LASTEXITCODE -ne 0) { throw 'Docker Compose failed' }
    }
    finally {
        Pop-Location
    }

    Write-Host 'Waiting for SQL Server to accept TCP connections (localhost:1433)...' -ForegroundColor Cyan
    Wait-DevTcpConnectable -Port 1433

    Write-Host "Ensuring database 'Apperture' exists..." -ForegroundColor Cyan
    docker exec apperture-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$password" -C -Q "IF DB_ID(N'Apperture') IS NULL CREATE DATABASE [Apperture];" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Failed to create Apperture database via sqlcmd.' }

    return $password
}

function Restart-DevDockerSqlService {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$DotNetExe,
        [string]$ServiceName = 'sqlserver'
    )

    Sync-DevEnvFromUserSecrets -RepoRoot $RepoRoot -DotNetExe $DotNetExe | Out-Null

    Write-Host "Restarting Docker service '$ServiceName'..." -ForegroundColor Cyan
    Push-Location $RepoRoot
    try {
        $definedServices = @(& docker compose config --services)
        if ($LASTEXITCODE -ne 0) { throw 'docker compose config --services failed' }
        if ($ServiceName -notin $definedServices) {
            $known = ($definedServices | Sort-Object) -join ', '
            throw "Compose service '$ServiceName' is not defined. Known services: $known."
        }

        & docker compose restart $ServiceName
        if ($LASTEXITCODE -ne 0) { throw "docker compose restart failed for service '$ServiceName'" }
    }
    finally {
        Pop-Location
    }

    Write-Host 'SQL Server container restarted (localhost:1433).' -ForegroundColor Green
}

function Test-DevSqlContainerRunning {
    $result = docker ps --filter 'name=apperture-sqlserver' --format '{{.Names}}' 2>$null
    return ($result -match 'apperture-sqlserver')
}
