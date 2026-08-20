# Taskdeck

**The local-first, review-first action-item engine.**

Paste notes, emails, checklists, or transcript text into Inbox and Taskdeck turns them into source-linked proposals. You decide what is correct; only then does Taskdeck apply approved board changes. Your entire workspace is a single SQLite file you own. Transcript-aware extraction with evidence spans is planned for v0.2, not shipped today.

[![CI](https://github.com/Chris0Jeky/Taskdeck/actions/workflows/ci-required.yml/badge.svg)](https://github.com/Chris0Jeky/Taskdeck/actions/workflows/ci-required.yml)
[![Status: Beta](https://img.shields.io/badge/status-beta-5b5bd6.svg)](https://github.com/Chris0Jeky/Taskdeck/releases)
[![License: GPL v3](https://img.shields.io/badge/license-GPL_v3-blue.svg)](LICENSE)

![Taskdeck capture, proposal, review, and apply loop](docs/assets/taskdeck-core-loop.gif)

> **Beta software:** Taskdeck is in the v0.x free open beta. Expect breaking changes while the public run paths, onboarding, and transcript workflow are hardened. The current open-source core is GPL-3.0-only; the transition and treatment of earlier MIT releases are documented in [LICENSING.md](LICENSING.md) and [ADR-0050](docs/decisions/ADR-0050-gplv3-copyleft-core.md). The DCO check is active but advisory; promotion into branch protection remains maintainer-owned under [#1173](https://github.com/Chris0Jeky/Taskdeck/issues/1173).

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
if [ ! -f deploy/.env.docker-run ]; then
  umask 077
  printf 'Jwt__SecretKey=%s\nConnectors__EncryptionKey=%s\n' \
    "$(openssl rand -base64 48)" \
    "$(openssl rand -base64 32)" \
    > deploy/.env.docker-run
fi
docker run --rm -p 5000:5000 \
  --env-file deploy/.env.docker-run \
  -v taskdeck-data:/app/data \
  taskdeck:local
```

Keep `deploy/.env.docker-run` with the `taskdeck-data` volume and reuse it for every restart. Back up both together: replacing `Jwt__SecretKey` signs everyone out, while losing or replacing `Connectors__EncryptionKey` makes connector credentials already stored in SQLite undecryptable. The env file is ignored by Git; never commit it.

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

Taskdeck supports local stdio plus API-key-authenticated Streamable HTTP at `/mcp`:

| Mode | Command / endpoint | Intended use |
|---|---|---|
| Local stdio | `dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj -- --mcp` | Local editor or agent client; zero network listener |
| Standalone HTTP | `dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj -- --mcp --transport http` → `http://127.0.0.1:5001/mcp` | Local HTTP client or same-host sidecar |
| Co-hosted HTTP | `<your Taskdeck API base>/mcp` | Reuse the normal API process and database |

Every Taskdeck process that should share a workspace must use the same `ConnectionStrings__DefaultConnection`. `dev-up` prints the database path, but its environment override belongs only to the API process it launches; a later MCP process does not inherit it. The launchers use these stable paths:

- Windows: `Data Source=$env:LOCALAPPDATA\Taskdeck\taskdeck-dev.db` (PowerShell expands `$env:LOCALAPPDATA` when you assign the value).
- macOS/Linux: `Data Source=${XDG_DATA_HOME:-$HOME/.local/share}/taskdeck/taskdeck-dev.db` (the shell expands the data directory when you export the value).

Before using stdio, run the web app once and create a local user. Then copy [mcp.example.json](mcp.example.json) into your client's MCP configuration, replace `REPLACE_WITH_THE_ABSOLUTE_taskdeck-dev.db_PATH` with the absolute database path printed by `dev-up`, and adjust the project path if Taskdeck is not the working directory. The stdio server uses the first user in that database unless `McpServer__DefaultUserId` names an existing user. It also needs the connector encryption key written by the normal first-run flow; for an explicit headless setup, provide `Connectors__EncryptionKey` yourself.

For HTTP, create a key in **Settings → API Keys** and start the standalone command with the same `ConnectionStrings__DefaultConnection` as the web app. Claude Code can use [mcp-claude-code-http.example.json](mcp-claude-code-http.example.json), whose `${VAR}` / `${VAR:-default}` expansion is Claude Code-specific. In Cursor or another client, configure the same URL and `Authorization` header through that client's native secret/environment support rather than committing a raw key. The real route requires `Authorization: Bearer tdsk_...`; missing, invalid, expired, or revoked keys receive `401`, and `/` is not an MCP endpoint. Authentication attempts are bounded by client IP before key lookup, and valid requests are rate-limited independently by the key's opaque ID.

The standalone server binds only to `127.0.0.1` by default and replaces blank or ASP.NET any-host `AllowedHosts` values (`*`, `0.0.0.0`, `[::]`, including mixed lists) with the loopback allowlist. Keep bearer keys on loopback. If you deliberately use `--host` for a container, tunnel, or deployment, terminate TLS before the request reaches an untrusted network and set `AllowedHosts` to the exact public host names; `--host` does not relax host-header validation. Cross-origin browser MCP is not enabled. One-command packaging and scoped-key hardening remain planned for [REVIVAL-13](https://github.com/Chris0Jeky/Taskdeck/issues/1309).

## Current scope

Shipped now:

- capture, triage, proposal review, explicit approval, and audited apply;
- boards, cards, labels, Inbox, Review, search, notifications, and local operations surfaces;
- SQLite persistence, JSON/board exports, authentication, and self-hosted container support;
- MCP resources, review-gated board changes, and bounded workflow actions;
- mock, OpenAI, and config-gated local/provider integrations (Gemini is deprecated, pending removal).

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
| LLM | Mock by default; OpenAI is config-gated (Gemini deprecated) |

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

PRs are welcome. Start with [CONTRIBUTING.md](CONTRIBUTING.md), pick or open an issue before a larger change, keep the scope focused, and include verification evidence. Every commit submitted in a pull request must include a `Signed-off-by:` trailer; see the [Developer Certificate of Origin guidance](CONTRIBUTING.md#developer-certificate-of-origin). The pull-request DCO check is active but advisory; promotion into branch protection remains maintainer-owned under [#1173](https://github.com/Chris0Jeky/Taskdeck/issues/1173).

Repository rules for automated contributors live in [AGENTS.md](AGENTS.md).

## License and security

Taskdeck's current open-source core is released under the [GNU General Public License version 3 only](LICENSE). Earlier copies released under MIT keep their existing grants; the transition, permanent free-core boundary, and posture for any future additive commercial module are documented in [LICENSING.md](LICENSING.md) and [ADR-0050](docs/decisions/ADR-0050-gplv3-copyleft-core.md).

Found a vulnerability? Follow the private reporting process in [SECURITY.md](SECURITY.md). Do not open a public issue for a suspected security problem.

---

[First 15 minutes](docs/START_HERE.md) | [Upgrading and backups](UPGRADING.md) | [Documentation index](docs/INDEX.md) | [Issue tracker](https://github.com/Chris0Jeky/Taskdeck/issues)
