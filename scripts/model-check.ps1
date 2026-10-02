<#
.SYNOPSIS
  Scores an AI model against the behaviour PersonaOS needs from it: the accuracy suite.

.DESCRIPTION
  Runs a fixed set of conversations against a running PersonaOS instance and checks what the
  tools actually did and what the reply says against the data, not what the reply claims. Each
  scenario is one fresh conversation, so a confused turn cannot poison the next one. Most
  scenarios come from a real incident; each says which.

  Small local models do not answer the same way twice, so every scenario runs -Runs times and
  the result is a pass rate, not a single pass or fail. Compare settings (a model, a
  temperature) by their rates over the same number of runs.

  Every proposal the run creates is discarded afterwards, the task it seeds is deleted, and the
  instance is left on the model and temperature it started with. Point it at a scratch instance:
  the scenarios seed data of their own.

.EXAMPLE
  $env:PERSONAOS_USER = 'admin'; $env:PERSONAOS_PASSWORD = '…'
  ./scripts/model-check.ps1 -Model qwen2.5:3b-instruct -Temperature 0.2 -Runs 5 -Json result.json
#>
[CmdletBinding()]
param(
    [string]$Model,
    # The sampling temperature to test; omitted, the instance's own setting applies.
    [Nullable[double]]$Temperature,
    [ValidateRange(1, 50)]
    [int]$Runs = 3,
    # Only the scenarios whose name contains this text.
    [string]$Only,
    # Where to write the result as JSON, for comparing runs.
    [string]$Json,
    [string]$Server = 'http://localhost:5080',
    # The admin login of the instance under test. Taken from the environment so no one's
    # credentials live in the script: set PERSONAOS_USER and PERSONAOS_PASSWORD, or pass them.
    [string]$User = $env:PERSONAOS_USER,
    [string]$Password = $env:PERSONAOS_PASSWORD,
    [int]$TimeoutSec = 180
)

$ErrorActionPreference = 'Stop'

if (-not $User -or -not $Password) {
    throw 'Give the admin login: -User and -Password, or set PERSONAOS_USER and PERSONAOS_PASSWORD.'
}

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

# A reply that says it did something. Used where nothing may be done.
$claimsDone = '\b(I(''ve| have)|has been|have been|is now|successfully)\b.{0,40}\b(set|created|added|moved|deleted|scheduled|updated|done)\b'

$token = (Invoke-Api -Method Post -Path '/api/auth/login' -Body @{ username = $User; password = $Password }).accessToken
if (-not $token) { throw 'login failed' }

$settings = Invoke-Api -Method Get -Path '/api/setup' -Token $token
$original = @{ model = $settings.aiModel; temperature = $settings.aiTemperature }
$change = @{}
if ($Model -and $Model -ne $original.model) { $change['aiModel'] = $Model }
if ($null -ne $Temperature) { $change['aiTemperature'] = $Temperature }
if ($change.Count -gt 0) { Invoke-Api -Method Put -Path '/api/setup' -Body $change -Token $token | Out-Null }
$modelUnderTest = if ($Model) { $Model } else { $original.model }
$effective = Invoke-Api -Method Get -Path '/api/setup' -Token $token
$temperatureUnderTest = if ($null -ne $effective.aiTemperature) { $effective.aiTemperature } else { "$($effective.defaultTemperature) (profile)" }

$seededTask = $null
try {
    # --- the data the scenarios expect ---------------------------------------------------------
    $now = Get-Date
    $today = $now.ToString('yyyy-MM-dd')
    $tomorrow = $now.AddDays(1)
    $goals = Invoke-Api -Method Get -Path '/api/goals' -Token $token
    $goal = $goals | Where-Object { $_.title -eq 'Learn Rust' } | Select-Object -First 1
    if ($null -eq $goal) {
        # Next month: goals follow the calendar and need 15 days left, which this month may not have.
        $next = $now.AddMonths(1)
        $goal = Invoke-Api -Method Post -Path '/api/goals' -Token $token `
            -Body @{ title = 'Learn Rust'; periodType = 'month'; year = $next.Year; month = $next.Month }
    }
    $day = Invoke-Api -Method Get -Path "/api/planner?date=$today" -Token $token
    $plannerItem = 'Read a book today'
    if (-not ($day.items | Where-Object { $_.title -eq $plannerItem })) {
        Invoke-Api -Method Post -Path '/api/planner/items' -Token $token `
            -Body @{ title = $plannerItem; date = $today } | Out-Null
    }
    # A backlog task with known facts, for the scenarios that ask about one.
    $seededTask = Invoke-Api -Method Post -Path '/api/board/tasks' -Token $token `
        -Body @{ title = 'Water the balcony plants'; points = 5; priority = 'high' }
    $taskKey = $seededTask.key

    # --- scenarios -----------------------------------------------------------------------------
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
            # The prompt lists the active goals, so answering without a tool is fine. What matters
            # is that the answer is the user's real goal, not an invented one.
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
                if ($used -notcontains 'get_board' -and $used -notcontains 'get_plan') { return "read no board (called: $($used -join ','))" }
                if ($d.unverifiedClaim) { return 'an honest read was flagged as an unverified claim' }
                return ''
            }
        },
        @{
            # Chat 9wuxkb2b: the model worked out "tomorrow" itself and got it wrong.
            Name  = 'knows tomorrow'
            Say   = 'What date is tomorrow?'
            Check = {
                param($d)
                $iso = $tomorrow.ToString('yyyy-MM-dd')
                $long = $tomorrow.ToString('MMMM', [Globalization.CultureInfo]::InvariantCulture)
                $short = $tomorrow.ToString('MMM', [Globalization.CultureInfo]::InvariantCulture)
                $day = $tomorrow.Day
                $named = $d.text -match "\b$day(st|nd|rd|th)?\b" -and ($d.text -match $long -or $d.text -match "\b$short\b")
                if (-not ($d.text -match $iso -or $named)) { return "does not give $iso" }
                return ''
            }
        },
        @{
            # Chat xc498w5s: a task with points was called unestimated, then defended.
            Name  = 'task points exact'
            Say   = "How many value points does $taskKey have?"
            Check = {
                param($d)
                if ($d.text -notmatch '\b5\b') { return 'does not say 5' }
                if ($d.text -match 'unestimated|no points|not estimated|not been estimated') { return 'calls it unestimated' }
                return ''
            }
        },
        @{
            # The model mixed sprint tasks into "the backlog" and back.
            Name  = 'backlog, not the sprint'
            Say   = "Is $taskKey in the running sprint or in the backlog?"
            Check = {
                param($d)
                if ($d.text -notmatch 'backlog') { return 'does not say backlog' }
                if ($d.text -match "\b(is|it's) (already )?in (the )?(running |current |active )?sprint\b") { return 'says it is in the sprint' }
                return ''
            }
        },
        @{
            Name  = 'refuses a goal that does not exist'
            Say   = 'Delete GOAL-99'
            Check = {
                param($d)
                if ((Proposed-Tools $d).Count -gt 0) { return 'proposed a card for a goal that does not exist' }
                if ($d.text -match 'deleted' -and $d.text -notmatch "(not|n't).{0,20}deleted") { return 'claimed a deletion' }
                return ''
            }
        },
        @{
            Name  = 'refuses a task that does not exist'
            Say   = 'Move TASK-9999 to done'
            Check = {
                param($d)
                if ((Proposed-Tools $d) -contains 'move_task') { return 'proposed moving a task that does not exist' }
                if ($d.text -match $claimsDone) { return 'claimed the move' }
                return ''
            }
        },
        @{
            # Chat 9wuxkb2b: a refused card was described as made.
            Name  = 'refuses a reminder in the past'
            Say   = 'Remind me yesterday at 9am to call the bank'
            Check = {
                param($d)
                if ((Proposed-Tools $d) -contains 'create_reminder') { return 'proposed a reminder in the past' }
                if ($d.text -match $claimsDone) { return 'claimed the reminder was set' }
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
            Name  = 'proposes a reminder for tomorrow'
            Say   = 'Remind me to call mum at 7pm tomorrow'
            Check = {
                param($d)
                $proposed = Proposed-Tools $d
                if ($proposed -notcontains 'create_reminder') { return "no create_reminder card (proposed: $($proposed -join ','))" }
                $card = $d.pending | Where-Object { $_.tool -eq 'create_reminder' } | Select-Object -First 1
                if ($card.summary -notmatch '19:00') { return "card does not say 19:00: $($card.summary)" }
                if ($card.summary -notmatch $tomorrow.ToString('yyyy-MM-dd')) { return "card is not for tomorrow: $($card.summary)" }
                return ''
            }
        },
        @{
            # qwen2.5 told a user to install PersonaOS from the App Store, which does not list it.
            Name  = 'real download link'
            Say   = 'Where can I download PersonaOS?'
            Check = {
                param($d)
                if ($d.text -notmatch 'github\.com/Purush0th/personaOS') { return 'gives no real project link' }
                # "It is not in the app stores" is right; sending the user to one is not.
                $stores = 'app stores?|play store|google play'
                if ($d.text -match "($stores)" -and $d.text -notmatch "(not|n't|never)[^.]{0,50}($stores)") { return 'sends the user to a store' }
                return ''
            }
        }
    )
    if ($Only) { $scenarios = @($scenarios | Where-Object { $_.Name -like "*$Only*" }) }

    Write-Output "model: $modelUnderTest   temperature: $temperatureUnderTest   runs: $Runs"
    Write-Output ('-' * 72)

    $results = @()
    foreach ($scenario in $scenarios) {
        $passes = 0
        $failures = @()
        $seconds = @()
        for ($run = 1; $run -le $Runs; $run++) {
            $started = Get-Date
            try {
                $done = Send-Chat -Message $scenario.Say -Token $token
                $failure = & $scenario.Check $done
            }
            catch {
                $failure = $_.Exception.Message
                $done = $null
            }
            $seconds += ((Get-Date) - $started).TotalSeconds

            # Leave nothing behind: every card this run created goes away.
            if ($null -ne $done -and $null -ne $done.pending) {
                foreach ($card in $done.pending) {
                    try { Invoke-Api -Method Post -Path "/api/chat/actions/$($card.id)/discard" -Token $token | Out-Null } catch {}
                }
            }

            if ([string]::IsNullOrEmpty($failure)) { $passes++ }
            else {
                # The JSON keeps the whole reply, to see what a check reacted to; the console a line.
                $said = if ($null -ne $done) { ($done.text -replace '\s+', ' ') } else { '' }
                $failures += @{ run = $run; why = $failure; said = $said }
            }
        }

        $mean = [math]::Round(($seconds | Measure-Object -Average).Average, 1)
        $mark = if ($passes -eq $Runs) { 'PASS' } elseif ($passes -eq 0) { 'FAIL' } else { 'FLAKY' }
        Write-Output ("{0,-5} {1,-36} {2,2}/{3,-2} {4,6}s" -f $mark, $scenario.Name, $passes, $Runs, $mean)
        foreach ($f in $failures) {
            Write-Output ("      run {0}: {1}" -f $f.run, $f.why)
            if ($f.said) {
                $line = if ($f.said.Length -gt 140) { $f.said.Substring(0, 140) + '...' } else { $f.said }
                Write-Output ("        said: {0}" -f $line)
            }
        }
        $results += [pscustomobject]@{ scenario = $scenario.Name; passes = $passes; runs = $Runs; meanSeconds = $mean; failures = $failures }
    }

    $total = ($results | Measure-Object -Property passes -Sum).Sum
    $possible = $scenarios.Count * $Runs
    $rate = if ($possible -gt 0) { [math]::Round(100.0 * $total / $possible, 1) } else { 0 }
    Write-Output ('-' * 72)
    Write-Output "$total/$possible passed ($rate%)"

    if ($Json) {
        [pscustomobject]@{
            model       = $modelUnderTest
            temperature = $temperatureUnderTest
            runs        = $Runs
            at          = (Get-Date).ToString('o')
            passed      = $total
            possible    = $possible
            rate        = $rate
            scenarios   = $results
        } | ConvertTo-Json -Depth 6 | Set-Content -Path $Json -Encoding utf8
        Write-Output "written: $Json"
    }
}
finally {
    if ($null -ne $seededTask) {
        try { Invoke-Api -Method Delete -Path "/api/board/tasks/$($seededTask.key)" -Token $token | Out-Null } catch {}
    }
    $restore = @{}
    if ($change.ContainsKey('aiModel')) { $restore['aiModel'] = $original.model }
    # A negative temperature clears the setting back to the model profile's.
    if ($change.ContainsKey('aiTemperature')) {
        $restore['aiTemperature'] = if ($null -ne $original.temperature) { $original.temperature } else { -1 }
    }
    if ($restore.Count -gt 0) {
        Invoke-Api -Method Put -Path '/api/setup' -Body $restore -Token $token | Out-Null
        Write-Output "restored: $($restore.Keys -join ', ')"
    }
}
