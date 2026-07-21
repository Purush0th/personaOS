# PersonaOS — Product Requirements & Architecture

> **PersonaOS** is an open-source, self-hosted, single-user personal AI assistant.
> You run it on your own machine, bring your own Anthropic API key, give your
> assistant any nickname you like — and it manages your goals, daily planner,
> reminders, documents, and a Claude-powered chat/voice interface.

**Document map** — each file has one job:

| Doc | Job |
|---|---|
| this PRD | *What* we're building and *why* — product rules, architecture decisions, phase scope. Changes rarely. |
| [TODO.md](../TODO.md) | *Where we are* — live task board, claims, per-task status. The only source of progress truth. |
| [CLAUDE.md](../CLAUDE.md) | *How* to work on the code — layout, build commands, layer rules, session conventions. |

## 1. Product vision

A **truly private** personal assistant:

- **Self-hosted** — runs on your PC/VM, reachable only over your private Tailscale network (public/cloud hosting is a later option).
- **Yours** — you name the assistant anything ("Siri", "Jarvis", …) and tune its persona/tone. Your data never leaves your machine except for calls to the Anthropic API with your own key.
- **Open source** — Apache-2.0. No paid tier, no license keys, no phone-home.

### Brand model (important)

| Thing | Rule |
|---|---|
| Product brand | **"PersonaOS"** — fixed everywhere: app stores, Docker images, GitHub, namespaces, marketing. Never changes. |
| Assistant nickname | Free-form, chosen at first-run setup. Only affects how the assistant refers to itself in chat/UI. |
| Visual identity | Fixed PersonaOS theme/logo across all installs. No per-install theming. |
| Persona/tone | Editable per install (formal / friendly / concise …) via a system-prompt template. |

Nothing user-chosen (nickname, persona, API key, personal facts) is ever hardcoded — it all lives in runtime config/DB (`InstanceConfig`).

## 2. System architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  User devices                                                   │
│   ├── Flutter mobile app  (chat, goals, planner, voice, remind) │
│   └── Angular dashboard   (web UI + first-run Setup Wizard)     │
│                    │  REST + SSE (JWT)                          │
│                    ▼                                            │
│  ASP.NET Core WebAPI  ──────────────►  Anthropic Messages API   │
│   ├── Claude agentic loop (native tool-use, no MCP)             │
│   ├── Domain services (goals/planner/reminders/docs)            │
│   ├── EF Core ──► SQL Server                                    │
│   ├── Filesystem docs storage                                   │
│   └── FCM push (reminders)                                      │
└─────────────────────────────────────────────────────────────────┘
```

Key decisions:

- **Backend**: ASP.NET Core (.NET), C#. SQL Server via EF Core; migrations auto-apply on startup.
  **Clean architecture** (`src/backend/`): `Api` (controllers + composition root) → `Application`
  (use cases + ports) ← `Infrastructure` (EF Core, Anthropic SDK, Data Protection, JWT adapters);
  `Application` → `Domain` (entities). Business logic never touches vendor SDKs directly —
  the Anthropic client sits behind an `IAiMessageStreamer` port, keeping the model provider
  swappable and use cases unit-testable. Layer rules: [CLAUDE.md](../CLAUDE.md).
- **Claude integration**: **native Anthropic tool-use** — capabilities are declared in the Messages API `tools` param and dispatched to in-process domain services. No MCP in v1 (the app is the only tool consumer); tools are thin wrappers over the same services the REST endpoints use, so an MCP façade stays a small additive change.
- **Chat**: streaming via SSE; system prompt = nickname + persona template + user profile + active-goals summary; history windowing + rolling summarization; token usage logged per request.
- **Auth**: JWT with a single admin username/password (created in the Setup Wizard).
- **Secrets**: the user's Anthropic API key is encrypted at rest (ASP.NET Data Protection); JWT signing key via env/secrets. Nothing committed.
- **Notifications**: FCM push so server-created reminders reach a closed device; local notifications for foreground display.
- **Time**: all timestamps UTC; one configured user time zone drives display/scheduling.
- **Feature toggles**: each module (docs, voice, proactive…) can be disabled per install; UI hides and endpoints short-circuit.

## 3. Setup & personalization

First-run **Setup Wizard** (dashboard-served, CLI fallback) collects:

1. Assistant nickname + persona/tone
2. Admin username/password
3. Anthropic API key (BYO) + Claude model choice
4. Feature toggles
5. Time zone

Writes the singleton `InstanceConfig`, seeds the admin user, runs migrations. Re-runnable behind admin auth.

`GET /api/branding` (unauthenticated, safe subset) returns: nickname, configured flag, enabled features, API version, min supported client version — used by clients to theme, gate modules, and show a "please update" screen when too old.

## 4. Distribution & updates

- **Packaging**: Docker Compose bundle (API + SQL Server + dashboard host). `docker compose up` → Setup Wizard → working assistant.
- **Channels**: public GitHub repo, versioned public images (GHCR), GitHub Releases carrying compose file + Android APK + docs. Mobile also published to app stores under the PersonaOS brand.
- **Updates**: `docker compose pull && docker compose up -d`; migrations auto-run; config/data preserved; previous tag pinnable for rollback. In-dashboard "update available" banner (checks GitHub Releases; notify-only, never unattended auto-update). API stays backward-compatible within a major version and advertises `minSupportedClientVersion`.

## 5. Roadmap (phases)

> Phase *scope* is defined here; live per-task **status and claims live in [TODO.md](../TODO.md)**
> (kept current by every working session — this table deliberately carries no status column).

| Phase | Scope |
|---|---|
| 0 | Solution scaffolding, EF Core + `InstanceConfig`, JWT auth, `/api/branding`, system-prompt builder |
| 0.5 | Setup Wizard (admin + config seeding), feature-toggle middleware |
| 1 | Chat: SSE streaming, agentic loop, history windowing, client-version gate, Flutter chat screen |
| 2 | Goals: Y/Q/M hierarchy (`ParentGoalId`), progress rollup, CRUD + Claude tools |
| 3 | Daily planner: CRUD + tools, day view |
| 4 | Reminders + FCM push, device tokens, conversational reminder creation |
| 5 | Docs: filesystem upload/download, `get_document` tool (no RAG) |
| 6 | Voice: push-to-talk STT + TTS in chat |
| 7 | Dashboard feature parity |
| 8 | Proactive scheduler (morning brief, goal reviews) — feature-toggled |
| 9 | Packaging: compose bundle, release pipeline, update notifier, install/upgrade tests, docs |

### Explicitly deferred (not v1)

Cloud/public-HTTP hosting · RAG over docs · MCP façade · wake-word voice ·
per-fork store builds · multi-user/multi-tenant · any license/activation gating.

## 6. Verification bar

- Every phase ends with a manual end-to-end check through the real UI.
- `dotnet test` green; build at 0 warnings (transitive CVEs pinned away).
- Personalization check: no user data in source/images; two installs with different nicknames diverge only in config.
- Feature-toggle check: disabled module vanishes from UI and its endpoints refuse.
- Upgrade check: N → N+1 migrates automatically, data + config survive, stale clients hit the version gate.
