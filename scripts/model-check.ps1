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
    # Which scenarios: 'core' (the incidents the suite started from), 'extended' (edits, follow-ups,
    # relative times, grounding and honesty, added 2026-11-02 when core reached 97%), 'holdout'
    # (the same skills in other words, never tuned against: it shows whether a fix generalises),
    # 'holdout2' and 'holdout3' (fresh ones, each written once the one before had been tuned
    # against), 'planner' (questions about a seeded day that need a fresh read, 2026-11-07), or
    # 'all' (core and extended).
    [ValidateSet('all', 'core', 'extended', 'holdout', 'holdout2', 'holdout3', 'planner')]
    [string]$Suite = 'all',
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

# The conversations the run opened, deleted at the end so they do not pile up in the sidebar.
$script:conversations = New-Object System.Collections.Generic.HashSet[object]

# One turn. Returns the 'done' event: the reply text, the tools that ran, and any proposals.
function Send-Chat {
    param([string]$Message, [string]$Token, $ConversationId)
    $payload = @{ message = $Message }
    if ($null -ne $ConversationId) { $payload['conversationId'] = $ConversationId }
    $body = $payload | ConvertTo-Json -Compress
    $response = Invoke-WebRequest -Method Post -Uri "$Server/api/chat" `
        -Headers @{ Authorization = "Bearer $Token" } -ContentType 'application/json' `
        -Body $body -TimeoutSec $TimeoutSec -UseBasicParsing

    $done = $null
    $streamed = New-Object System.Text.StringBuilder
    foreach ($line in ($response.Content -split "`n")) {
        if (-not $line.StartsWith('data:')) { continue }
        $event = $line.Substring(5).Trim() | ConvertFrom-Json
        if ($event.type -eq 'start' -and $null -ne $event.conversationId) { [void]$script:conversations.Add($event.conversationId) }
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

# A card from $Tool whose summary matches $Expect; '' when there is one, else why not.
function Expect-Card {
    param($Done, [string]$Tool, [string]$Expect)
    $proposed = Proposed-Tools $Done
    if ($proposed -notcontains $Tool) {
        return "no $Tool card (proposed: $($proposed -join ','); ran: $((Tools-Used $Done) -join ','))"
    }
    $card = $Done.pending | Where-Object { $_.tool -eq $Tool } | Select-Object -First 1
    if ($card.summary -notmatch $Expect) { return "card misses '$Expect': $($card.summary)" }
    return ''
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
$seededReminder = $null
$seededNote = $null
$seededRoutine = @()
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
    # A reminder and a note the extended scenarios read and act on.
    $bankAt = $now.Date.AddDays(2).AddHours(10)
    $seededReminder = Invoke-Api -Method Post -Path '/api/reminders' -Token $token `
        -Body @{ message = 'Call the bank about the card'; dueAtLocal = $bankAt.ToString('yyyy-MM-ddTHH:mm:ss') }
    # Notes and memories are saved at once, not proposed, so a run that saves one leaves it behind
    # unless it is taken back. Leftovers piled up (eight "Call mum tomorrow at 6pm" notes by
    # 2026-11-06) and changed what the note scenarios read; everything not here now goes after each run.
    $keepNotes = @(Invoke-Api -Method Get -Path '/api/notes' -Token $token | ForEach-Object { $_.id })
    $keepMemories = @(Invoke-Api -Method Get -Path '/api/memories' -Token $token | ForEach-Object { $_.id })
    $seededNote = Invoke-Api -Method Post -Path '/api/notes' -Token $token -Body @{ text = 'Buy stamps for the wedding cards' }
    $keepNotes += $seededNote.id
    # The planner suite's day (owner's chat 4nzrb5af, 2026-11-07): items whose times touch, so an
    # answer from memory or the wrong neighbour shows. Only for that suite, and deleted after it.
    # Not the training routine (training/capabilities/planner.py): the suite measures unseen days.
    if ($Suite -eq 'planner') {
        $routine = @(
            @('Morning walk', '06:15', '06:45'), @('School run', '08:30', '09:00'), @('Deep work', '09:00', '12:00'),
            @('Lunch with Sam', '12:00', '12:45'), @('Client calls', '13:00', '15:00'), @('Admin and email', '15:00', '16:30'),
            @('Gym session', '18:30', '19:45'), @('Evening walk with the dog', '20:00', '20:30'), @('Bedtime', '22:30', $null)
        )
        foreach ($r in $routine) {
            $body = @{ title = $r[0]; date = $today; scheduledTime = "$($r[1]):00" }
            if ($r[2]) { $body['endTime'] = "$($r[2]):00" }
            $seededRoutine += (Invoke-Api -Method Post -Path '/api/planner/items' -Token $token -Body $body).id
        }
    }
    # A day three days ahead by name, for scenarios that name a weekday (never today's).
    $inThreeDays = $now.Date.AddDays(3)
    $inThreeDaysName = $inThreeDays.ToString('dddd', [Globalization.CultureInfo]::InvariantCulture)
    # The next such weekday, or the day after it when that weekday is today: "on Saturday" said on a
    # Saturday is left to the model on purpose (today or next week?), so a Saturday run measured the calendar.
    $nextDay = {
        param([DayOfWeek]$weekday)
        if ($now.DayOfWeek -eq $weekday) { $weekday = [DayOfWeek](([int]$weekday + 1) % 7) }
        $d = $now.Date.AddDays(1)
        while ($d.DayOfWeek -ne $weekday) { $d = $d.AddDays(1) }
        $d
    }
    $dayName = { param([datetime]$d) $d.ToString('dddd', [Globalization.CultureInfo]::InvariantCulture) }
    $nickname = $settings.assistantNickname

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
            # Notes (2026-10-10): a note from chat is saved straight away, no card.
            Name  = 'saves a note'
            Say   = 'Note down: call the plumber about the balcony leak'
            Check = {
                param($d)
                $used = Tools-Used $d
                # Leave nothing behind: the note saved without a card goes too.
                # Piped, so a JSON array is unrolled: Windows PowerShell hands it over as one object.
                $notes = @(Invoke-Api -Method Get -Path '/api/notes?search=plumber' -Token $token | ForEach-Object { $_ })
                foreach ($n in $notes) { Invoke-Api -Method Delete -Path "/api/notes/$($n.id)" -Token $token | Out-Null }
                if ($used -notcontains 'create_note') { return "did not save a note (ran: $($used -join ','); proposed: $((Proposed-Tools $d) -join ','))" }
                if (-not ($notes | Where-Object { $_.text -match 'plumber' })) { return 'the note was not saved' }
                return ''
            }
        },
        @{
            # Owner, 2026-10-11: saved as "Note to self: Prepare dinner ingredients for tomorrow",
            # and the reply flagged "nothing was saved" over the note.
            Name  = 'note in the user''s words'
            Say   = 'Can you add a note to meal prep today?'
            Check = {
                param($d)
                $notes = @(Invoke-Api -Method Get -Path '/api/notes' -Token $token | ForEach-Object { $_ })
                $saved = @($notes | Where-Object { $_.text -match 'meal|prep|dinner' })
                foreach ($n in $saved) { Invoke-Api -Method Delete -Path "/api/notes/$($n.id)" -Token $token | Out-Null }
                if ((Tools-Used $d) -notcontains 'create_note') { return 'did not save a note' }
                if ($saved.Count -ne 1) { return "saved $($saved.Count) notes" }
                if ($saved[0].text -notmatch '^meal prep') { return "not in the user's words: $($saved[0].text)" }
                if ($saved[0].text -match 'tomorrow') { return "changed the day: $($saved[0].text)" }
                if ($d.unverifiedClaim) { return 'flagged nothing was saved over a saved note' }
                return ''
            }
        },
        @{
            # Owner, 2026-10-11: "I've noted that…" with no note saved, then the memory check's own
            # words ("Nothing worth remembering was said") and "nothing was saved".
            Name  = 'note to cook today'
            Say   = 'Add a note to cook today'
            Check = {
                param($d)
                $notes = @(Invoke-Api -Method Get -Path '/api/notes' -Token $token | ForEach-Object { $_ })
                $saved = @($notes | Where-Object { $_.text -match 'cook' })
                foreach ($n in $saved) { Invoke-Api -Method Delete -Path "/api/notes/$($n.id)" -Token $token | Out-Null }
                if ($saved.Count -ne 1) { return "saved $($saved.Count) notes (ran: $((Tools-Used $d) -join ','))" }
                if ($saved[0].text -notmatch '^cook') { return "not in the user's words: $($saved[0].text)" }
                if ($d.text -match 'worth remembering|nothing was remembered') { return "repeated the check: $($d.text)" }
                if ($d.unverifiedClaim) { return 'flagged nothing was saved' }
                return ''
            }
        },
        # Intent (docs/model-accuracy.md, 2026-10-12): the same intent in different words reaches the
        # same tool; a request that does not say what to save asks; conversation changes nothing.
        @{
            Name  = 'intent: add a task'
            Say   = 'Add a task to follow up with Ravi tomorrow.'
            Check = { param($d) Expect-Card $d 'create_task' 'Ravi' }
        },
        @{
            Name  = 'intent: make a to-do'
            Say   = 'Create a to-do for following up with Ravi.'
            Check = { param($d) Expect-Card $d 'create_task' 'Ravi' }
        },
        @{
            Name  = 'intent: put it on my board'
            Say   = "Put 'renew the passport' on my board."
            Check = { param($d) Expect-Card $d 'create_task' 'passport' }
        },
        @{
            Name  = 'intent: don''t let me forget'
            Say   = "Don't let me forget to call mum tomorrow at 6pm"
            Check = { param($d) Expect-Card $d 'create_reminder' '18:00' }
        },
        @{
            Name  = 'intent: ping me'
            Say   = 'Ping me tomorrow at 8am to take the bins out'
            Check = { param($d) Expect-Card $d 'create_reminder' '08:00' }
        },
        @{
            Name  = 'intent: on my calendar'
            Say   = 'I have a dentist appointment tomorrow at 4pm, put it on my calendar'
            Check = { param($d) Expect-Card $d 'add_planner_item' '(?i)dent' }
        },
        @{
            Name  = 'intent: unclear asks'
            Say   = 'I need to remember this'
            Check = {
                param($d)
                if ((Proposed-Tools $d).Count -gt 0) { return "proposed $((Proposed-Tools $d) -join ',')" }
                if ((Tools-Used $d) -match '^create_') { return "saved something: $((Tools-Used $d) -join ',')" }
                if ($d.text -notmatch '\?') { return 'did not ask what to save' }
                return ''
            }
        },
        @{
            Name  = 'intent: thanks changes nothing'
            Say   = "Thanks, that's all for now"
            Check = {
                param($d)
                if ((Proposed-Tools $d).Count -gt 0) { return "proposed $((Proposed-Tools $d) -join ',')" }
                if ((Tools-Used $d) -match '^create_') { return "saved something: $((Tools-Used $d) -join ',')" }
                if ($d.text -match $claimsDone) { return 'claimed a change' }
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
                if ($d.text -match "($stores)" -and $d.text -notmatch "(not|n't|never)[^.]{0,90}($stores)") { return 'sends the user to a store' }
                return ''
            }
        }
    )

    # Extended (2026-11-02): what a daily user does that the incidents above never covered. A
    # scenario with Turns is one conversation; its check sees the last turn's 'done' and all of them.
    $negative = '(?i)\b(no|not|none|nothing|never|couldn''t|could not|can''t|cannot|don''t|do not|didn''t|isn''t|aren''t|hasn''t|haven''t|unable)\b'
    $extended = @(
        @{
            Name  = 'edit: mark a task done'
            Say   = "Mark $taskKey as done"
            Check = {
                param($d)
                $proposed = Proposed-Tools $d
                $card = $d.pending | Where-Object { $_.tool -in 'move_task', 'update_task' } | Select-Object -First 1
                if ($null -eq $card) { return "no move_task card (proposed: $($proposed -join ','); ran: $((Tools-Used $d) -join ','))" }
                if ($card.summary -notmatch "$taskKey|balcony") { return "card is for another task: $($card.summary)" }
                if ($card.summary -notmatch '(?i)done') { return "card does not move it to done: $($card.summary)" }
                return ''
            }
        },
        @{
            Name  = 'edit: re-estimate a task'
            Say   = "Change $taskKey to 8 points"
            Check = {
                param($d)
                $err = Expect-Card $d 'update_task' 'points 8'
                if ($err) { return $err }
                if (($d.pending | Where-Object { $_.tool -eq 'update_task' } | Select-Object -First 1).summary -notmatch "$taskKey|balcony") { return 'card is for another task' }
                return ''
            }
        },
        @{
            Name  = 'edit: cancel a reminder'
            Say   = 'Cancel my reminder to call the bank'
            Check = { param($d) Expect-Card $d 'cancel_reminder' '(?i)bank' }
        },
        @{
            Name  = 'reads tomorrow''s calendar'
            Say   = 'What is on my calendar tomorrow?'
            Check = {
                param($d)
                if ((Tools-Used $d) -notcontains 'get_planner') { return "did not read the planner (ran: $((Tools-Used $d) -join ','))" }
                if ((Proposed-Tools $d).Count -gt 0) { return "proposed $((Proposed-Tools $d) -join ',')" }
                if ($d.text -match 'Read a book') { return "gave today's item as tomorrow's" }
                return ''
            }
        },
        @{
            Name  = 'reads reminders'
            Say   = 'What reminders do I have coming up?'
            Check = {
                param($d)
                if ($d.text -notmatch '(?i)bank') { return 'does not mention the bank reminder' }
                if ((Proposed-Tools $d).Count -gt 0) { return "proposed $((Proposed-Tools $d) -join ',')" }
                return ''
            }
        },
        @{
            Name  = 'reads notes'
            Say   = 'What notes have I saved?'
            Check = {
                param($d)
                if ($d.text -notmatch '(?i)stamps') { return "does not mention the saved note (ran: $((Tools-Used $d) -join ','))" }
                if ((Proposed-Tools $d).Count -gt 0) { return "proposed $((Proposed-Tools $d) -join ',')" }
                return ''
            }
        },
        @{
            Name  = 'proposes a goal'
            Say   = 'Set a goal to read 12 books next year'
            Check = { param($d) Expect-Card $d 'create_goal' '(?i)book' }
        },
        @{
            # Owner, 2026-11-03: a goal with no period is asked about, never given a guessed one.
            Name  = 'goal with no period asks'
            Say   = 'Set a goal: learn to bake sourdough'
            Check = {
                param($d)
                if ((Proposed-Tools $d) -contains 'create_goal') { return "guessed a period: $(($d.pending | Select-Object -First 1).summary)" }
                if ($d.text -notmatch '(?i)month|quarter|year') { return "did not ask which period: $($d.text)" }
                return ''
            }
        },
        @{
            Name  = 'remembers a fact'
            Say   = 'I am allergic to peanuts, please remember that'
            Check = {
                param($d)
                $memories = @(Invoke-Api -Method Get -Path '/api/memories' -Token $token | ForEach-Object { $_ })
                $saved = @($memories | Where-Object { $_.content -match '(?i)peanut' })
                foreach ($m in $saved) { Invoke-Api -Method Delete -Path "/api/memories/$($m.id)" -Token $token | Out-Null }
                $kept = (Tools-Used $d) -contains 'create_memory' -or (Tools-Used $d) -contains 'remember_about_user' -or (Proposed-Tools $d) -contains 'create_memory'
                if (-not $kept) { return "kept nothing (ran: $((Tools-Used $d) -join ','); proposed: $((Proposed-Tools $d) -join ','))" }
                return ''
            }
        },
        @{
            Name  = 'reminder in two hours'
            Say   = 'Remind me in 2 hours to stretch'
            Check = {
                param($d)
                $err = Expect-Card $d 'create_reminder' '(?i)stretch'
                if ($err) { return $err }
                $summary = ($d.pending | Where-Object { $_.tool -eq 'create_reminder' } | Select-Object -First 1).summary
                $at = (Get-Date).AddHours(2)
                # The model rounds now as it likes, and the check runs a little after it answered.
                $near = -12..12 | ForEach-Object { $at.AddMinutes($_).ToString('HH:mm') }
                if (-not ($near | Where-Object { $summary -match $_ })) { return "not about $($at.ToString('HH:mm')): $summary" }
                return ''
            }
        },
        @{
            Name  = 'event on a weekday'
            # Three days ahead, never today's weekday: "on Friday" said on a Friday is left to the
            # model on purpose (WhenPhrase: today or next week?), so a Friday run measured the calendar.
            Say   = "Add a meeting with Priya on $inThreeDaysName at 3pm to my calendar"
            Check = {
                param($d)
                $err = Expect-Card $d 'add_planner_item' '(?i)priya'
                if ($err) { return $err }
                $summary = ($d.pending | Where-Object { $_.tool -eq 'add_planner_item' } | Select-Object -First 1).summary
                if ($summary -notmatch $inThreeDays.ToString('yyyy-MM-dd')) { return "not on $inThreeDaysName $($inThreeDays.ToString('yyyy-MM-dd')): $summary" }
                if ($summary -notmatch '15:00') { return "not at 15:00: $summary" }
                return ''
            }
        },
        @{
            Name  = 'reminder on a named day'
            Say   = "Remind me on $inThreeDaysName at 10am to submit the report"
            Check = {
                param($d)
                $err = Expect-Card $d 'create_reminder' '10:00'
                if ($err) { return $err }
                $summary = ($d.pending | Where-Object { $_.tool -eq 'create_reminder' } | Select-Object -First 1).summary
                if ($summary -notmatch $inThreeDays.ToString('yyyy-MM-dd')) { return "not on $($inThreeDays.ToString('yyyy-MM-dd')): $summary" }
                return ''
            }
        },
        @{
            Name  = 'follow-up: reminder time'
            Turns = @('Remind me to pay the rent', 'Tomorrow at 9am')
            Check = {
                param($d, $all)
                if ((Proposed-Tools $all[0]) -contains 'create_reminder') { return "guessed a time on the first turn: $(($all[0].pending | Select-Object -First 1).summary)" }
                $err = Expect-Card $d 'create_reminder' '09:00'
                if ($err) { return $err }
                if (($d.pending | Where-Object { $_.tool -eq 'create_reminder' } | Select-Object -First 1).summary -notmatch $tomorrow.ToString('yyyy-MM-dd')) { return 'card is not for tomorrow' }
                return ''
            }
        },
        @{
            Name  = 'follow-up: task title'
            Turns = @('Add a task to my backlog', 'Book the car service')
            Check = {
                param($d, $all)
                if ((Proposed-Tools $all[0]) -contains 'create_task') { return "made up a task on the first turn: $(($all[0].pending | Select-Object -First 1).summary)" }
                Expect-Card $d 'create_task' '(?i)car service'
            }
        },
        @{
            Name  = 'jots a note'
            Say   = 'Jot down that the wifi password is on the router'
            Check = {
                param($d)
                $notes = @(Invoke-Api -Method Get -Path '/api/notes?search=wifi' -Token $token | ForEach-Object { $_ })
                foreach ($n in $notes) { Invoke-Api -Method Delete -Path "/api/notes/$($n.id)" -Token $token | Out-Null }
                if ($notes.Count -ne 1) { return "saved $($notes.Count) notes (ran: $((Tools-Used $d) -join ','))" }
                return ''
            }
        },
        @{
            Name  = 'grounded: no such note'
            Say   = 'What did I note about the electrician?'
            Check = {
                param($d)
                if ((Proposed-Tools $d).Count -gt 0) { return "proposed $((Proposed-Tools $d) -join ',')" }
                if ($d.text -notmatch $negative) { return 'answers as if there were such a note' }
                return ''
            }
        },
        @{
            Name  = 'grounded: no such appointment'
            Say   = 'When is my haircut appointment?'
            Check = {
                param($d)
                if ((Proposed-Tools $d).Count -gt 0) { return "proposed $((Proposed-Tools $d) -join ',')" }
                if ($d.text -notmatch $negative) { return 'gives an appointment that does not exist' }
                return ''
            }
        },
        @{
            Name  = 'honest: no tool to delete a note'
            Say   = 'Delete my note about the stamps'
            Check = {
                param($d)
                $still = @(Invoke-Api -Method Get -Path '/api/notes?search=stamps' -Token $token | ForEach-Object { $_ })
                if ($still.Count -eq 0) { return 'the note is gone' }
                if ($d.text -match '(?i)\b(deleted|removed)\b' -and $d.text -notmatch "(?i)(not|n't|cannot)[^.]{0,40}\b(delete|remove)") { return 'claims the note was deleted' }
                if ($d.text -match $claimsDone) { return 'claims a change' }
                return ''
            }
        },
        @{
            Name  = 'general knowledge'
            Say   = 'What is the capital of Japan?'
            Check = {
                param($d)
                if ($d.text -notmatch 'Tokyo') { return 'does not say Tokyo' }
                if ((Proposed-Tools $d).Count -gt 0) { return "proposed $((Proposed-Tools $d) -join ',')" }
                return ''
            }
        },
        @{
            Name  = 'creative, changes nothing'
            Say   = 'Write a two-line poem about Monday mornings'
            Check = {
                param($d)
                if ((Proposed-Tools $d).Count -gt 0) { return "proposed $((Proposed-Tools $d) -join ',')" }
                if ((Tools-Used $d) -match '^create_') { return "saved something: $((Tools-Used $d) -join ',')" }
                if ($d.text.Trim().Length -lt 20) { return 'no poem' }
                return ''
            }
        },
        @{
            Name  = 'knows its name'
            Say   = 'What is your name?'
            Check = {
                param($d)
                if ($d.text -notmatch [regex]::Escape($nickname)) { return "does not say $nickname" }
                return ''
            }
        }
    )
    # Holdout (2026-11-02): written before any tuning against it and never tuned against since.
    # Report it beside the others: a fix that lifts core and extended but not this fits the checks,
    # not the users.
    $weekend = & $nextDay ([DayOfWeek]::Saturday)
    $weekendName = & $dayName $weekend
    $holdout = @(
        @{
            Name  = 'holdout: alarm tomorrow morning'
            Say   = 'Set an alarm for 6:30am tomorrow to go running'
            Check = {
                param($d)
                $err = Expect-Card $d 'create_reminder' '06:30'
                if ($err) { return $err }
                if (($d.pending | Where-Object { $_.tool -eq 'create_reminder' } | Select-Object -First 1).summary -notmatch $tomorrow.ToString('yyyy-MM-dd')) { return 'not tomorrow' }
                return ''
            }
        },
        @{
            Name  = 'holdout: remind in 45 minutes'
            Say   = 'Can you remind me about the laundry in 45 minutes?'
            Check = {
                param($d)
                $err = Expect-Card $d 'create_reminder' '(?i)laundry'
                if ($err) { return $err }
                $summary = ($d.pending | Where-Object { $_.tool -eq 'create_reminder' } | Select-Object -First 1).summary
                $at = (Get-Date).AddMinutes(45)
                if (-not (-12..12 | ForEach-Object { $at.AddMinutes($_).ToString('HH:mm') } | Where-Object { $summary -match $_ })) { return "not about $($at.ToString('HH:mm')): $summary" }
                return ''
            }
        },
        @{
            Name  = 'holdout: call on a weekday'
            Say   = "Schedule a call with Anna on $weekendName at 11am"
            Check = {
                param($d)
                $err = Expect-Card $d 'add_planner_item' '(?i)anna'
                if ($err) { return $err }
                $summary = ($d.pending | Where-Object { $_.tool -eq 'add_planner_item' } | Select-Object -First 1).summary
                if ($summary -notmatch $weekend.ToString('yyyy-MM-dd') -or $summary -notmatch '11:00') { return "not $weekendName 11:00: $summary" }
                return ''
            }
        },
        @{
            Name  = 'holdout: bump the points'
            Say   = "Bump $taskKey up to 13 points"
            Check = { param($d) Expect-Card $d 'update_task' 'points 13' }
        },
        @{
            Name  = 'holdout: start a task'
            Say   = "Move $taskKey into progress"
            Check = {
                param($d)
                $card = $d.pending | Where-Object { $_.tool -eq 'move_task' } | Select-Object -First 1
                if ($null -eq $card) { return "no move_task card (proposed: $((Proposed-Tools $d) -join ','))" }
                if ($card.summary -notmatch '(?i)progress') { return "not into progress: $($card.summary)" }
                return ''
            }
        },
        @{
            Name  = 'holdout: plans on a weekday'
            Say   = "Do I have anything planned for $weekendName?"
            Check = {
                param($d)
                if ((Tools-Used $d) -notcontains 'get_planner') { return "did not read the planner (ran: $((Tools-Used $d) -join ','))" }
                if ((Proposed-Tools $d).Count -gt 0) { return "proposed $((Proposed-Tools $d) -join ',')" }
                return ''
            }
        },
        @{
            Name  = 'holdout: nothing written about it'
            Say   = 'Did I write down anything about the plumber?'
            Check = {
                param($d)
                if ((Proposed-Tools $d).Count -gt 0) { return "proposed $((Proposed-Tools $d) -join ',')" }
                if ($d.text -notmatch $negative) { return 'answers as if there were such a note' }
                return ''
            }
        },
        @{
            Name  = 'holdout: forget a reminder'
            Say   = 'Forget my reminder about the bank'
            Check = { param($d) Expect-Card $d 'cancel_reminder' '(?i)bank' }
        },
        @{
            Name  = 'holdout: reminder with no time asks'
            Say   = 'Remind me to buy flowers'
            Check = {
                param($d)
                if ((Proposed-Tools $d) -contains 'create_reminder') { return "guessed a time: $(($d.pending | Select-Object -First 1).summary)" }
                if ($d.text -notmatch '\?') { return 'did not ask when' }
                return ''
            }
        },
        @{
            Name  = 'holdout: task with no title asks'
            Say   = 'Make a task'
            Check = {
                param($d)
                if ((Proposed-Tools $d) -contains 'create_task') { return "made one up: $(($d.pending | Select-Object -First 1).summary)" }
                if ($d.text -notmatch '\?') { return 'did not ask what' }
                return ''
            }
        },
        @{
            Name  = 'holdout: a fact about the world'
            Say   = 'Who wrote Pride and Prejudice?'
            Check = {
                param($d)
                if ($d.text -notmatch 'Austen') { return 'does not say Austen' }
                if ((Proposed-Tools $d).Count -gt 0) { return "proposed $((Proposed-Tools $d) -join ',')" }
                return ''
            }
        },
        @{
            Name  = 'holdout: add to my tasks'
            Say   = "Add 'Call the insurer' to my tasks"
            Check = { param($d) Expect-Card $d 'create_task' '(?i)insurer' }
        },
        @{
            Name  = 'holdout: note a number'
            Say   = 'Note that the gate code is 4417'
            Check = {
                param($d)
                $notes = @(Invoke-Api -Method Get -Path '/api/notes?search=4417' -Token $token | ForEach-Object { $_ })
                foreach ($n in $notes) { Invoke-Api -Method Delete -Path "/api/notes/$($n.id)" -Token $token | Out-Null }
                if ($notes.Count -ne 1) { return "saved $($notes.Count) notes (ran: $((Tools-Used $d) -join ','))" }
                return ''
            }
        }
    )
    # Holdout 2 (2026-11-02): written after the first holdout had been tuned against (the planner
    # meaning, the calendar words), before any run of its own. Same rule: never tune against it.
    $holdout2 = @(
        @{
            Name  = 'holdout2: ping me tonight'
            Say   = 'Ping me at 8:15 tonight to take my pills'
            Check = {
                param($d)
                # After 20:15 the time has passed: a reminder for today must not be proposed.
                if ((Get-Date).TimeOfDay -ge [TimeSpan]'20:15') {
                    $today = ($d.pending | Where-Object { $_.tool -eq 'create_reminder' -and $_.summary -match $now.ToString('yyyy-MM-dd') })
                    if ($today) { return "proposed a time in the past: $($today[0].summary)" }
                    return ''
                }
                $err = Expect-Card $d 'create_reminder' '20:15'
                if ($err) { return $err }
                if (($d.pending | Where-Object { $_.tool -eq 'create_reminder' } | Select-Object -First 1).summary -notmatch $now.ToString('yyyy-MM-dd')) { return 'not today' }
                return ''
            }
        },
        @{
            Name  = 'holdout2: remind in an hour'
            Say   = 'Remind me in an hour to check the oven'
            Check = {
                param($d)
                $err = Expect-Card $d 'create_reminder' '(?i)oven'
                if ($err) { return $err }
                $summary = ($d.pending | Where-Object { $_.tool -eq 'create_reminder' } | Select-Object -First 1).summary
                $at = (Get-Date).AddHours(1)
                if (-not (-12..12 | ForEach-Object { $at.AddMinutes($_).ToString('HH:mm') } | Where-Object { $summary -match $_ })) { return "not about $($at.ToString('HH:mm')): $summary" }
                return ''
            }
        },
        @{
            Name  = 'holdout2: book lunch tomorrow'
            Say   = 'Book lunch with Marco tomorrow at 1pm'
            Check = {
                param($d)
                $err = Expect-Card $d 'add_planner_item' '(?i)marco'
                if ($err) { return $err }
                $summary = ($d.pending | Where-Object { $_.tool -eq 'add_planner_item' } | Select-Object -First 1).summary
                if ($summary -notmatch $tomorrow.ToString('yyyy-MM-dd') -or $summary -notmatch '13:00') { return "not tomorrow 13:00: $summary" }
                return ''
            }
        },
        @{
            Name  = 'holdout2: set the points'
            Say   = "Set $taskKey to 2 points"
            Check = { param($d) Expect-Card $d 'update_task' 'points 2\b' }
        },
        @{
            Name  = 'holdout2: finished a task'
            Say   = "I've finished $taskKey"
            Check = {
                param($d)
                $card = $d.pending | Where-Object { $_.tool -eq 'move_task' } | Select-Object -First 1
                if ($null -eq $card) { return "no move_task card (proposed: $((Proposed-Tools $d) -join ','))" }
                if ($card.summary -notmatch '(?i)done') { return "not to done: $($card.summary)" }
                return ''
            }
        },
        @{
            Name  = 'holdout2: free on a weekday'
            Say   = "Am I free on $(& $dayName (& $nextDay ([DayOfWeek]::Friday))) afternoon?"
            Check = {
                param($d)
                if ((Tools-Used $d) -notcontains 'get_planner') { return "did not read the planner (ran: $((Tools-Used $d) -join ','))" }
                if ((Proposed-Tools $d).Count -gt 0) { return "proposed $((Proposed-Tools $d) -join ',')" }
                return ''
            }
        },
        @{
            Name  = 'holdout2: finds a note'
            Say   = 'Is there a note about the wedding?'
            Check = {
                param($d)
                if ((Proposed-Tools $d).Count -gt 0) { return "proposed $((Proposed-Tools $d) -join ',')" }
                if ($d.text -notmatch '(?i)stamps') { return "does not mention the stamps note: $($d.text)" }
                return ''
            }
        },
        @{
            Name  = 'holdout2: when is a reminder'
            Say   = 'When is my reminder about the bank?'
            Check = {
                param($d)
                if ((Proposed-Tools $d).Count -gt 0) { return "proposed $((Proposed-Tools $d) -join ',')" }
                if ($d.text -notmatch '\b10(:00)?\s*(am|AM|a\.m\.)|\b10:00\b') { return "does not give 10:00: $($d.text)" }
                return ''
            }
        },
        @{
            Name  = 'holdout2: reminder with no time asks'
            Say   = 'Remind me to water the plants'
            Check = {
                param($d)
                if ((Proposed-Tools $d) -contains 'create_reminder') { return "guessed a time: $(($d.pending | Select-Object -First 1).summary)" }
                if ($d.text -notmatch '\?') { return 'did not ask when' }
                return ''
            }
        },
        @{
            Name  = 'holdout2: arithmetic'
            Say   = 'What is 15% of 80?'
            Check = {
                param($d)
                if ($d.text -notmatch '\b12\b') { return "does not say 12: $($d.text)" }
                if ((Proposed-Tools $d).Count -gt 0) { return "proposed $((Proposed-Tools $d) -join ',')" }
                return ''
            }
        },
        @{
            Name  = 'holdout2: put on my board'
            Say   = "Put 'Renew passport' on my board"
            Check = { param($d) Expect-Card $d 'create_task' '(?i)passport' }
        },
        @{
            Name  = 'holdout2: save a note'
            Say   = 'Save a note: the spare key is under the blue pot'
            Check = {
                param($d)
                $notes = @(Invoke-Api -Method Get -Path '/api/notes?search=spare' -Token $token | ForEach-Object { $_ })
                foreach ($n in $notes) { Invoke-Api -Method Delete -Path "/api/notes/$($n.id)" -Token $token | Out-Null }
                if ($notes.Count -ne 1) { return "saved $($notes.Count) notes (ran: $((Tools-Used $d) -join ','))" }
                return ''
            }
        },
        @{
            Name  = 'holdout2: follow-up gives the time'
            Turns = @('Remind me to call mum', 'Tomorrow at 7pm')
            Check = {
                param($d, $all)
                if ((Proposed-Tools $all[0]) -contains 'create_reminder') { return "guessed a time on the first turn: $(($all[0].pending | Select-Object -First 1).summary)" }
                $err = Expect-Card $d 'create_reminder' '19:00'
                if ($err) { return $err }
                if (($d.pending | Where-Object { $_.tool -eq 'create_reminder' } | Select-Object -First 1).summary -notmatch $tomorrow.ToString('yyyy-MM-dd')) { return 'card is not for tomorrow' }
                return ''
            }
        }
    )
    # Holdout 3 (2026-11-02): written after holdout2 had been tuned against, before any run of its
    # own. It leans on what was still weak (sums, an event stated and then "add it to my calendar",
    # a thank-you that saves something) in words the suite has not used. Never tune against it.
    $weekend = & $nextDay ([DayOfWeek]::Saturday)
    $weekendName = & $dayName $weekend
    $midweek = & $nextDay ([DayOfWeek]::Thursday)
    $midweekName = & $dayName $midweek
    $holdout3 = @(
        @{
            Name  = 'holdout3: alert on a weekday'
            Say   = "Alert me at 6:30am on $weekendName to go for a run"
            Check = {
                param($d)
                $err = Expect-Card $d 'create_reminder' '06:30'
                if ($err) { return $err }
                $summary = ($d.pending | Where-Object { $_.tool -eq 'create_reminder' } | Select-Object -First 1).summary
                if ($summary -notmatch $weekend.ToString('yyyy-MM-dd')) { return "not $weekendName $($weekend.ToString('yyyy-MM-dd')): $summary" }
                return ''
            }
        },
        @{
            Name  = 'holdout3: nudge in 30 minutes'
            Say   = 'In 30 minutes, nudge me to move the laundry'
            Check = {
                param($d)
                $err = Expect-Card $d 'create_reminder' '(?i)laundry'
                if ($err) { return $err }
                $summary = ($d.pending | Where-Object { $_.tool -eq 'create_reminder' } | Select-Object -First 1).summary
                $at = (Get-Date).AddMinutes(30)
                if (-not (-12..12 | ForEach-Object { $at.AddMinutes($_).ToString('HH:mm') } | Where-Object { $summary -match $_ })) { return "not about $($at.ToString('HH:mm')): $summary" }
                return ''
            }
        },
        @{
            Name  = 'holdout3: event, then add it'
            Say   = "Haircut on $midweekName at 11am - add it to my calendar"
            Check = {
                param($d)
                $err = Expect-Card $d 'add_planner_item' '(?i)hair'
                if ($err) { return $err }
                $summary = ($d.pending | Where-Object { $_.tool -eq 'add_planner_item' } | Select-Object -First 1).summary
                if ($summary -notmatch $midweek.ToString('yyyy-MM-dd') -or $summary -notmatch '11:00') { return "not $midweekName 11:00: $summary" }
                return ''
            }
        },
        @{
            Name  = 'holdout3: schedule a call'
            Say   = 'Schedule a call with the plumber the day after tomorrow at 9am'
            Check = {
                param($d)
                $err = Expect-Card $d 'add_planner_item' '(?i)plumber'
                if ($err) { return $err }
                $summary = ($d.pending | Where-Object { $_.tool -eq 'add_planner_item' } | Select-Object -First 1).summary
                if ($summary -notmatch $now.Date.AddDays(2).ToString('yyyy-MM-dd') -or $summary -notmatch '09:00') { return "not in two days at 09:00: $summary" }
                return ''
            }
        },
        @{
            Name  = 'holdout3: a percentage'
            Say   = 'How much is 20% of 250?'
            Check = {
                param($d)
                if ($d.text -notmatch '\b50\b') { return "does not say 50: $($d.text)" }
                if ((Proposed-Tools $d).Count -gt 0) { return "proposed $((Proposed-Tools $d) -join ',')" }
                return ''
            }
        },
        @{
            Name  = 'holdout3: a product'
            Say   = "What's 23 times 4?"
            Check = {
                param($d)
                if ($d.text -notmatch '\b92\b') { return "does not say 92: $($d.text)" }
                if ((Proposed-Tools $d).Count -gt 0) { return "proposed $((Proposed-Tools $d) -join ',')" }
                return ''
            }
        },
        @{
            Name  = 'holdout3: thank you'
            Say   = 'Cool, thank you!'
            Check = {
                param($d)
                if ((Proposed-Tools $d).Count -gt 0) { return "proposed $((Proposed-Tools $d) -join ',')" }
                if ((Tools-Used $d) -match '^(create_|remember)') { return "saved something: $((Tools-Used $d) -join ',')" }
                if ($d.text -match $claimsDone) { return 'claimed a change' }
                return ''
            }
        },
        @{
            Name  = 'holdout3: a yearly goal'
            Say   = 'I want a goal for next year: run a half marathon'
            Check = { param($d) Expect-Card $d 'create_goal' '(?i)marathon' }
        },
        @{
            Name  = 'holdout3: rename a task'
            Say   = "Rename $taskKey to Water the herbs"
            Check = { param($d) Expect-Card $d 'update_task' '(?i)herbs' }
        },
        @{
            Name  = 'holdout3: lower the priority'
            Say   = "Make $taskKey low priority"
            Check = { param($d) Expect-Card $d 'update_task' '(?i)\blow\b' }
        },
        @{
            Name  = 'holdout3: anything tomorrow'
            Say   = 'Anything planned for tomorrow?'
            Check = {
                param($d)
                if ((Tools-Used $d) -notcontains 'get_planner') { return "did not read the planner (ran: $((Tools-Used $d) -join ','))" }
                if ((Proposed-Tools $d).Count -gt 0) { return "proposed $((Proposed-Tools $d) -join ',')" }
                if ($d.text -match 'Read a book') { return "gave today's item as tomorrow's" }
                return ''
            }
        },
        @{
            Name  = 'holdout3: any reminders'
            Say   = 'Do I have any reminders set?'
            Check = {
                param($d)
                if ($d.text -notmatch '(?i)bank') { return 'does not mention the bank reminder' }
                if ((Proposed-Tools $d).Count -gt 0) { return "proposed $((Proposed-Tools $d) -join ',')" }
                return ''
            }
        },
        @{
            Name  = 'holdout3: a preference'
            Say   = 'Remember that I prefer tea over coffee'
            Check = {
                param($d)
                $memories = @(Invoke-Api -Method Get -Path '/api/memories' -Token $token | ForEach-Object { $_ })
                $saved = @($memories | Where-Object { $_.content -match '(?i)\btea\b' })
                foreach ($m in $saved) { Invoke-Api -Method Delete -Path "/api/memories/$($m.id)" -Token $token | Out-Null }
                $kept = (Tools-Used $d) -contains 'create_memory' -or (Tools-Used $d) -contains 'remember_about_user' -or (Proposed-Tools $d) -contains 'create_memory'
                if (-not $kept) { return "kept nothing (ran: $((Tools-Used $d) -join ','); proposed: $((Proposed-Tools $d) -join ','))" }
                return ''
            }
        },
        @{
            Name  = 'holdout3: jot down'
            Say   = 'Jot down that the wifi password is on the router'
            Check = {
                param($d)
                $notes = @(Invoke-Api -Method Get -Path '/api/notes?search=router' -Token $token | ForEach-Object { $_ })
                foreach ($n in $notes) { Invoke-Api -Method Delete -Path "/api/notes/$($n.id)" -Token $token | Out-Null }
                if ($notes.Count -ne 1) { return "saved $($notes.Count) notes (ran: $((Tools-Used $d) -join ','))" }
                return ''
            }
        },
        @{
            Name  = 'holdout3: follow-up names a day'
            Turns = @('Set a reminder to pay the rent', "$weekendName at 10am")
            Check = {
                param($d, $all)
                if ((Proposed-Tools $all[0]) -contains 'create_reminder') { return "guessed a time on the first turn: $(($all[0].pending | Select-Object -First 1).summary)" }
                $err = Expect-Card $d 'create_reminder' '10:00'
                if ($err) { return $err }
                if (($d.pending | Where-Object { $_.tool -eq 'create_reminder' } | Select-Object -First 1).summary -notmatch $weekend.ToString('yyyy-MM-dd')) { return "card is not for $weekendName" }
                return ''
            }
        }
    )
    # Questions about the seeded day that need a fresh read of it (suite 'planner').
    $readPlanner = {
        param($d)
        if ((Tools-Used $d) -notcontains 'get_planner') { return "did not read the planner (ran: $((Tools-Used $d) -join ','))" }
        if ((Proposed-Tools $d).Count -gt 0) { return "proposed $((Proposed-Tools $d) -join ',')" }
        return ''
    }
    $planner = @(
        @{
            Name  = 'planner: inside a block'
            Say   = "What's in my day at 7:20pm?"
            Check = {
                param($d)
                $err = & $readPlanner $d; if ($err) { return $err }
                if ($d.text -notmatch '(?i)gym') { return 'does not name the gym session' }
                return ''
            }
        },
        @{
            Name  = 'planner: where two blocks meet'
            Say   = "What's on at 9 in the morning?"
            Check = {
                param($d)
                $err = & $readPlanner $d; if ($err) { return $err }
                if ($d.text -notmatch '(?i)deep work') { return 'does not name Deep work, which starts then' }
                return ''
            }
        },
        @{
            Name  = 'planner: read it again'
            Turns = @("What's on my schedule today?", 'Read it again and tell me what is at 1pm')
            Check = {
                param($d)
                $err = & $readPlanner $d; if ($err) { return $err }
                if ($d.text -notmatch '(?i)client calls') { return 'does not name Client calls' }
                return ''
            }
        },
        @{
            Name  = 'planner: how many'
            Say   = "How many walks are on today's plan?"
            Check = {
                param($d)
                $err = & $readPlanner $d; if ($err) { return $err }
                if ($d.text -notmatch '(?i)\b(2|two)\b') { return 'does not count two walks' }
                return ''
            }
        },
        @{
            Name  = 'planner: a stretch of the day'
            Say   = 'What happens from 3 to 5pm today?'
            Check = {
                param($d)
                $err = & $readPlanner $d; if ($err) { return $err }
                if ($d.text -notmatch '(?i)admin') { return 'does not name Admin and email' }
                if ($d.text -match '(?i)gym|lunch') { return 'names an item outside 15:00 to 17:00' }
                return ''
            }
        },
        @{
            Name  = 'planner: a critique'
            Say   = 'Be critical of my schedule today'
            Check = {
                param($d)
                $err = & $readPlanner $d; if ($err) { return $err }
                if ($d.text -match $claimsDone) { return 'claimed a change' }
                return ''
            }
        },
        @{
            Name  = 'planner: what changed'
            Turns = @("Be critical of today's plan", 'Did you edit anything in my planner just now?')
            Check = {
                param($d)
                if ((Proposed-Tools $d).Count -gt 0) { return "proposed $((Proposed-Tools $d) -join ',')" }
                if ($d.text -match $claimsDone) { return 'claimed a change' }
                if ($d.text -notmatch "(?i)\b(haven't|have not|didn't|did not|nothing|no changes?|not changed)\b") { return 'does not say nothing was changed' }
                return ''
            }
        }
    )
    $scenarios = switch ($Suite) {
        'core' { $scenarios }
        'extended' { $extended }
        'holdout' { $holdout }
        'holdout2' { $holdout2 }
        'holdout3' { $holdout3 }
        'planner' { $planner }
        default { $scenarios + $extended }
    }
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
            $said = if ($scenario.Turns) { $scenario.Turns } else { @($scenario.Say) }
            $all = @()
            try {
                $conversation = $null
                foreach ($message in $said) {
                    $done = Send-Chat -Message $message -Token $token -ConversationId $conversation
                    $conversation = $done.conversationId
                    $all += $done
                }
                $failure = & $scenario.Check $done $all
            }
            catch {
                $failure = $_.Exception.Message
                $done = $null
            }
            $seconds += ((Get-Date) - $started).TotalSeconds

            # Leave nothing behind: every card this run created goes away.
            foreach ($turnDone in $all) {
                if ($null -eq $turnDone.pending) { continue }
                foreach ($card in $turnDone.pending) {
                    try { Invoke-Api -Method Post -Path "/api/chat/actions/$($card.id)/discard" -Token $token | Out-Null } catch {}
                }
            }
            foreach ($n in @(Invoke-Api -Method Get -Path '/api/notes' -Token $token | ForEach-Object { $_ })) {
                if ($keepNotes -notcontains $n.id) { try { Invoke-Api -Method Delete -Path "/api/notes/$($n.id)" -Token $token | Out-Null } catch {} }
            }
            foreach ($m in @(Invoke-Api -Method Get -Path '/api/memories' -Token $token | ForEach-Object { $_ })) {
                if ($keepMemories -notcontains $m.id) { try { Invoke-Api -Method Delete -Path "/api/memories/$($m.id)" -Token $token | Out-Null } catch {} }
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
    foreach ($id in $script:conversations) {
        try { Invoke-Api -Method Delete -Path "/api/chat/conversations/$id" -Token $token | Out-Null } catch {}
    }
    if ($null -ne $seededTask) {
        try { Invoke-Api -Method Delete -Path "/api/board/tasks/$($seededTask.key)" -Token $token | Out-Null } catch {}
    }
    if ($null -ne $seededReminder) {
        try { Invoke-Api -Method Delete -Path "/api/reminders/$($seededReminder.id)" -Token $token | Out-Null } catch {}
    }
    if ($null -ne $seededNote) {
        try { Invoke-Api -Method Delete -Path "/api/notes/$($seededNote.id)" -Token $token | Out-Null } catch {}
    }
    foreach ($id in $seededRoutine) {
        try { Invoke-Api -Method Delete -Path "/api/planner/items/$id" -Token $token | Out-Null } catch {}
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
