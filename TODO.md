# PersonaOS — Task Board

> Shared across all Claude Code sessions. **Claim a task before starting it** (put your
> session name/date in the `claimed` column), tick it when done, and add newly discovered
> work to the right phase. Keep this file truthful — it is the single source of progress.
> Full product spec: [docs/PRD.md](docs/PRD.md).

## How to work in parallel sessions

- **Partition by area** — the safest split is one session per column: `backend` (.NET),
  `mobile` (Flutter), `dashboard` (Angular), `infra` (Docker/CI). Cross-area tasks go solo.
- **Claim before you code**: edit this file first (`[ ]` → `[~] (claimed: <session>, <date>)`).
- Prefer `git worktree` / separate branches when two sessions touch the same project.
- After finishing: mark `[x]`, note anything a follow-up session must know.

Legend: `[ ]` open · `[~]` in progress (claimed) · `[x]` done · `[-]` dropped

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
- [ ] **Live-key smoke test** — set a real Anthropic key (`PUT /api/setup`), confirm a real
      streamed reply end-to-end (blocked on: owner's API key)
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
- [ ] Flutter Goals screen (hierarchy view)
- [ ] Dashboard Goals view

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
- [ ] Flutter planner day view; dashboard planner view

## Phase 4 — Reminders + FCM push

- [x] `Reminders` + `DeviceTokens` tables
      — `Reminder.cs` (UTC `DueAtUtc`, pending/delivered/cancelled/failed, attempt counter,
      optional Goal + PlannerItem links via `SetNull`) and `DeviceToken.cs` (unique token,
      android/ios). Migration `20260723052926_RemindersAndDevices`, applied to dev DB.
- [ ] Firebase project setup (FCM Android; APNs via FCM for iOS) — **owner task**, needs a
      Firebase account. Backend is complete behind the `IPushSender` port; drop in an FCM
      adapter and swap the `NullPushSender` registration in `Infrastructure/DependencyInjection`.
- [x] Device-token registration on login; server hosted-service sends push at due time
      — `POST/DELETE /api/reminders/devices` (idempotent per token, verified); dispatcher
      `ReminderDispatcher` + `ReminderDispatchService` (30s `PeriodicTimer`, per-pass scope,
      never crashes the host). Prunes provider-rejected tokens; gives up after 5 attempts.
      Verified: with push unconfigured, due reminders stay **pending** (not failed) so they
      fire once FCM is added; cancelled reminders are skipped.
      ⚠️ **Untested: the success path** (mark delivered / prune invalid token / retry-then-fail)
      — needs either FCM or the test project below with a fake `IPushSender`.
- [x] Claude tool: conversational reminder creation (resolve relative times vs configured TZ)
      — tools `get_reminders`, `create_reminder`, `cancel_reminder`. `create_reminder` takes
      `dueAtLocal` (user wall-clock); the system prompt supplies their zone + today's date so
      the model resolves "tomorrow 9am" itself. Verified: 09:00 Asia/Calcutta → 03:30 UTC and
      back. New shared `UserClock` helper handles conversion incl. DST gaps.
      System prompt now also lists the next 5 upcoming reminders.
      **End-to-end tool call still needs the live-key smoke test** (same blocker as Phases 1–3).
- [ ] `flutter_local_notifications` for foreground display (mobile session)

## Phase 5 — Docs storage

- [ ] `POST/GET /api/documents` — filesystem storage under `data/docs-storage/`
- [ ] Claude tool `get_document(name)` (retrieval by name; NO RAG in v1)

## Phase 6 — Voice (push-to-talk)

- [ ] Flutter mic button → `speech_to_text` → send as chat message
- [ ] `flutter_tts` read-back toggle

## Phase 7 — Dashboard parity

- [ ] Dashboard chat (reuse SSE endpoint)
- [ ] Goals / Planner / Reminders views
- [ ] Settings page (edit nickname/persona/model/key — wraps `PUT /api/setup`)

## Phase 8 — Proactive scheduler (feature-toggled)

- [ ] Background scheduler (BackgroundService or Quartz.NET)
- [ ] Morning brief via FCM; goal-review prompts; nightly rollup

## Phase 9 — Packaging + open-source release

- [ ] Dockerfiles + `deploy/docker-compose.yml` (API + SQL Server + dashboard) + `.env.example`
- [ ] `LICENSE` (Apache-2.0) + `NOTICE` + `README` + `CONTRIBUTING`
- [ ] Release pipeline: tag → GHCR images (`:x.y.z` + `:latest`) → GitHub Release (compose + APK)
- [ ] In-dashboard "update available" banner (GitHub Releases API; notify-only)
- [ ] Clean-machine install test; upgrade test (N → N+1, data survives, version gate works)
- [ ] Self-hoster docs (install, Tailscale, BYO key, update)

## Cross-cutting / anytime

- [x] Initial git commit (`0ca5bbb`, 2026-07-20)
- [ ] Backend unit tests project (`PersonaOS.Tests`) — none exist yet. **First target:**
      `ReminderDispatcher` with a fake `IPushSender` (delivery success, invalid-token pruning,
      retry-then-fail) — the one Phase 4 path that live testing can't reach without FCM.
      Also cheap now that ports exist: `ChatService` tool loop with a fake `IAiMessageStreamer`.
- [ ] Personalization grep-check (no user data in source) before first public push
- [ ] Node upgrade to ≥ 24.15 → unpin Angular 20 → Angular latest
