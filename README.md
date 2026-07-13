# Taskdeck

**The local-first, review-first action-item engine.**

Paste notes, emails, checklists, or transcript text into Inbox and Taskdeck turns them into source-linked proposals. You decide what is correct; only then does Taskdeck apply approved board changes. Your entire workspace is a single SQLite file you own. Transcript-aware extraction with evidence spans is planned for v0.2, not shipped today.

[![CI](https://github.com/Chris0Jeky/Taskdeck/actions/workflows/ci-required.yml/badge.svg)](https://github.com/Chris0Jeky/Taskdeck/actions/workflows/ci-required.yml)
[![Status: Beta](https://img.shields.io/badge/status-beta-5b5bd6.svg)](https://github.com/Chris0Jeky/Taskdeck/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT-yellow.svg)](LICENSE)

![Taskdeck capture, proposal, review, and apply loop](docs/assets/taskdeck-core-loop.gif)

> **Beta software:** Taskdeck is in the v0.x free open beta. Expect breaking changes while the public run paths, onboarding, and transcript workflow are hardened. The shipped repository remains MIT; the fuller permanent-license commitment and DCO gate are tracked in [REVIVAL-03](https://github.com/Chris0Jeky/Taskdeck/issues/1299) for the v0.1 ship gate.

## The loop

1. **Capture** - paste a note, email, checklist, or transcript text into Inbox.
2. **Proposal** - Taskdeck prepares structured, source-linked board changes instead of mutating the board directly.
3. **Review** - inspect the diff, side effects, provenance, and risk; approve or reject it.
4. **Apply** - approved changes land on the board with an audit trail.

Taskdeck ships this capture -> proposal -> review -> apply loop today. Transcript-aware extraction with evidence spans is planned for **v0.2**, not claimed as a current beta feature; follow the [revival plan in PR #1296](https://github.com/Chris0Jeky/Taskdeck/pull/1296) for that work.

## Why Taskdeck

- **Local-first ownership.** The default runtime uses SQLite, so backup and portability start with a file you control.
- **Review-first trust.** Automation stops at a proposal. Approval and execution are explicit human actions.
- **Useful provenance.** Capture-linked proposals preserve where suggested work came from.
- **Agent-safe writes.** The MCP server lets AI clients read Taskdeck and propose changes without granting them the ability to approve their own work.
- **Calm product surface.** The Paper workspace keeps Inbox, Review, and Boards focused on the decisions that matter.

Taskdeck is single-instance and self-hosted in the current beta. A managed hosted service is a future commercial possibility, not a shipped feature or a requirement for using the open-source core.

## Quick start

Choose the path that matches how you want to evaluate Taskdeck.

### 1. Desktop release

The self-contained desktop executable is the intended quickest path for v0.1. No public desktop build is published yet; use the [Releases page](https://github.com/Chris0Jeky/Taskdeck/releases) as the download placeholder and follow [REVIVAL-07](https://github.com/Chris0Jeky/Taskdeck/issues/1303) for release readiness.

### 2. Docker

Build and run the production image locally:

```bash
docker build -f deploy/Dockerfile.production -t taskdeck:local .
docker run --rm -p 5000:5000 \
  -e Jwt__SecretKey="$(openssl rand -base64 48)" \
  -e Connectors__EncryptionKey="$(openssl rand -base64 32)" \
  -v taskdeck-data:/app/data \
  taskdeck:local
```

Or use the Compose baseline:

```bash
cp deploy/.env.example deploy/.env
# Set TASKDECK_JWT_SECRET and TASKDECK_CONNECTORS_ENCRYPTION_KEY in deploy/.env.
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline up -d --build
```

Open `http://localhost:5000` after the direct `docker run` command. The Compose baseline publishes its reverse proxy at `http://localhost:8080`. See [DEPLOYMENT_CONTAINERS.md](docs/ops/DEPLOYMENT_CONTAINERS.md) for secret generation, volumes, health checks, and shutdown.

### 3. From source

Prerequisites: .NET 8 SDK and Node.js 24.x (minimum 24.13.1 LTS).

```powershell
git clone https://github.com/Chris0Jeky/Taskdeck.git
Set-Location Taskdeck
.\scripts\dev-up.ps1 -Seed
```

```bash
git clone https://github.com/Chris0Jeky/Taskdeck.git
cd Taskdeck
scripts/dev-up.sh --seed
```

The seeded account is `demo` / `demo123`. Open the frontend URL printed by the launcher (normally `http://localhost:5173`). Stop it with `.\scripts\dev-up.ps1 -Stop` or `scripts/dev-up.sh --stop`.

For the first guided run, see [START_HERE.md](docs/START_HERE.md).

## MCP: write access with a human gate

Taskdeck includes an MCP server for AI clients such as Claude Code and Cursor. Read tools expose boards, cards, captures, and proposal status. Board-mutating tools stop at proposals, and MCP intentionally exposes no approve or apply tool, so an agent cannot approve its own suggested board changes. Bounded workflow actions such as creating a capture or dismissing a proposal are direct writes.

Three launch modes are available across two MCP transports:

| Mode | Command / endpoint | Intended use |
|---|---|---|
| Local stdio | `dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj -- --mcp` | Local editor or agent client; zero network listener |
| Standalone Streamable HTTP | Add `--transport http --port 5001` | Dedicated remote-capable MCP host; send `Authorization: Bearer tdsk_...` |
| Co-hosted Streamable HTTP | Start the normal API and connect to `/mcp` | REST, UI, and authenticated MCP from one Taskdeck process; send the same Bearer header |

Before using stdio, run the web app once and create a local user. The stdio server uses the first user in that SQLite database unless `McpServer__DefaultUserId` names an existing user. It also needs the connector encryption key written by the normal first-run flow; for an explicit headless setup, provide `Connectors__EncryptionKey` yourself. Copy [mcp.example.json](mcp.example.json) into your client's MCP configuration and adjust the project path if Taskdeck is not the working directory.

For either HTTP mode, sign in to the web app, open **Settings -> API Keys**, create a key, and copy the one-time `tdsk_...` value into your MCP client's Bearer-auth configuration. One-command packaging and scoped-key hardening are planned for [REVIVAL-13](https://github.com/Chris0Jeky/Taskdeck/issues/1309); the embedded server itself already ships.

## Current scope

Shipped now:

- capture, triage, proposal review, explicit approval, and audited apply;
- boards, cards, labels, Inbox, Review, search, notifications, and local operations surfaces;
- SQLite persistence, JSON/board exports, authentication, and self-hosted container support;
- MCP resources, review-gated board changes, and bounded workflow actions;
- mock, OpenAI, Gemini, and config-gated local/provider integrations.

Coming through the revival roadmap:

- **v0.1 First Light:** honest public defaults, Paper onboarding, tested release paths, and licensing posture;
- **v0.2 Transcript Engine:** transcript-aware triage, evidence spans, and OpenAI-compatible provider support;
- **v0.3 Open Beta:** a slimmer public surface, packaged MCP setup, and the feedback channel.

This README follows the maintainer-owned revival direction proposed in [PR #1296](https://github.com/Chris0Jeky/Taskdeck/pull/1296) and must not land before that direction update. Taskdeck is not claiming a hosted service, production transcript engine, or stable v1 API today.

## Technology

| Layer | Technology |
|---|---|
| Backend | .NET 8, ASP.NET Core, EF Core, SQLite |
| Frontend | Vue 3, TypeScript, Pinia, Vite, Tailwind CSS |
| Realtime | SignalR |
| Testing | xUnit, Vitest, Playwright |
| LLM | Mock by default; OpenAI and Gemini are config-gated |

```text
backend/          .NET solution and layered application
frontend/         Vue application and browser tests
docs/             Product, architecture, operations, and contributor guidance
deploy/           Container and deployment configuration
scripts/          Development, demo, verification, and operations helpers
```

## Verification

```bash
# Backend
dotnet test backend/Taskdeck.sln -c Release -m:1

# Frontend (run from frontend/taskdeck-web)
npm run typecheck
npm run build
npx vitest --run
npx playwright test --reporter=line
```

See [TESTING_GUIDE.md](docs/TESTING_GUIDE.md) for suite ownership and CI parity.

## Contributing

PRs are welcome. Start with [CONTRIBUTING.md](CONTRIBUTING.md), pick or open an issue before a larger change, keep the scope focused, and include verification evidence. Taskdeck is adopting a Developer Certificate of Origin workflow in [REVIVAL-03](https://github.com/Chris0Jeky/Taskdeck/issues/1299); until that gate lands, do not claim the DCO check is active.

Repository rules for automated contributors live in [AGENTS.md](AGENTS.md).

## License and security

Taskdeck is released under the [MIT License](LICENSE). The revival commitment is that code already shipped under MIT stays MIT; the complete licensing posture is the v0.1 [REVIVAL-03](https://github.com/Chris0Jeky/Taskdeck/issues/1299) deliverable.

Found a vulnerability? Follow the private reporting process in [SECURITY.md](SECURITY.md). Do not open a public issue for a suspected security problem.

---

[First 15 minutes](docs/START_HERE.md) | [Documentation index](docs/INDEX.md) | [Issue tracker](https://github.com/Chris0Jeky/Taskdeck/issues)
