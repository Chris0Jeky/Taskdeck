# Taskdeck

**Stop managing your task board. Start using it.**

Taskdeck is a local-first execution workspace for developers. It captures messy inputs, generates structured proposals, and only changes your board when you approve — no silent mutations, no surprise cards.

[![CI](https://github.com/Chris0Jeky/Taskdeck/actions/workflows/ci-required.yml/badge.svg)](https://github.com/Chris0Jeky/Taskdeck/actions/workflows/ci-required.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

<!-- demo GIF will be added here -->

---

## What It Does

1. **Capture anything, structure it later.** Paste a client email, a voice-note transcript, a checklist dump. Taskdeck triages it into actionable board changes — cards, columns, labels — without you doing the formatting.

2. **Nothing changes without your approval.** Every automation produces a proposal you review before it touches your board. You see exactly what will change, where it came from, and why.

3. **Your data stays on your machine.** Taskdeck runs locally with SQLite. No cloud account required, no data leaves your device unless you choose to export or share.

---

## How It Works

| Step | What Happens |
|------|-------------|
| **1. Capture** | Paste or type anything into Inbox — raw notes, emails, checklists |
| **2. Triage** | Taskdeck generates a structured proposal from your input |
| **3. Review** | See exactly what will change. Approve, edit, or reject |
| **4. Apply** | Approved changes land on your board — clean, traceable, intentional |

---

## Quick Start

**Prerequisites:** .NET 8 SDK and Node.js 24.x (minimum 24.13.1 LTS)

```bash
# Clone the repo
git clone https://github.com/Chris0Jeky/Taskdeck.git
cd Taskdeck

# Start the backend
dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj

# In a second terminal, start the frontend
cd frontend/taskdeck-web
npm install
npm run dev
```

Open `http://localhost:5173` to start. See [docs/START_HERE.md](docs/START_HERE.md) for the full guided walkthrough.

**Default URLs:**
- Frontend: `http://localhost:5173`
- API: `http://localhost:5000`
- Swagger: `http://localhost:5000/swagger`

### Docker (optional)

```bash
cp deploy/.env.example deploy/.env
# Set a strong TASKDECK_JWT_SECRET value in deploy/.env before continuing.
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline up -d --build
```

Reverse proxy: `http://localhost:8080`. See [docs/ops/DEPLOYMENT_CONTAINERS.md](docs/ops/DEPLOYMENT_CONTAINERS.md).

---

## What Taskdeck Is NOT

- **Not a cloud SaaS (yet).** Local-first is the current posture. Cloud collaboration is on the roadmap.
- **Not a team platform.** Single-user today, with local SignalR for development.
- **Not an autonomous AI agent.** Review-first means you stay in control — proposals are suggestions, not commands.

---

## Key Concepts

- **Inbox / Capture** — zero-friction input. Type fast, format later.
- **Proposal** — a structured diff of what _would_ change on your board, held for your review.
- **Review** — the explicit gate. Nothing reaches your board without your approval.
- **Board** — columns, cards, and labels managed by your decisions, not silent automation.

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend | .NET 8, ASP.NET Core, EF Core, SQLite |
| Frontend | Vue 3, TypeScript, Pinia, Vite, Tailwind CSS |
| Realtime | SignalR |
| Testing | xUnit, Vitest, Playwright |
| LLM | Mock (default), OpenAI, Gemini (config-gated) |

---

## Repository Layout

```
backend/          .NET 8 solution (Domain / Application / Infrastructure / Api)
frontend/         Vue 3 app (taskdeck-web)
docs/             Project documentation
deploy/           Docker Compose and container configs
scripts/          Build and ops scripts
```

---

## Running Tests

```bash
# Backend
dotnet test backend/Taskdeck.sln -c Release -m:1

# Frontend unit + type + build
cd frontend/taskdeck-web
npx vitest --run --reporter=verbose
npm run typecheck
npm run build

# Frontend E2E
TASKDECK_E2E_DB=taskdeck.e2e.local.db npx playwright test --reporter=line
```

For CI parity and verified test totals, see [docs/STATUS.md](docs/STATUS.md) and [docs/TESTING_GUIDE.md](docs/TESTING_GUIDE.md).

---

## Current Status and Roadmap

Taskdeck is in active development (pre-v1.0). The core capture → triage → review → apply loop is shipped and stable.

| Version | Focus |
|---------|-------|
| v0.1.0 | Self-contained executable — download and run, no install required |
| v0.2.0 | Hosted cloud option |
| v0.3.0 | PWA / mobile |
| v0.4.0 | Real-time collaboration |
| v1.0.0 | General availability |

- [docs/STATUS.md](docs/STATUS.md) — current shipped reality
- [docs/IMPLEMENTATION_MASTERPLAN.md](docs/IMPLEMENTATION_MASTERPLAN.md) — delivery sequencing

---

## Contributing

Open or pick a GitHub issue before larger changes. Keep PRs scoped and include verification evidence. See [AGENTS.md](AGENTS.md) for the full contributor protocol, definition of done, and output expectations.

---

## Beta Interest

We are looking for developers and small-team leads who manage their own boards and want to reduce maintenance overhead. If you want to try Taskdeck and share feedback, open a GitHub Discussion or file an issue.

---

*[docs/START_HERE.md](docs/START_HERE.md) — first 15 minutes guided path | [docs/INDEX.md](docs/INDEX.md) — full documentation map*
- Open or pick a GitHub issue before larger changes.
- Keep PRs scoped and include verification evidence.
- For contribution guidance and repo rules, see `AGENTS.md`.

## License

Taskdeck is released under the [MIT License](LICENSE).
