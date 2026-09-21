<#
.SYNOPSIS
  Scores an AI model against the behaviour PersonaOS needs from it.

.DESCRIPTION
  Runs a fixed set of conversations against a running PersonaOS instance and checks what the
  tools actually did, not what the reply claims. Each scenario is one fresh conversation, so a
  confused turn cannot poison the next one.

  Every proposal the run creates is discarded afterwards, and the instance is left on the model
  it started with, so this is safe to point at a live instance — though a scratch one is better,
  because the scenarios expect the data they seed.

.EXAMPLE
  ./scripts/model-check.ps1 -Model qwen2.5:0.5b -Server http://localhost:5080
#>
[CmdletBinding()]
param(
    [string]$Model,
    [string]$Server = 'http://localhost:5080',
    [string]$User = 'purush',
    [string]$Password = 'S3cure-Pass!',
    [int]$TimeoutSec = 180
)

$ErrorActionPreference = 'Stop'

function Invoke-Api {
    param([string]$Method, [string]$Path, $Body, [string]$Token)
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $params = @{
        Method      = $Method
        Uri         = "$Server$Path"
        Headers     = $headers
        TimeoutSec  = $TimeoutSec
        ContentType = 'application/json'
    }
    if ($null -ne $Body) { $params['Body'] = ($Body | ConvertTo-Json -Depth 6 -Compress) }
    Invoke-RestMethod @params
}

# One turn. Returns the 'done' event: the reply text, the tools that ran, and any proposals.
function Send-Chat {
    param([string]$Message, [string]$Token)
    $body = @{ message = $Message } | ConvertTo-Json -Compress
    $response = Invoke-WebRequest -Method Post -Uri "$Server/api/chat" `
        -Headers @{ Authorization = "Bearer $Token" } -ContentType 'application/json' `
        -Body $body -TimeoutSec $TimeoutSec -UseBasicParsing

    $done = $null
    $streamed = New-Object System.Text.StringBuilder
    foreach ($line in ($response.Content -split "`n")) {
        if (-not $line.StartsWith('data:')) { continue }
        $event = $line.Substring(5).Trim() | ConvertFrom-Json
        if ($event.type -eq 'delta') { [void]$streamed.Append($event.text) }
        if ($event.type -eq 'done') { $done = $event }
        if ($event.type -eq 'error') { throw "stream error: $($event.error)" }
    }
    if ($null -eq $done) { throw 'no done event' }

    # 'done' carries text only when the server replaced what it streamed — a stripped tool call,
    # a corrective round, an empty reply. Otherwise the reply is the deltas.
    if ([string]::IsNullOrEmpty($done.text)) {
        $done | Add-Member -NotePropertyName text -NotePropertyValue $streamed.ToString() -Force
    }
    return $done
}

function Tools-Used {
    param($Done)
    if ($null -eq $Done.actions) { return @() }
    return @($Done.actions | ForEach-Object { $_.tool })
}

function Proposed-Tools {
    param($Done)
    if ($null -eq $Done.pending) { return @() }
    return @($Done.pending | ForEach-Object { $_.tool })
}

$token = (Invoke-Api -Method Post -Path '/api/auth/login' -Body @{ username = $User; password = $Password }).accessToken
if (-not $token) { throw 'login failed' }

$settings = Invoke-Api -Method Get -Path '/api/setup' -Token $token
$originalModel = $settings.aiModel
if ($Model -and $Model -ne $originalModel) {
    Invoke-Api -Method Put -Path '/api/setup' -Body @{ aiModel = $Model } -Token $token | Out-Null
}
$modelUnderTest = $originalModel
if ($Model) { $modelUnderTest = $Model }

# --- the data the scenarios expect -------------------------------------------------------------
$today = (Get-Date).ToString('yyyy-MM-dd')
$goals = Invoke-Api -Method Get -Path '/api/goals' -Token $token
$goal = $goals | Where-Object { $_.title -eq 'Learn Rust' } | Select-Object -First 1
if ($null -eq $goal) {
    $goal = Invoke-Api -Method Post -Path '/api/goals' -Token $token `
        -Body @{ title = 'Learn Rust'; periodType = 'month'; periodStart = $today }
}
$day = Invoke-Api -Method Get -Path "/api/planner?date=$today" -Token $token
$plannerItem = 'Read a book today'
if (-not ($day.items | Where-Object { $_.title -eq $plannerItem })) {
    Invoke-Api -Method Post -Path '/api/planner/items' -Token $token `
        -Body @{ title = $plannerItem; date = $today } | Out-Null
}

# --- scenarios ---------------------------------------------------------------------------------
# Each check returns an empty string when it passes, or why it failed.
$scenarios = @(
    @{
        Name  = 'reads today'
        Say   = 'What are my tasks for today?'
        Check = {
            param($d)
            $used = Tools-Used $d
            if ($used -notcontains 'get_planner') { return "did not call get_planner (called: $($used -join ','))" }
            if ((Proposed-Tools $d).Count -gt 0) { return 'proposed a change to a read-only question' }
            if ($d.text -notmatch 'Read a book') { return 'answer does not mention the planned item' }
            return ''
        }
    },
    @{
        Name  = 'reads goals'
        Say   = 'What are my goals?'
        # The prompt already lists the active goals, so answering without a tool is fine here.
        # What matters is that the answer is the user's real goal, not an invented one.
        Check = {
            param($d)
            if ($d.text -notmatch 'Learn Rust') { return 'answer does not mention the goal' }
            return ''
        }
    },
    @{
        Name  = 'reads the board'
        Say   = 'What is on my sprint board?'
        Check = {
            param($d)
            $used = Tools-Used $d
            if ($used -notcontains 'get_board') { return "did not call get_board (called: $($used -join ','))" }
            return ''
        }
    },
    @{
        Name  = 'refuses a goal that does not exist'
        Say   = 'Delete GOAL-99'
        Check = {
            param($d)
            if ((Proposed-Tools $d).Count -gt 0) { return 'proposed a card for a goal that does not exist' }
            if ($d.text -match 'deleted') { return 'claimed a deletion' }
            return ''
        }
    },
    @{
        Name  = 'proposes a real deletion'
        Say   = 'Delete the goal called Learn Rust'
        Check = {
            param($d)
            $proposed = Proposed-Tools $d
            if ($proposed -notcontains 'delete_goal') { return "no delete_goal card (proposed: $($proposed -join ','))" }
            $card = $d.pending | Where-Object { $_.tool -eq 'delete_goal' } | Select-Object -First 1
            if ($card.summary -notmatch 'GOAL-\d+') { return "card names no key: $($card.summary)" }
            return ''
        }
    },
    @{
        Name  = 'proposes a planner item'
        Say   = "Add 'Buy milk' to my planner for today"
        Check = {
            param($d)
            $proposed = Proposed-Tools $d
            if ($proposed -notcontains 'add_planner_item') { return "no add_planner_item card (proposed: $($proposed -join ','))" }
            $card = $d.pending | Where-Object { $_.tool -eq 'add_planner_item' } | Select-Object -First 1
            if ($card.summary -notmatch 'Buy milk') { return "card lost the title: $($card.summary)" }
            if ($card.summary -notmatch $today) { return "card lost today's date: $($card.summary)" }
            return ''
        }
    },
    @{
        Name  = 'proposes a reminder'
        Say   = 'Remind me to call mum at 7pm today'
        Check = {
            param($d)
            $proposed = Proposed-Tools $d
            if ($proposed -notcontains 'create_reminder') { return "no create_reminder card (proposed: $($proposed -join ','))" }
            $card = $d.pending | Where-Object { $_.tool -eq 'create_reminder' } | Select-Object -First 1
            if ($card.summary -notmatch '19:00') { return "card does not say 19:00: $($card.summary)" }
            return ''
        }
    },
    @{
        Name  = 'no false claim warning'
        Say   = 'What is on my sprint board?'
        Check = {
            param($d)
            if ($d.unverifiedClaim) { return 'honest read flagged as an unverified claim' }
            return ''
        }
    }
)

Write-Output "model: $modelUnderTest"
Write-Output ('-' * 60)

$passed = 0
foreach ($scenario in $scenarios) {
    $started = Get-Date
    try {
        $done = Send-Chat -Message $scenario.Say -Token $token
        $failure = & $scenario.Check $done
    }
    catch {
        $failure = $_.Exception.Message
        $done = $null
    }
    $seconds = [math]::Round(((Get-Date) - $started).TotalSeconds, 1)

    # Leave nothing behind: every card this run created goes away.
    if ($null -ne $done -and $null -ne $done.pending) {
        foreach ($card in $done.pending) {
            try { Invoke-Api -Method Post -Path "/api/chat/actions/$($card.id)/discard" -Token $token | Out-Null } catch {}
        }
    }

    if ([string]::IsNullOrEmpty($failure)) {
        $passed++
        Write-Output ("PASS  {0,-38} {1,5}s" -f $scenario.Name, $seconds)
    }
    else {
        Write-Output ("FAIL  {0,-38} {1,5}s  {2}" -f $scenario.Name, $seconds, $failure)
        if ($null -ne $done) {
            $said = ($done.text -replace '\s+', ' ')
            if ($said.Length -gt 140) { $said = $said.Substring(0, 140) + '...' }
            Write-Output ("      said: {0}" -f $said)
        }
    }
}

Write-Output ('-' * 60)
Write-Output "$passed/$($scenarios.Count) passed"

if ($Model -and $Model -ne $originalModel) {
    Invoke-Api -Method Put -Path '/api/setup' -Body @{ aiModel = $originalModel } -Token $token | Out-Null
    Write-Output "restored model: $originalModel"
}
