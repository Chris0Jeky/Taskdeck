# Self-Host + Tunnel Guide — private shared instance

Last Updated: 2026-08-29

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

## 5. LLM providers, quota, and the egress disclosure

Skip this section entirely if you are staying on the mock provider — `deploy/docker-compose.yml`
defaults `Llm__EnableLiveProviders` to `false` and `Llm__Provider` to `Mock`, so a stock stack
sends nothing to any LLM provider.

Turning live triage on puts this instance in ADR-0061's **operator-funded** variant
(`llm-cost-ownership` = A): your provider key is the deployment-global key, your collaborator's
captured content egresses under **your** provider account, and you bear the cost. Per-user BYO keys
are not buildable today (`#1879` is open), so this is the only buildable variant. Do all five steps
below — the disclosure is not optional and must be given **before the collaborator captures
anything real**.

1. **Enable the live provider.** Add to `deploy/.env` (compose maps these onto
   `Llm__EnableLiveProviders`, `Llm__Provider`, and `Llm__OpenAi__ApiKey`):

   ```bash
   TASKDECK_LLM_ENABLE_LIVE_PROVIDERS=true
   TASKDECK_LLM_PROVIDER=OpenAI
   TASKDECK_LLM_OPENAI_API_KEY=<your provider key>
   ```

   The key is a host secret: keep it in `deploy/.env` only, which is gitignored and already backed
   up in separate custody from the database (step 1). Generate and paste it yourself so it never
   passes through an agent transcript, and never put it in the compose file or an image.

2. **Set a real global ceiling.** `LlmQuota:GlobalBudgetCeilingTokens` is `0` — *unlimited* — by
   default, and ADR-0061 `budget-alerts-cost-owner` requires a real number before live providers
   carry someone else's traffic. **There is no `TASKDECK_*` passthrough for it**: unlike the keys
   above, setting it in `deploy/.env` alone does nothing, because `deploy/docker-compose.yml`
   forwards only the variables its `environment:` block names. Create
   **`deploy/docker-compose.llm-quota.yml`** with exactly this content:

   ```yaml
   services:
     api:
       environment:
         LlmQuota__GlobalBudgetCeilingTokens: "<tokens per day>"
   ```

   The per-user limits (`LlmQuota:RequestsPerHour` 60, `LlmQuota:TokensPerDay` 100000) already
   default to non-zero values; the global ceiling is the one that does not. On breach the ruled
   action is to **disable live providers, not to shut the instance down**, so collaboration
   walkthroughs survive a spend stop.

3. **Recreate the API so the new values take effect.** Editing `deploy/.env` and adding an override
   file change nothing in the container that is already running from step 2 — `docker compose up`
   applies changed service configuration by recreating the container, exactly as the
   registration-mode step in section 4 does. From now on **every** compose command for this stack
   must pass both files, in this order:

   ```bash
   docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.llm-quota.yml \
     --env-file deploy/.env --profile baseline up -d --build
   ```

   Compose merges only the files given with `-f`, so a later base-only `up` silently drops
   `LlmQuota__GlobalBudgetCeilingTokens` and restores the **unlimited** default while the live
   provider credentials in `deploy/.env` stay enabled — an uncapped live instance. That is why the
   upgrade step in section 7 repeats the two-file command. Verify the ceiling actually reached the
   container before handing the instance over:

   ```bash
   docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.llm-quota.yml \
     --env-file deploy/.env exec api printenv LlmQuota__GlobalBudgetCeilingTokens
   ```

4. **Configure a provider spend alert** where the provider offers one, and record the all-in monthly
   ceiling on `#1772`. Both the ceiling and the alert threshold are still **pending maintainer
   values** on `#1772`, and the amounts must be re-verified against current provider prices before
   any purchase — until they are supplied, this step is not executable and live providers should
   stay off.

5. **Give the written disclosure.** Before the collaborator captures anything real, send them a
   written note stating that live triage runs on your provider key, that their captured content
   therefore leaves the instance under your provider account, and that you pay for it. Point them at
   both:

   - `GET /api/privacy/egress` — the live destination list, readable once they are logged in;
   - `docs/security/MANAGED_KEY_USAGE_POLICY.md` — the managed-key usage policy.

   Connectors, outbound webhooks, and analytics remain independent egress destinations covered by
   the general egress disclosure; enabling or disabling live LLM providers does not change them.

## 6. Share a board

Boards → create or open a board → Settings → **Access** (`/workspace/settings/access`):
grant your friend the `Editor` role. On builds that include PR #1774 (issue
#1771) the grant field takes their email or username; on older builds it takes
their user ID, which they can read from `GET /api/users` after logging in.
Realtime presence and updates are per-board and re-check read access on join.

## 7. Care and feeding

- **Backup**: the database lives in the `taskdeck-db` volume
  (`/app/data/taskdeck.db`) in WAL mode — **never copy the file while the app is
  running** (`scripts/backup.sh` and `CLOUD_DEPLOYMENT_GUIDE.md` both warn a live copy
  can be incomplete or corrupt). The production image ships neither `scripts/backup.sh`
  nor `sqlite3` (closing that gap is tracked on #1772), so until then run the
  application-consistent backup from the host through a throwaway container that mounts
  the volume — note `scripts/backup.sh` defaults to `~/.taskdeck/taskdeck.db`, so the
  explicit `--db-path` is required:

  ```bash
  # from the repo root. Compose prefixes the volume with the project name, and
  # deploy/docker-compose.yml:1 fixes that name (`name: taskdeck`) rather than
  # deriving it from the directory — so the volume is taskdeck_taskdeck-db.
  # Always confirm against what `docker volume ls` actually reports before running.
  docker run --rm -v taskdeck_taskdeck-db:/data -v "$PWD/backups:/backups" \
    -v "$PWD/scripts:/scripts:ro" alpine:3 sh -c \
    'apk add --no-cache bash sqlite >/dev/null && bash /scripts/backup.sh --db-path /data/taskdeck.db --output-dir /backups --retain 7'
  ```

  Run it daily, then copy the newest file to maintainer-controlled off-platform storage,
  encrypted, with a stated retention window (ADR-0061 `backup-retention-destination`;
  for host loss the RPO is the age of that off-platform copy). Keep `deploy/.env` (the
  connector key) in separate custody, never in the same bundle.
- **Upgrade**: `git pull`, then re-run the same `docker compose … up -d --build`
  command you started with. Migrations run automatically through the serialized migrator.
  **If you enabled live providers in section 5, that command is the two-file one** — a
  base-only `up` drops the quota override and restores the unlimited default while the
  provider key stays enabled:

  ```bash
  docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.llm-quota.yml \
    --env-file deploy/.env --profile baseline up -d --build
  ```
- **Revoke access**: remove the grant in the Access view; revoke a registration
  by deactivating the user; rotate the tunnel URL if it leaks.
- Do not enable MFA on this instance until #1653 lands.
