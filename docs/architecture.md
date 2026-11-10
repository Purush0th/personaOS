# Architecture
Map for finding things. The full design, with diagram and reasons, is
[docs/PRD.md §4](PRD.md#4-system-architecture); layer rules and build commands are in
[CLAUDE.md](../CLAUDE.md).

## Components
- **API** (`src/backend/`, .NET 10): `PersonaOS.Api` (controllers, `Program.cs` composition
  root, JWT validation, Data Protection) -> `PersonaOS.Application` (use cases, ports in
  `Common/Interfaces`, chat tool loop, guards, prompt building) <- `PersonaOS.Infrastructure`
  (EF Core + SQLite, AI provider adapters, secrets, JWT, push, file storage, speech);
  `PersonaOS.Domain` (entities, pure rules). Tests: `PersonaOS.Tests` (xUnit, Application layer).
- **Web** (`src/frontend/PersonaOS.Web`, Angular 22): everything except voice and alarms, plus
  the Setup Wizard and admin Settings.
- **Phone** (`src/frontend/PersonaOS.Mobile`, Flutter, Android): chat, voice and hands-free,
  goals, board, planner, reminders with alarms, notes, memories.
- **AI providers**: Anthropic, OpenAI-compatible and Ollama adapters behind `IAiMessageStreamer`;
  per-model `ModelProfiles` (context size, thinking, tool rounds, prompt variant).
- **Speech** (optional): `deploy/speech/whisper` (faster-whisper, also proxies TTS to Kokoro),
  compose profile `speech`.
- **Deploy** (`deploy/`): docker-compose, `.env`, updater service (`deploy/updater`, Python).
- **Training** (`training/`): dataset building, collection, fine-tuning (`train.py`), bench and
  overnight scripts, Modelfiles. Private traces and run reports live in the
  `personaos-training-data` repo; `data/training/` stays local.
- **Scripts** (`scripts/`): `model-check.ps1` accuracy suite, icon and chime generators,
  release notes.

## Data flow
Client -> REST + SSE (JWT) -> API -> `ChatService` builds the prompt from `.prompty` fragments
(overridable from the data volume's `prompts/`), streams from the provider, runs tools through
guards; writes become confirm cards, executed tools leave receipts. Data in SQLite (WAL) at
`Database:Path`; attachments on the filesystem; schedulers for reminders, nudges, briefs, backup;
FCM for push.

## Releases
Tags `v*` (server + web, `release.yml`) and `mobile-v*` (Android, `release-mobile.yml`);
CI in `.github/workflows/ci.yml`. Prod updates through the app's updater.

## Which doc does what
| Doc | Role |
|---|---|
| `CLAUDE.md` | How to work: rules, commands, orchestration. Auto-loaded every session. |
| `TODO.md` | Where we are: the single source of progress, task claims, status. |
| `docs/status.md` | Short snapshot of TODO.md (≤60 lines). TODO.md wins if they differ. |
| `docs/decisions.md` | Append-only, one line per decision with its source. |
| `docs/architecture.md` | This map. |
| `docs/PRD.md` | What and why: product rules, architecture decisions, phase scope. |
| `docs/ENGINEERING.md` | Coding standard, review checklist, definition of done. |

## Backend detail
- **Domain**: entities only, no dependencies.
- **Application**: use cases (`AuthService`, `InstanceConfigService`, `SystemPromptBuilder`,
  `ChatService`) and ports in `Common/Interfaces` (`IAppDbContext`, `ISecretProtector`,
  `IPasswordHasher`, `IJwtTokenGenerator`, `IAiMessageStreamer`). No EF SQLite, Anthropic SDK or
  Data Protection here. Registered by `AddApplication()`. Model tools use an Application-level
  registry dispatched inside `ChatService`.
- **Infrastructure**: `AppDbContext` and migrations; AI adapters `AnthropicMessageStreamer`,
  `OpenAiCompatibleMessageStreamer` and `OllamaMessageStreamer`, picked per install by
  `AiMessageStreamerFactory`; `DataProtectionSecretProtector` (its purpose string protects
  whichever provider's key); `JwtTokenGenerator`; `IdentityPasswordHasher`. Registered by
  `AddInfrastructure(config)`.
- **Api**: `Program.cs` is the composition root and hosts JWT validation and Data Protection
  persistence.
- Providers: Anthropic always needs a key; OpenAI-compatible (OpenAI, Ollama, Groq, OpenRouter,
  LM Studio, …) may be keyless.
- Build settings: warnings are errors via `src/backend/Directory.Build.props`, .NET analyzers at
  `latest-recommended`, exceptions with reasons in `.editorconfig`; transitive CVEs are pinned.
- Tests: `TestDbContext` implements `IAppDbContext` over EF InMemory; `TestSupport/Fakes.cs` has
  scriptable fakes for push, the AI streamer and tools.
- EF tools are pinned locally (`dotnet dotnet-ef`), not installed globally.

## Local dev
- Dev DB: embedded SQLite at `src/backend/PersonaOS.Api/data/personaos.db` (WAL, gitignored),
  created and migrated on first run; path is `Database:Path`. No LocalDB or SQL Server.
- Free local chat: OpenAI-compatible provider at Ollama (`http://localhost:11434/v1`, e.g.
  `qwen2.5:latest`, keyless) via the Setup Wizard or `POST /api/setup`.
- Web: Angular 22 (core, CLI, Material, CDK on 22.1); unit tests on Karma.
- Phone: Flutter with strict casts, inference and raw types.
- Voice: `COMPOSE_PROFILES=speech` in `deploy/.env` runs faster-whisper (`deploy/speech/whisper`)
  and Kokoro on the GPU; PersonaOS uses `http://whisper:8000/v1`, models `large-v3-turbo` /
  `kokoro`. Python pins (PyAV below 15) are in that folder's `requirements.txt`. No host ports;
  see README "Voice".
- The server's `MinSupportedClient` is the oldest phone app it works with.
