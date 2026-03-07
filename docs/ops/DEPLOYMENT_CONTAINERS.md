# Container Deployment Baseline

Last Updated: 2026-02-25
Issue: `#69` OPS-07 containerized deployment baseline

This runbook defines the minimal production-oriented container baseline for Taskdeck:
- backend API image
- frontend static image
- reverse proxy entrypoint with compression and proxy security headers

Hardening verification follow-through:
- `#142` OPS-16 deployment/container hardening verification matrix (`docs/ops/DEPLOYMENT_HARDENING_MATRIX.md`)

Terraform follow-through:
- `#102` OPS-10 Terraform/IaC baseline (`docs/ops/DEPLOYMENT_TERRAFORM_BASELINE.md`)

## Files

- `deploy/docker/backend.Dockerfile`
- `deploy/docker/frontend.Dockerfile`
- `deploy/docker-compose.yml`
- `deploy/nginx/reverse-proxy.conf`
- `deploy/nginx/frontend.conf`
- `deploy/.env.example`

When Terraform is used, these compose assets remain the workload layer. The Terraform baseline provisions the host/network/storage shell around them; it does not replace the compose deployment model.

## Prerequisites

- Docker Engine with `docker compose` support
- Repository checked out at target commit

## Reproducible Image Builds (Repo Root)

```bash
docker build -f deploy/docker/backend.Dockerfile -t taskdeck-api:local .
docker build --build-arg VITE_API_BASE_URL=/api -f deploy/docker/frontend.Dockerfile -t taskdeck-web:local .
```

## Local Self-Host (Compose Baseline)

1. Copy env defaults and replace secrets:

```bash
cp deploy/.env.example deploy/.env
```

2. Start stack:

```bash
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline up -d --build
```

3. Validate:
- App entrypoint: `http://localhost:8080`
- API via proxy: `http://localhost:8080/api`
- Optional (only when backend runs with `ASPNETCORE_ENVIRONMENT=Development`): `http://localhost:8080/swagger`

4. Stop stack:

```bash
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline down
```

PowerShell helpers from repo root:

```powershell
powershell -File ./scripts/deploy/Upgrade-DockerDesktop.ps1
powershell -File ./scripts/deploy/Check-ContainerHost.ps1
powershell -File ./scripts/deploy/Build-TaskdeckImages.ps1
powershell -File ./scripts/deploy/Start-TaskdeckStack.ps1 -Build
powershell -File ./scripts/deploy/Smoke-TestTaskdeckStack.ps1
powershell -File ./scripts/deploy/Stop-TaskdeckStack.ps1
```

`Start-TaskdeckStack.ps1` now waits for proxy readiness (`/health/ready`) by default before returning.
Use `-SkipReadyWait` only when you intentionally want fire-and-forget startup behavior.

## OPS-16 Hardening Verification

Run the full hardening matrix automation from repo root:

```powershell
powershell -File ./scripts/deploy/Verify-TaskdeckDeploymentHardening.ps1 -Port 8080
```

This command verifies:
- required secret enforcement for compose rendering
- reverse-proxy security header posture
- unauthorized endpoint behavior through the proxy (`401`)
- startup/restart/shutdown reliability for the baseline stack

Matrix details and pass/fail criteria:
- `docs/ops/DEPLOYMENT_HARDENING_MATRIX.md`

## Staging Bootstrap Path

1. Pin environment values in `deploy/.env` (especially `TASKDECK_JWT_SECRET`).
2. Build both images from repo root using the exact commit SHA.
3. Export portable artifacts:

```bash
docker save taskdeck-api:local | gzip -1 > taskdeck-api.tar.gz
docker save taskdeck-web:local | gzip -1 > taskdeck-web.tar.gz
sha256sum taskdeck-api.tar.gz taskdeck-web.tar.gz > taskdeck-images.sha256
```

4. Load on staging host:

```bash
docker load -i taskdeck-api.tar.gz
docker load -i taskdeck-web.tar.gz
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline up -d
```

PowerShell export helper:

```powershell
powershell -File ./scripts/deploy/Export-TaskdeckImages.ps1
```

This writes uncompressed exports:
- `artifacts/container-images/taskdeck-api.tar`
- `artifacts/container-images/taskdeck-web.tar`
- `artifacts/container-images/SHA256SUMS.txt`

## Reverse Proxy Security and Performance Posture

`deploy/nginx/reverse-proxy.conf` applies:
- routing:
  - `/` -> frontend container
  - `/api/*` -> backend container
  - `/hubs/*` -> backend container (WebSocket upgrade enabled)
- forwarded headers:
  - `X-Forwarded-For`
  - `X-Forwarded-Proto`
  - `X-Forwarded-Host`
- response headers:
  - `X-Content-Type-Options: nosniff`
  - `X-Frame-Options: SAMEORIGIN`
  - `Referrer-Policy: strict-origin-when-cross-origin`
  - `Permissions-Policy: geolocation=(), microphone=(), camera=()`
  - `Content-Security-Policy: default-src 'self'; ...`
- gzip compression for common text/json/js/css/svg payloads

## TLS and Forwarded-Header Assumptions

- This baseline terminates HTTP at the containerized Nginx proxy.
- For staging/production TLS, terminate HTTPS at an edge proxy/load balancer and forward plain HTTP only on private network links to this stack.
- Backend forwarded-header handling is enabled in compose via:
  - `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`
- Do not expose the backend container directly to the public internet while trusting forwarded headers.

Health endpoint routing:
- `/health/ready` is routed through reverse proxy to backend for readiness probes.

## Data Persistence

- SQLite data persists in compose volume `taskdeck-db` mounted at `/app/data`.
- Connection string override in compose:
  - `ConnectionStrings__DefaultConnection=Data Source=/app/data/taskdeck.db`

## Registry Authentication Notes

- Local development and compose runs only require Docker Desktop running.
- Registry pulls/pushes in terminal should use `docker login` for each registry as needed.
- CI should use scoped registry tokens/secrets (never personal passwords in scripts or committed files).

## CI Packaging

CI workflow `.github/workflows/ci-required.yml` includes `container-images` job that:
- validates compose rendering
- builds backend and frontend images from repo root
- exports compressed image artifacts and checksums
