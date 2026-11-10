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

## Not settled (unverified)
- CLAUDE.md lists two AI adapters (Anthropic, OpenAI-compatible); the PRD lists three
  (adds Ollama). The code has `OllamaMessageStreamer.cs`, so the PRD is current.
