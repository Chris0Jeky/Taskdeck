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

**One command** (starts the API + frontend, pins the dev database to a stable
per-user location, waits for readiness; add `-Seed`/`--seed` to create the
`demo` / `demo123` account):

```powershell
.\scripts\dev-up.ps1 -Seed         # Windows (PowerShell)
```

```bash
scripts/dev-up.sh --seed           # macOS / Linux
```

Stop it with `.\scripts\dev-up.ps1 -Stop` (or `scripts/dev-up.sh --stop`).

<details>
<summary>Or start the two processes manually</summary>

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
</details>

Open `http://localhost:5173` to start. See [docs/START_HERE.md](docs/START_HERE.md) for the full guided walkthrough.

**Default URLs:**
- Frontend: `http://localhost:5173`
- API: `http://localhost:5000`
- Swagger: `http://localhost:5000/swagger`

### Docker (optional)

```bash
cp deploy/.env.example deploy/.env
```

Set BOTH required secrets in `deploy/.env` before continuing — compose refuses to start without them:

- `TASKDECK_JWT_SECRET` (generate with: `openssl rand -base64 48`)
- `TASKDECK_CONNECTORS_ENCRYPTION_KEY` (generate with: `openssl rand -base64 32`)

```bash
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline up -d --build
```

Reverse proxy: `http://localhost:8080`. See [docs/ops/DEPLOYMENT_CONTAINERS.md](docs/ops/DEPLOYMENT_CONTAINERS.md).

---

## What Taskdeck Is NOT

- **Not a cloud SaaS.** Local-first is the permanent posture — Taskdeck is a personal-use tool, not a hosted service.
- **Not a team platform.** Single-user-first by design (one local owner) — board-access sharing (`BoardAccessController`, `/workspace/settings/access`) and SignalR realtime board updates do ship for local use; there is just no hosted multi-user service.
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
npx playwright test --reporter=line
```

For CI parity and verified test totals, see [docs/STATUS.md](docs/STATUS.md) and [docs/TESTING_GUIDE.md](docs/TESTING_GUIDE.md).

---

## Current Status and Direction

Taskdeck is being **finished for personal use, then archived** (maintainer decision, 2026-06-13). The core capture → triage → review → apply loop is shipped and stable. There is **no distribution roadmap** — the earlier v0.1.0→v1.0.0 release plan (cloud, mobile, GA) is retired.

Remaining work, in order:

1. **Finish + activate the Paper UI** as the canonical frontend (ADR-0038; the default-theme flip is the last activation step).
2. **Trivially easy to run** locally — one-command `dev-up` plus a self-contained executable for personal use.
3. **General quality** — backend correctness and usability.
4. **Archive cleanly** — docs reflect the final state.

- [docs/STATUS.md](docs/STATUS.md) — current shipped reality
- [docs/IMPLEMENTATION_MASTERPLAN.md](docs/IMPLEMENTATION_MASTERPLAN.md) — delivery sequencing and the archive-pivot direction

---

## Contributing

New contributors: start with [CONTRIBUTING.md](CONTRIBUTING.md) for local setup, prerequisites, testing commands, commit conventions, and PR process. See [AGENTS.md](AGENTS.md) for the full contributor protocol, definition of done, and output expectations.

Open or pick a GitHub issue before larger changes. Keep PRs scoped and include verification evidence.

---

## Personal Project

Taskdeck is built for the maintainer's personal use and will be archived once it is finished — it is not distributed or supported as a product. The code is public as a portfolio/reference project; the contributor protocol in [AGENTS.md](AGENTS.md) governs any changes.

---

*[docs/START_HERE.md](docs/START_HERE.md) — first 15 minutes guided path | [docs/INDEX.md](docs/INDEX.md) — full documentation map*

## Security

Found a vulnerability? Please report it privately — see [SECURITY.md](SECURITY.md) for our responsible-disclosure policy, supported-version scope, and response timeline. Do not open a public issue or discussion for suspected security issues.

## License

Taskdeck is released under the [MIT License](LICENSE).
