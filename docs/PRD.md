# PersonaOS — Product Requirements & Architecture

> **PersonaOS** is an open-source, self-hosted, single-user personal AI assistant.
> You run it on your own machine, bring your own AI provider key (or point it at a
> local model), give your assistant any nickname you like — and it manages your goals,
> a personal sprint board, a daily planner, reminders and documents through chat and voice.

> **Which doc does what:** this PRD = *what & why*; [TODO.md](../TODO.md) = *live progress*
> (the only status source); [CLAUDE.md](../CLAUDE.md) = *how to work* + the session protocol
> every Claude Code session follows. The sprint board's detailed rules are in
> [sprint-board.md](sprint-board.md); backup and restore in [BACKUP.md](BACKUP.md).
>
> Last revised 2026-09-26, to match what is built.

## 1. Product vision

A **truly private** personal assistant:

- **Self-hosted** — runs on your PC, VM or Raspberry Pi, reachable only over your private
  network (Tailscale recommended). Public or cloud hosting is a later option.
- **Yours** — you name the assistant anything ("Juno", "Friday", "Jarvis", …) and tune its
  persona and tone. Your data never leaves your machine except for calls to the AI provider you
  configure — and with a local model (Ollama), not even those.
- **Open source** — Apache-2.0. No paid tier, no licence keys, no phone-home.
- **Trustworthy with small models** — the assistant must be safe to use on a free 3B local
  model: it never changes your data without your say-so, and it shows what it actually did.

### Brand model (important)

| Thing | Rule |
|---|---|
| Product brand | **"PersonaOS"** — fixed everywhere: app stores, Docker images, GitHub, namespaces, marketing. Never changes. |
| Assistant nickname | Free-form, chosen at first-run setup. Only affects how the assistant refers to itself in chat and UI. |
| Visual identity | Fixed PersonaOS theme and logo across all installs. No per-install theming. Light and dark follow the device unless the user picks one. |
| Persona/tone | Editable per install (formal / friendly / concise …), appended to the system prompt. |

Nothing user-chosen (nickname, persona, API key, personal facts) is ever hardcoded — it all lives
in runtime config and the database (`InstanceConfig`, `UserProfile`).

## 2. Product principles

These rules hold across every module and every client.

1. **Nothing writes without the user's confirmation.** Every change the assistant wants to make
   becomes a card that says exactly what will happen, in the user's terms ("Delete TASK-7 “File
   taxes”"), with **Confirm** and **Discard**. Reads never need a card. This is enforced in code,
   per tool, not by the prompt.
2. **Show what was done, not what the model says was done.** Each executed tool leaves a receipt
   under the reply, built from the tool's own result ("Created goal — Learn Rust · month").
3. **One behavioural contract for every provider.** Prompt, tools and guards live in the
   Application layer and are identical for Anthropic, OpenAI-compatible and Ollama. Adapters
   translate wire formats only.
4. **Keys, not ids.** Every goal, task and sprint has a human key (`GOAL-3`, `TASK-12`,
   `SPRINT-2`). The assistant and the UI use keys; a key is also the item's link.
5. **Plain, short wording.** A label names the action in one word where one word is clear
   ("Edit", "Delete", "Open"). Anything not yet set reads "–". Replies are short, one line per
   item, and never name internal tools or describe fields the data does not have.
6. **Manual beats automatic for the user's plan.** Sprints start and finish when the user says;
   nothing on a timer changes the plan. Timers only nudge.

## 3. Modules

Each module can be switched off per install; its UI disappears, its endpoints refuse
(`403 feature_disabled`), and the assistant loses its tools and is told it is off.

| Module | Default | What it does |
|---|---|---|
| Chat | always on | Streaming conversation with the assistant; history, receipts and confirmation cards |
| `goals` | on | Yearly, quarterly and monthly goals with dates and progress |
| `board` | on | A one-person Scrum board: tasks, value points, weekly sprints, backlog, reports |
| `planner` | on | A day-by-day list; items can come from the sprint |
| `reminders` | on | Scheduled alarms and push notifications, created in chat or by hand |
| `docs` | on | Upload files; the assistant can read text files by name |
| `voice` | on | Push-to-talk and hands-free voice on the phone, with read-back |
| `proactive` | **off** | Morning brief and evening rollup pushed to the phone |

### 3.1 Chat and the assistant

- Replies stream over SSE and render Markdown (raw HTML is escaped). The user's messages stay
  plain text.
- Conversations have opaque 8-character addresses (`/chat/m74gjks6`), never the title or a row
  number. They can be deleted, which also drops any unconfirmed proposals.
- The system prompt carries the nickname, persona, the user's **About you** notes (at most 2,000
  characters; the `remember_about_user` tool adds to them, behind a card), today's date and time
  zone, and live context: active goals, the running sprint, today's planner, upcoming reminders.
- Long conversations fold older turns into a rolling summary; history is budgeted by tokens so
  the prompt always fits the model's context.
- **Guards** run on every tool call and every reply, and each firing is logged and counted:
  - tool calls — unwrap arguments sent inside an envelope, answer repeated calls from the first
    result, validate a change before it becomes a card (a card that cannot work is never shown);
  - replies — strip leaked reasoning and tool-call text, drop sentences that name a tool or ask
    the user to type "yes" above a card with buttons, catch a claim that something was done when
    it was not (one corrective round, then an amber "nothing was saved" note), and flag item keys
    the reply mentions that do not exist.
- Tools (native tool use, not MCP): goals (`get_goals`, `create_goal`, `update_goal_status`,
  `delete_goal`), board (`get_board`, `get_plan`, `get_sprint_report`, `create_sprint`,
  `start_sprint`, `complete_sprint`, `create_task`, `update_task`, `move_task`, `delete_task`,
  `add_comment`), planner (`get_planner`, `add_planner_item`, `update_planner_item_status`,
  `move_planner_item`), reminders (`get_reminders`, `create_reminder`, `cancel_reminder`),
  documents (`list_documents`, `read_document`) and `remember_about_user`. The UI names them in
  words ("Reading goals…", "Created goal"), never by their tool name.

### 3.2 Goals

- A goal is top level (goals do not nest; what used to be a sub-goal is a task under it). It has
  a key, title, optional description, priority, status (active, completed, dropped), and a type:
  year, quarter or month.
- **Dates:** any start, future included. A month runs at most 31 days, a quarter at most 90, and
  a year ends on 31 December of its start year. With no end given, the end defaults to the
  longest the type allows. A goal still active after its end shows **Overdue**.
- **Progress:** from its tasks when it has any — done points ÷ estimated points, or done tasks ÷
  tasks when none are estimated. A goal with no tasks is set by hand with **Update progress**
  (a slider in 5% steps, in a popup from the goal's menu or its page). The bar on the page only
  reads out.
- The goals page lists each goal as a lane with its tasks, a menu (Update progress, Complete,
  Drop or Reopen, Delete) and Create task. Each goal has its own page at `/goals/GOAL-3` with
  description, tasks, comments and attachments. Goals stand apart from the board: the board
  shows a goal only as a chip on its tasks.
- Deleting a goal keeps its tasks and planner items, unlinked.

### 3.3 Board (personal Scrum)

Full rules: [sprint-board.md](sprint-board.md). In short:

- **Tasks** (`TASK-n`) may belong to a goal or to none. Statuses read **Backlog**, **To do**, **In
  progress**, **Done**. Tasks have priority, description, comments and attachments (25 MB each).
- **Value points** are Fibonacci only (1, 2, 3, 5, 8, 13, 21) or unestimated ("–"); 13 and 21
  suggest a split.
- **Sprints** (`SPRINT-n`, optional name) are created, started and completed by hand, one running
  at a time. The user picks the **start day**; the end is always the first Sunday 18:00 after
  it, shown read-only. A sprint cannot be started before its start day. Completing a sprint
  freezes its totals and carries unfinished work to the next planned sprint or the backlog.
- **Scope changes** — adding work to a running sprint or taking unfinished work out — need an
  explicit acknowledgement, enforced by the API, and are reported as added or removed.
- **Views** are tabs, as in Jira: **Sprint** (`/board`, To do | In progress | Done, drag from
  anywhere, soft limit of 3 in progress), **Backlog** (`/board/backlog`, sprint sections and the
  backlog, drag between them or use Move…) and **Reports** (`/board/reports`: find any started
  sprint; tiles for tasks done, points completed of committed, added and removed, days left;
  tasks by status and by priority; the **Burndown**; a sortable task table; velocity over recent
  sprints). A task opens in a dialog over the page (`?task=TASK-3`) or on its own page
  (`/board/tasks/TASK-3`); sprints have `/board/sprints/SPRINT-2`.
- A push nudge on **Sunday 19:00** says how the sprint stands; it never changes anything.

### 3.4 Planner

- The planner is the **daily** list; the board is the **weekly** one. Items have a date, an
  optional time, a status (planned, done, skipped) and optionally a linked sprint task.
- Marking a planner item done does not finish its task; the user moves the card.

### 3.5 Reminders

- A reminder is stored in UTC and shown in the user's time zone; "tomorrow at 9" is resolved
  against the configured zone.
- On the phone a reminder is an **alarm**: exact, full screen over the lock screen, with Snooze
  (10 minutes) and Dismiss. The phone keeps its alarms in step with the server whenever it opens
  or changes a reminder; a server push is the fallback.
- **Push is bring-your-own Firebase:** the admin uploads their own project's two files in web
  Settings. The app carries no Firebase config; the service-account key is encrypted at rest and
  never returned by any endpoint.

### 3.6 Documents

- Upload, search, download and delete files (25 MB each). Files are stored under
  server-generated names; the user's file name is metadata only.
- The assistant can list documents and read **text** formats by name (up to 20,000 characters).
  PDFs, images and Office files are stored but not read. No RAG.

### 3.7 Voice (phone)

- Push-to-talk and a hands-free mode, with spoken read-back. Hands-free pauses while a
  confirmation card waits and resumes when it is resolved.

### 3.8 Proactive (off by default)

- A **morning brief** (default 07:30) and **evening rollup** (default 21:00), built from the
  planner, reminders, goals and the sprint, pushed to the phone and filed as a chat conversation
  so the user can reply to it.
- Briefs are composed without the model, so a provider outage never silences them. Optionally
  the model rewords them; the rewording is used only if every title, time and number survives.
- A brief more than 3 hours late is skipped rather than sent stale.

## 4. System architecture

```
┌──────────────────────────────────────────────────────────────────────┐
│  User devices                                                        │
│   ├── Flutter app (Android)  chat, voice, goals, board, planner,    │
│   │                          reminders + alarms, documents, settings │
│   └── Angular web app        everything above except voice/alarms,   │
│                              plus the Setup Wizard and admin Settings │
│                     │  REST + SSE (JWT)                              │
│                     ▼                                                │
│  ASP.NET Core API  ─────────────────────►  AI provider (pluggable)   │
│   ├── Chat: prompt fragments, tool loop, guard pipelines             │
│   ├── Domain services: goals, board, planner, reminders, docs        │
│   ├── EF Core ──► SQLite (embedded, WAL)                             │
│   ├── Filesystem storage: documents + attachments                    │
│   ├── Schedulers: reminders, sprint nudge, proactive briefs, backup  │
│   └── FCM push (bring-your-own Firebase)                             │
└──────────────────────────────────────────────────────────────────────┘
```

Key decisions:

- **Backend**: ASP.NET Core (.NET 10), C#. **Embedded SQLite** (WAL) via EF Core; migrations
  auto-apply on startup. **Clean architecture** (`src/backend/`): `Api` (controllers +
  composition root) → `Application` (use cases + ports) ← `Infrastructure` (EF Core, provider
  adapters, Data Protection, JWT, push, file storage); `Application` → `Domain` (entities and pure
  rules). Business logic never touches vendor SDKs. Layer rules: [CLAUDE.md](../CLAUDE.md).
- **AI provider (pluggable)**: chat goes through the neutral `IAiMessageStreamer` port. Three
  adapters ship: **Anthropic** (official C# SDK), **OpenAI-compatible** (OpenAI, Groq,
  OpenRouter, LM Studio, … via a base URL) and **Ollama** (its own API, so it can set the context
  size and switch a thinking model's reasoning off). A new provider is one more adapter;
  Application and Domain never change.
- **Model profiles**: per provider and model name — context size, whether it thinks, tool-round
  budget, prompt variant and idle timeout. Small models (1.5B and under) get compact prompt
  fragments and fewer tool rounds. A context size set in Settings wins. A model that goes silent
  ends the turn with an explanation instead of a spinner.
- **Prompt as files**: the system prompt is built from small `.prompty` fragments (identity,
  tool rules, product, modules, clock, goals, board, planner, reminders, …) embedded in the
  server. A file of the same name in the data volume's `prompts/` folder overrides one, read per
  request with no restart; a broken override falls back to the default with a warning.
- **Tool results are trimmed** before the model sees them (no nulls, empty lists or internal
  fields), because a small model describes whatever it is given.
- **Auth**: JWT for a single admin user created in the Setup Wizard; username is
  case-insensitive. The phone can sign in with a fingerprint (credentials in Keystore-backed
  storage).
- **Secrets**: the provider API key and the Firebase service-account key are encrypted at rest
  with ASP.NET Data Protection, each under its own purpose string. The JWT signing key comes from
  the environment. Nothing is committed. Keyless local providers need no key.
- **Time**: all timestamps UTC; one configured time zone drives display and scheduling.
- **Feature toggles**: `[RequireFeature]` on endpoints, branding tells clients what is on.

## 5. Setup & personalization

First-run **Setup Wizard** (web) collects:

1. Assistant nickname + persona/tone
2. Admin username and password
3. AI provider (Anthropic, Ollama, OpenAI-compatible), base URL, model and key
4. Modules
5. Time zone

It writes the singleton `InstanceConfig`, seeds the admin user and runs migrations. **Settings**
(web, and most of it on the phone) changes all of this later, plus: context size, About you,
Firebase push files, brief times, whether the model words the briefs, and a **Test
connection** button that checks the provider, model and key before saving.

`GET /api/branding` (unauthenticated, safe subset) returns nickname, configured flag, enabled
modules, API version, minimum supported client version and the project repository — used by
clients to gate modules and to show "please update" when too old.

## 6. Clients

- **Web** (Angular 22, Angular Material): side navigation — Goals, Board, Planner, Reminders,
  Documents, Chat with its history underneath — and an account menu with Settings and Sign out.
  Usable on a phone browser. First download kept under a 750 kB budget.
- **Mobile** (Flutter, Android only for now): chat with voice, conversations, goals, board,
  planner, reminders with alarms, documents and settings. The phone has no item pages or
  comments yet; rows open sheets.
- **Addresses** (web):

  | Page | Address |
  |---|---|
  | Chat | `/chat`, `/chat/<id>` |
  | Goals | `/goals`, `/goals/GOAL-3` |
  | Board | `/board`, `/board/backlog`, `/board/reports?sprint=SPRINT-2` |
  | Task, sprint | `/board/tasks/TASK-7`, `/board/sprints/SPRINT-2` |
  | Planner, reminders, documents, settings | `/planner`, `/reminders`, `/documents`, `/settings` |

## 7. Distribution, updates and data

- **Packaging**: Docker Compose bundle — the API (with the embedded database) and an nginx web
  container. Only the web container publishes a port, on loopback by default. Images build for
  x86-64 and ARM. `docker compose up` → Setup Wizard → working assistant.
- **Channels**: public GitHub repo, versioned images on GHCR, GitHub Releases carrying the
  compose file, Android APK and docs. Store listings later, under the PersonaOS brand.
- **Updates**: `docker compose pull && docker compose up -d`; migrations run automatically; data
  and settings are preserved; a previous version can be pinned for rollback. The web app shows
  an "update available" banner (GitHub Releases; notify only, never automatic). The API stays
  backward-compatible within a major version and advertises `minSupportedClientVersion`.
- **Data**: everything lives in one Docker volume — the database, document files and the Data
  Protection keyring that decrypts the stored keys, so it is backed up as a unit. A nightly
  snapshot is built in; Litestream replication off the host is optional. See
  [BACKUP.md](BACKUP.md).
- **Release gate**: the first public release waits until the features are mature and the
  assistant's reliability on local models is acceptable to the owner. Private Android alphas for
  the owner's phone are separate from that.

## 8. Roadmap (phases)

> Phase *scope* is defined here; live per-task **status and claims live in [TODO.md](../TODO.md)**
> (this table deliberately carries no status column).

| Phase | Scope |
|---|---|
| 0 | Solution scaffolding, EF Core + `InstanceConfig`, JWT auth, `/api/branding`, system-prompt builder |
| 0.5 | Setup Wizard (admin + config seeding), feature-toggle middleware |
| 1 | Chat: SSE streaming, tool loop, history, client-version gate, provider-neutral adapters |
| 2 | Goals: CRUD + tools, dates by type, progress |
| 2b | Sprint board: tasks, value points, manual sprints, backlog, item pages, comments and attachments, reports |
| 3 | Daily planner: CRUD + tools, day view, tasks from the sprint |
| 4 | Reminders: alarms, bring-your-own FCM push, conversational creation |
| 5 | Documents: filesystem upload/download, read tools (no RAG) |
| 6 | Voice: push-to-talk, hands-free, read-back |
| 7 | Web parity with the phone |
| 8 | Proactive scheduler: morning brief, evening rollup, sprint nudge |
| 9 | Packaging: compose bundle, release pipeline, update notifier, install/upgrade tests, docs |
| AI | Assistant reliability: confirmation cards, receipts, guard pipelines, prompt files, model profiles, model scoring (`scripts/model-check.ps1`) |

### Explicitly deferred (not v1)

Cloud or public-HTTP hosting · RAG over documents · MCP façade · wake-word voice · iOS app ·
sub-tasks, multiple boards, custom columns or sprint lengths, time tracking, labels ·
multi-user or multi-tenant · per-fork store builds · any licence or activation gating.

### Open product questions

- **Push for non-technical users.** Bring-your-own Firebase serves technical self-hosters. A
  project-run push relay, or UnifiedPush as an option, would serve everyone else. Undecided.
- **Recommended local model.** `qwen2.5:3b-instruct` scores 8/8 on the model check but misreads
  numbers it is given; whether the documented minimum moves to a 7B model is to be decided after
  testing on a GPU.
- **Password on the phone.** Access tokens last 12 hours with no refresh, so the phone stores
  credentials behind the fingerprint. Refresh tokens would remove the password from the phone.
- **What goes public.** `CLAUDE.md` and `TODO.md` are working documents that name the owner and
  their setup; decide what to keep before the repository goes public.

## 9. Verification bar

- Every change is checked through the real UI; a UI change counts as verified only after looking
  at it (screenshots, with scrollbars shown), on desktop and phone widths, light and dark.
- `dotnet test` green; build at 0 warnings (transitive CVEs pinned away); web and Flutter tests
  green; `flutter analyze` clean on every Dart change.
- Assistant changes are checked against a real local model, and `scripts/model-check.ps1` scores
  the tool scenarios end to end.
- Personalization check: no user data in source or images; two installs with different nicknames
  differ only in config.
- Feature-toggle check: a disabled module vanishes from the UI, its endpoints refuse, and the
  assistant loses its tools.
- Upgrade check: N → N+1 migrates automatically, data, config and encrypted keys survive, stale
  clients hit the version gate.
