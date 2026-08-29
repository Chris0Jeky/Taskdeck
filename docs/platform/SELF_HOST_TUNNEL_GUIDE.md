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

Back up `deploy/.env` **separately from** the database backups — never in the
same bundle or location (ADR-0061 connector-key custody, 2026-08-29): keep it in
a password manager plus one offline copy. The encryption key it holds is required
to decrypt stored connector credentials, and the JWT secret keeps sessions valid
across restarts; a single stolen bundle containing both the database and the key
would expose every stored connector credential.

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

ADR-0061 (2026-08-29, `private-access-perimeter` = A) requires an **identity/access policy in
front of the tunnel** for the trusted private instance: only the two named identities may reach the
origin at all. Registration mode plus an unlisted URL is not a perimeter — login, health and SignalR
endpoints answer anyone who finds the URL. Before inviting the collaborator, put the origin behind
Cloudflare Access (a named tunnel + an Access application allowing exactly the two identities) or
keep it inside a Tailscale tailnet (`tailscale serve`, **not** Funnel — Funnel URLs are public
internet URLs), and verify from a device outside the policy that the login page is not reachable.

Two tunnel mechanisms are supported. Both proxy WebSockets, which SignalR realtime needs.

**Option A — Cloudflare quick tunnel** (fastest to try; the public URL changes
on every restart, so treat it as a trial run — it has **no** access policy and is
**not** acceptable for the private instance beyond a minutes-long smoke test):

```bash
cloudflared tunnel --url http://localhost:8080
```

**Option B — a stable URL behind an access policy** (required for the private
instance): a named Cloudflare tunnel (requires a domain on Cloudflare) fronted by
a Cloudflare Access application that allows only the two named identities, or
Tailscale **Serve** (`tailscale serve 8080`) inside a tailnet the collaborator has
joined — a stable `https://<machine>.<tailnet>.ts.net` URL reachable only by tailnet
members. A tailnet is not the perimeter by itself: on any tailnet with a third user,
add a Tailscale ACL/grant that limits access to the two named identities (or use a
dedicated two-user tailnet) and verify from a third tailnet identity that the login page
is denied. Do not use Tailscale Funnel for this instance: Funnel URLs are public
internet URLs with no identity check.

The instance is only up while your machine is on. #1777 tracks migrating to
Render for an always-on host.

## 4. Accounts

1. Open the public URL yourself and **register first** — the first registration
   claims the bootstrap slot even in InviteOnly mode.

   **After the collaborator's account exists, close registration** (ADR-0061
   `access-boundary`: exactly two accounts; InviteOnly only while the second is created):
   set `TASKDECK_REGISTRATION_MODE=Closed` in `deploy/.env`, re-run the
   `docker compose … up -d` command so the container is recreated with the new value,
   and verify that a fresh `POST /api/auth/register` is refused. Any invite minted
   earlier must not be able to create a third account.
2. Mint an invite code for each additional person:

   ```bash
   docker compose -f deploy/docker-compose.yml --env-file deploy/.env exec api \
     dotnet /app/cli/Taskdeck.Cli.dll invite create --expires 7
   ```

3. Send the code to your friend; they open the public URL → Register.

## 5. Share a board

Boards → create or open a board → Settings → **Access** (`/workspace/settings/access`):
grant your friend the `Editor` role. On builds that include PR #1774 (issue
#1771) the grant field takes their email or username; on older builds it takes
their user ID, which they can read from `GET /api/users` after logging in.
Realtime presence and updates are per-board and re-check read access on join.

## 6. Care and feeding

- **Backup**: the database lives in the `taskdeck-db` volume
  (`/app/data/taskdeck.db`) in WAL mode — **never copy the file while the app is
  running** (`scripts/backup.sh` and `CLOUD_DEPLOYMENT_GUIDE.md` both warn a live copy
  can be incomplete or corrupt). The production image ships neither `scripts/backup.sh`
  nor `sqlite3` (closing that gap is tracked on #1772), so until then run the
  application-consistent backup from the host through a throwaway container that mounts
  the volume — note `scripts/backup.sh` defaults to `~/.taskdeck/taskdeck.db`, so the
  explicit `--db-path` is required:

  ```bash
  # from the repo root; the volume name is what `docker volume ls` reports
  # (Compose prefixes it with the project name, e.g. deploy_taskdeck-db)
  docker run --rm -v deploy_taskdeck-db:/data -v "$PWD/backups:/backups" \
    -v "$PWD/scripts:/scripts:ro" alpine:3 sh -c \
    'apk add --no-cache bash sqlite >/dev/null && bash /scripts/backup.sh --db-path /data/taskdeck.db --output-dir /backups --retain 7'
  ```

  Run it daily, then copy the newest file to maintainer-controlled off-platform storage,
  encrypted, with a stated retention window (ADR-0061 `backup-retention-destination`;
  for host loss the RPO is the age of that off-platform copy). Keep `deploy/.env` (the
  connector key) in separate custody, never in the same bundle.
- **Upgrade**: `git pull`, then re-run the `docker compose … up -d --build`
  command. Migrations run automatically through the serialized migrator.
- **Revoke access**: remove the grant in the Access view; revoke a registration
  by deactivating the user; rotate the tunnel URL if it leaks.
- Do not enable MFA on this instance until #1653 lands.
