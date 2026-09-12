# PersonaOS — session context

Open-source (Apache-2.0), self-hosted, single-user personal AI assistant.
.NET 10 API + SQLite · Flutter mobile · Angular 20 web app · pluggable AI providers
(Anthropic + any OpenAI-compatible endpoint) behind a neutral streaming port.

## Every session: working agreement

This file is the **only doc auto-loaded into every Claude Code session** — so it is the
authoritative source for *how to work here*. Any session, fresh or parallel, must follow this
without needing a prior conversation. Do not rely on chat history for project state; rely on
the files below.

**Which doc does what (do not mix these up):**

| Doc | Role |
|---|---|
| `CLAUDE.md` (this file) | **How** to work: layout, layer rules, build commands, conventions. Auto-loaded. |
| `TODO.md` | **Where we are**: the single source of progress truth — live task board, claims, status. |
| `docs/PRD.md` | **What & why**: product rules, architecture decisions, phase scope. Changes rarely. |

**Session protocol (every session, in order):**
1. Read [TODO.md](TODO.md). Progress lives *only* there — never record status in the PRD or code comments.
2. **Claim before coding**: change the task to `[~] (claimed: <area> session, <date>)` in TODO.md so
   parallel sessions don't collide. Mark `[x]` when done, with a one-line note for the next session.
3. **Stay in your partition** (backend / web / mobile / infra). Cross-area work goes solo.
   If two sessions must touch the same project, use a `git worktree`.
4. Keep the build at **0 warnings** and follow the layer rules below. Do **not** commit unless the
   owner explicitly asks.

## Repo layout

```
src/backend/    PersonaOS.Api · PersonaOS.Application · PersonaOS.Domain · PersonaOS.Infrastructure
src/frontend/   PersonaOS.Web (Angular) · PersonaOS.Mobile (Flutter)
docs/           PRD.md
data/           docs-storage/ (runtime files, gitignored)
```

### Backend clean architecture (dependency rule: Api → Application ← Infrastructure; Application → Domain)

- **Domain** — entities only, no dependencies.
- **Application** — use cases (`AuthService`, `InstanceConfigService`, `SystemPromptBuilder`,
  `ChatService`) + ports in `Common/Interfaces` (`IAppDbContext`, `ISecretProtector`,
  `IPasswordHasher`, `IJwtTokenGenerator`, `IAiMessageStreamer`). No EF SQLite, no
  Anthropic SDK, no Data Protection here. Register via `AddApplication()`.
- **Infrastructure** — adapters only: `AppDbContext` (+ migrations), the AI provider adapters
  (`AnthropicMessageStreamer` — the only file touching the Anthropic SDK — and
  `OpenAiCompatibleMessageStreamer`, selected per install by `AiMessageStreamerFactory`),
  `DataProtectionSecretProtector` (purpose string `PersonaOS.AnthropicApiKey.v1` — never change
  it; it protects whichever provider's key), `JwtTokenGenerator`, `IdentityPasswordHasher`.
  Register via `AddInfrastructure(config)`.
- **Api** — controllers use Application interfaces only (never `AppDbContext` directly);
  `Program.cs` is the composition root and hosts JWT *validation* + Data Protection setup.
- New capability = port in Application + adapter in Infrastructure. Model tools (Phase 2+)
  get an Application-level tool registry dispatched inside `ChatService`.

## Non-negotiable product rules

- The brand **"PersonaOS" is fixed everywhere** (namespaces, images, store). The ONLY
  per-install personalization is the assistant **nickname** + **persona/tone**, stored in the
  singleton `InstanceConfig` row. No theme/logo customization. Never hardcode user data.
- **Provider-neutral AI, native tool-use (not MCP).** Chat goes through the neutral
  `IAiMessageStreamer` port; each provider is one adapter that translates the neutral turns +
  tool definitions to that provider's wire format. Ships with Anthropic and an OpenAI-compatible
  adapter (OpenAI, Ollama, Groq, OpenRouter, LM Studio, …). Tools stay thin wrappers over domain
  services. A new provider = a new adapter behind the port — nothing in Application/Domain moves.
- BYO provider API key, encrypted at rest (Data Protection). Never log or commit keys. The
  OpenAI-compatible provider may be keyless (e.g. local Ollama); Anthropic always needs a key.
- Module endpoints are gated with `[RequireFeature(InstanceConfig.Modules.X)]`.

## Build & run

- Build: `dotnet build PersonaOS.slnx` (keep it at **0 warnings**; pin transitive CVEs).
- Test: `dotnet test src/backend/PersonaOS.Tests` (xUnit; keep it green). Tests target the
  **Application layer** — `TestDbContext` implements `IAppDbContext` over EF InMemory, and
  `TestSupport/Fakes.cs` has scriptable fakes for the ports (push, AI streamer, tools).
  Extend those fakes instead of introducing a mocking library.
- EF migrations (tools pinned locally, NOT global dotnet-ef):
  `dotnet dotnet-ef migrations add <Name> --project src/backend/PersonaOS.Infrastructure --startup-project src/backend/PersonaOS.Infrastructure -o Persistence/Migrations`
- Run API: from `src/backend/PersonaOS.Api`:
  `ASPNETCORE_URLS="http://localhost:5080" ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile`
  (auto-applies migrations). **Stop it with `taskkill //F //IM PersonaOS.Api.exe`** — killing
  the wrapper PID leaves a child that locks DLLs on the next build.
- Web app: `npx ng serve` in `src/frontend/PersonaOS.Web` (port 4200, proxies `/api` → :5080).
  Angular is on v20 (`@angular/cli` `^20.3.3`), matched to `@angular/core` `^20.3.0`. This is an
  ordinary version choice, not a Node constraint: the old note here claimed the local Node was
  too old for a newer CLI, which no longer holds. Moving off v20 means a real Angular major
  upgrade, so treat it as work, not a version bump.
- Flutter: `flutter analyze && flutter test` in `src/frontend/PersonaOS.Mobile`.
- Dev DB: **embedded SQLite** at `src/backend/PersonaOS.Api/data/personaos.db` (WAL; created +
  migrated on first run; gitignored). No LocalDB / SQL Server. Path is `Database:Path` in config.
  Dev credentials once set up: admin `purush` / `S3cure-Pass!`. Free local chat: point the
  OpenAI-compatible provider at Ollama (`http://localhost:11434/v1`, model e.g. `qwen2.5:latest`,
  keyless) via the Setup Wizard or `POST /api/setup`.
  Reset to unconfigured: stop the API, then `rm src/backend/PersonaOS.Api/data/personaos.db*`
  (deletes the db + `-wal`/`-shm`); the next run recreates an empty, unconfigured instance.

## Conventions

- Web-host concerns (JWT bearer validation, Data Protection persistence) live in `Program.cs`;
  business logic lives in `PersonaOS.Application`; technical adapters in `PersonaOS.Infrastructure`
  (see clean-architecture section above).
- Do not commit to git unless the owner explicitly asks.
- Parallel sessions: partition by area (backend / mobile / web / infra); claim tasks in
  TODO.md first; prefer git worktrees if two sessions must touch the same project.
