# Epic 102 API smoke (dev: tenny / dev @ localhost:9000)
$ErrorActionPreference = "Stop"
$base = "http://localhost:9000"
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

$login = Invoke-RestMethod -Uri "$base/api/auth/local/login" -Method POST `
    -Body '{"username":"tenny","password":"dev"}' -ContentType "application/json" -WebSession $session
Write-Host "LOGIN userId=$($login.userId)"

$list = @(Invoke-RestMethod -Uri "$base/api/me/notifications?readFilter=all&take=10" -WebSession $session)
Write-Host "LIST count=$($list.Count) ids=$($list.id -join ',')"

$unread = Invoke-RestMethod -Uri "$base/api/me/notifications/unread-count" -WebSession $session
Write-Host "UNREAD_COUNT=$($unread.unreadCount)"

if ($list.Count -eq 0) { throw "Expected at least one inbox item for tenny" }

$itemId = $list[0].id
Invoke-RestMethod -Uri "$base/api/me/notifications/record-menu-viewed?take=10" -Method POST -WebSession $session | Out-Null
$dbAfterMenu = docker exec apperture-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "YourStrong!Passw0rd" -C -d Apperture -Q "SELECT Id, ReadAtUtc, LastViewedAtUtc FROM NotificationUserInboxItems WHERE PrincipalUserId=N'49955a58e84244a28af8341b';" -W 2>&1
Write-Host "DB_AFTER_MENU:"
Write-Host $dbAfterMenu

Invoke-RestMethod -Uri "$base/api/me/notifications/$itemId/mark-read" -Method POST -WebSession $session | Out-Null
$unreadAfterOne = Invoke-RestMethod -Uri "$base/api/me/notifications/unread-count" -WebSession $session
Write-Host "UNREAD_AFTER_MARK_ONE=$($unreadAfterOne.unreadCount)"

# Re-materialize unread via mark-unread for filter test
Invoke-RestMethod -Uri "$base/api/me/notifications/$itemId/mark-unread" -Method POST -WebSession $session | Out-Null
$unreadList = @(Invoke-RestMethod -Uri "$base/api/me/notifications?readFilter=unread" -WebSession $session)
$readList = @(Invoke-RestMethod -Uri "$base/api/me/notifications?readFilter=read" -WebSession $session)
Write-Host "FILTER unread=$($unreadList.Count) read=$($readList.Count)"

Invoke-RestMethod -Uri "$base/api/me/notifications/mark-all-read" -Method POST -WebSession $session | Out-Null
$unreadFinal = Invoke-RestMethod -Uri "$base/api/me/notifications/unread-count" -WebSession $session
Write-Host "UNREAD_AFTER_MARK_ALL=$($unreadFinal.unreadCount)"

$other = @(Invoke-RestMethod -Uri "$base/api/me/notifications?readFilter=all&take=50" -WebSession $session | Where-Object { $_.id -eq "ninbox_smoke_other" })
Write-Host "OTHER_USER_ROW_VISIBLE=$($other.Count -gt 0)"

Write-Host "SMOKE_OK"
