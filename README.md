# PersonaOS

**A private, self-hosted personal AI assistant.** It runs on your own machine and manages your
goals, daily planner, reminders, and documents through a chat and voice interface — powered by
**the AI provider you choose**: Anthropic Claude, OpenAI, or any OpenAI-compatible endpoint,
**including a free local model via [Ollama](https://ollama.com/)**.

Give it any name you like — "Juno", "Friday", "Jarvis", whatever — and it becomes yours. Your
data never leaves your machine except for the calls it makes to whichever model you point it at
(and with a local model, nothing leaves at all).

> **Status: pre-release.** Feature-complete and tested; see [TODO.md](TODO.md) for what's left.
> Published container images don't exist yet — build from source with the overlay shown below.
> Images build for both x86-64 and ARM (Raspberry Pi, Apple Silicon).

---

## What it does

| | |
|---|---|
| **Chat** | Streaming conversation, with your profile and context in every prompt |
| **Goals** | Yearly → quarterly → monthly hierarchy with progress that rolls up automatically |
| **Planner** | A day-by-day plan; tasks can link to the goal they serve |
| **Reminders** | Scheduled push notifications, created conversationally ("remind me tomorrow at 9") |
| **Documents** | Upload files and ask questions about them |
| **Proactive** | An optional morning brief and evening rollup, pushed to your phone |

The assistant does all of this through **native tool use** — it isn't a chatbot bolted onto a
CRUD app. Ask it to "move everything I didn't finish to tomorrow" and it will.

## AI providers

Pick your backend in the Setup Wizard (and change it anytime in Settings). PersonaOS talks to
every provider through one neutral streaming port, so switching is a config change, not a
rebuild:

- **Anthropic (Claude)** — bring your own [API key](https://console.anthropic.com/); you pay
  Anthropic directly for usage.
- **OpenAI-compatible** — one setting (a base URL) covers **OpenAI, local Ollama, Groq,
  OpenRouter, LM Studio**, and more. Local models like Ollama are **free and need no key**.

A **"Test connection"** button verifies the provider, model, and key before you save.

## Requirements

- Docker and Docker Compose
- An AI provider — either an API key (Anthropic / OpenAI / …) **or** a free local model
  (run [Ollama](https://ollama.com/) with a tool-capable model such as `qwen2.5` or `llama3.1`)

## Install

```bash
git clone https://github.com/Purush0th/personaOS.git
cd personaos/deploy
cp .env.example .env
```

Edit `.env` and set the one required value:

- `JWT_SIGNING_KEY` — any long random string; generate one with `openssl rand -base64 48`

Then start it. Until published images exist, build from source:

```bash
docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build
```

Open <http://localhost:8080> and the **Setup Wizard** will ask for your assistant's name, an
admin login, your AI provider (key, or base URL for a local model), and which modules you want.
That's it — no database to set up; PersonaOS uses embedded SQLite.

### Free, fully local setup (Ollama)

Run PersonaOS with no API key and no cloud calls at all:

```bash
ollama pull qwen2.5          # a tool-capable local model
```

In the Setup Wizard pick **OpenAI-compatible**, base URL `http://host.docker.internal:11434/v1`
(so the container reaches Ollama on your host), model `qwen2.5`, and leave the key blank.

#### Choosing a local model

The model must support **tool calling** — PersonaOS reads and writes your data through tools, so
a model without them fails every request. Ollama says which: `ollama show <model>` lists
`tools` under capabilities. Gemma 3 and Qwen 2 do not have it; Qwen 2.5, Qwen 3 and Llama 3.1
do.

Size matters more than it looks. Scored with `scripts/model-check.ps1` on one machine
(RTX 3060), running eight scenarios that check what the tools actually did:

| Model | Score | Per turn |
|---|---|---|
| `qwen2.5:0.5b` | 3/8 | under 3 s |
| `qwen2.5:3b-instruct` | 8/8 | 0.6–7.7 s |
| `qwen3:4b` | 7/8 | 8–76 s |

**`qwen2.5:3b-instruct` is a good default.** Below 3B a model starts answering "what are my
tasks today" out of the wrong tool and inventing item keys. Qwen 3 reasons before every reply,
which multiplies latency on a tool turn; `/no_think` does not reach it through Ollama's
OpenAI-compatible endpoint.

#### Give the model enough context

Ollama sizes a model's context from its own default, which is often **4,096 tokens** — smaller
than a PersonaOS request, which carries the system prompt, the tool definitions and your live
goals and board, and runs 8,000 tokens and up. The prompt is then silently truncated and the
model looks stupid rather than starved: it asks you for today's date, calls the same tool
repeatedly, or answers from nothing.

Check what it loaded with `ollama ps` — the `CONTEXT` column. To raise it for one model:

```bash
printf 'FROM qwen2.5:3b-instruct\nPARAMETER num_ctx 16384\n' > Modelfile
ollama create qwen2.5-3b-16k -f Modelfile
```

Then use `qwen2.5-3b-16k` as the model in Settings. For every model at once, set
`OLLAMA_CONTEXT_LENGTH=16384` in Ollama's environment instead. Watch `ollama ps` afterwards: if
the size no longer fits your GPU it will show part on CPU, and generation slows to a crawl —
drop to `8192` in that case.

## Reaching it from your phone

By default PersonaOS binds to **loopback only** — it is not exposed to your network. The
recommended way to reach it from other devices is [Tailscale](https://tailscale.com/): install
it on the host and your phone, then point the mobile app at
`http://<your-machine>.<your-tailnet>.ts.net:8080`.

If you'd rather expose it on your LAN, set `PERSONAOS_BIND=0.0.0.0` in `.env` — but put a
reverse proxy with TLS in front of it first. There is no HTTPS inside the compose bundle.

## Updating

```bash
cd personaos/deploy
docker compose pull && docker compose up -d
```

Database migrations run automatically on start; your data and settings are preserved. To pin a
version for reproducible upgrades, set `PERSONAOS_VERSION` in `.env` — and to roll back, set it
to the previous version and run the same command.

## Your data & backups

Everything lives in **one** Docker volume, `personaos_api-data`:

- `personaos.db` — the SQLite database (config, chat, goals, planner, reminders, and the
  **encrypted** provider API key)
- `docs-storage/` — uploaded document files
- `dp-keys/` — the Data Protection keyring that **decrypts** the stored key

Because the ciphertext (in the db) and its keyring live in the same volume, **back that volume
up as a unit**. PersonaOS also writes a nightly self-contained snapshot, and an optional
[Litestream](https://litestream.io/) overlay gives continuous off-host replication — see
**[docs/BACKUP.md](docs/BACKUP.md)** for the full backup & restore runbook.

## Building and developing

```bash
dotnet build PersonaOS.slnx          # backend (keep it at 0 warnings)
dotnet test src/backend/PersonaOS.Tests
npx ng serve                          # web app, in src/frontend/PersonaOS.Web
flutter run                           # mobile, in src/frontend/PersonaOS.Mobile
```

The dev backend uses embedded SQLite at `src/backend/PersonaOS.Api/data/personaos.db` (created
on first run). See [CLAUDE.md](CLAUDE.md) for the architecture, layer rules, and working
conventions, and [docs/PRD.md](docs/PRD.md) for what the product is and why.

## Licence

[Apache-2.0](LICENSE). Forks may use the code freely; please pick your own name and branding
for anything you publish — see [NOTICE](NOTICE).
