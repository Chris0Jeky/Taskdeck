# Taskdeck

**A local-first, review-first work operating system for turning context into accountable work.**

Taskdeck takes the messy material work arrives in and turns automation-suggested work into reviewable proposals. The shipped wedge is typed and transcript capture with `.txt` transcript upload; source-linked evidence is available where transcript extraction is configured to return it, and broader file and meeting intake stays future direction. You inspect the diff, provenance, side effects, and risk; for automation-originated board changes, only an explicit approval and apply step changes the board, while direct human edits remain first-class.

The current product wedge is a dependable capture-to-action loop. The destination is broader: an adaptive project companion where people and agents can understand work, propose changes, coordinate execution, and retain evidence without silently taking authority away from the user.

[![CI](https://github.com/Chris0Jeky/Taskdeck/actions/workflows/ci-required.yml/badge.svg)](https://github.com/Chris0Jeky/Taskdeck/actions/workflows/ci-required.yml)
[![Status: Beta](https://img.shields.io/badge/status-beta-5b5bd6.svg)](https://github.com/Chris0Jeky/Taskdeck/releases)
[![License: GPL v3](https://img.shields.io/badge/license-GPL_v3-blue.svg)](LICENSE)

[Download the latest stable release](https://github.com/Chris0Jeky/Taskdeck/releases/latest) ·
[Try the v0.3 release candidate](https://github.com/Chris0Jeky/Taskdeck/releases/tag/v0.3.0-rc.1) ·
[Documentation](https://chris0jeky.github.io/Taskdeck/) ·
[Start here](docs/START_HERE.md) ·
[Product direction](docs/strategy/PRODUCT_DIRECTION.md)

![Taskdeck capture, proposal, review, and apply loop](docs/assets/taskdeck-core-loop.gif)

> **Beta software.** Taskdeck is in the v0.x free open beta. Expect breaking changes while public run paths, onboarding, collaboration, agent access, transcript workflows, backup/recovery, and release operations are hardened. The current stable release is v0.2.0; v0.3.0-rc.1 is a prerelease and is not marked Latest. Windows 10/11 x64 is the supported desktop release platform today.

## The core loop

1. **Capture** — paste or submit raw context into Inbox.
2. **Understand** — deterministic logic and, where explicitly configured, a bounded live model extract structured work and evidence.
3. **Propose** — Taskdeck prepares source-linked operations instead of mutating the workspace directly.
4. **Review** — inspect the before/after state, provenance, confidence, side effects, and risk.
5. **Apply** — an authorized user applies approved operations and Taskdeck records the outcome.

That loop already ships for notes, checklists, and transcript-source captures. With a configured live provider, transcript extraction can return evidence spans that deep-link to the stored source. If the provider is unavailable or unusable, the system degrades visibly to deterministic extraction. Ordinary short-form capture triage remains deterministic and offline.

## Why Taskdeck exists

Useful automation should not require surrendering ownership or accepting invisible mutations.

- **Local-first ownership.** The default workspace is one SQLite file you control. Back it up together with its local configuration keys.
- **Review-first automation.** Agents and integrations can prepare work; approval and execution remain distinct capabilities.
- **Provenance by default.** Suggested cards and operations retain where they came from and what evidence supports them.
- **Legible state.** Unknown, degraded, stale, blocked, and failed states remain visible instead of being painted as success.
- **Calm execution.** Inbox, Review, Boards, search, notifications, and operations surfaces focus on decisions rather than activity theatre.
- **Portable core.** The open-source, self-hosted product is useful without a managed service.

Taskdeck is single-instance and self-hosted in the current beta. A managed hosted service is part of the longer commercial direction, not a shipped requirement or a claim about today’s product.

## What ships today

### Work and capture

- boards, columns, cards, labels, ownership, archive/restore, search, and notifications;
- Inbox captures with explicit dispositions and source retention;
- transcript-source extraction with evidence-linked proposals when a live provider is configured;
- deterministic fallback and visible degradation receipts;
- Review queue with proposal history, approval, rejection, apply, and blocked-operation reporting;
- JSON and board exports, local operations views, and authenticated self-hosted access.

### Review-gated agent access

Taskdeck includes an MCP server for clients such as Claude Code and Cursor.

- read tools expose boards, cards, captures, proposals, and resources;
- mutating board tools produce reviewable proposals;
- MCP intentionally exposes no approve or apply tool, so an agent cannot approve its own proposal;
- bounded workflow writes such as creating a capture or dismissing a completed proposal are separate `manage` operations;
- local stdio and API-key-authenticated Streamable HTTP transports are supported;
- v0.3 keys use explicit independent `read`, `propose`, and `manage` capabilities; upgraded legacy keys retain Full access until replaced.

See [MCP_SERVER.md](docs/MCP_SERVER.md) for packaged Windows, Docker, source, Claude Code, Claude Desktop, and Cursor setup.

### Local and self-hosted operation

- self-contained Windows x64 portable release;
- source launchers for Windows and macOS/Linux;
- production Dockerfile and Compose baseline;
- SQLite migrations, pre-migration snapshots, backup/restore guidance, and upgrade notes;
- health, release, provenance, security-scan, and CI contracts;
- mock, OpenAI, and compatible/local provider integrations behind explicit configuration.

## Direction at a glance

Taskdeck’s roadmap has three layers that should not be confused.

### Destination: adaptive work operating system

Taskdeck should become a project companion that can hold context, work structure, decisions, agents, collaboration, and execution history in one legible system. It should support both serious Jira-like operation and a lighter flow-oriented mode without splitting into two products.

### Engine: context-to-action with user-sovereign automation

The durable engine is a Context Fabric that can ingest and relate text, transcripts, files, images, meetings, messages, project state, and agent output. Every automated change remains explainable, reviewable, attributable, and bounded by an authority profile.

### Current wedge: messy context into daily work

The near-term product stays focused on the loop people can evaluate now: capture raw material, understand it, create proposals, review the evidence, and apply accountable work.

### Release horizons

- **v0.3 — Accountable Agents + Downloadable Beta:** scoped MCP keys, a dependable live Review queue, honest provider degradation, packaged operation, and release hardening. `v0.3.0-rc.1` is published; final has no fixed date.
- **v0.4 — Hosted Open Beta + Work Model + Fabric Foundation:** install-free access, opt-in analytics, work-model foundations, durable capture/evidence structures, and extraction-worker seams. This is planned, not shipped.
- **v0.5 — Speak, Type, Paste, or Drop:** one capture surface for text, voice, meetings, images, and files, with boardless understanding before filing.
- **v0.6 — Under Your Rules:** processing profiles, authority profiles, policy-aware agents, and clearer user/organization control.
- **v0.7 and beyond — Project Companion:** richer collaboration, planning, execution, insights, and an adaptive interface built on the same review/provenance contracts.

The canonical direction is [PRODUCT_DIRECTION.md](docs/strategy/PRODUCT_DIRECTION.md). The execution ledger is [REVIVAL_PLAN.md](docs/REVIVAL_PLAN.md). Neither is a promise that an unshipped release or hosted service exists today.

## Quick start

Choose the path that matches how you want to evaluate Taskdeck.

### Windows desktop

Download the stable Windows x64 ZIP and checksum from the [latest release](https://github.com/Chris0Jeky/Taskdeck/releases/latest), verify it, extract it, and run `Taskdeck.Api.exe`.

The release is currently an unsigned portable ZIP. Follow the [Windows quick start](docs/releases/WINDOWS_QUICK_START.md) for verification, SmartScreen guidance, registration, shutdown, backup, and optional model-provider setup.

The v0.3.0-rc.1 prerelease contains schema and integration behavior changes. Read [UPGRADING.md](UPGRADING.md) before opening a workspace you care about with it.

### Docker

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

Keep the secret file and data volume together. Replacing `Jwt__SecretKey` signs users out; losing `Connectors__EncryptionKey` makes already stored connector credentials undecryptable. See [DEPLOYMENT_CONTAINERS.md](docs/ops/DEPLOYMENT_CONTAINERS.md).

### From source

Requirements: .NET 8 SDK and Node.js 24.x, minimum 24.13.1 LTS.

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

The source-only seeded account is `demo` / `demo123`. Open the frontend URL printed by the launcher and stop the stack with `.\scripts\dev-up.ps1 -Stop` on Windows or `scripts/dev-up.sh --stop` on macOS/Linux; closing the shell is not the documented stop path.

## MCP transports

| Mode | Command / endpoint | Intended use |
| --- | --- | --- |
| Packaged Windows stdio | `C:\absolute\path\to\Taskdeck.Api.exe --mcp` | released desktop ZIP; no network listener |
| Released Docker stdio | `docker run --rm -i --no-healthcheck --user 1001:1001 ... IMAGE dotnet Taskdeck.Api.dll --mcp` | released image sharing the normal web volume |
| Source stdio | `dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj -- --mcp` | source checkout; no network listener |
| Standalone HTTP | `dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj -- --mcp --transport http` → `http://127.0.0.1:5001/mcp` | local HTTP client or same-host sidecar |
| Co-hosted HTTP | `<your Taskdeck API base>/mcp` | reuse the normal API process and database |

All processes that should share a workspace must use the same `ConnectionStrings__DefaultConnection`. HTTP keys should remain least-privilege and on trusted transport. The standalone server binds to `127.0.0.1` by default and does not enable cross-origin browser MCP. Using `--host` does not replace TLS, host allowlists, or network controls.

Runtime tool-hash approval remains planned; scoped-key enforcement does not imply that separate lifecycle exists.

## Architecture

| Layer | Technology |
| --- | --- |
| Backend | .NET 8, ASP.NET Core, EF Core, SQLite |
| Frontend | Vue 3, TypeScript, Pinia, Vite, Tailwind CSS |
| Realtime | SignalR |
| Testing | xUnit, Vitest, Playwright |
| Model providers | Mock by default; OpenAI and compatible/local providers are config-gated |
| Agent interface | MCP over stdio or authenticated Streamable HTTP |

```text
backend/          .NET solution and layered application
frontend/         Vue application and browser tests
docs/             Product, architecture, operations, decisions, and contributor guidance
deploy/           Container and deployment configuration
scripts/          Development, demo, verification, release, and operations helpers
```

## Verification

```bash
dotnet test backend/Taskdeck.sln -c Release -m:1

cd frontend/taskdeck-web
npm run typecheck
npm run build
npx vitest --run --maxWorkers=2
npx playwright test --reporter=line
```

See [TESTING_GUIDE.md](docs/TESTING_GUIDE.md) for suite ownership and CI parity.

## Security, privacy, and operational limits

- The default provider is the offline Mock provider. A configured live provider may receive bounded transcript or Automation Chat content; ordinary short-form triage stays deterministic and offline.
- Protect the SQLite database, secret configuration, backups, and connector keys together.
- The Windows release is unsigned today; installers, signing, SBOM/attestation expansion, and additional platforms remain tracked work.
- Taskdeck sends no product telemetry unless a future explicitly opt-in path is activated; see [TELEMETRY.md](docs/TELEMETRY.md).
- Non-SQLite databases are not supported by the current release contract.
- The current product is not a stable v1 API, multi-tenant SaaS, autonomous agent platform, or guarantee that model output is correct.
- Recent reliability work—backup retention, workspace interleavings, webhook time normalization, container release contracts, import diagnostics, accessibility, and modal/viewport behavior—supports the beta’s trust boundary; it does not expand the product claim by itself.

Read [SECURITY.md](SECURITY.md), [UPGRADING.md](UPGRADING.md), and the architecture decisions before production or exposed-network deployment.

## Contributing and licence

Issues and bug reports are welcome. Report suspected vulnerabilities through the private path in [SECURITY.md](SECURITY.md), not a public issue. External code contributions are currently paused while the open-core commercial model and relicensing-capable contribution terms are completed; see [CONTRIBUTING.md](CONTRIBUTING.md), [ADR-0067](docs/decisions/ADR-0067-open-core-commercial-model-and-inbound-rights.md), and issue `#2012`.

The current open-source core is GPL-3.0-only. The licence transition and treatment of earlier MIT releases are documented in [LICENSING.md](LICENSING.md) and [ADR-0050](docs/decisions/ADR-0050-gplv3-copyleft-core.md).
