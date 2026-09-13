# Docker SQL and User Secrets sync for native dev mode.
Set-StrictMode -Version Latest

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

    [System.IO.File]::WriteAllText($envPath, "MSSQL_SA_PASSWORD=$password")
    Write-Host 'Synced .env from User Secrets (SqlServer:Password -> MSSQL_SA_PASSWORD)' -ForegroundColor Green
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
