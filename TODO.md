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
- [ ] Firebase project setup (FCM Android; APNs via FCM for iOS) — **owner task**, needs a
      Firebase account. Backend is complete behind the `IPushSender` port; drop in an FCM
      adapter and swap the `NullPushSender` registration in `Infrastructure/DependencyInjection`.
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
- [ ] **Re-run the clean-machine install + upgrade test on the SQLite stack.** The verification
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
      ⚠️ **The Angular build was NOT run — Node is not installed on this machine any more**
      (`where node` empty, nothing on PATH). The web change is a one-line string constant, but a
      session with Node should run `npx ng build` to confirm.
- [ ] Personalization grep-check (no user data in source) before first public push
- [ ] Node upgrade to ≥ 24.15 → unpin Angular 20 → Angular latest
