# Self-Host + Tunnel Guide — private shared instance

Last Updated: 2026-08-19

Purpose: run one Taskdeck instance on your own machine and share it with a small,
trusted group (the #1772 two-person collaboration setup) over HTTPS, without a
cloud host. This is the "private evaluation" posture from
`CLOUD_DEPLOYMENT_GUIDE.md` — not a public beta. The security scope accepted for
this posture is recorded on #1644 and #1653: browser tokens stay in
localStorage per ADR-0009, and **MFA must stay disabled** until TOTP secrets are
encrypted at rest.

## Prerequisites

- Docker (Desktop on Windows) with Compose v2.
- A tunnel client — either Cloudflare `cloudflared` or Tailscale (see step 3).

## 1. Secrets

Create `deploy/.env` (gitignored — never commit it). Generate it yourself so the
secrets never pass through an agent transcript:

```bash
cd deploy
printf 'TASKDECK_JWT_SECRET=%s\nTASKDECK_CONNECTORS_ENCRYPTION_KEY=%s\nTASKDECK_REGISTRATION_MODE=InviteOnly\nTASKDECK_PROXY_PORT=8080\n' \
  "$(openssl rand -base64 48)" "$(openssl rand -base64 32)" > .env
```

Back up `deploy/.env` alongside the database volume: the encryption key is
required to decrypt stored connector credentials, and the JWT secret keeps
sessions valid across restarts.

## 2. Start the stack

```bash
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline up -d --build
```

Verify locally before exposing anything:

```bash
curl -fsS http://localhost:8080/health/ready
```

The proxy serves SPA, API, and SignalR hubs from one origin on `:8080`, so no
CORS configuration is needed. `DevelopmentSandbox__Enabled` is forced `false`
in the compose file — leave it that way; when enabled it bypasses all board
authorization.

## 3. Expose it over HTTPS

Two supported options. Both proxy WebSockets, which SignalR realtime needs.

**Option A — Cloudflare quick tunnel** (fastest to try; the public URL changes
on every restart, so treat it as a trial run):

```bash
cloudflared tunnel --url http://localhost:8080
```

**Option B — a stable URL** (recommended once the trial works): either a named
Cloudflare tunnel (requires a domain on Cloudflare) or Tailscale Funnel
(`tailscale funnel 8080`), which gives a stable
`https://<machine>.<tailnet>.ts.net` URL with no domain required. Your friend
does NOT need Tailscale installed — Funnel URLs are public internet URLs.

The instance is only up while your machine is on. #1777 tracks migrating to
Render for an always-on host.

## 4. Accounts

1. Open the public URL yourself and **register first** — the first registration
   claims the bootstrap slot even in InviteOnly mode.
2. Mint an invite code for each additional person:

   ```bash
   docker compose -f deploy/docker-compose.yml --env-file deploy/.env exec api \
     dotnet /app/cli/Taskdeck.Cli.dll invite create --expires 7
   ```

3. Send the code to your friend; they open the public URL → Register.

## 5. Share a board

Boards → create or open a board → Settings → **Access** (`/workspace/settings/access`):
grant your friend the `Editor` role by email or username (shipped with #1771).
Realtime presence and updates are per-board and re-check read access on join.

## 6. Care and feeding

- **Backup**: the database lives in the `taskdeck-db` volume
  (`/app/data/taskdeck.db`). Snapshot it together with `deploy/.env`.
- **Upgrade**: `git pull`, then re-run the `docker compose … up -d --build`
  command. Migrations run automatically through the serialized migrator.
- **Revoke access**: remove the grant in the Access view; revoke a registration
  by deactivating the user; rotate the tunnel URL if it leaks.
- Do not enable MFA on this instance until #1653 lands.
