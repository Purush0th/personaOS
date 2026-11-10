# PersonaOS — working rules
Map: [docs/architecture.md](docs/architecture.md). Coding bar: [docs/ENGINEERING.md](docs/ENGINEERING.md).

## Session protocol
1. Read docs/status.md, then the relevant part of TODO.md (the only place status lives).
2. Claim before coding: `[~] (claimed: <area> session, <date>)`; mark `[x]` with a one-line note.
3. Stay in one partition (backend / web / mobile / infra); cross-area work goes solo; use a
   `git worktree` if two sessions share a project.
4. 0 warnings. Never commit unless the owner asks. Log decisions in docs/decisions.md.

## Rules
- Layers: Api → Application ← Infrastructure; Application → Domain. Controllers use Application
  interfaces only, never `AppDbContext`. New capability = port in Application + adapter in
  Infrastructure. Only `AnthropicMessageStreamer` touches the Anthropic SDK.
- Brand "PersonaOS" is fixed everywhere; the only per-install settings are nickname and persona
  (`InstanceConfig`). Never hardcode user data.
- AI goes through `IAiMessageStreamer` with native tool-use (not MCP); a provider is one adapter.
  Tools stay thin wrappers over domain services.
- Provider keys are encrypted at rest; never log or commit them. Never change the Data
  Protection purpose string `PersonaOS.AnthropicApiKey.v1`.
- Gate module endpoints with `[RequireFeature(InstanceConfig.Modules.X)]`.
- Tests target the Application layer; extend `TestSupport/Fakes.cs`, no mocking library.

## Commands
- Build: `dotnet build PersonaOS.slnx`
- Test: `dotnet test src/backend/PersonaOS.Tests`
- Migration: `dotnet dotnet-ef migrations add <Name> --project src/backend/PersonaOS.Infrastructure --startup-project src/backend/PersonaOS.Infrastructure -o Persistence/Migrations`
- API (from `src/backend/PersonaOS.Api`):
  `ASPNETCORE_URLS="http://localhost:5080" ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile`;
  stop with `taskkill //F //IM PersonaOS.Api.exe` (killing the wrapper locks DLLs).
- Web (`src/frontend/PersonaOS.Web`): `npx ng serve` (:4200, proxies `/api`);
  tests `npx ng test --watch=false --browsers=ChromeHeadless`.
- Phone (`src/frontend/PersonaOS.Mobile`): `flutter analyze && flutter test`; `dart format` at 120.
- Accuracy: `scripts/model-check.ps1 -Runs 5` (needs `PERSONAOS_USER` / `PERSONAOS_PASSWORD`).
- Reset dev DB: stop the API, `rm src/backend/PersonaOS.Api/data/personaos.db*`.

## Orchestration & model routing
Orchestrator: Opus 5.5 (medium; high when needed). Owns final edits, judgment calls and deliverables.
Agents in `.claude/agents` never edit source or commit; any outputs go to `output/` (gitignored).
Route by complexity before delegating:
- T1 Haiku: search (`Explore`), build/test output and logs (`log-reader`)
- T2 Sonnet: one well-scoped chunk in one area (`worker`), docs, refactors to a spec
- T3 Opus: ambiguous or multi-area work, AI accuracy and guards, migrations, performance,
  UI/design judgment (`worker-deep`)
- T4 Fable 5.2: only when Opus at high falls short; needs the owner's approval
Unsure -> T3. Migrations, performance and AI-accuracy work -> at least T3.
Verify with the area's build/test commands above; AI changes with `model-check.ps1 -Runs 5`.
On failure: verify -> raise effort one level -> move up one tier at a lower effort. Avoid effort above high.
Orchestrate only independent or oversized work; dispatch in parallel; agents return short reports.
Run /compact before switching models. Check /usage.
One session per task: start with /setup-session, end with /handoff-session.
