# PersonaOS

**A private, self-hosted personal AI assistant.** It runs on your own machine, uses your own
Anthropic API key, and manages your goals, daily planner, reminders, and documents through a
Claude-powered chat and voice interface.

Give it any name you like — "Friday", "Jarvis", whatever — and it becomes yours. Your data
never leaves your machine except for the calls it makes to Anthropic with your key.

> **Status: pre-release.** The backend is feature-complete and tested; the mobile and web
> clients are still catching up (see [TODO.md](TODO.md)). Published container images do not
> exist yet — build from source with the overlay shown below.

---

## What it does

| | |
|---|---|
| **Chat** | Streaming conversation with Claude, with your profile and context in every prompt |
| **Goals** | Yearly → quarterly → monthly hierarchy with progress that rolls up automatically |
| **Planner** | A day-by-day plan; tasks can link to the goal they serve |
| **Reminders** | Scheduled push notifications, created conversationally ("remind me tomorrow at 9") |
| **Documents** | Upload files and ask questions about them |
| **Proactive** | An optional morning brief and evening rollup, pushed to your phone |

The assistant does all of this through **native tool use** — it isn't a chatbot bolted onto a
CRUD app. Ask it to "move everything I didn't finish to tomorrow" and it will.

## Requirements

- Docker and Docker Compose
- An [Anthropic API key](https://console.anthropic.com/) (you pay Anthropic directly for usage)

## Install

```bash
git clone https://github.com/personaos/personaos.git
cd personaos/deploy
cp .env.example .env
```

Edit `.env` and set the two required values:

- `SA_PASSWORD` — a SQL Server password (8+ chars, mixed case, a digit or symbol)
- `JWT_SIGNING_KEY` — any long random string; generate one with `openssl rand -base64 48`

Then start it. Until published images exist, build from source:

```bash
docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build
```

Open <http://localhost:8080> and the **Setup Wizard** will ask for your assistant's name, an
admin login, your Anthropic API key, and which modules you want. That's it.

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

## Your data

Everything lives in two Docker volumes:

- `personaos_db-data` — the database (goals, planner, reminders, chat history)
- `personaos_api-data` — uploaded documents and the encryption keyring

Your Anthropic API key is **encrypted at rest** using a keyring in `api-data`. Losing that
volume means re-entering the key. Back up both volumes together.

## Building and developing

```bash
dotnet build PersonaOS.slnx          # backend (keep it at 0 warnings)
dotnet test src/backend/PersonaOS.Tests
npx ng serve                          # dashboard, in src/frontend/PersonaOS.Dashboard
flutter run                           # mobile, in src/frontend/PersonaOS.Mobile
```

See [CLAUDE.md](CLAUDE.md) for the architecture, layer rules, and working conventions, and
[docs/PRD.md](docs/PRD.md) for what the product is and why.

## Licence

[Apache-2.0](LICENSE). Forks may use the code freely; please pick your own name and branding
for anything you publish — see [NOTICE](NOTICE).
