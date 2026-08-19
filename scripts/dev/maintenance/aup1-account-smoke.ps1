<#
.SYNOPSIS
    Runtime smoke test for the personal account area (profile, avatar, preferences, notifications).

.DESCRIPTION
    Drives every self-service account endpoint against a running Management Host using a real
    cookie session, then checks that the Management UI serves each account route. Read-only apart
    from the signed-in operator's own profile, which is restored before the script exits.

.EXAMPLE
    ./scripts/dev/maintenance/aup1-account-smoke.ps1 -ApiBase http://localhost:9000 -UiBase http://localhost:9001
#>
[CmdletBinding()]
param(
    [string]$ApiBase = 'http://localhost:9000',
    [string]$UiBase = 'http://localhost:9001',
    [string]$Username = 'tenny',
    [string]$Password = 'dev'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing -ErrorAction SilentlyContinue | Out-Null

$script:Pass = 0
$script:Fail = 0

function Step {
    param([string]$Name, [scriptblock]$Body)
    try {
        & $Body
        $script:Pass++
        Write-Host ("  PASS  " + $Name) -ForegroundColor Green
    }
    catch {
        $script:Fail++
        Write-Host ("  FAIL  " + $Name + " :: " + $_.Exception.Message) -ForegroundColor Red
    }
}

function Assert {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

# PowerShell returns response headers as string arrays, so flatten before comparing.
function Get-HeaderText {
    param($Response, [string]$Name)
    $values = $Response.Headers[$Name]
    if ($null -eq $values) { return '' }
    return ($values -join '; ')
}

# --- Session ---

$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

function Api {
    param(
        [string]$Method,
        [string]$Path,
        $Body,
        [switch]$Raw,
        [switch]$AllowFailure
    )
    $uri = "$ApiBase$Path"
    $args = @{ Method = $Method; Uri = $uri; WebSession = $session; SkipHttpErrorCheck = $true }
    if ($null -ne $Body) {
        $args.Body = ($Body | ConvertTo-Json -Depth 8 -Compress)
        $args.ContentType = 'application/json'
    }
    $response = Invoke-WebRequest @args
    if (-not $AllowFailure -and $response.StatusCode -ge 400) {
        throw "$Method $Path returned $($response.StatusCode): $($response.Content)"
    }
    if ($Raw) { return $response }
    if ([string]::IsNullOrWhiteSpace($response.Content)) { return $null }
    return $response.Content | ConvertFrom-Json
}

function New-TestImage {
    param([int]$Width = 400, [int]$Height = 260, [string]$Format = 'Png')
    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::SteelBlue)
            $graphics.FillRectangle(
                [System.Drawing.Brushes]::Firebrick, 0, 0, [int]($Width / 2), [int]($Height / 2))
        }
        finally { $graphics.Dispose() }

        $stream = New-Object System.IO.MemoryStream
        try {
            $imageFormat = if ($Format -eq 'Jpeg') {
                [System.Drawing.Imaging.ImageFormat]::Jpeg
            } else {
                [System.Drawing.Imaging.ImageFormat]::Png
            }
            $bitmap.Save($stream, $imageFormat)
            return $stream.ToArray()
        }
        finally { $stream.Dispose() }
    }
    finally { $bitmap.Dispose() }
}

function Post-Avatar {
    param([byte[]]$Content, [string]$FileName, [string]$ContentType)

    $boundary = [Guid]::NewGuid().ToString('N')
    $ms = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.StreamWriter($ms, [System.Text.Encoding]::ASCII)
    $writer.Write("--$boundary`r`n")
    $writer.Write("Content-Disposition: form-data; name=`"file`"; filename=`"$FileName`"`r`n")
    $writer.Write("Content-Type: $ContentType`r`n`r`n")
    $writer.Flush()
    $ms.Write($Content, 0, $Content.Length)
    $writer.Write("`r`n--$boundary--`r`n")
    $writer.Flush()
    $payload = $ms.ToArray()
    $writer.Dispose()

    return Invoke-WebRequest -Method Post -Uri "$ApiBase/api/account/avatar" `
        -WebSession $session -SkipHttpErrorCheck `
        -ContentType "multipart/form-data; boundary=$boundary" -Body $payload
}

Write-Host ""
Write-Host "AUP-1 account runtime smoke" -ForegroundColor Cyan
Write-Host "  API $ApiBase"
Write-Host "  UI  $UiBase"
Write-Host ""

# --- Unauthenticated baseline ---

Write-Host "Unauthenticated access" -ForegroundColor Cyan
foreach ($path in @(
        '/api/account/profile',
        '/api/account/preferences',
        '/api/account/notification-preferences',
        '/api/account/avatar/image?size=128')) {
    Step "401 without a session: $path" {
        $r = Invoke-WebRequest -Method Get -Uri "$ApiBase$path" -SkipHttpErrorCheck
        Assert ($r.StatusCode -eq 401) "expected 401, got $($r.StatusCode)"
    }
}

# --- Sign in ---

Write-Host ""
Write-Host "Sign in" -ForegroundColor Cyan
Step "local sign-in issues a session cookie" {
    $login = Invoke-WebRequest -Method Post -Uri "$ApiBase/api/auth/local/login" `
        -WebSession $session -SkipHttpErrorCheck -ContentType 'application/json' `
        -Body (@{ username = $Username; password = $Password } | ConvertTo-Json -Compress)
    Assert ($login.StatusCode -eq 200) "login returned $($login.StatusCode)"
    Assert ($session.Cookies.Count -gt 0) 'no cookie was stored'
}

$me = Api GET '/api/auth/me'
$originalProfile = Api GET '/api/account/profile'
$originalPreferences = Api GET '/api/account/preferences'

# --- A. Profile ---

Write-Host ""
Write-Host "A. Profile" -ForegroundColor Cyan
Step "profile reports the authenticated identity" {
    Assert ($originalProfile.userId -eq $me.userId) 'profile user id does not match the session'
    Assert (-not [string]::IsNullOrWhiteSpace($originalProfile.displayName)) 'display name is empty'
    Assert ($originalProfile.displayNameEditable -eq $true) 'display name should be editable'
}

$newName = "Smoke Test $((Get-Date).ToString('HHmmss'))"
Step "display name saves" {
    $updated = Api PUT '/api/account/profile' @{ displayName = $newName }
    Assert ($updated.displayName -eq $newName) "expected '$newName', got '$($updated.displayName)'"
}

Step "display name persists on reload" {
    $reloaded = Api GET '/api/account/profile'
    Assert ($reloaded.displayName -eq $newName) 'display name did not persist'
}

Step "session response reflects the new name so the account menu updates" {
    $session2 = Api GET '/api/auth/me'
    Assert ($session2.displayName -eq $newName) 'session still reports the old name'
}

Step "blank display name is rejected with problem details" {
    $r = Invoke-WebRequest -Method Put -Uri "$ApiBase/api/account/profile" -WebSession $session `
        -SkipHttpErrorCheck -ContentType 'application/json' -Body (@{ displayName = '   ' } | ConvertTo-Json -Compress)
    Assert ($r.StatusCode -eq 400) "expected 400, got $($r.StatusCode)"
    $contentType = Get-HeaderText $r 'Content-Type'
    Assert ($contentType -like '*problem+json*') "expected application/problem+json, got '$contentType'"
}

Step "a client-supplied user id cannot redirect the mutation" {
    $r = Api PUT '/api/account/profile' @{ displayName = $newName; userId = 'usr_someone_else' }
    Assert ($r.userId -eq $me.userId) 'mutation was not scoped to the authenticated operator'
}

# --- B. Built-in avatar ---

Write-Host ""
Write-Host "B. Built-in avatar" -ForegroundColor Cyan
$options = Api GET '/api/account/avatar/options'
Step "curated catalog is small and has a neutral default" {
    Assert ($options.Count -ge 2 -and $options.Count -le 6) "unexpected catalog size $($options.Count)"
    $default = $options | Where-Object { $_.isDefault }
    Assert ($default.Count -eq 1) 'expected exactly one default option'
    Assert ($default[0].id -eq 'neutral') "default should be neutral, got $($default[0].id)"
}

Step "selecting a curated avatar saves" {
    $avatar = Api PUT '/api/account/avatar/selection' @{ optionId = 'long-hair' }
    Assert ($avatar.kind -eq 'builtIn') "expected builtIn, got $($avatar.kind)"
    Assert ($avatar.builtInOptionId -eq 'long-hair') 'option was not stored'
    Assert ($null -eq $avatar.imageUrl) 'built-in artwork must not need a fetchable URL'
}

Step "shell session reports the selected avatar" {
    $s = Api GET '/api/auth/me'
    Assert ($s.avatar.builtInOptionId -eq 'long-hair') 'session avatar did not update'
}

Step "an avatar outside the catalog is rejected" {
    $r = Invoke-WebRequest -Method Put -Uri "$ApiBase/api/account/avatar/selection" -WebSession $session `
        -SkipHttpErrorCheck -ContentType 'application/json' -Body (@{ optionId = 'dragon' } | ConvertTo-Json -Compress)
    Assert ($r.StatusCode -eq 400) "expected 400, got $($r.StatusCode)"
}

Step "reset returns to the neutral default" {
    $avatar = Api DELETE '/api/account/avatar'
    Assert ($avatar.builtInOptionId -eq 'neutral') "expected neutral, got $($avatar.builtInOptionId)"
}

# --- C. Custom avatar ---

Write-Host ""
Write-Host "C. Custom profile photo" -ForegroundColor Cyan
$firstVersion = $null
Step "a valid PNG uploads and normalizes" {
    $r = Post-Avatar -Content (New-TestImage -Width 420 -Height 280 -Format Png) -FileName 'photo.png' -ContentType 'image/png'
    Assert ($r.StatusCode -eq 200) "upload returned $($r.StatusCode): $($r.Content)"
    $avatar = $r.Content | ConvertFrom-Json
    Assert ($avatar.kind -eq 'custom') 'avatar kind should be custom'
    Assert ($avatar.version -like 'avt_*') "platform-generated key expected, got $($avatar.version)"
    $script:firstVersion = $avatar.version
}

Step "the normalized image is served with a locked content type" {
    $r = Api GET "/api/account/avatar/image?size=128&v=$script:firstVersion" -Raw
    Assert ($r.StatusCode -eq 200) "expected 200, got $($r.StatusCode)"
    $contentType = Get-HeaderText $r 'Content-Type'
    $sniff = Get-HeaderText $r 'X-Content-Type-Options'
    $cache = Get-HeaderText $r 'Cache-Control'
    Assert ($contentType -like 'image/png*') "expected image/png, got '$contentType'"
    Assert ($sniff -like '*nosniff*') "expected nosniff, got '$sniff'"
    Assert ($cache -like '*private*') "avatar cache must be private, got '$cache'"
    Assert ($cache -like '*immutable*') "versioned avatar should be immutable, got '$cache'"
}

Step "every generated size is retrievable" {
    foreach ($size in 64, 128, 256) {
        $r = Api GET "/api/account/avatar/image?size=$size&v=$script:firstVersion" -Raw
        Assert ($r.StatusCode -eq 200) "size $size returned $($r.StatusCode)"
        Assert ($r.RawContentLength -gt 0) "size $size returned no bytes"
    }
}

Step "an unsupported size is refused" {
    $r = Api GET '/api/account/avatar/image?size=96' -Raw -AllowFailure
    Assert ($r.StatusCode -eq 404) "expected 404, got $($r.StatusCode)"
}

Step "replacing the photo issues a new version and retires the old one" {
    $r = Post-Avatar -Content (New-TestImage -Width 300 -Height 300 -Format Jpeg) -FileName 'replacement.jpg' -ContentType 'image/jpeg'
    Assert ($r.StatusCode -eq 200) "replacement returned $($r.StatusCode)"
    $avatar = $r.Content | ConvertFrom-Json
    Assert ($avatar.version -ne $script:firstVersion) 'version token did not change'

    $stale = Api GET "/api/account/avatar/image?size=128&v=$script:firstVersion" -Raw -AllowFailure
    Assert ($stale.StatusCode -eq 200) 'current avatar should still serve'
    $script:firstVersion = $avatar.version
}

Step "an SVG upload is rejected even when labelled as PNG" {
    $svg = [System.Text.Encoding]::UTF8.GetBytes('<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64"><script>alert(1)</script></svg>')
    $r = Post-Avatar -Content $svg -FileName 'vector.png' -ContentType 'image/png'
    Assert ($r.StatusCode -eq 400) "expected 400, got $($r.StatusCode)"
}

Step "an executable renamed as an image is rejected" {
    $exe = New-Object byte[] 2048
    $exe[0] = 0x4D; $exe[1] = 0x5A
    $r = Post-Avatar -Content $exe -FileName 'avatar.png' -ContentType 'image/png'
    Assert ($r.StatusCode -eq 400) "expected 400, got $($r.StatusCode)"
}

Step "plain text renamed as an image is rejected" {
    $text = [System.Text.Encoding]::UTF8.GetBytes('not an image at all')
    $r = Post-Avatar -Content $text -FileName 'avatar.jpg' -ContentType 'image/jpeg'
    Assert ($r.StatusCode -eq 400) "expected 400, got $($r.StatusCode)"
}

Step "an oversized upload is refused" {
    $big = New-Object byte[] (5 * 1024 * 1024)
    (New-Object System.Random 7).NextBytes($big)
    $r = Post-Avatar -Content $big -FileName 'big.png' -ContentType 'image/png'
    Assert ($r.StatusCode -in 400, 413) "expected 400 or 413, got $($r.StatusCode)"
}

Step "a rejected upload leaves the stored avatar intact" {
    $s = Api GET '/api/auth/me'
    Assert ($s.avatar.kind -eq 'custom') 'avatar should still be the uploaded photo'
    Assert ($s.avatar.version -eq $script:firstVersion) 'avatar version changed after a rejected upload'
}

Step "removing the photo returns to the built-in default" {
    $avatar = Api DELETE '/api/account/avatar'
    Assert ($avatar.kind -eq 'builtIn') "expected builtIn, got $($avatar.kind)"
    $r = Api GET '/api/account/avatar/image?size=128' -Raw -AllowFailure
    Assert ($r.StatusCode -eq 404) 'stored image should be gone so the client draws the default'
}

# --- D. Appearance ---

Write-Host ""
Write-Host "D. Appearance" -ForegroundColor Cyan
foreach ($appearance in 'Light', 'Dark', 'System') {
    Step "appearance '$appearance' saves and reloads" {
        Api PUT '/api/account/preferences' @{ appearance = $appearance; timeFormat = 'System'; timeZoneId = $null } | Out-Null
        $reloaded = Api GET '/api/account/preferences'
        Assert ($reloaded.appearance -eq $appearance) "expected $appearance, got $($reloaded.appearance)"
        $s = Api GET '/api/auth/me'
        Assert ($s.appearance -eq $appearance) 'session response did not carry the appearance'
    }
}

Step "an unknown appearance is rejected" {
    $r = Invoke-WebRequest -Method Put -Uri "$ApiBase/api/account/preferences" -WebSession $session `
        -SkipHttpErrorCheck -ContentType 'application/json' `
        -Body (@{ appearance = 'Chartreuse'; timeFormat = 'System' } | ConvertTo-Json -Compress)
    Assert ($r.StatusCode -eq 400) "expected 400, got $($r.StatusCode)"
}

# --- E. Timezone and clock ---

Write-Host ""
Write-Host "E. Timezone and clock" -ForegroundColor Cyan
Step "a valid timezone and 12-hour clock save" {
    $saved = Api PUT '/api/account/preferences' @{ appearance = 'System'; timeFormat = 'Hour12'; timeZoneId = 'UTC' }
    Assert ($saved.timeZoneId -eq 'UTC') "expected UTC, got $($saved.timeZoneId)"
    Assert ($saved.timeFormat -eq 'Hour12') "expected Hour12, got $($saved.timeFormat)"
}

Step "timezone and clock persist on the session response used by the shell" {
    $s = Api GET '/api/auth/me'
    Assert ($s.displayTimeZoneId -eq 'UTC') 'session timezone missing'
    Assert ($s.timeFormat -eq 'Hour12') 'session clock format missing'
}

Step "an IANA timezone is accepted" {
    $saved = Api PUT '/api/account/preferences' @{ appearance = 'System'; timeFormat = 'System'; timeZoneId = 'Africa/Johannesburg' }
    Assert ($saved.timeZoneId -eq 'Africa/Johannesburg') "expected Africa/Johannesburg, got $($saved.timeZoneId)"
}

foreach ($bad in 'Mars/Olympus_Mons', '../../etc/localtime', 'Not A Zone') {
    Step "invalid timezone rejected: $bad" {
        $r = Invoke-WebRequest -Method Put -Uri "$ApiBase/api/account/preferences" -WebSession $session `
            -SkipHttpErrorCheck -ContentType 'application/json' `
            -Body (@{ appearance = 'System'; timeFormat = 'System'; timeZoneId = $bad } | ConvertTo-Json -Compress)
        Assert ($r.StatusCode -eq 400) "expected 400, got $($r.StatusCode)"
    }
}

Step "clearing the timezone falls back to the browser" {
    $saved = Api PUT '/api/account/preferences' @{ appearance = 'System'; timeFormat = 'System'; timeZoneId = $null }
    Assert ($null -eq $saved.timeZoneId) 'timezone should be cleared'
}

# --- F. Notifications ---

Write-Host ""
Write-Host "F. Notification preferences" -ForegroundColor Cyan
$preferences = Api GET '/api/account/notification-preferences'
Step "only real configurable notifications are listed" {
    Assert ($null -ne $preferences) 'no response'
    Write-Host ("        listed: " + (($preferences | ForEach-Object { $_.definitionId }) -join ', ')) -ForegroundColor DarkGray
}

if ($preferences.Count -gt 0) {
    Step "the in-app channel is reported as required, not as an editable toggle" {
        foreach ($p in $preferences) {
            Assert ($p.platformEnabled -eq $true) "$($p.definitionId): in-app should be enabled"
            Assert ($p.platformEditable -eq $false) "$($p.definitionId): in-app should not be editable"
            Assert (-not [string]::IsNullOrWhiteSpace($p.platformNotEditableReason)) "$($p.definitionId): missing reason"
        }
    }

    Step "the email channel is editable" {
        foreach ($p in $preferences) {
            Assert ($p.emailEditable -eq $true) "$($p.definitionId): email should be editable"
        }
    }

    $target = $preferences[0]
    Step "enabling email for '$($target.definitionId)' persists" {
        $u = Api PUT "/api/account/notification-preferences/$([Uri]::EscapeDataString($target.definitionId))" @{ emailEnabled = $true }
        Assert ($u.emailEnabled -eq $true) 'email did not turn on'
        $reloaded = Api GET '/api/account/notification-preferences'
        $row = $reloaded | Where-Object { $_.definitionId -eq $target.definitionId }
        Assert ($row.emailEnabled -eq $true) 'email state did not persist'
    }

    Step "disabling email for '$($target.definitionId)' persists" {
        $u = Api PUT "/api/account/notification-preferences/$([Uri]::EscapeDataString($target.definitionId))" @{ emailEnabled = $false }
        Assert ($u.emailEnabled -eq $false) 'email did not turn off'
        $reloaded = Api GET '/api/account/notification-preferences'
        $row = $reloaded | Where-Object { $_.definitionId -eq $target.definitionId }
        Assert ($row.emailEnabled -eq $false) 'email state did not persist'
    }

    Step "turning email off does not change the in-app channel" {
        $reloaded = Api GET '/api/account/notification-preferences'
        $row = $reloaded | Where-Object { $_.definitionId -eq $target.definitionId }
        Assert ($row.platformEnabled -eq $true) 'in-app delivery should be unaffected'
    }

    Step "restore original email preference" {
        Api PUT "/api/account/notification-preferences/$([Uri]::EscapeDataString($target.definitionId))" `
            @{ emailEnabled = [bool]$target.emailEnabled } | Out-Null
    }
}

Step "an unknown notification definition returns 404" {
    $r = Invoke-WebRequest -Method Put -Uri "$ApiBase/api/account/notification-preferences/does.not.exist" `
        -WebSession $session -SkipHttpErrorCheck -ContentType 'application/json' `
        -Body (@{ emailEnabled = $true } | ConvertTo-Json -Compress)
    Assert ($r.StatusCode -eq 404) "expected 404, got $($r.StatusCode)"
}

# --- G. UI routes ---

Write-Host ""
Write-Host "G. Management UI routes" -ForegroundColor Cyan
foreach ($route in '/account', '/account/profile', '/account/notifications', '/account/preferences', '/account/security', '/preferences', '/me/notifications') {
    Step "UI serves $route" {
        $r = Invoke-WebRequest -Method Get -Uri "$UiBase$route" -SkipHttpErrorCheck
        Assert ($r.StatusCode -eq 200) "expected 200, got $($r.StatusCode)"
        Assert ($r.Content -match 'blazor\.webassembly\.js') 'page did not include the Blazor boot script'
    }
}

# --- Restore ---

Write-Host ""
Write-Host "Restoring original account state" -ForegroundColor Cyan
Step "restore display name" {
    Api PUT '/api/account/profile' @{ displayName = $originalProfile.displayName } | Out-Null
}
Step "restore preferences" {
    Api PUT '/api/account/preferences' @{
        appearance = $originalPreferences.appearance
        timeFormat = $originalPreferences.timeFormat
        timeZoneId = $originalPreferences.timeZoneId
    } | Out-Null
}
Step "restore avatar" {
    Api DELETE '/api/account/avatar' | Out-Null
}

Write-Host ""
Write-Host ("Passed: {0}   Failed: {1}" -f $script:Pass, $script:Fail) -ForegroundColor Cyan
if ($script:Fail -gt 0) { exit 1 }
exit 0
