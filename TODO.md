# PersonaOS — Task Board

> Shared across all Claude Code sessions. **Claim a task before starting it** (put your
> session name/date in the `claimed` column), tick it when done, and add newly discovered
> work to the right phase. Keep this file truthful — it is the single source of progress.
> Full product spec: [docs/PRD.md](docs/PRD.md).

## How to work in parallel sessions

- **Partition by area** — the safest split is one session per column: `backend` (.NET),
  `mobile` (Flutter), `web` (Angular), `infra` (Docker/CI). Cross-area tasks go solo.
- **Claim before you code**: edit this file first (`[ ]` → `[~] (claimed: <session>, <date>)`).
- Prefer `git worktree` / separate branches when two sessions touch the same project.
- After finishing: mark `[x]`, note anything a follow-up session must know.

Legend: `[ ]` open · `[~]` in progress (claimed) · `[x]` done · `[-]` dropped

---

## AI layer refactor (raised by the owner 2026-09-21)

Six guardrails landed this week as incident responses — each a real bug, each the smallest fix
that worked, none followed by a refactor. The result: the prompt is 257 lines of `StringBuilder`
calls that need a rebuild and a redeploy to change one sentence, and `ChatService` is 545 lines
holding streaming, the tool loop, the confirmation gate, pre-proposal validation, call dedup, the
claim check, the corrective round and persistence. Adding a guardrail means editing that method
again. The layer boundaries are fine; the shape inside this layer is not.

Do these in order — A is the one the owner feels immediately, and B is easier once the prompt
stops being code. Estimates assume one focused session each, tests kept green throughout.

- [x] **A. Prompt lives in files, not in C#** (2026-09-23). Nine `.prompty` fragments
      (`identity`, `tool-rules`, `product`, `modules`, `clock`, `goals`, `board`, `planner`,
      `reminders`) under `Application/Ai/Prompts/Fragments`, embedded in the assembly. Front
      matter says what each is for; the body is a Mustache subset (`{{var}}`, `{{#list}}`,
      `{{^empty}}`, `{{! why }}`) rendered by `PromptTemplate`, about 200 lines with no new
      dependency. `SystemPromptBuilder` now only gathers values and picks fragments; user-written
      text (persona, about me) is appended raw, never rendered, so braces in it cannot inject tags.
      Overrides: a file of the same name in `Prompts:OverridePath` (compose:
      `/app/data/prompts`) replaces a default and is read per request, so edits apply without a
      restart; a broken override logs a warning and falls back to the default.
      `FilePromptOverrideSource` refuses any name that is not a plain `*.prompty`. Verified the
      new output byte-for-byte against the old builder (with and without data, thinking model
      and not) before deleting the comparison. Also: `InstanceConfig.IsEnabled` replaces six
      copies of `Features.TryGetValue(...) && on`. README: "Editing the assistant's
      instructions". 353 backend tests.
- [ ] **B. Guardrails become a pipeline** (~5-7 hours, the riskiest of the three). Two ports:
      `IToolCallGuard`, run before a call executes or becomes a card, and `IReplyGuard`, run on
      the finished reply. Move the existing six into it — dedup and `ValidateAsync` and the
      mutating-tool gate into the first; `LeakedToolCallScrubber`, `ActionClaimDetector`,
      `CorrectionPreamble` and the empty-reply fallback into the second — each as one class with
      one test and one log counter, so it is visible how often each fires. `ChatService` keeps
      orchestration only and should land near half its current size. The 32 tool-loop tests are
      the safety net; do not change their assertions while refactoring.
- [ ] **C. Model profiles** (~3 hours). Per-model settings instead of treating a 270M model and a
      72B model alike: whether it supports tools, its tool-iteration budget, and which prompt
      variant it gets (a small model needs a short, blunt prompt; a large one can take the full
      thing). Fill the profile from evidence with a `scripts/model-check.sh <model>` that points
      the instance at a model, runs scripted turns against `/api/chat` — tools supported at all,
      "tasks for today" answered in one call, keys used instead of list positions, a card refused
      for a target that does not exist, no tool syntax in prose — prints pass/fail per scenario
      and restores the previous model. Depends on A for the prompt variants.

## Sprint board (requested 2026-09-15; spec: [docs/sprint-board.md](docs/sprint-board.md))

The owner put bug fixing on hold for this feature. Cross-area, built solo.

- [x] Backend (2026-09-15). Goals no longer nest and get `GOAL-n` keys; `BoardTask` (`TASK-n`)
      and `Sprint` entities; migration `SprintBoard` turns sub-goals at any depth into tasks
      under their top-level goal (checked against a copy of the live DB first) and switches the
      `board` module on. `BoardService` owns the rules: lowest-free key numbers, Fibonacci
      points, scope changes refused without acknowledgement (`scope_change_unacknowledged`),
      and the weekly cycle (`RunCycleAsync`, ticked every minute by `SprintScheduleService`):
      close Sunday 18:00 with carry-over, 19:00 push nudge, auto-start 20:00, catch-up after
      downtime. The first week of a new board has no committed number, so filling it is not a
      scope change. Tools: `get_board`, `get_sprint_report`, `create_task`, `update_task`,
      `move_task`, `delete_task`, `delete_goal`; goal tools take keys (`link_goal` removed). The
      system prompt carries the sprint and planning guidance; the morning brief lists the
      week's open tasks. Planner items can link a task (`TaskId`). 228 tests.
- [x] Web (2026-09-15): `/board` with native HTML5 drag and drop plus a "Move…" menu,
      Current/Next, Start sprint, sprint report table, task dialog; goals page lists tasks with
      keys and points; planner "From the board…" picker and goal chips. **Not looked at in a
      browser** (signing in there means typing the password); build and API checked live.
- [x] Mobile (2026-09-15): `BoardScreen` — swipeable columns, long-press drag onto a drop bar,
      task sheet with point chips and Move to, sprint report; goals screen without sub-goals;
      planner picks from the board. Widget tests for drag, scope confirmation and the sheet
      (mutation-checked); layout checked from rendered screenshots, light and dark.
- [x] Follow-up (2026-09-16, owner: "I deleted task 1 from the board but not deleted"). A task
      that is done but in no sprint — how a completed sub-goal arrives from the migration —
      was labelled "Backlog" under its goal while never appearing on the board, so it could
      not be deleted anywhere. `BoardColumns.Of` now reads such a task as Done, and the goals
      page (web and mobile) can reopen or delete a task. Regression test
      `Work_finished_outside_a_sprint_reads_as_done_not_as_backlog` (mutation-checked).
      **Uncommitted, but deployed to the owner's server**; the phone needs a new build.
- [x] **Board changes round 2** (2026-09-16, owner's 13-point list). Sprints are now manual like
      Jira — create / start / complete / delete, with keys `SPRINT-n`, an optional name and
      editable start and end dates; nothing runs on a timer any more, only the Sunday 19:00 nudge
      (`RunRemindersAsync`, recorded as the `sprint_planning` proactive job). The board shows the
      running sprint's three columns only; the backlog moved to its own Jira-style page
      (`GET /api/board/plan`) with sprint sections, inline status / priority / points, drag and
      drop between sprints and a "Move…" menu. Tasks and goals gained **priority**, description,
      comments and attachments (`WorkItemComment` / `WorkItemAttachment`, stored beside documents,
      25 MB cap, `/api/items/{type}/{key}/…`). "Story points" are now **value points**. Every item
      has a URL: `/board/tasks/TASK-1`, `/board/sprints/SPRINT-1` (with a points-remaining chart
      and the work by column), `/board/goals/GOAL-1`. Tools: `get_plan`, `create_sprint`,
      `start_sprint`, `complete_sprint`, `add_comment`; `move_task` and `create_task` take sprint
      keys. Migrations `BoardPlanning` + `NormalizePriority` (the first shipped an empty priority
      default, caught on the live server and repaired). 238 backend tests, 61 mobile tests.
      The phone keeps the board working (sprint picker on a task); its own detail views, comments
      and attachments are the next round. Released as alpha.8 and deployed to the owner's server.
      The web pages were checked in a browser afterwards — see the entry below for what that found.
- [x] **Backlog is a view of the board, not a section** (2026-09-16, owner). The top navigation
      has one **Board** entry again; a `Sprint | Backlog` switch inside the page moves between
      them, and the backlog lives at `/board/backlog` (`/backlog` redirects). Found and fixed
      while finally looking at these pages in a browser: every `<select>` whose options come from
      `@for` showed its first option instead of the task's real value, so the backlog rows and the
      task page claimed "Highest / not estimated / no sprint" — they bind with `ngModel` now;
      comments and attachments never loaded on first paint (the parent called `load()` while the
      child was still hidden) — `Discussion` loads itself; every new comment was flagged *edited*
      because `CreatedAtUtc` and `UpdatedAtUtc` each ran their own `UtcNow`; and the app had no
      body background, so a browser in dark mode painted dark text on a dark canvas.
      239 backend tests. Committed as 1340b19 and deployed.
- [x] **`get_planner` refused a call with no date** (2026-09-21, seen live). "What are my tasks
      for today?" produced five `Provide 'date', or both 'from' and 'to'.` failures and then a
      date from 2023. An empty call now reads the user's local today; half a range is still
      refused, because "from" alone has no sensible reading.
- [x] **A model repeating one tool burned the turn and wrote nothing** (2026-09-21, seen live).
      After the `get_planner` fix above, the same question called it eight times with the right
      date and left the user an empty bubble. Repeat calls are now served from the first result
      with a note saying so, the loop stops after two repeats, and a turn that ran tools but
      produced no text gets a reply saying so instead of an empty bubble.
- [x] **Tool arguments arrived wrapped in an envelope** (2026-09-21, qwen3:4b on the owner's
      instance). The model sent
      `{"function":"add_planner_item","arguments":{"date":"2026-09-21","title":"Check tasks"}}`
      where the tool expects the arguments bare, so every field read as missing: the confirm card
      said only "Add planner item", and confirming it would have failed with "'date' is
      required." `ToolCallInput.Normalize` unwraps `arguments` / `parameters` / `args` / `input`
      (object or JSON string) where the rest of the object is only envelope keys, applied where
      tool calls enter the chat loop so the registry, the card, its summary and the repeat check
      all see the same fields. `add_planner_item` also validates its date before proposing.
- [ ] Qwen3 emits thinking. The instance ran it with a 4096-token context, which truncates our
      prompt (turns log 8k-16k input tokens), and the chat sat on "Using get_planner…" with no
      content ever arriving. Worked around by hand with a `num_ctx 16384` Modelfile and
      `/no_think` in the persona field. Three gaps behind it, none fixed: nothing times out or
      says "still working" when a model streams nothing after a tool result; `<think>` blocks are
      never stripped, so reasoning shown as content would reach the bubble; and nothing warns
      when the model's context is smaller than the prompt being sent.
- [x] **False "nothing was saved" warning on a reply that read the board back** (2026-09-21).
      "All tasks are either in progress or have been scheduled for future planned sprints" is a
      description of existing state, but the passive rule read "have been scheduled" plus "tasks"
      as a claim. The passive rule — always the weakest signal — now loses to reporting wording:
      "as of", "currently", "already", "there are", "you have", "all/both/each/none of the X
      are". The first-person rule is unchanged.
- [x] **Scored the models instead of guessing** (2026-09-21). `scripts/model-check.ps1` runs eight
      scenarios against a running instance — read today's planner, read goals, read the board,
      refuse `GOAL-99`, propose a real deletion, propose a planner item with title and date,
      propose a reminder at the right time, and no false claim warning on an honest read — each
      in its own conversation, checking what the tools did rather than what the reply says. It
      discards every card it creates and restores the model it found. Results on the owner's box:

      | Model | Score | Per turn |
      |---|---|---|
      | `qwen2.5:0.5b` | 3/8 | under 3 s |
      | `qwen2.5:3b-instruct` | **8/8** | 0.6-7.7 s |
      | `qwen3:4b` | 7/8 | 8-76 s |

      So `qwen2.5:3b-instruct` is the floor that works, and 0.5B is not usable: it answered "what
      are my tasks today" from `get_goals`, replied "FOREVER" to another question, and proposed
      nothing where a card was wanted. qwen3:4b passes but is five to ten times slower and put
      21:00 in a reminder whose own text said 7 PM.

      Three app bugs the run turned up, all fixed: `get_planner` and `get_goals` both sounded like
      "tasks", so the descriptions now say which owns "what are my tasks today"; a reminder card
      could be proposed with no time at all, and `create_reminder` now validates `dueAtLocal`
      before proposing; and the gate's own message ("Tell them what you are proposing…") was
      phrased as instructions, which a 0.5B model repeated to the user word for word — it is a
      status object now.
- [ ] **`/no_think` does not reach Qwen3 through Ollama's OpenAI-compatible endpoint.** Measured
      both ways — in the system prompt and appended to the user's turn — and neither changed the
      timings. The switch stays in the prompt because a provider that reads it costs nothing, but
      the real fix is the native Ollama adapter in item C: `/api/chat` takes `think`, `num_ctx`
      and `keep_alive` as parameters, which would also settle the context-length problem below.
- [ ] **History is a flat window of 20 messages, whatever the model.** A 0.5B model answered a
      fresh thread correctly and produced nonsense in a long one — twenty turns of a confused
      conversation crowd out the prompt's rules on a small context. Budget the history by tokens
      instead: keep the system prompt whole, drop the oldest turns until the request fits a share
      of the model's context, and leave out replies that were only a fallback or only reasoning,
      which teach the next turn nothing. Roughly 1-2 hours.
- [ ] **"I've created a goal called Learn Rust for you" — said above an unconfirmed card**
      (2026-09-22, qwen2.5:3b-instruct). The claim check is skipped whenever the turn proposed
      something, because "I've proposed a reminder" is honest. A past-tense claim is not, and the
      user is told the thing exists while the card still waits. Worth checking done-tense claims
      even when a proposal exists, minus the proposing verbs (propose, prepare, draft, set up a
      card); costs one corrective round when it fires.
- [ ] **A reply about the user's data when no read tool ran in that turn.** Seen with a 0.5B:
      "check again" and "anything in the todo" were answered from its own earlier message, and
      one turn announced TASK-6 as completed with no tool call and no card. Nothing was written,
      so the confirmation gate held, but the user was told something untrue. Consider a quiet
      "not checked against your data" note, the way an unverified claim is marked. Judged noisy
      and imperfect when raised; recorded rather than built.
- [ ] The assistant tells the user to reply "yes" or "no" to a confirm card, though the prompt
      says the card has buttons. Small models ignore the rule. Consider rewriting that sentence
      out of the reply the way the correction preamble is stripped.
- [x] **Goals have a start and an end date** (2026-09-22, owner's rules). A goal starts on any
      day the user picks, future ones included, and its type bounds the end: a month runs at most
      31 days, a quarter at most 90, a year always ends on 31 December of its start year — both
      ends counted. With no end given it defaults to a month on less a day, 89 days on, or 31
      December. This replaces snapping every start to the 1st of its period, which left "Complete
      LLM Engineering Course by Oct 15th" filed as the whole of October with the real date in the
      title and nothing knowing when a goal was due. `GoalPeriodCalculator` holds the rules;
      `GoalService` enforces them on create and update for every caller, and refuses a missing
      start (it used to store 0001-01-01). `create_goal` takes `periodEnd`, checks the dates before
      the card is shown, and the card names the end. Migration `GoalPeriodEnd` backfills existing
      goals from their type. Web form has labelled Starts / Ends fields that reset the end when
      the type or start changes, lock it for a year, and explain a bad range before saving; goals
      show their range and an overdue badge. Phone: the same, with date pickers in the new-goal
      sheet. Both mirror the rules for instant feedback; the server stays the authority.
      307 backend tests, 74 mobile.
- [ ] Open questions for later: planner items still use ids in chat tools (same position
      risk as goals had); scope-change warning is not shown for re-estimating mid-sprint by
      design; web drag and drop does not work on touch browsers (the menu does).

## Phone testing issues (alpha.6, reported by the owner 2026-09-15)

Noted as reported, not yet investigated.

- [ ] **Reminder alarm rang a few minutes late.** A reminder created on the phone synced to the
      web correctly, but the alarm did not go off at the scheduled time; it came a few minutes
      later. Alpha.5 moved reminders to exact alarms set on the phone the moment the reminder is
      created, which should ring on time. So either the exact alarm was never set (and the
      server's due-time push arrived late instead), or Android deferred it.
      Follow-up from the owner: **the same happens for reminders created on the server side**
      (web or chat). The **first** reminder was late; after that late one rang, the following
      reminders were all on time. That pattern points at something that only starts working
      after the first alarm or push arrives: for example the exact-alarm schedule not being
      set until the app wakes (the `reminder_scheduled` push delivered late while the app is
      idle), a missing exact-alarm permission prompt, or the notification channel or timezone
      data only being set up on first delivery.
- [x] **Sub-goal form should not ask for a period.** Settled by the sprint board: goals stopped
      nesting, sub-goals became tasks, and the child period picker went with them (web and phone).
      Original report below.
      On the phone, the "New sub-goal" sheet under a
      parent goal shows the Yearly / Quarterly / Monthly selector (it defaulted to Monthly under a
      yearly goal). The owner wants a sub-goal to **inherit the parent goal's timeline**, with no
      period choice at the child level: just the title and "Add goal". Check whether the web
      goals page has the same selector for sub-goals, and whether the server should enforce the
      inherited period (including for sub-goals the assistant creates in chat), not only the form.
- [x] **Confirming an update or delete fails, and hands-free voice stalls afterwards.**
      Fixed 2026-09-20, in four parts. (1) Tools now carry a `ValidateAsync` the registry runs
      *before* a mutating call becomes a card: a call naming something that does not exist is
      refused, the model is handed the reason as an error result while it can still correct
      itself, and the user never sees a card that cannot work. Implemented for the goal, board,
      sprint, work-item, planner and reminder tools. (2) `ToolReceiptBuilder` unwraps the
      registry's `{"error":"…"}`, so a failed card reads "Goal 5 does not exist." instead of raw
      JSON — web and phone both, since it is stored server-side. (3) The registry caught only
      `GoalValidationException`, so a board, planner or reminder problem reached the model as
      "The tool failed unexpectedly."; it catches `DomainValidationException` now. (4) The system
      prompt explains the card — Confirm and Discard buttons, never ask the user to say
      "confirm" — and the phone remembers that a card paused hands-free, resuming the loop when
      the last card is resolved (three widget tests; `VoiceService` is injectable for them).
      Checked live against the dev instance on qwen2.5: `Delete GOAL-99` produced no card and
      logged "Refused to propose delete_goal", `Delete GOAL-2` still produced one.
      Original report below.
      The owner
      tried to delete and update goals from the phone chat; tapping Confirm did not make the change.
      What the screenshot shows: the goals list numbered 1–5 (Create Tutorial was no. 4, Master AI
      no. 5). The user said "delete", then "create tutorial", then "yes". The proposal card
      resolved as failed with the **raw JSON** `✕ {"error":"Goal 5 does not exist."}`. So the
      model most likely passed a **list position instead of the real goal id** (and not even the
      right position).
      **Second example confirms it (progress update):** `get_goals` listed 1. Get Masters,
      2. Learn Rust, 3. Read Books Daily, 4. Master AI. "set progress for read books to 20%"
      produced a card that failed with `✕ {"error":"Goal 3 does not exist."}` — 3 is exactly
      Read Books Daily's position in the list. So the model uses the numbered position as the
      goal id. Things to fix and check:
      - The model needs real ids to act on. Check that `get_goals` returns ids and that the
        prompt or tool description tells it to use them, or let update/delete tools accept a
        title and resolve it.
      - A proposal whose target does not exist should be rejected when it is proposed, not
        after the user confirms it.
      - Show a readable failure instead of raw JSON on the card (mobile, and check web).
      - The same reply told the user to *say* "confirm" rather than tap the button. The model
        does not know how the confirmation card works; the prompt should say so.
      - Hands-free: the snackbar said "Hands-free paused — confirm the change first", and after
        the failed confirm the voice conversation never resumed. Resolving a card (confirmed,
        failed or discarded) should resume listening, or at least show how to resume.
- [x] **"Nothing was saved" warning fires on an honest reply, and the correction leaks into the
      reply.** Fixed 2026-09-20. `ActionClaimDetector` now separates "I've set" (done) from
      "I'll set" (intended): an intention counts only when the reply asks the user for nothing.
      "Sure, I'll add a new goal … Would you like to proceed with these details?" is a proposal
      and passes; "I'll create a sub-goal for that." on its own is still caught, which is why
      "I'll" is in there at all. Anything claimed as already done is flagged regardless of what
      else the reply asks. The corrective round's instruction now says to write only what the
      user should read and not to introduce the reply, and `CorrectionPreamble` strips a leftover
      "Sure, here is the corrected message:" lead-in. What the first draft said was not recovered
      from the logs — the correction round is only logged as a warning with the claim sentence.
      Original report below.
      reply.** Two problems with the claim check shipped in alpha.6, both seen on the phone:
      - **False positive.** "please add a goal to create a tutorial" got *"Sure, I'll add a new
        goal to create a tutorial. … Here's the goal: … Would you like to proceed with these
        details?"*. That is a request for permission, not a claim, but it was flagged with the
        amber note. `ActionClaimDetector` counts "I'll/I will + verb" as a claim. A future-tense
        sentence in a reply that ends by asking to proceed should not count. Decide whether "I'll"
        belongs in the detector at all (it was included because models say "I'll create…" and
        then never call the tool), and add this reply as a negative test.
      - **Correction leaks.** Another reply began *"Sure, here is the corrected message:"* before
        "Would you like to delete the 'Create Tutorial' goal?". The model is answering the hidden
        "[Automatic check]" turn as if talking to it. Strip that preamble, or reword the request so
        the model writes only the reply to the user. (It also means a correction round ran on a
        reply the user never saw as wrong; confirm what the first draft said, from the API logs.)

---

## ✅ Phase 0 + 0.5 — Foundation (done 2026-07-19)

- [x] Solution scaffold: `PersonaOS.Api` / `Domain` / `Infrastructure` (.NET 10, .slnx)
- [x] EF Core + SQL Server, `InstanceConfig` + `AdminUser`, auto-migrate on startup
- [x] JWT auth (`POST /api/auth/login`), single-admin model
- [x] `GET /api/branding` (nickname + enabled features + version contract)
- [x] Setup Wizard backend (`GET /api/setup/status`, one-shot `POST /api/setup`, authed `PUT`)
- [x] `RequireFeatureAttribute` feature-toggle guard (functional test pending first gated module)
- [x] Angular 20 dashboard skeleton + Setup Wizard UI (browser-verified)
- [x] Flutter skeleton: server-URL entry → branding fetch
- [x] API-key encryption at rest (Data Protection), CVE-pinned transitive packages

## ✅ Phase 1 — Chat (done 2026-07-20)

- [x] `Conversation` / `ChatMessage` / `UserProfile` entities + migration
- [x] `ChatService` on official Anthropic C# SDK (streaming, history window 20, usage capture)
- [x] SSE `POST /api/chat` (start/delta/done/error) + conversation history endpoints
- [x] Flutter login + streaming chat screens
- [x] **Provider-neutral AI** — the app is no longer Claude-only. `OpenAiCompatibleMessageStreamer`
      (thin `HttpClient` + SSE) sits behind the existing `IAiMessageStreamer` port alongside
      `AnthropicMessageStreamer`; `AiMessageStreamerFactory` picks one per `InstanceConfig.AiProvider`.
      One OpenAI Chat Completions adapter covers OpenAI, **local Ollama (free/keyless)**, Groq,
      OpenRouter, LM Studio, etc. via `AiBaseUrl`. Migration `MultiProviderAi` renames
      `ClaudeModel`→`AiModel` (value preserved) and adds `AiProvider`/`AiBaseUrl`; the encrypted-key
      column + DP purpose are untouched. Setup Wizard + Settings expose provider/base-URL/model/key;
      Anthropic requires a key, OpenAI-compatible may be keyless. Backend + web build 0-warning,
      46 tests green. **Verified live against local Ollama** (`qwen2.5:latest`, keyless): a real
      reply streamed, and the full native tool-use loop fired — model called `create_goal`, the
      adapter assembled the streamed tool call, `ChatService` dispatched it, and the goal actually
      persisted to the DB. Free, no Anthropic credits.
- [x] **Live smoke test — DONE (free, via Ollama).** The blocker was Anthropic *credits*, not code:
      the Anthropic path authenticated a real key and reached the API; the completion + native
      tool-use loop is now proven end-to-end against a local OpenAI-compatible model instead.
- [ ] Rolling summarization of older turns (windowing only for now)
- [ ] UserProfile edit endpoint + UI (entity exists; no API surface yet)

## Phase 2 — Goals (Y/Q/M)

- [x] `Goals` table: self-referencing hierarchy (`ParentGoalId`), period type, status, progress
      — entity `Goal.cs`, migration `20260721054352_Goals`, applied to dev DB.
- [x] Progress rollup in domain service (parent derives from children)
      — `Domain/Services/GoalProgressCalculator.cs` (rounded avg of non-dropped children;
      leaf falls back to own progress). Verified: avg(80,20)=50; dropping a child re-rolls to 80.
- [x] REST CRUD endpoints (`/api/goals`) behind `RequireFeature("goals")`
      — `GoalsController` (GET tree/one, POST, PUT, PUT /status, PUT /parent, DELETE subtree).
      `GoalValidationException` → 400 via `GoalValidationExceptionFilter`.
- [x] **Tool registry** in `ChatService` (`IPersonaTool`: name/description/schema/execute)
      — `Ai/Tools/IPersonaTool.cs` + `PersonaToolRegistry` (feature-gated, never throws to model).
      `ChatService` now runs a tool loop (max 8 iterations) and emits an SSE `tool` event.
- [x] First Claude tools: `get_goals`, `create_goal`, `update_goal_status`, `link_goal`
      — `Goals/Tools/GoalTools.cs`; all 4 JSON schemas validated. **End-to-end tool call still
      needs the live-key smoke test** (blocked on owner's API key — same blocker as Phase 1).
- [x] Feature-toggle functional test (disable goals → 403 + hidden in branding)
      — verified live: disabling `goals` drops it from `/api/branding` and returns 403
      `feature_disabled` on `/api/goals`; re-enabling restores 200.
- [x] Flutter Goals screen (hierarchy view) — indented tree, rollup bars, add/sub-goal/
      set-progress/complete/drop/delete. `flutter analyze` clean, `flutter test` green.
- [x] Dashboard Goals view — shipped in Phase 7 (flattened tree, rollup bars, add/complete/drop/delete).

## Phase 3 — Daily Planner

- [x] `PlannerItems` table (date, task, optional Goal link)
      — entity `PlannerItem.cs` (+ `PlannerItemStatuses` planned/done/skipped, optional
      `ScheduledTime`, `SortOrder`), migration `20260721132154_PlannerItems`, applied to dev DB.
      Goal FK is `OnDelete(SetNull)`: deleting a goal keeps its tasks and unlinks them (verified).
- [x] REST CRUD + Claude tools (mirror Phase 2 pattern)
      — `PlannerService` + `PlannerController` (`GET /api/planner?date=|from=&to=`,
      POST/PUT/DELETE `/items`, `PUT /items/{id}/status`, `PUT /items/{id}/date`) behind
      `RequireFeature("planner")`. Tools: `get_planner`, `add_planner_item`,
      `update_planner_item_status`, `move_planner_item`.
      Verified live: create (timed + linked), day/range query, ordering (timed before
      unscheduled), status, move-to-tomorrow, 400s for bad status / missing goal,
      feature toggle 403 + hidden in branding.
      **Note:** refactored `GoalValidationException` → shared `DomainValidationException`
      base + single `DomainValidationExceptionFilter`; goal error codes unchanged
      (`goal_validation_failed`). Subclass it for future modules — don't add a filter.
      System prompt now includes the user's local *today* + today's planner items.
      **End-to-end tool call still needs the live-key smoke test** (same blocker as Phases 1–2).
- [x] Flutter planner day view — date nav, add (optional time), status cycle, move-to-tomorrow, delete.
- [x] Dashboard planner view — shipped in Phase 7 (day nav, add, status cycle, move-to-tomorrow).

## Phase 4 — Reminders + FCM push

- [x] `Reminders` + `DeviceTokens` tables
      — `Reminder.cs` (UTC `DueAtUtc`, pending/delivered/cancelled/failed, attempt counter,
      optional Goal + PlannerItem links via `SetNull`) and `DeviceToken.cs` (unique token,
      android/ios). Migration `20260723052926_RemindersAndDevices`, applied to dev DB.
- [x] **Android toolchain installed, and the first APK actually builds (2026-09-11).**
      Per the owner's convention everything lives under `C:\Apps\SDK\`: `Java\jdk-17.0.20.1+1`
      (Temurin; `sdkmanager` needs a JDK and none was installed) and `Android\` with
      cmdline-tools 9862592, platform-tools, platforms;android-36, build-tools;36.0.0.
      `flutter config --android-sdk` / `--jdk-dir` point Flutter at them, and
      **`flutter doctor` now reports "No issues found!"** where the Android toolchain used to fail.
      `flutter build apk --release` produced **app-release.apk, 50.7 MB** — the first APK this
      project has ever produced.
      ⚠️ Licence acceptance **cannot** be done from the PowerShell tool: it runs non-interactive
      with stdin from null, so `sdkmanager --licenses` silently accepts nothing and the install
      then does nothing. Use the Bash tool: `yes | sdkmanager.bat --sdk_root=... --licenses`.
- [x] 🔴 **CRITICAL, found only by running a release build (2026-09-11): the mobile app had no
      network access at all.** Flutter puts `android.permission.INTERNET` in the *debug* and
      *profile* manifests only — never in `main`. So every **release** APK shipped with just
      `RECORD_AUDIO`, could not open a single socket, and would have failed for every self-hoster
      with "Could not reach a PersonaOS server at that address" no matter what they typed.
      Confirmed by `aapt2 dump permissions` on the built APK, and invisible until today because
      nobody had ever built and run a release build — `flutter analyze`, `flutter test` and even
      `flutter run` (debug) all pass happily, because debug *does* have the permission.
      Fixed by declaring INTERNET in `main/AndroidManifest.xml`, plus a
      `network_security_config.xml` allowing cleartext: PersonaOS is HTTP-by-design over a
      private network, and Android blocks cleartext from API 28, so that would have been the
      very next wall.
      Also made the failure diagnosable — `_connect` swallowed the exception (`catch (_)`) and
      showed one generic line, which is why a missing permission looked identical to a typo'd
      address. It now reports the actual reason.
- [x] **Confirmation cards verified on a real Android build (2026-09-11).** Emulator (Pixel 6,
      API 36) with `adb reverse tcp:8080 tcp:8080`, so the device reached the instance over the
      cable with **no change to network exposure** — `PERSONAOS_BIND` stayed on loopback.
      Setup → login → asked for a monthly goal → the card rendered
      `Create goal “Mobile Test” — month · 2026-09-01 · top-level` with Discard/Confirm, and the
      assistant said it was *proposing*, not claiming it was done. `/api/goals` held **only**
      "Master AI" at that point — nothing written. Tapping Confirm turned the card into
      `✓ Mobile Test — month · 2026-09-01 · active` and the goal appeared. Period normalisation
      also held (the 1st, not today's date).
      ⚠️ The APK is unsigned-for-release (debug signing) and is **not** on a phone yet: the stack
      still binds to `127.0.0.1`, so a device on the tailnet cannot reach it — set
      `PERSONAOS_BIND=100.90.80.20` first.
- [ ] Firebase project setup (FCM Android; APNs via FCM for iOS) — **owner task**, needs a
      Firebase account. Backend is complete behind the `IPushSender` port; drop in an FCM
      adapter and swap the `NullPushSender` registration in `Infrastructure/DependencyInjection`.
- [x] **Alarm-style reminders + push fixes (2026-09-14).** Confirmed on the owner's phone: the alarm
      rang full screen. Follow-up found and fixed there: Dismiss/Snooze left the screen open
      (`maybePop()` honoured the alarm's own `PopScope(canPop: false)`; now `pop()`), and the
      confirm card lost its Confirm button (theme `Size.fromHeight` = infinite width in a Row).
      Original report: owner tested
      the push build on his phone: the reminder arrived ~29 s late, the notification header read
      "2032y", and he wants a prominent alarm rather than a notification. Causes: (1) the
      dispatcher polls every 30 s and Doze adds more, so a push sent at due time can never be
      punctual; (2) FirebaseAdmin 3.6's `AndroidNotification.EventTimestamp` is a NON-nullable
      `DateTime` that serialises `event_time: 0001-01-01T00:00:00Z` when unset — confirmed by
      reflection on the DLL — so Android dates the notification ~2000 years ago; (3) two
      concurrent `POST /api/devices` for one token raced the unique index → 500. Plan: the server
      pushes the reminder SCHEDULE (data-only) when a reminder is created/changed/cancelled, the
      phone sets an exact alarm-clock alarm that fires a full-screen alarm with looping sound and
      Dismiss/Snooze; the due-time push stays as a data-only fallback the phone ignores if it
      already raised that alarm. Alarms resync on sign-in, and survive reboot via the plugin's
      boot receiver.
      **Built and ✅ verified on the emulator 2026-09-14** (deployed to the live stack):
      server `ReminderAlarmPublisher` pushes `reminder_scheduled` / `reminder_removed` from all
      four `ReminderService` mutations (best-effort — a push outage never fails the reminder);
      the dispatcher's due push is now data-only `reminder_due`; `IPushSender.SendDataAsync`
      added; `EventTimestamp` set explicitly; `RegisterDeviceAsync` survives the insert race.
      App: `ReminderAlarms` (exact `alarmClock` schedules, UTC-based, state in shared prefs so
      background isolates work), `AlarmScreen` over the lock screen, Snooze 10 min / Dismiss,
      a background FCM handler, sync on sign-in that runs even with push unconfigured.
      Evidence: a reminder created via the API became an exact alarm on the device within 8 s
      (`origWhen` = due time, `window=0`, `exactAllowReason=policy_permission`); with the screen
      locked it rang **0.8 s after due** (audio focus 20:48:36.820, `USAGE_ALARM`, INSISTENT,
      importance 5) and the full-screen AlarmScreen showed — also when the app process was
      already alive. Snooze stopped the sound and scheduled exactly +10 min; Dismiss stopped it
      and scheduled nothing; cancelling a reminder removed its alarm; the fallback due push
      woke the background handler and produced no second alert; a single `POST /api/devices`
      per sign-in (the race is gone). Backend 0 warnings, 153 tests; mobile 37 tests.
      **Caught in own code before shipping:** `PushService` re-initialised the notification
      plugin, which would have replaced the alarm engine's callbacks and silently broken
      Snooze, Dismiss and opening an alarm — removed.
      **NOT verified:** the owner's real phone (APK sent); alarms restored after a REBOOT (boot
      receiver is wired but was not exercised); the Android 14+ full-screen permission was
      already granted on the emulator, so the settings prompt path was not seen.
      **Heads-up:** the previous test build on the phone does not understand data-only reminder
      messages, so it gets no reminders from this server until updated. Status-bar alarm icon
      still uses the launcher icon (renders as a white blob) until real artwork exists.
- [x] FCM push, end to end (2026-09-14; confirmed on the owner's phone). The owner chose FCM over a
      self-hosted distributor (would have needed the separate ntfy app on the phone) and over
      server-synced local alarms. Found while planning: **the mobile app has no FCM code at all** —
      the "device-token registration" item below is backend-only — and **device registration is
      gated behind the `reminders` feature**, so turning reminders off also silently stops
      proactive briefs, which push to the same tokens. Plan: `FcmPushSender` adapter selected
      only when a credentials file is present (falls back to `NullPushSender`); move device
      endpoints to an ungated `/api/devices`; mobile registers its token on login and on refresh,
      shows foreground notifications, and builds cleanly WITHOUT `google-services.json` so the
      open-source repo stays buildable. Credentials stay out of git and out of the image.
      **Redesigned the same day to bring-your-own FCM, configured in the UI** (owner's call): the
      release APK must not carry any Firebase config, and other self-hosters must be able to use
      their own Firebase project without rebuilding the app. The first cut — a key file mounted
      from `deploy/secrets/` plus `google-services.json` compiled in via a conditional Gradle
      plugin — was never committed and has been removed.
      **How it works now:** the admin uploads BOTH Firebase files on the web Settings page
      (`PushConfig` component → `PUT /api/setup/push`, admin-only). `FirebaseConfigParser`
      validates them — refuses swapped files, files from different projects, and a
      google-services.json with no `com.personaos.personaos_mobile` app. The service-account
      key is encrypted under its own Data Protection purpose `PersonaOS.FcmServiceAccount.v1`
      (keyed `ISecretProtector`; never the AI key's purpose) and is never returned by any
      endpoint. The public client options go to the app via `GET /api/devices/push-config`, and
      `PushService` starts Firebase at runtime with them — so a reinstall recovers push on sign-in.
      `ConfigurablePushSender` swaps the live sender without a restart; `PushConfigLoader` restores
      it at startup. `NullPushSender` is deleted. Migration `FcmPushConfig` adds three nullable
      columns (safe for existing rows).
      **Ordering matters:** `SetAsync` loads the key into the live sender BEFORE saving it, so a
      well-formed file with an unparseable private key is rejected with nothing stored.
      **Verified:** backend 0 warnings, 145 tests (15 new); mobile analyze clean, 32 tests; web
      image builds; migration applied to the live DB. Live against the real Firebase SDK:
      swapped files, mismatched projects, and a fake private key each return a specific 400,
      and status stays `configured: false`. Also still true from the first cut: registration
      returns 204 with reminders disabled (was 403).
      **✅ Verified END TO END on the emulator, 2026-09-14, with the owner's real Firebase project
     ** uploaded through the web page: sign-in → app fetched the client
      options → `Firebase.initializeApp(options:)` started Firebase with NO google-services.json
      in the APK → notification permission granted → token registered → the dispatcher sent a
      queued reminder → notification shown (the reminder text, channel `reminders`, importance HIGH).
      Reminder 6 flipped to `delivered` at 13:04:30 UTC, the same second the device logged the
      FCM receipt. Before any device registered, the dispatcher correctly held the reminder
      ("no devices are registered") rather than consuming it.
      **Still to do:** (1) the owner's real phone — it runs alpha.4, which has no push code, so it
      needs a new build; (2) **the notification shows the app as `personaos_mobile` with the
      default Flutter icon** — `android:label` and the launcher icon were never set, which breaks
      the fixed "PersonaOS" brand rule; (3) the emulator's token is still registered, so reminders
      now go to it as well as the phone until it is removed or the app is uninstalled there
      (FCM then reports it `Unregistered` and the dispatcher prunes it).
      Decisions worth knowing: `MulticastMessage.Tokens` is obsolete in FirebaseAdmin 3.6 in favour
      of FIDs, but firebase_messaging 16.6.0 has no FID API, so tokens stay behind a scoped
      `#pragma` — move to `Fids` when the client can register by FID. Only `Unregistered` /
      `SenderIdMismatch` prune a token; `InvalidArgument` does not, since it can mean a bad
      payload and pruning on it would delete every device at once. Changing Firebase project
      while the app runs needs an app restart — Firebase cannot re-initialise its default app.
      **Next:** owner uploads both files in Settings, signs in on the phone, sends a reminder.
      **Open product question:** this BYO path serves technical self-hosters. A project-run push
      relay (or UnifiedPush as an opt-in) is what would serve everyone else — undecided.
- [x] Device-token registration on login; server hosted-service sends push at due time
      — `POST/DELETE /api/reminders/devices` (idempotent per token, verified); dispatcher
      `ReminderDispatcher` + `ReminderDispatchService` (30s `PeriodicTimer`, per-pass scope,
      never crashes the host). Prunes provider-rejected tokens; gives up after 5 attempts.
      Verified: with push unconfigured, due reminders stay **pending** (not failed) so they
      fire once FCM is added; cancelled reminders are skipped.
      ✅ The success path (mark delivered / prune invalid token / retry-then-fail) is now
      covered by `ReminderDispatcherTests` with a fake `IPushSender`.
- [x] Claude tool: conversational reminder creation (resolve relative times vs configured TZ)
      — tools `get_reminders`, `create_reminder`, `cancel_reminder`. `create_reminder` takes
      `dueAtLocal` (user wall-clock); the system prompt supplies their zone + today's date so
      the model resolves "tomorrow 9am" itself. Verified: 09:00 Asia/Calcutta → 03:30 UTC and
      back. New shared `UserClock` helper handles conversion incl. DST gaps.
      System prompt now also lists the next 5 upcoming reminders.
      **End-to-end tool call still needs the live-key smoke test** (same blocker as Phases 1–3).
- [x] Flutter Reminders screen (list, create with date+time picker, cancel, delete).
- [ ] `flutter_local_notifications` for foreground display (mobile session)

## Phase 5 — Docs storage

- [x] `POST/GET /api/documents` — filesystem storage under `data/docs-storage/`
      — `Document` entity + migration `20260723055854_Documents`; `DocumentService` +
      `DocumentsController` (list/search, metadata, download, upload multipart, edit
      description, delete) behind `RequireFeature("docs")`.
      `IDocumentStorage` port → `FileSystemDocumentStorage` (root from
      `Documents:StoragePath`, default `<app>/data/docs-storage`).
      **Security:** on-disk names are server-generated GUIDs, user names are only metadata,
      and every resolved path is checked to stay inside the root. Verified: a filename of
      `../../../../etc/passwd` collapses to `passwd`; 25 MB cap; empty upload rejected;
      delete removes both the row and the file.
- [x] Claude tool `get_document(name)` (retrieval by name; NO RAG in v1)
      — shipped as `list_documents` + `read_document` (accepts `documentId` **or**
      `fileName`, disambiguating multiple matches). Text formats only (txt/md/csv/json/
      xml/code); PDFs/images/Office return a clear "cannot be read as text" message —
      extraction stays out of scope per the PRD. Reads truncate at 20k characters.
      **End-to-end tool call still needs the live-key smoke test** (same blocker as Phases 1–4).

## Phase 6 — Voice (push-to-talk)

- [x] Flutter mic button → `speech_to_text` → send as chat message. Push-to-talk: partial
      results fill the composer live; on the final result it auto-sends. Gated on the `voice`
      feature. `VoiceService` wraps both plugins; Android `RECORD_AUDIO` + recognizer `<queries>`
      and iOS mic/speech `Info.plist` keys added.
- [x] `flutter_tts` read-back toggle — AppBar toggle; when on, speaks each completed reply.
      **Not runnable headless here** — mic capture + TTS audio need a device/emulator; verified
      by `flutter analyze` (clean) + `flutter test` (green). Manual device check still pending.
- [x] Voice input failed silently — fixed in `ba4dbf3`, and **confirmed working on a real device
      on 2026-09-12**: dictation transcribes, no mic errors. The emulator still cannot transcribe
      (no SODA language pack), so test voice on hardware, never on the AVD.
- [x] Fingerprint sign-in (2026-09-13). Verified on the emulator with an enrolled print: the
      biometric prompt fires on cold start and one touch reaches chat with no password typed.
      Credentials go in `EncryptedSharedPreferences` (Keystore-backed) and are read only after
      the prompt passes. Chosen over storing the JWT because the access token lasts 12h and there
      is no refresh endpoint, so a token alone would still mean typing a password daily. Revisit
      if refresh tokens ever land — that removes the password from the phone entirely.
- [x] Mobile parity + modern UI + hands-free voice (2026-09-12). The
      owner tested the alpha on a real phone and asked for four things: a more modern interface,
      a hands-free voice mode rather than push-to-talk, chat history, and every feature the web
      app has. Gaps found by inventory — mobile has chat, goals, planner, reminders, login; the
      web also has conversation history (list/open/delete), documents, and settings. The backend
      endpoints for all three already exist, so this is client work only.
- [ ] (superseded, kept for the record) Voice input fails silently. Found on the emulator:
      the mic turns on, Android shows the recording indicator, and then nothing ever comes back.
      `VoiceService.ensureStt` passes `onError: (_) {}`, so every recognition error is discarded
      and the chat screen leaves `_listening` true forever — the mic looks stuck on and the user
      is told nothing. Surface the error and reset the mic state. Separately, `listen` never
      passes a `localeId`, so it uses the device default with no fallback to a locale the
      recognizer actually has. Note the emulator itself cannot transcribe — the AOSP image has no
      SODA language pack (`Failed to get language pack of required locale: error 13`) and the
      online recognizer needs a signed-in Google account — so confirming a *successful*
      transcription still needs a real device.

## Phase 7 — Dashboard parity

- [x] Dashboard chat (reuse SSE endpoint) — `ChatService` reads the SSE stream with
      `fetch` + `ReadableStream` (not `EventSource`, which cannot send the bearer header).
      Conversation sidebar, streaming bubbles, per-call tool indicator, Enter-to-send.
- [x] Goals / Planner / Reminders views — all three verified against a live API in the browser.
      Auth is signal-based (`AuthService` + `authInterceptor` + `authGuard`); token in
      localStorage, 401 → `sessionExpired()`.
- [x] Settings page (edit nickname/persona/model/key — wraps `PUT /api/setup`).
      Needed a new **admin-only `GET /api/setup`** to prefill it; it returns
      `HasAnthropicApiKey` rather than the key — the key is never sent to a client.
      Saving refreshes `/api/branding`, so toggling a module updates the nav immediately.

**Gotcha for anyone doing dates on the frontend:** `new Date().toISOString().slice(0,10)`
is wrong for calendar days — it converts to UTC first, so east of Greenwich local midnight
lands on the previous UTC day and every date silently shifts back one. This shipped a bug
where "next day" and "move to tomorrow" both resolved to the *current* day. Use
`core/local-date.ts` (`todayLocal` / `shiftLocalDate`) instead.

- [x] Documents view (list, search, upload, download, delete), gated on the `docs` module.
      Download goes through `HttpClient` (responseType blob) so the bearer interceptor runs —
      a plain `<a href>` can't carry the Authorization header. Verified E2E: upload 201,
      authed download 200, search filter, delete 204.
- [x] "Update available" banner (`UpdatesService` + a dismissible banner in the app shell).
      Notify-only: checks the GitHub Releases API for `personaos/personaos`, compares
      `tag_name` against `/api/branding` `apiVersion` with a dotted-numeric `isNewer`, shows the
      `docker compose pull && up -d` command + a release-notes link. **Fail-silent** — a private/
      absent repo (currently a real 404), rate-limit, or offline just means no banner. Dismissal
      is remembered per-version in localStorage. Verified both paths in the browser (real 404 →
      no banner; injected newer release → banner renders; dismiss clears + persists).

Phase 7 dashboard parity is complete. Remaining across the project: **Phase 6 (Flutter voice)**,
plus the Flutter Goals/Planner/Reminders screens, and the live-key chat smoke test (blocked on a
real Anthropic key).

## Phase 8 — Proactive scheduler (feature-toggled)

- [x] Background scheduler (`ProactiveScheduleService`, 5-min `PeriodicTimer`, scoped per pass,
      survives failures). No Quartz needed — `ProactiveJobRun` + a unique index on
      (JobName, LocalDate) is what makes it idempotent, not the timer.
      `ProactiveService` takes a **`TimeProvider`** so scheduling is unit-testable;
      DI registers `TimeProvider.System`.
- [x] Morning brief + evening rollup (goal nudges included in the brief)
      — `ProactiveBriefComposer` builds text **deterministically** from planner/reminders/goals:
      no API call, so a quota or key problem can never silence the briefs.
      Scheduled per-job at local times on `InstanceConfig` (`MorningBriefTime` 07:30,
      `EveningRollupTime` 21:00; null disables that job). A job more than 3h late is skipped
      rather than fired stale after downtime. Silent when there's nothing to report.
      Each brief is pushed **and** filed as a chat conversation, so the user can reply
      ("yes, move them to tomorrow") with full context.
      `GET /api/proactive/runs` (audit) and `POST /api/proactive/run/{job}?force=` (manual)
      behind `RequireFeature("proactive")`, which is **off by default**.
      Verified live: 403 when disabled, real brief from planner+goals, `force=false` skipped
      as "already ran today", rollup with open items, unknown job 400, audit trail, and both
      briefs appearing in chat history. 11 unit tests cover schedule/idempotency/edge cases.
- [ ] Optional: model-written phrasing for briefs (must stay a graceful enhancement over the
      deterministic text, never a dependency).

## Phase 9 — Packaging + open-source release

- [x] Dockerfiles + `deploy/docker-compose.yml` (API + SQL Server + dashboard) + `.env.example`
      — API Dockerfile (multi-stage, non-root, restore-layer cached, data dirs pre-chowned);
      dashboard Dockerfile (Angular → nginx) with `nginx.conf` that proxies `/api`, disables
      buffering on `/api/chat` (SSE would otherwise be held until the reply completed), and
      caches hashed assets while marking `index.html` no-store.
      Compose: **only the dashboard publishes a host port, bound to 127.0.0.1 by default**;
      API and SQL Server are internal-only. DB healthcheck gates API start. Named volumes for
      the database and for `api-data` (documents + Data Protection keyring).
      `docker-compose.build.yml` overlay builds from source instead of pulling.
      ✅ **Both images build and the full stack runs** (verified 2026-07-24, Docker 29.6.2):
      API and dashboard images built first try; `docker compose up -d` brought the stack up
      with the DB healthcheck correctly gating API start.
- [x] `LICENSE` (Apache-2.0) + `NOTICE` + `README` + `CONTRIBUTING`
      — NOTICE spells out that the licence covers the code, not the PersonaOS name/logo.
      README is written for a self-hoster (install, Tailscale, update, where your data lives,
      what happens if you lose the keyring volume). CONTRIBUTING documents the layer rules,
      the ports pattern, the zero-warning bar, and the `TimeProvider`/fakes testing conventions.
- [x] Release pipeline: tag → GHCR images (`:x.y.z` + `:latest`) → GitHub Release (compose + APK)
      — `.github/workflows/release.yml` (tag `v*`), plus `ci.yml` running backend build
      (`-warnaserror`) + tests, dashboard build, Flutter analyze/test, and an image build.
      YAML validated locally; **not yet executed** (needs a GitHub remote).
- [x] In-dashboard "update available" banner (GitHub Releases API; notify-only) — shipped in Phase 7
      (`UpdatesService` + dismissible banner; fail-silent; dotted-numeric version compare).
- [x] **Build and run the container images** — done, no fixes needed.
- [x] Clean-machine install test; upgrade test (data survives)
      — **Install:** empty volumes → all 7 migrations applied automatically on first boot →
      Setup Wizard → login → goals/planner/documents created through nginx. Dashboard renders
      "Friday is ready" from the container. SSE chat streams **through nginx** as separate
      frames (proves `proxy_buffering off` on `/api/chat` works). Uploads land in the
      `api-data` volume under a GUID name; API runs **non-root** (uid 1654).
      Port exposure confirmed: only `dashboard` publishes, on `127.0.0.1:8080`; API and
      SQL Server are internal-only.
      **Upgrade:** `up -d --force-recreate` (volumes kept) → second boot applied
      *no* migrations ("already up to date") → nickname, admin login, goals, planner and
      documents all intact → **the encrypted Anthropic key still decrypted**, proving the
      Data Protection keyring survived (the failure mode that would silently force re-entry).
      `down -v` removes both volumes cleanly.
      ⚠️ Not yet tested: a genuine **N → N+1** upgrade across two different image versions
      (needs a published prior release) and the `minSupportedClient` version gate.
- [x] **Full E2E pass DONE 2026-09-11** — clean install *and* restart-with-volumes, on the SQLite
      stack. Run against an **isolated instance** (`docker compose -p personaos-e2e`, port 8081,
      own volumes) so the owner's live instance was never touched; torn down with `down -v` after.
      **Install:** empty volumes → 3 migrations applied → WAL + `synchronous=NORMAL` → integrity
      check → nightly backup written → singleton config seeded. Only `web` publishes a port.
      **Through the real UI:** Setup Wizard (provider switched to OpenAI-compatible, which
      revealed the Base URL field) → login **as `E2E-Admin` for the stored `e2e-admin`**, proving
      the case-insensitive fix end to end → chat → `add_planner_item` fired and the receipt
      rendered inline under the reply.
      **Modules:** goals rollup `effectiveProgress` 40 = avg(0,80); planner status cycle;
      documents upload/list/**authed download 200 / unauthenticated 401**/delete removes row+file;
      feature toggle → hidden in `/api/branding` + 403 `feature_disabled` → re-enable restores 200.
      **Path traversal properly defended:** a hand-built multipart upload with filename
      `../../../../etc/passwd` stored metadata `passwd` only, wrote a GUID inside `docs-storage`,
      escaped nothing, and left the container's real `/etc/passwd` untouched.
      **Restart with volumes kept:** *zero* migrations on second boot, integrity passed, and
      nickname/admin/planner/goals/reminder/document/conversations **and both tool receipts** all
      survived.
      ✅ **Keyring gap CLOSED** — the thing earlier runs could not test because the instance was
      keyless. Set a provider key (plaintext absent from `personaos.db`, keyring file present),
      recreated the containers, and after restart `POST /api/setup/test` returned
      `{"ok":true,...}` — i.e. the stored key still *decrypts*, not merely "is present". Zero
      crypto errors in the log.
      ⚠️ Still untested: a genuine **N → N+1** across two published image versions, and the
      `minSupportedClient` gate. Both need a release, which is deliberately not being cut yet.
      ⚠️ During teardown the owner's own containers were removed too (cause unclear — the `down -v`
      targeted only the `personaos-e2e` project). No data lost: `personaos_api-data` survived and
      `up -d` restored everything. If running a second project again, check `docker ps` afterwards.
- [-] ~~Re-run the clean-machine install + upgrade test on the SQLite stack.~~ Superseded by the
      entry above. Original note: the verification
      recorded above was performed against the old **SQL Server** compose stack (before `5ae9092`),
      so it is no longer evidence for what ships today: there is no DB container or healthcheck
      any more, and the database now lives inside the `api-data` volume alongside the Data
      Protection keyring. The keyring-survives-upgrade check in particular is worth repeating,
      since a lost keyring silently forces re-entry of the provider API key.
      ✅ **Install half DONE on the SQLite stack (2026-09-05, Docker 29.7.2).** Built both images
      from source (`-f docker-compose.yml -f docker-compose.build.yml up -d --build`) into empty
      volumes: `InitialCreate` applied, WAL + `synchronous=NORMAL` set, integrity check passed,
      nightly backup written, singleton config seeded pre-serving. Drove the **real UI** at
      `127.0.0.1:8080` through nginx: Setup Wizard → login → chat. Verified only `web` publishes a
      port (api is `expose`-only); `proactive` stays off and is absent from both `/api/branding`
      and the nav. Full loop proven through the proxy — the model called `add_planner_item`, it
      persisted to SQLite, and the Planner view rendered it. Provider was local Ollama
      (`llama3.1:8b`, keyless) reached from the container at `http://host.docker.internal:11434/v1`
      — **note `localhost` does NOT work from inside the container**; the name resolves to an IPv6
      address and worked fine.
      ⬜ Still open: the **upgrade** half (N → N+1 across two image versions, keyring survival) and
      the `minSupportedClient` gate.
- [x] Self-hoster docs (install, Tailscale, BYO key, update) — in README.md.

## Cross-cutting / anytime

- [x] Initial git commit (`0ca5bbb`, 2026-07-20)
- [x] Backend unit tests project (`PersonaOS.Tests`) — xUnit, **35 tests, all passing**,
      run with `dotnet test src/backend/PersonaOS.Tests`. Tests sit at the Application layer:
      a `TestDbContext` implements `IAppDbContext` over EF InMemory, so no Infrastructure or
      real DB is involved. Fakes in `TestSupport/` (`FakePushSender`, `FakeAiMessageStreamer`,
      `FakeTool`, …) are scriptable — extend those rather than writing new mocks.
      Covers: **ReminderDispatcher** (delivery, invalid-token pruning, retry-then-fail at 5,
      recovery, unconfigured push, no devices, feature off) — closing the Phase 4 gap;
      **ChatService tool loop** (tool executed + result fed back, parallel tool calls, unknown
      tool degrades to an error result, disabled feature hides the tool, stream failure,
      history replay, missing key); **GoalProgressCalculator**; **UserClock** (incl. the
      DST spring-forward gap). Mutation-checked: breaking `MaxAttempts` fails a test.
- [x] **Fixed: username login was case-sensitive — regression from
      the SQLite migration (`5ae9092`).** `AuthService` did a plain `u.Username == username`, so
      case sensitivity is decided by the column collation. SQL Server defaulted to
      `SQL_Latin1_General_CP1_CI_AS` (case-INsensitive); SQLite defaults to `BINARY`
      (case-SENSITIVE), and nothing configures a collation. Verified live: `purush` → 200,
      `Purush`/`PURUSH` → 401. Also means `IX_AdminUsers_Username` no longer prevents `purush`
      and `Purush` coexisting.
      **Fix (two halves, deliberately):** `AuthService.LoginAsync` now lower-cases both sides
      explicitly instead of trusting the provider's collation — that is what makes the behaviour
      testable and provider-independent, since EF InMemory (which every test here uses) ignores
      collation entirely. Separately the column is declared `.UseCollation("NOCASE")`, migration
      `20260905182358_AdminUsernameCaseInsensitive`, so `IX_AdminUsers_Username` also stops
      treating `purush` and `Purush` as different accounts.
      **Tests:** new `Auth/AuthServiceTests.cs` (10 cases: casing variants, stored-mixed-case,
      whitespace, returned casing, wrong password, unknown user, rehash path) plus
      `FakePasswordHasher` + `FakeJwtTokenGenerator` added to `TestSupport/Fakes.cs`.
      56 tests green, build 0-warning. **Mutation-checked:** restoring the plain `==` fails 7.
      **Verified live in the container** after rebuild: `purush` / `Purush` / `PURUSH` /
      `"  pUrUsH  "` all → 200, wrong password still 401, and the SQLite table rebuild preserved
      the admin row, nickname and planner data.
- [x] **Guard against weak models faking tool use (2026-09-06).** Live testing with `llama3.1:8b`
      surfaced two failures. (1) It *narrated* actions it never took — claimed a planner item and a
      reminder were created when the DB had neither; the tool loop itself was fine (an explicit
      "use the add_planner_item tool" fired and persisted correctly). (2) It printed tool calls as
      prose using the **definition** shape (`"parameters"`, not `"arguments"`); nothing executed,
      the raw JSON was shown to the user AND stored, so the next turn read it back from history and
      copied the pattern — a self-reinforcing loop.
      **Fix:** `SystemPromptBuilder` now states tool rules explicitly — never write a call as text,
      never claim an action without a tool result, never invent a tool/parameter/document name.
      New `LeakedToolCallScrubber` strips JSON objects whose `name` matches a **registered** tool
      (so ordinary JSON in a reply survives); `ChatService` scrubs before persisting, logs a
      warning, and substitutes an explanation if the whole reply was a leaked call. The `done` SSE
      event now carries `text` **only when** the stored reply differs from the streamed deltas, and
      the web client replaces the bubble on it — so the user stops seeing internals live, not just
      after a reload.
      73 tests green, 0 warnings. Mutation-checked: storing the unscrubbed text fails 2.
      ✅ Flutter updated 2026-09-10 — it now applies `text` on `done` (see the receipts entry
      below), though it is still uncompiled here for want of the SDK.
      ⚠️ Known limitation: if a user genuinely asks "show me the JSON to call get_goals", the reply
      is scrubbed. Judged acceptable — the shape needs a real tool name plus an args key.
      **qwen2.5 retest (2026-09-06), same flows, same instance:** the *action* class is fixed —
      "Whats on my list today?" → real `get_planner`; "Yes - travel to home" → real
      `add_planner_item`, item **actually persisted**; "set a reminder for 6pm" → real
      `create_reminder`, stored correctly (18:00 Asia/Calcutta → 12:30 UTC). No JSON leak; nothing
      needed scrubbing. llama3.1:8b faked all three. **Recommend qwen2.5 as the documented minimum
      for local use.**
      ⚠️ **Two hallucination classes survive the model swap:**
      (a) *Factual invention about the product* — asked how to install the mobile app, qwen2.5
      confidently said to search the App Store / Play Store for "Juno". The app is published
      nowhere. Fluent and plausible, so worse than the old JSON leak. Fix: ground the system
      prompt with what PersonaOS actually is (self-hosted; no store listing; APK from GitHub
      Releases) and tell it to say it does not know rather than invent.
      (b) *Wrong narration of a correct action* — it created the 6pm reminder correctly but
      described it as "6pm UTC, which is 12:30pm your time", inverting local and UTC. The write
      was right; the sentence would mislead. Tool receipts (showing stored values) would expose
      this; the prompt cannot reliably.
- [x] **Ground the assistant + guarantee one behavioural
      contract across providers.** qwen2.5 invented App Store install steps for "Juno"; the model
      knows nothing about the app it runs inside. Grounding goes in `SystemPromptBuilder`
      (Application), which every adapter shares — so the rules are identical for Anthropic and
      every OpenAI-compatible model by construction. **Design rule: never put behavioural or
      prompt logic in an adapter** — adapters translate wire formats only, or providers drift apart.
      **Done.** `SystemPromptBuilder` now appends (a) product grounding — self-hosted, no app-store
      listing, APK from GitHub releases, private-network access, data stays on the server, fixed
      brand vs chosen nickname; (b) the **real** project/releases URLs as constants; (c) a list of
      the modules actually enabled, naming the switched-off ones so the model stops offering them;
      (d) "say I don't know rather than guess".
      **Key lesson: forbidding invention does not work — supplying the fact does.** With only
      "never invent URLs", qwen2.5 still emitted a confident link to `github.com/PersomalAI/
      perspective`, which does not exist. Adding the true URL fixed it.
      **Cross-model proof of the contract (same prompt, same question, model swapped live):**
      qwen2.5 → correct steps with the correct URL; llama3.1:8b → *"I don't know. …refer to the
      README at https://github.com/Purush0th/personaOS."* Different fluency, neither fabricates —
      that is the achievable form of model-agnostic: identical contract, not identical prose.
      83 tests green, 0 warnings, incl. a test asserting the prompt is byte-identical across
      providers so behaviour cannot start diverging per model.
      ✅ Deduplicated 2026-09-10: the canonical repo now lives once, in
      `PersonaOS.Domain/PersonaOsProject.cs`. `SystemPromptBuilder` uses it, `/api/branding`
      serves it as `repository`, and the web `updates.service.ts` reads it from branding instead
      of its own constant — so the banner can no longer poll a different repo than the backend
      believes in. Verified live: branding returns `"repository":"Purush0th/personaOS"`.
- [x] **Tool receipts — show what the app actually did.**
      qwen2.5 stored a 6pm reminder correctly then described it as "6pm UTC, which is 12:30pm your
      time", inverting local and UTC. Prompting cannot reliably fix wrong narration of a correct
      action; showing the stored values can. Record what each tool actually executed and return,
      persist it on the assistant message, and render it under the reply so prose is never the
      only evidence.
      **Done.** `ToolReceiptBuilder` summarises each executed tool from its own result JSON —
      label (`title`/`message`/`fileName`) plus context (`dueAtLocal`/`date`/`status`), ISO stamps
      tidied, long values truncated. Receipts persist on the assistant message
      (`ChatMessage.ToolActionsJson`, migration `20260905194831_ChatMessageToolActions`), ride the
      `done` SSE event, and are returned by the history endpoint so they survive a reload. The web
      chat renders them as a quiet checklist under the reply, red when a tool failed. Built from
      tool output only — never model text — so it is provider-neutral like the rest of the contract.
      95 tests green, 0 warnings. **Verified live over the API** on the exact case that was
      misdescribed: "Remind me to call the bank at 6pm today" →
      `create_reminder ✓ "Call the bank — 2026-09-06 18:00 · pending"`. A test pins that the receipt
      shows the stored **local** time and never the UTC value, since showing UTC is the confusion
      it exists to prevent.
      ✅ **Browser rendering VERIFIED 2026-09-10**: opening an old conversation renders
      `✓ create_reminder Call the bank — 2026-09-06 18:00 · pending` beneath the reply, styled
      (border-top applied, green, bold mark) — and since it came from history, receipts survive a
      reload as intended. Superseded note below:
      ⚠️ ~~**Browser rendering is NOT yet verified**~~ — Docker Desktop crashed mid-check (orphaned
      AF_UNIX sockets under %LOCALAPPDATA%/Docker/run after a force-kill; needs a reboot). The
      Angular build compiled and deployed, but nobody has seen the receipts painted on screen.
      Confirm before treating this as complete.
      ✅ Flutter now handles both (2026-09-10): `ChatEvent` parses `actions` into a new
      `ToolReceipt`, the `done` case applies `event.text` **before** read-back (TTS would
      otherwise have spoken the raw JSON aloud) and stores the receipts, and `_ReceiptList`
      renders them under the bubble.
      ✅ **Verified: Flutter SDK 3.47.3 installed at `D:\flutter` (2026-09-10).**
      `flutter analyze` clean, `flutter test` **7 passing**. Worth noting why this mattered:
      analyze immediately found 2 real compile errors — the `actions` field had been added to
      `ChatEvent` without adding it to the constructor, so the client would not have built at
      all. Code review had not caught it. **Do not ship Dart changes on review alone.**
      New `test/chat_event_test.dart` pins the SSE parsing contract (receipts present/absent,
      failed tool, missing summary, corrected `text` on `done`) since that is precisely what broke.
- [x] **Three defects found by reading a real chat (2026-09-11).** The owner shared a goals
      conversation; the goal *was* created, but everything around it was wrong.
      1. **Receipts were blind to wrapped payloads.** `create_goal` returns `{"created": {…}}` and
         `get_goals` returns `{"goals": […]}`; `ToolReceiptBuilder` only read the root, so every
         goal receipt stored `"summary":null` — empty at exactly the moment the user needed it.
         Now unwraps single-property wrappers (bounded depth; `{"ok":true}` untouched) and adds
         `periodType`/`periodStart` to the detail fields. Verified live:
         `create_goal ✓ "Read more books — month · 2026-09-01 · active"`.
      2. **Goal periods were never normalised.** The tool documents `periodStart` as "the first
         day of the period", nothing enforced it, and a *monthly* goal got `2026-10-10`. New
         `Domain/Services/GoalPeriodCalculator` snaps month → 1st, quarter → Jan/Apr/Jul/Oct 1,
         year → Jan 1, on create **and** update. Deterministic, so it holds for any model.
         Verified live: month `2026-10-10` → `2026-10-01`; quarter `2026-09-11` → `2026-07-01`.
      3. **A dangling ```json fence leaked into the reply.** The model opened a fence mid-reply
         and never closed it; the scrubber only tidied fences after removing a JSON object, so it
         survived. Now removes an unmatched fence — the **last** one, so an earlier balanced code
         block is not torn apart (test covers that).
      116 tests green, 0 warnings.
      ⚠️ Pre-existing rows keep their old values: `Master AI` is still `2026-10-10` and the C#
      goal is still top-level. Normalisation only applies on write — no backfill was done.
- [x] **Nothing writes without the user's confirmation (2026-09-11).** The owner's call after a
      third unbidden write (a sub-goal nobody asked for, right after "Lets discuss before we add
      anything"). Prompt rules never held, so this is enforced in code instead.
      `IPersonaTool.Mutates` is declared per tool — **not guessed from the name**, so a new tool
      cannot slip past by being called something unexpected — and reads (`get_goals`,
      `get_planner`, `get_reminders`, `list_documents`, `read_document`) stay ungated, since the
      gate would be unusable if every question needed a tap. `ChatService` turns a mutating call
      into a `PendingAction` row instead of executing it, and hands the model back a result
      beginning `NOT EXECUTED`, so it stops announcing things as done. The reply renders a card
      ("Create goal “Learn Rust” — month · 2026-09-01 · **top-level**") with Confirm / Discard;
      `POST /api/chat/actions/{id}/confirm|discard` is the *only* path by which a model-requested
      write reaches the database.
      The card deliberately states parentage either way — that is the detail a model has actually
      got wrong, and "top-level" on screen catches it *before* it is saved.
      Confirm is idempotent, discard is final (confirming afterwards is refused), and proposals
      survive a reload. Migration `PendingActions`. 127 backend + 10 web specs green.
      Verified live: asking for a goal left `/api/goals` **unchanged** until Confirm; confirming
      twice created one row; a discarded proposal never ran; in the browser, Confirm turned the
      card into `✓ Water the plants — 2026-09-11 20:00 · pending` and the reminder appeared.
      ✅ **Flutter renders the cards too (2026-09-11)** — `PendingAction` model, `confirmAction`
      / `discardAction` on the API client, and a `_ProposalList` widget under the bubble with
      Confirm / Discard, replaced in place by the outcome. Double-tap guarded; a failed call
      leaves the card pending and shows a snackbar rather than losing the action. Writes work on
      mobile again. `flutter analyze` clean, **11 tests** (4 new, covering pending/confirmed/
      discarded/absent parsing).
      ⚠️ Always on; there is no "trust it" setting. Add one only if the tapping becomes tiresome.
- [x] **Flag a claimed action that has no receipt** (no-tool case, 2026-09-15). `ActionClaimDetector`
      (Application/Ai) is an English heuristic: a first-person or completed-passive change verb
      plus a domain noun (reminder, goal, planner, …), skipping questions, offers, proposals and
      negatives. When a finished reply matches and nothing was proposed this turn, `ChatService`
      runs **one corrective round**: the draft plus an "[Automatic check]" user turn naming the
      claim (never stored). The model then either calls the tool — which becomes a normal
      confirmation card — or rewrites the reply; `done.Text` replaces the streamed false claim.
      If the claim survives, or the corrective call fails, the reply is kept and stored with
      `ChatMessage.UnverifiedClaim` (migration `ChatMessageUnverifiedClaim`), sent as
      `done.unverifiedClaim` and in history. Web and mobile render an amber "This reply says a
      change was made, but nothing was saved" note; hands-free mobile speaks it too. Tests:
      `ActionClaimDetectorTests`, `ChatServiceClaimCheckTests`, mobile `chat_event_test` +
      `unverified_claim_note_test`. Live check 2026-09-15: qwen2.5 called `create_reminder` /
      `update_goal_status` properly in 5 tries, so the corrective path has not been seen live
      yet, and the web note has not been looked at in a browser. Honest replies cost nothing
      extra; a flagged one costs one more model call.
- [ ] **Claim and action disagree even though a tool fired** (split from the item above).
      2026-09-11, reading a real chat: the model said it was creating a sub-goal "as part of your
      Master AI goal", the tool **did** run, but it passed no `parentGoalId`, so the goal was
      created top-level. **The claim and the action disagreed even though a tool fired** — the
      no-tool case below is only half the problem. It also wrote after the user said
      *"Lets discuss before we add anything"*, i.e. acted without consent.
      A richer receipt is the defence (fix 1 above makes the period visible); showing parentage
      would close this specific case. (Writes now go through the confirmation card, so the
      consent half is covered; the parentage mismatch is not.)
- [ ] Widen coverage: `PlannerService`, `GoalService`, `DocumentService` (esp. the
      path-traversal guard), `PersonaToolRegistry` feature gating.
- [x] Fix the wrong upstream repo slug (2026-09-01) — `personaos/personaos` was hardcoded in
      four places but the real remote is `Purush0th/personaOS`, and `release.yml` pushes images
      to `ghcr.io/${{ github.repository_owner }}/…` = `ghcr.io/purush0th/*`. So the **shipped
      compose file pulled images that will never exist**, and the update banner could never fire
      (its 404 was being read as intended fail-silent behaviour). Fixed in `updates.service.ts`,
      `README.md` clone URL, and both `deploy/docker-compose.yml` image names. Also cleared
      post-SQLite doc rot: `docs/PRD.md` (arch diagram, backend/packaging bullets, and the
      Anthropic-only prose that predates provider-neutral) and stale "SQL Server" comments in
      `AppDbContext.cs` + `GoalService.cs`. Backend build 0-warning `-warnaserror`, 46 tests green.
      ✅ Angular build confirmed 2026-09-10 — Node 24.19.0 has since been installed, `npm ci` +
      `npx ng build` run clean on the host. (The note that Node was missing is now historical.)
- [ ] Personalization grep-check (no user data in source) before first public push
- [ ] Unpin Angular 20 → Angular latest. (Node half is **done**: 24.19.0 installed 2026-09-05,
      which is ≥ 24.15, so the constraint that forced the CLI pin is gone. Only the version bump
      itself remains, and it should be its own change — it is a framework upgrade, not a fix.)
- [x] **Conversation URLs use an opaque 8-char public id (2026-09-11).** `/chat/m74gjks6`.
      Owner's call: the title must not appear in the address bar (it leaks the subject into
      history, bookmarks and proxy logs) and neither should the sequential row id (it advertises
      how many conversations exist). `Conversation.PublicId` is generated by
      `Domain/Services/PublicIdGenerator` from a 31-symbol alphabet that omits the characters
      people misread when copying a link by hand (0/o, 1/l, i), using `RandomNumberGenerator`
      so ids are not guessable in sequence. Unique index; migration `ConversationPublicId`.
      `GET /api/chat/conversations/{idOrPublicId}` accepts either form, and the web client
      silently upgrades a numeric URL to the public id so the good form is what gets copied.
      ⚠️ **The generated migration was broken and had to be fixed by hand**: EF defaults every
      existing row to `""` and then creates a *unique* index, which fails outright on any
      instance with more than one conversation. Added a backfill
      (`UPDATE ... lower(hex(randomblob(4)))`) between the two. Verified on the owner's real
      database: **all 18 conversations got distinct ids**, integrity check passed. A copy of the
      pre-migration db sits in the volume at `/app/data/pre-publicid/` — delete when satisfied.
      Backfilled ids are hex (`0c311a15`); ids minted by the app use the safer alphabet.
      Verified live: click → `/chat/21e38e80`; legacy `/chat/14` resolves *and* rewrites itself
      to `/chat/0c311a15`; Back/Forward correct; unknown id → 404 → `/chat`; new thread →
      `/chat/m74gjks6`. 119 backend + 10 web specs green.
      ⚠️ The title-slug format below shipped for about an hour this same day and no longer
      resolves (`/chat/14-what-are-my-goals` → `/chat`). Never released, so nobody holds one.
      Superseded design: `/chat/<id>-<slugified title>`, e.g.
      `/chat/14-what-are-my-goals`. The **id leads** so a link survives a retitle or a title with
      nothing slugifiable (emoji/CJK) — the tail is for humans and is never used for lookup.
      New route `chat/:slug` alongside `chat`; the URL is the source of truth (the component
      loads from the route param, so Back/Forward work); opening a conversation navigates rather
      than mutating state; a new thread gets its slug via `replaceUrl` once the server auto-titles
      it. Dead id → falls back to `/chat`; slug with no id → URL normalised to `/chat`.
      Pure helper in `core/conversation-slug.ts` with 11 specs.
      **Found and fixed a real race while verifying:** `ngOnInit` awaited `refreshConversations()`
      *before* subscribing to `paramMap`. Send a message inside that window and the subscription
      fired late with no slug, resetting `conversationId` to null and **wiping the thread on
      screen mid-reply**. Now it subscribes first, and the null branch refuses to clear while
      `streaming()`. Reproduced (submit immediately after load) and confirmed fixed.
      Also repaired `app.spec.ts`, which was still the untouched CLI scaffold: it provided no
      `HttpClient` (BrandingService needs it) and asserted the long-deleted "Hello, PersonaOS.Web"
      text, so `ng test` had been red. **12 web specs green**, `ng build` clean.
      ✅ Delete shipped 2026-09-11 (see below), so throwaway threads can be cleared.
- [x] **Conversations can be deleted (2026-09-11).** `DELETE /api/chat/conversations/{idOrPublicId}`
      plus a ✕ on each sidebar row. Messages, receipts and any **unconfirmed proposals** cascade,
      so nothing is orphaned — a pending write must not outlive the thread that proposed it.
      Irreversible, so the row turns into an explicit "Delete this conversation? Delete / Cancel"
      rather than acting on one stray click; the ✕ stays hidden until hover/focus-within so it
      cannot be hit by accident. Deleting the thread that is **open** clears it and returns to
      `/chat`, since the URL would otherwise point at something gone.
      130 backend + 10 web specs green. Verified in the UI: Cancel deletes nothing (22 rows before
      and after), Delete removes exactly one row, and deleting the open thread redirected
      `/chat/00b13469` → `/chat` with the transcript cleared.
      ⚠️ Flutter has no conversation list at all, so nothing to add there yet.
- [x] **Web app is usable on a phone (2026-09-10).** `.topbar` was a no-wrap flex row with six
      nav links, brand and Sign out; on a narrow screen Reminders/Documents/Settings were simply
      clipped and the page scrolled sideways. Now the topbar wraps, and under 720px the nav takes
      its own full-width row that scrolls horizontally. Verified at a real 380px viewport:
      media query matches, `document.scrollWidth` no longer exceeds the viewport, and scrolling
      the nav reaches Settings. Update banner wraps too; content padding tightened on mobile.
- [x] **Web nav: chat history under Chat, and a light/dark switch (2026-09-23).** The conversation
      list moved out of the chat page into the nav, as an expandable group under Chat (state
      remembered per browser). Chat is the last nav item and its history takes the remaining
      height with its own scroll, so the pages above and the footer below never move. Settings
      and Sign out moved into an account menu in the footer (`mat-menu`, opens upward), labelled
      with the username read from the JWT's `unique_name` claim.
      `ConversationsStore` (core) is the one copy shared by nav and chat page; delete lives there
      and lazy-loads the confirm dialog so the dialog code stays out of the first load. The nav
      footer has a light/dark button (`ThemeService`): the device decides until the user picks,
      the pick is `data-theme` on `<html>` and is applied by an inline script in `index.html`
      before first paint. Also fixed `app.spec.ts`, failing since the Material commit (the brand
      only renders once signed in), and added a spec for the history list. 11/11 web tests.
- [x] **Web: finish the Material migration and fix field sizing (2026-09-23).** The owner
      spotted uneven fields on the backlog. Root cause: `_legacy-primitives.scss` was still
      imported globally, and its bare `label { display:block; margin:.9rem 0 }` rule hit
      Material's floating label (which is a `<label>`), pushing labels down inside every field.
      Deleted the file; migrated the setup wizard (now behind `@defer`). Four component
      stylesheets had kept pre-Material rules (undefined `--muted`/`--line`, hardcoded light
      greys, bare `input`/`textarea` rules): task, goal and sprint pages now share
      `board/item-page.scss`, and discussion and push-config were rewritten on `--mat-sys-*`
      tokens. One field size app-wide: 40px (button height) with 14px text, via
      `mat.form-field-overrides` in `styles.scss`; outline and dynamic hint spacing are the
      defaults in `app.config.ts`, so no template sets them. Backlog rows show priority and points
      as compact chips that open shared menus instead of full-size selects. Cards whose forms sat
      against the border got `mat-card-content`. Card subtitles use body text, not a second bold
      title. Audit command for leftovers:
      `grep -rnE "var\(--(muted|line|ink)|#[0-9a-f]{3,6}\b" --include=*.scss src/app` (empty).
- [x] **Chat renders Markdown** (2026-09-23). Replies showed `**bold**`, backticks and `-`
      lists literally. `shared/markdown.pipe.ts` renders them with `marked` (GFM, single line
      breaks kept), escapes any raw HTML the model writes instead of passing it through, opens
      links in a new tab with `noopener`, and still goes through Angular's sanitizer via
      `[innerHTML]`. Loaded with the chat route only, so the first download is unchanged. The
      user's own messages stay plain text. Spec covers rendering, escaping and link attributes.
- [x] **Chat page no longer scrolls twice on a phone** (2026-09-23). `chat.scss` sized the page
      `calc(100vh - 6rem)`, ignoring the 56px handset toolbar. The shell's content area is now a
      flex column on the chat route (`.mat-drawer-content:has(app-chat)` in `styles.scss`), and
      the page takes the height left over, so only the message list scrolls. Measured at 375x812
      and 1280x720: page scroll height equals its client height.
- [ ] **Web first-load budget.** `ng build` warns: the initial bundle is ~713 kB against a
      500 kB budget (578 kB after the Material commit, +8 kB for the history nav, +75 kB raw,
      ~18 kB gzipped, for `MatMenu` in the account footer, +58 kB for app-wide
      `MAT_FORM_FIELD_DEFAULT_OPTIONS` in `app.config.ts`, which pulls the form-field module into
      the main chunk; the setup wizard moving behind `@defer` saved ~7 kB). Candidates: self-host
      a Material Symbols subset, check what the shell pulls eagerly (`MatSidenav`, `MatList`,
      `MatToolbar`), then raise the budget deliberately if what is left is the real floor.
