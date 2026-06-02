<#
.SYNOPSIS
    End-to-end verification of mid-session bundle switching (#93), fully
    headless. No WAM picker, no Flutter dashboard, no human input.

.DESCRIPTION
    Proves the chain issue #93 adds:
      1. Agent launches headless and AUTO-JOINS the session (--auto-join skips
         the toast so the agent becomes an active participant - the only state
         that receives mid-session pushes).
      2. With no bundles, the agent's allowlist is baseline-only (msedge, etc.)
         and does NOT include the app the test bundle grants.
      3. Teacher PUTs /sessions/{id}/bundles adding the seeded "Notepad (dev)"
         app bundle. Backend recomputes the allowlist and pushes
         SessionBundlesUpdated to the student's user group.
      4. The agent rebuilds its matcher; its /status allowedApps now includes
         "notepad".
      5. Teacher PUTs an empty bundle set; the agent's allowedApps drops
         "notepad" again.

    Observes the agent's real matcher state via the --status-endpoint
    allowedApps field, so this clears the "seen in a running agent" bar - not
    just a green unit test.

    Requires the backend to be running already (start it the same way
    verify-session-start.ps1 documents). Cleans up the agent process on exit.

.PARAMETER BackendUrl
    Backend base URL. Default http://localhost:5276.

.PARAMETER StatusPort
    Loopback port for the agent's status endpoint. Default 5296 (one above
    verify-session-start's default so the two can run back-to-back).

.PARAMETER TeacherOid
    Seeded Dev Teacher OID. Default 11111111-1111-1111-1111-111111111111.

.PARAMETER StudentOid
    Seeded Dev Student OID for the agent to impersonate. Default
    22222222-2222-2222-2222-222222222222.

.PARAMETER ClassName
    Seeded class name. Default "3A".

.PARAMETER AppBundleName
    Seeded app-bearing bundle name. Default "Notepad (dev)".

.PARAMETER AppProcessName
    The process name that bundle grants and the script watches for in the
    agent's allowedApps. Default "notepad".

.PARAMETER SkipBuild
    Skip the dotnet builds.
#>

[CmdletBinding()]
param(
    [string]$BackendUrl = 'http://localhost:5276',
    [int]$StatusPort = 5296,
    [string]$TeacherOid = '11111111-1111-1111-1111-111111111111',
    [string]$StudentOid = '22222222-2222-2222-2222-222222222222',
    [string]$ClassName = '3A',
    [string]$AppBundleName = 'Notepad (dev)',
    [string]$AppProcessName = 'notepad',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')

function Write-Step($msg) { Write-Host "[verify] $msg" -ForegroundColor Cyan }
function Write-Pass($msg) { Write-Host "[PASS]   $msg" -ForegroundColor Green }
function Write-Fail($msg) { Write-Host "[FAIL]   $msg" -ForegroundColor Red }

$agentProcess = $null

try {
    # ---------------------------------------------------------------- build
    if (-not $SkipBuild) {
        Write-Step 'Building backend...'
        & dotnet build (Join-Path $repoRoot 'backend\Anchor.sln') --nologo -v:q | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Backend build failed.' }

        Write-Step 'Building agent (x64)...'
        & dotnet build (Join-Path $repoRoot 'agent\FocusAgent.sln') -p:Platform=x64 --nologo -v:q | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Agent build failed.' }
    }

    # -------------------------------------------------------------- backend
    Write-Step "Checking backend at $BackendUrl ..."
    $backendUp = $false
    try {
        Invoke-WebRequest -UseBasicParsing -Uri $BackendUrl -TimeoutSec 2 -ErrorAction Stop | Out-Null
        $backendUp = $true
    } catch {
        $sc = $_.Exception.Response.StatusCode.value__
        if ($null -ne $sc -and ($sc -eq 401 -or $sc -eq 404)) { $backendUp = $true }
    }
    if (-not $backendUp) {
        throw "Backend not reachable at $BackendUrl. Start it first with: ``dotnet run --project backend/src/Anchor.Api --no-launch-profile --urls $BackendUrl``"
    }
    Write-Step 'Backend reachable.'

    # --------------------------------------------------------------- agent
    Write-Step "Launching agent (--inject-token --auto-join --status-endpoint $StatusPort)..."
    $agentExe = Join-Path $repoRoot 'agent\src\FocusAgent.App\bin\x64\Debug\net10.0-windows10.0.19041.0\FocusAgent.App.exe'
    if (-not (Test-Path $agentExe)) { throw "Agent exe not found at $agentExe (rebuild?)." }

    $agentProcess = Start-Process -FilePath $agentExe `
        -ArgumentList @('--inject-token', '--auto-join', '--status-endpoint', $StatusPort) `
        -PassThru
    Write-Step "Agent pid=$($agentProcess.Id)"

    # ------------------------------------------------- wait for Connected
    Write-Step 'Waiting for agent to reach Connected...'
    $statusUrl = "http://127.0.0.1:$StatusPort/status"
    $deadline = (Get-Date).AddSeconds(15)
    $status = $null
    do {
        Start-Sleep -Milliseconds 400
        try { $status = Invoke-RestMethod -Uri $statusUrl -TimeoutSec 2 } catch { $status = $null }
        $statusKind = if ($status) { $status.connectionStatus } else { '<unreachable>' }
    } while ($statusKind -ne 'Connected' -and (Get-Date) -lt $deadline)

    if ($statusKind -ne 'Connected') {
        $lastErr = if ($status) { $status.lastError } else { '<none>' }
        throw "Agent did not reach Connected within 15s (last status: $statusKind, error: $lastErr)"
    }
    Write-Pass "Agent connected as '$($status.displayName)'"

    # --------------------------------------------------- find class + bundle
    Write-Step "Finding class id for '$ClassName' and bundle '$AppBundleName'..."
    $classes = Invoke-RestMethod -Uri "$BackendUrl/classes" -Headers @{ 'X-Dev-Impersonate-Oid' = $TeacherOid } -TimeoutSec 5
    $targetClass = $classes | Where-Object { $_.name -eq $ClassName } | Select-Object -First 1
    if ($null -eq $targetClass) { throw "Class '$ClassName' not found. Did the dev seeder run?" }

    $bundles = Invoke-RestMethod -Uri "$BackendUrl/bundles" -Headers @{ 'X-Dev-Impersonate-Oid' = $TeacherOid } -TimeoutSec 5
    $appBundle = $bundles | Where-Object { $_.name -eq $AppBundleName } | Select-Object -First 1
    if ($null -eq $appBundle) { throw "Bundle '$AppBundleName' not found. Did EnsureDevBundlesAsync run with the app-bundle seed (#93)?" }
    Write-Step "Class id: $($targetClass.id), app bundle id: $($appBundle.id)"

    # -------------------------------------------------- POST /sessions
    Write-Step 'POSTing /sessions (no bundles) as Dev Teacher...'
    $body = @{ classId = $targetClass.id; bundleIds = @() } | ConvertTo-Json
    $session = Invoke-RestMethod -Method Post -Uri "$BackendUrl/sessions" `
        -Headers @{ 'X-Dev-Impersonate-Oid' = $TeacherOid; 'Content-Type' = 'application/json' } `
        -Body $body -TimeoutSec 5
    Write-Pass "Session created: $($session.id)"

    # ------------------------------------- wait for the agent to JOIN
    Write-Step 'Polling agent /status for joinedSessionId (auto-join)...'
    $deadline = (Get-Date).AddSeconds(8)
    $joined = $false
    do {
        Start-Sleep -Milliseconds 200
        try { $status = Invoke-RestMethod -Uri $statusUrl -TimeoutSec 2 } catch { }
        if ($status -and $status.joinedSessionId -eq $session.id) { $joined = $true; break }
    } while ((Get-Date) -lt $deadline)
    if (-not $joined) { throw "Agent did not auto-join within 8s (joinedSessionId: $($status.joinedSessionId))." }
    Write-Pass 'Agent auto-joined the session.'

    # ------------------------------------- baseline allowlist (no notepad)
    $baseline = @($status.allowedApps)
    Write-Step "Baseline allowedApps: $($baseline -join ', ')"
    if ($baseline -contains $AppProcessName) {
        throw "Baseline allowlist already contains '$AppProcessName' before adding the bundle - test bundle is leaking into the baseline."
    }
    Write-Pass "Baseline excludes '$AppProcessName' (as expected)."

    # ------------------------------------- ADD bundle mid-session
    Write-Step "PUT /sessions/$($session.id)/bundles adding '$AppBundleName'..."
    $putBody = @{ bundleIds = @($appBundle.id) } | ConvertTo-Json
    Invoke-RestMethod -Method Put -Uri "$BackendUrl/sessions/$($session.id)/bundles" `
        -Headers @{ 'X-Dev-Impersonate-Oid' = $TeacherOid; 'Content-Type' = 'application/json' } `
        -Body $putBody -TimeoutSec 5 | Out-Null

    Write-Step "Polling agent /status until allowedApps includes '$AppProcessName'..."
    $deadline = (Get-Date).AddSeconds(5)
    $added = $false
    do {
        Start-Sleep -Milliseconds 200
        try { $status = Invoke-RestMethod -Uri $statusUrl -TimeoutSec 2 } catch { }
        if ($status -and (@($status.allowedApps) -contains $AppProcessName)) { $added = $true; break }
    } while ((Get-Date) -lt $deadline)
    if (-not $added) {
        throw "Agent did not pick up '$AppProcessName' within 5s of the bundle add. allowedApps: $(@($status.allowedApps) -join ', ')"
    }
    Write-Pass "Agent rebuilt its matcher - allowedApps now includes '$AppProcessName'."

    # ------------------------------------- REMOVE bundle mid-session
    Write-Step "PUT /sessions/$($session.id)/bundles removing all bundles..."
    $emptyBody = @{ bundleIds = @() } | ConvertTo-Json
    Invoke-RestMethod -Method Put -Uri "$BackendUrl/sessions/$($session.id)/bundles" `
        -Headers @{ 'X-Dev-Impersonate-Oid' = $TeacherOid; 'Content-Type' = 'application/json' } `
        -Body $emptyBody -TimeoutSec 5 | Out-Null

    Write-Step "Polling agent /status until allowedApps drops '$AppProcessName'..."
    $deadline = (Get-Date).AddSeconds(5)
    $removed = $false
    do {
        Start-Sleep -Milliseconds 200
        try { $status = Invoke-RestMethod -Uri $statusUrl -TimeoutSec 2 } catch { }
        if ($status -and -not (@($status.allowedApps) -contains $AppProcessName)) { $removed = $true; break }
    } while ((Get-Date) -lt $deadline)
    if (-not $removed) {
        throw "Agent still allows '$AppProcessName' 5s after removing the bundle. allowedApps: $(@($status.allowedApps) -join ', ')"
    }
    Write-Pass "Agent tightened its matcher - allowedApps no longer includes '$AppProcessName'."

    # -------------------------------------------------- summary
    Write-Host ''
    Write-Host '=================================' -ForegroundColor Green
    Write-Host '  BUNDLE-SWITCH VERIFY: PASS' -ForegroundColor Green
    Write-Host '=================================' -ForegroundColor Green
    exit 0
}
catch {
    Write-Fail $_.Exception.Message
    exit 2
}
finally {
    if ($agentProcess) {
        Write-Step "Stopping agent pid=$($agentProcess.Id)..."
        try { Stop-Process -Id $agentProcess.Id -Force -ErrorAction SilentlyContinue } catch { }
    }
}
