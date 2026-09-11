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
      ⚠️ **Flutter shows no card**, so a mobile user sees the reply but cannot confirm — writes
      are effectively blocked on mobile until the client renders `pendingActions`. Mobile-partition
      follow-up, and the most urgent one on the board.
      ⚠️ Always on; there is no "trust it" setting. Add one only if the tapping becomes tiresome.
- [ ] **Flag a claimed action that has no receipt** — now with a second, worse variant.
      2026-09-11, reading a real chat: the model said it was creating a sub-goal "as part of your
      Master AI goal", the tool **did** run, but it passed no `parentGoalId`, so the goal was
      created top-level. **The claim and the action disagreed even though a tool fired** — the
      no-tool case below is only half the problem. It also wrote after the user said
      *"Lets discuss before we add anything"*, i.e. acted without consent.
      A richer receipt is the defence (fix 1 above makes the period visible); showing parentage
      would close this specific case. The original no-tool case: the 2026-09-11 E2E run caught qwen2.5 doing
      it again: asked "Remind me to submit the report at 7pm today", it replied *"I've set a
      reminder to remind you to submit the report at 19:00 today."* and **never called
      `create_reminder`** — the reminders table stayed empty. The tool is fine; an explicit "use
      the create_reminder tool" fired it and stored 19:00 local → 13:30 UTC correctly. So this is
      the model ignoring the prompt rule, which prompting cannot fix.
      Receipts worked exactly as designed — no tool ran, so no receipt appeared. **But absence is
      a weak signal**: a user reading a confident "I've set a reminder" will not notice that
      nothing was rendered beneath it. `ChatService` already knows whether any *mutating* tool ran
      this turn, so it can detect claim-without-receipt and either force one corrective round or
      mark the reply. This is the last meaningful hallucination gap and it now has concrete
      evidence behind it.
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
