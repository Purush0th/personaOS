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

- [ ] `Goals` table: self-referencing hierarchy (`ParentGoalId`), period type, status, progress
- [ ] Progress rollup in domain service (parent derives from children)
- [ ] REST CRUD endpoints (`/api/goals`) behind `RequireFeature("goals")`
- [ ] **Tool registry** in `ChatService` (`IPersonaTool`: name/description/schema/execute)
- [ ] First Claude tools: `get_goals`, `create_goal`, `update_goal_status`, `link_goal`
- [ ] Feature-toggle functional test (disable goals → 403 + hidden in branding)
- [ ] Flutter Goals screen (hierarchy view)
- [ ] Dashboard Goals view

## Phase 3 — Daily Planner

- [ ] `PlannerItems` table (date, task, optional Goal link)
- [ ] REST CRUD + Claude tools (mirror Phase 2 pattern)
- [ ] Flutter planner day view; dashboard planner view

## Phase 4 — Reminders + FCM push

- [ ] `Reminders` + `DeviceTokens` tables
- [ ] Firebase project setup (FCM Android; APNs via FCM for iOS)
- [ ] Device-token registration on login; server hosted-service sends push at due time
- [ ] Claude tool: conversational reminder creation (resolve relative times vs configured TZ)
- [ ] `flutter_local_notifications` for foreground display

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

- [ ] **Initial git commit** (owner asked to hold until requested)
- [ ] Backend unit tests project (`PersonaOS.Tests`) — none exist yet
- [ ] Personalization grep-check (no user data in source) before first public push
- [ ] Node upgrade to ≥ 24.15 → unpin Angular 20 → Angular latest
