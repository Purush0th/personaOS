# PersonaOS — session context

Open-source (Apache-2.0), self-hosted, single-user personal AI assistant.
.NET 10 API + SQL Server · Flutter mobile · Angular 20 dashboard · official Anthropic C# SDK.

## Repo layout

```
src/backend/    PersonaOS.Api · PersonaOS.Application · PersonaOS.Domain · PersonaOS.Infrastructure
src/frontend/   PersonaOS.Dashboard (Angular) · PersonaOS.Mobile (Flutter)
docs/           PRD.md
data/           docs-storage/ (runtime files, gitignored)
```

### Backend clean architecture (dependency rule: Api → Application ← Infrastructure; Application → Domain)

- **Domain** — entities only, no dependencies.
- **Application** — use cases (`AuthService`, `InstanceConfigService`, `SystemPromptBuilder`,
  `ChatService`) + ports in `Common/Interfaces` (`IAppDbContext`, `ISecretProtector`,
  `IPasswordHasher`, `IJwtTokenGenerator`, `IAiMessageStreamer`). No EF SQL Server, no
  Anthropic SDK, no Data Protection here. Register via `AddApplication()`.
- **Infrastructure** — adapters only: `AppDbContext` (+ migrations), `AnthropicMessageStreamer`
  (the ONLY file touching the Anthropic SDK), `DataProtectionSecretProtector` (purpose string
  `PersonaOS.AnthropicApiKey.v1` — never change it), `JwtTokenGenerator`, `IdentityPasswordHasher`.
  Register via `AddInfrastructure(config)`.
- **Api** — controllers use Application interfaces only (never `AppDbContext` directly);
  `Program.cs` is the composition root and hosts JWT *validation* + Data Protection setup.
- New capability = port in Application + adapter in Infrastructure. Claude tools (Phase 2+)
  get an Application-level tool registry dispatched inside `ChatService`.

**Start here every session:**
1. Read [TODO.md](TODO.md) — the shared task board across all Claude Code sessions.
   Claim a task there (`[~] (claimed: …)`) before coding; mark `[x]` when done.
2. Product spec: [docs/PRD.md](docs/PRD.md).

## Non-negotiable product rules

- The brand **"PersonaOS" is fixed everywhere** (namespaces, images, store). The ONLY
  per-install personalization is the assistant **nickname** + **persona/tone**, stored in the
  singleton `InstanceConfig` row. No theme/logo customization. Never hardcode user data.
- **Native Anthropic tool-use, not MCP.** Tools must be thin wrappers over domain services.
- BYO Anthropic API key, encrypted at rest (Data Protection). Never log or commit keys.
- Module endpoints are gated with `[RequireFeature(InstanceConfig.Modules.X)]`.

## Build & run

- Build: `dotnet build PersonaOS.slnx` (keep it at **0 warnings**; pin transitive CVEs).
- EF migrations (tools pinned locally, NOT global dotnet-ef):
  `dotnet dotnet-ef migrations add <Name> --project src/backend/PersonaOS.Infrastructure --startup-project src/backend/PersonaOS.Infrastructure -o Persistence/Migrations`
- Run API: from `src/backend/PersonaOS.Api`:
  `ASPNETCORE_URLS="http://localhost:5080" ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile`
  (auto-applies migrations). **Stop it with `taskkill //F //IM PersonaOS.Api.exe`** — killing
  the wrapper PID leaves a child that locks DLLs on the next build.
- Dashboard: `npx ng serve` in `src/frontend/PersonaOS.Dashboard` (port 4200, proxies `/api` → :5080).
  Angular CLI pinned to v20 (local Node 24.9 < latest CLI minimum).
- Flutter: `flutter analyze && flutter test` in `src/frontend/PersonaOS.Mobile`.
- Dev DB: LocalDB `(localdb)\MSSQLLocalDB`, database `PersonaOS`.
  Dev credentials: admin `purush` / `S3cure-Pass!`, nickname "Friday", **placeholder API key**
  (real chat replies need a real key via authed `PUT /api/setup`).
  Reset to unconfigured: `sqlcmd -S "(localdb)\MSSQLLocalDB" -d PersonaOS -Q "DELETE FROM AdminUsers; DELETE FROM InstanceConfig;"`

## Conventions

- Web-host concerns (JWT bearer validation, Data Protection persistence) live in `Program.cs`;
  business logic lives in `PersonaOS.Application`; technical adapters in `PersonaOS.Infrastructure`
  (see clean-architecture section above).
- Do not commit to git unless the owner explicitly asks.
- Parallel sessions: partition by area (backend / mobile / dashboard / infra); claim tasks in
  TODO.md first; prefer git worktrees if two sessions must touch the same project.
