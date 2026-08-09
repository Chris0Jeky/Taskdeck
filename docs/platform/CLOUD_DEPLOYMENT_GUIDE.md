# Cloud Deployment Guide

Last Updated: 2026-07-13
Issue: `#538` CLD-01 Deploy Taskdeck to managed cloud platform

> **Private evaluation only unless registration is gated.** Do not expose a build to the public internet unless it includes the registration-gating work in [#1297](https://github.com/Chris0Jeky/Taskdeck/issues/1297) and the operator has explicitly chosen a safe registration mode. Otherwise, keep the service behind provider access controls, a private network, or another authentication layer.
>
> Taskdeck's current supported posture is local-first, self-hosted, single-instance SQLite. This guide is an evaluation reference for a single private hosted container. A managed Taskdeck cloud is a future possibility, not a shipped beta service; multi-instance/PostgreSQL work remains unsupported today. This update follows the direction proposed in [PR #1296](https://github.com/Chris0Jeky/Taskdeck/pull/1296) and must not land before it.

---

## Overview

This guide covers deploying Taskdeck to managed cloud platforms (**Railway** and **Render**) using the production Dockerfile. The setup produces a single container serving both the .NET 8 API and the Vue 3 SPA, with SQLite on a persistent volume.

**Architecture:**

```
[Cloud Platform (Railway / Render)]
  +-- Single container (ASP.NET 8)
  |     +-- /api/*    -> REST API controllers
  |     +-- /hubs/*   -> SignalR WebSocket hub
  |     +-- /health/* -> Liveness and readiness probes
  |     +-- /*        -> Vue SPA (static files from wwwroot/)
  +-- Persistent volume (/app/data)
        +-- taskdeck.db (SQLite)
```

Related documents:
- `docs/ops/DEPLOYMENT_CONTAINERS.md` -- local Docker Compose baseline
- `docs/ops/CLOUD_REFERENCE_ARCHITECTURE.md` -- exploratory AWS/ECS scale-out architecture; not a supported beta run path
- `docs/platform/SQLITE_TO_POSTGRES_MIGRATION_RUNBOOK.md` -- PostgreSQL migration path
- `docs/strategy/03_CLOUD_COLLABORATION_STRATEGY.md` -- strategic context

---

## Prerequisites

- A GitHub account with access to the Taskdeck repository
- A Railway or Render account; verify the provider's current plans and limits before deploying
- A strong JWT secret (generate with `openssl rand -base64 48`)
- A connector encryption key (generate with `openssl rand -base64 32`)
- Provider access controls or another private-network boundary; the current target branch is not safe for public registration

---

## Production Dockerfile

The production Dockerfile (`deploy/Dockerfile.production`) uses a multi-stage build:

1. **frontend-build**: Node.js builds the Vue SPA via `npm run build`
2. **backend-build**: .NET 8 SDK restores and publishes the API
3. **runtime**: ASP.NET 8 runtime with the SPA copied into `wwwroot/`

Key properties:
- Single container (no nginx, no separate frontend service)
- Non-root user (`taskdeck:1001`)
- Built-in HEALTHCHECK against `/health/ready`
- Respects `PORT` env var via `ASPNETCORE_URLS=http://+:${PORT:-5000}`
- SQLite data at `/app/data/taskdeck.db` (mount a volume here)

### Local build test

```bash
# From repo root
docker build -f deploy/Dockerfile.production -t taskdeck-prod .

# Run locally
docker run -p 5000:5000 \
  -e Jwt__SecretKey=$(openssl rand -base64 48) \
  -e Connectors__EncryptionKey=$(openssl rand -base64 32) \
  -e Auth__Registration__Mode=Closed \
  -e Cors__AllowedOrigins=http://localhost:5000 \
  -v taskdeck-data:/app/data \
  taskdeck-prod
```

Open `http://localhost:5000` to verify the SPA loads and the API responds.

---

## Railway Deployment

### Step 1: Create a Railway project

1. Go to [railway.app](https://railway.app) and sign in
2. Click **New Project** and select **Deploy from GitHub repo**
3. Connect the Taskdeck repository
4. In the Railway service settings, set the **Config Path** to `deploy/railway.toml` (Railway does not auto-detect config files outside the repo root)

### Step 2: Attach a persistent volume

SQLite requires a persistent volume to survive redeploys.

1. In the Railway dashboard, click your service
2. Go to **Settings** > **Volumes**
3. Click **Add Volume**
4. Set the mount path to `/app/data`
5. Set size to 1 GB (expandable later)

### Step 3: Set environment variables

In the Railway dashboard, go to **Variables** and add:

| Variable | Value | Required |
|----------|-------|----------|
| `Jwt__SecretKey` | Output of `openssl rand -base64 48` | Yes |
| `Connectors__EncryptionKey` | Output of `openssl rand -base64 32`; preserve it with database backups | Yes |
| `Auth__Registration__Mode` | `Closed` or `InviteOnly` | Yes |
| `Cors__AllowedOrigins` | Your Railway URL (e.g., `https://taskdeck-production.up.railway.app`) | Yes |
| `ConnectionStrings__DefaultConnection` | `Data Source=/app/data/taskdeck.db` | Yes |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Yes |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` | Yes |
| `FirstRun__AutoOpenBrowser` | `false` | Yes |
| `FirstRun__ResolveAppDataDbPath` | `false` | Yes |
| `TASKDECK_HEADLESS` | `true` | Yes |

See `deploy/.env.production.template` for the full variable reference including optional LLM provider and observability settings.

### Step 4: Deploy

Railway deploys automatically on push to the connected branch. Verify deployment:

1. Check the Railway deploy logs for successful startup
2. Visit your Railway URL -- the Taskdeck SPA should load
3. Check `https://your-url.up.railway.app/health/ready` for a healthy status from inside your private boundary
4. Confirm anonymous/public access is still blocked by your provider or network controls

### Railway-specific notes

- Railway injects `PORT` as an environment variable; the Dockerfile handles this
- Railway provides automatic HTTPS on the `.up.railway.app` domain
- Custom domains are supported in Railway settings
- Railway's health check uses the `healthcheckPath` from `railway.toml`

---

## Render Deployment

### Step 1: Create a Render service

> **Blueprint path**: Render auto-detects `render.yaml` only at the repo root.
> Since Taskdeck keeps it at `deploy/render.yaml`, you must set the
> **Blueprint Sync Path** in the Render dashboard:
> 1. Go to **Account Settings** > **Blueprints**
> 2. Click **Sync** next to the connected repo
> 3. Set **Root Directory** (or "Blueprint Path") to `deploy`
>
> Alternatively, use the manual Web Service setup below.

1. Go to [render.com](https://render.com) and sign in
2. Click **New** > **Blueprint**
3. Connect the Taskdeck repository
4. Set the Blueprint path to `deploy` (see note above)
5. Render reads `deploy/render.yaml` and provisions the service and disk

Alternatively, create a **Web Service** manually:
1. Click **New** > **Web Service**
2. Connect the repo
3. Set **Docker** as the runtime
4. Set Dockerfile path to `deploy/Dockerfile.production`
5. Set Docker context to `.` (repo root)

### Step 2: Persistent disk

If using the Blueprint (`render.yaml`), the disk is created automatically at `/app/data` with 1 GB.

If creating manually:
1. Go to service **Settings** > **Disks**
2. Add a disk with mount path `/app/data` and size 1 GB

### Step 3: Set environment variables

In the Render dashboard, go to **Environment** and add the same variables as Railway (see table above). The `render.yaml` blueprint pre-populates restrictive defaults; you must set `Jwt__SecretKey`, `Connectors__EncryptionKey`, and `Cors__AllowedOrigins` manually, then configure a private access boundary before deploying.

### Step 4: Deploy

Render deploys automatically on push to the configured branch.

1. Check the Render deploy logs
2. Visit your Render URL (e.g., `https://taskdeck.onrender.com`)
3. Check `https://taskdeck.onrender.com/health/ready` from inside your private boundary
4. Confirm anonymous/public access is blocked by your provider or network controls

### Render-specific notes

- Render injects `PORT` as an environment variable
- Render provides automatic HTTPS on `.onrender.com`
- Verify the current plan's sleep, cold-start, storage, and retention behavior in Render's documentation
- Render's health check uses `healthCheckPath` from `render.yaml`

---

## Environment Variable Reference

See `deploy/.env.production.template` for the authoritative list with descriptions.

### Required variables

| Variable | Purpose |
|----------|---------|
| `Jwt__SecretKey` | JWT signing secret (min 32 bytes, `openssl rand -base64 48`) |
| `Connectors__EncryptionKey` | AES-256 key that encrypts stored connector credentials (`openssl rand -base64 32`); restore the same value with the database |
| `Cors__AllowedOrigins` | Comma-separated allowed origins for CORS |
| `ConnectionStrings__DefaultConnection` | SQLite connection string (use `/app/data/` path) |

### Recommended production defaults

| Variable | Value | Purpose |
|----------|-------|---------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Disables Swagger UI, enables security headers |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` | Trust X-Forwarded headers from cloud proxy |
| `FirstRun__AutoOpenBrowser` | `false` | No browser to open in containers |
| `FirstRun__ResolveAppDataDbPath` | `false` | Use explicit DB path, not OS AppData |
| `DevelopmentSandbox__Enabled` | `false` | Disable sandbox mode |
| `TASKDECK_HEADLESS` | `true` | Prevent ephemeral JWT secret generation on restart |
| `Auth__Registration__Mode` | `Closed` | Require an operator invite for the first owner, then deny later signup |

### Optional variables

| Variable | Default | Purpose |
|----------|---------|---------|
| `Llm__EnableLiveProviders` | `false` | Enable live LLM API calls |
| `Llm__Provider` | `Mock` | LLM provider: `Mock`, `OpenAI`, `OpenAICompatible`, or `Gemini` |
| `Llm__OpenAi__ApiKey` | (empty) | OpenAI API key |
| `Llm__OpenAiCompatible__ApiKey` | (empty) | Compatible-provider API key; store as a platform secret |
| `Llm__OpenAiCompatible__BaseUrl` | (empty) | Required public HTTPS API base URL; cloud Production does not permit local HTTP |
| `Llm__OpenAiCompatible__Model` | (empty) | Required compatible model identifier |
| `Llm__OpenAiCompatible__TimeoutSeconds` | `30` | Full response deadline, including body/SSE reads |
| `Llm__OpenAiCompatible__MaxResponseBytes` | `1048576` | Buffered or aggregate SSE response byte budget |
| `Llm__OpenAiCompatible__MaxSseLineBytes` | `65536` | Per-line SSE byte budget |
| `Llm__OpenAiCompatible__MaxSseEventBytes` | `131072` | Per-event SSE byte budget |
| `Llm__OpenAiCompatible__ExtraHeaders__<HeaderName>` | (empty) | Optional non-secret gateway header; `HTTP-Referer` and `X-Title` are common |
| `Llm__Gemini__ApiKey` | (empty) | Gemini API key |
| `GitHubOAuth__ClientId` | (empty) | GitHub OAuth app client ID |
| `GitHubOAuth__ClientSecret` | (empty) | GitHub OAuth app secret |
| `SignalR__Redis__ConnectionString` | (empty) | Redis for SignalR backplane (multi-instance only) |
| `Observability__OtlpEndpoint` | (empty) | OTLP endpoint for metrics export |
| `Sentry__Enabled` | `false` | Enable Sentry error tracking |
| `Sentry__Dsn` | (empty) | Sentry DSN |

### Restrictive-mode operator flow

Keep the service URL private/unannounced while provisioning the owner. From a
Railway or Render private shell, mint the first-owner invite as the non-root
application user:

```bash
gosu taskdeck dotnet /app/cli/Taskdeck.Cli.dll invite create --expires 7
```

The command writes JSON containing the plaintext code once. Use it immediately
to create the first owner; the database stores only its SHA-256 hash. In
`Closed`, every later registration is denied. To admit later users, switch to
`InviteOnly`, restart the API, and mint a separate code per account. New
OAuth/OIDC identities cannot redeem an invite directly in this v0.1 CLI-only
flow; register first, then link the external account. Existing linked external
logins continue to work. The production image includes `gosu`; use it as shown
so CLI-created SQLite WAL/journal files retain the same owner as the API process.

---

## SQLite Persistence Considerations

SQLite is Taskdeck's default database. It works well for single-instance cloud deployments, but comes with constraints:

### Volume requirement

SQLite stores data in a file. Without a persistent volume, data is lost on every deploy. **Always mount a volume at `/app/data`.**

### Single-writer constraint

SQLite supports one writer at a time (WAL mode improves this but does not eliminate it). This means:
- **Do not scale to multiple instances** while using SQLite
- Railway `numReplicas` and Render `numInstances` must remain at 1
- PostgreSQL and horizontal scaling are not supported beta paths. Treat the retained migration material as research unless a future commercial plan explicitly ratifies that work.

### Backups

Do not assume a cloud volume includes backups. Verify the provider's current snapshot and retention behavior, then implement and restore-test a backup strategy.

> **Warning**: Do not copy the SQLite database file while the application is running.
> A raw `cp` of `taskdeck.db` during active writes can produce a corrupt or
> incomplete backup. Use the `sqlite3 .backup` command instead, which is safe
> for online backups regardless of journal mode.

> **Critical — back up the connector encryption key with the database.** The key that
> decrypts stored connector credentials must be restored *unchanged*. If it is supplied via
> `Connectors__EncryptionKey` (environment or secret store), keep that value safe. If the
> deployment generated the key onto the data volume instead — the AWS single-node Terraform
> module writes `connector-encryption.key` next to `taskdeck.db` — **that file must be backed
> up and restored alongside the database**. Restoring only `taskdeck.db` onto a fresh volume
> makes the app generate a *different* key, leaving every stored connector credential
> undecryptable. `scripts/backup.sh` copies a sibling `connector-encryption.key` automatically;
> the manual `sqlite3 .backup` steps below do **not**, so copy the key file yourself.
>
> **On restore**, the container reads `Connectors__EncryptionKey` from its *environment* (injected at
> deploy time), not from the key file. So after restoring the paired key, you must also update the
> injected value (`.env` / secret) to the restored key and **recreate** the service — a plain restart
> keeps using the stale key. `scripts/restore.sh` / `restore.ps1` restore the key file and print this
> reminder; on the AWS Terraform path, replacing the instance re-renders `.env` from the restored key file.

The production Taskdeck image does **not** include the `sqlite3` CLI, so commands that assume it is available inside the running container will fail. Use one of these operator-controlled paths:

1. Stop the Taskdeck instance and create a provider volume snapshot or offline export.
2. Attach the stopped volume to a trusted one-off administration image that includes `sqlite3`, then run `.backup` from that image.
3. Build a separately reviewed backup job image that includes `sqlite3` and uploads encrypted snapshots to operator-controlled storage. Do not add backup tooling to the public runtime image casually.

Whichever path you use, preserve `Connectors__EncryptionKey` in the same protected recovery set and prove a restore before relying on the backup.

### Migration to PostgreSQL

> **Future horizon, not current support.** PostgreSQL migration and horizontal scaling are not part of the free beta or an executable operator path today. Hosted infrastructure remains a possible future surface; any provider change still requires a separately ratified implementation plan.

The existing `docs/platform/SQLITE_TO_POSTGRES_MIGRATION_RUNBOOK.md` records earlier research. Do not treat it as proof that the current application supports switching providers.

---

## Monitoring and Health Checks

### Health endpoints

| Endpoint | Purpose | Expected response |
|----------|---------|-------------------|
| `GET /health/live` | Liveness probe (process is running) | `200 { status: "Healthy" }` |
| `GET /health/ready` | Readiness probe (DB connected, workers healthy) | `200 { status: "Ready" }` or `503 { status: "NotReady" }` |

The readiness check validates:
- Database connectivity (SQLite file accessible)
- LLM queue depth (not excessively backed up)
- SignalR backplane status (if Redis is configured)
- Worker heartbeats (queue processing and housekeeping workers)

### Platform monitoring

- **Railway**: Built-in metrics (CPU, memory, network) in the dashboard. Set up alerts for deploy failures.
- **Render**: Built-in metrics and health check status. Configure Slack/email notifications for failed health checks.
- **External**: Point an uptime monitor (UptimeRobot, Betterstack) at `/health/live` for independent availability tracking.

---

## Cost Planning

Provider prices, free-tier behavior, storage charges, and bandwidth allowances change. Before deploying, verify the current Railway or Render pricing page and budget for one always-on container, persistent storage, backups, and network egress. Treat any provider calculator result as an estimate, then monitor actual usage during the private evaluation.

---

## Troubleshooting

### Container fails to start

**Symptom**: Deploy logs show the container exiting immediately.

**Check**:
1. Verify `Jwt__SecretKey` is set. The app will fail to start without it (first-run bootstrap generates one locally, but in production you must provide it).
2. Verify `Connectors__EncryptionKey` is set and decodes to exactly 32 bytes.
3. Verify the volume is mounted at `/app/data`. Without it, the SQLite path may not be writable.
4. Check that `ASPNETCORE_URLS` matches the platform's expected port. Both Railway and Render inject `PORT`; the Dockerfile defaults handle this.

### Health check fails after deploy

**Symptom**: Platform reports unhealthy service.

**Check**:
1. Visit `/health/live` first. If this fails, the app is not running at all.
2. If `/health/live` works but `/health/ready` returns 503, check the response body for which subsystem is unhealthy (database, workers, queue).
3. Worker heartbeat failures on first deploy are normal -- workers need ~30 seconds to initialize. The readiness check has a startup grace period.

### CORS errors in browser

**Symptom**: API calls from the frontend fail with CORS errors.

**Check**:
1. Verify `Cors__AllowedOrigins` exactly matches the URL in the browser address bar (including protocol and no trailing slash).
2. If using a custom domain, add both the platform domain and the custom domain to `Cors__AllowedOrigins` (comma-separated).

### Database locked errors

**Symptom**: API returns 500 errors with "database is locked".

**Check**:
1. Verify only one instance is running. SQLite does not support multiple writers.
2. Confirm the deployment is still single-instance. `UseSqlite()` is currently hardwired with no provider switch, so PostgreSQL/scale-out material is reference research rather than an executable recovery path.

### SPA shows blank page

**Symptom**: The URL loads but the page is blank.

**Check**:
1. Open browser developer tools and check the Console and Network tabs.
2. Verify the frontend build succeeded in the deploy logs (look for `npm run build` output).
3. Check that `VITE_API_BASE_URL` is set to `/api` (the default).

---

## Custom Domain Setup

Both Railway and Render support custom domains with automatic TLS.

1. Add your custom domain in the platform dashboard
2. Create a CNAME DNS record pointing to the platform URL
3. Update `Cors__AllowedOrigins` to include the custom domain
4. Wait for DNS propagation and TLS certificate provisioning (usually 5-15 minutes)

---

## Possible future extensions

> These are uncommitted architecture options, not beta roadmap promises. The current supported deployment remains one Taskdeck instance with a persistent SQLite volume.

- **Scale beyond single instance**: Migrate to PostgreSQL and add Redis for SignalR backplane. See `docs/ops/CLOUD_REFERENCE_ARCHITECTURE.md`.
- **CI/CD pipeline**: Configure GitHub Actions to auto-deploy on merge to main. See `.github/workflows/` for existing CI configuration.
- **CDN for static assets**: Offload SPA delivery to Cloudflare Pages for global edge caching. See `docs/strategy/03_CLOUD_COLLABORATION_STRATEGY.md`.
