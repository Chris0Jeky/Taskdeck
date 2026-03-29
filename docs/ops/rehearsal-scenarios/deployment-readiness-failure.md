# Scenario: Deployment Readiness Failure

Last Updated: 2026-03-29
Issue: `#150` OPS-19 incident rehearsal and recovery evidence program

## Overview

Simulate a Docker Compose deployment where the API container starts but fails readiness checks. Triage whether the failure is in the container build, runtime configuration, networking, or dependent services.

## Pre-Conditions

- Repository checked out at a known commit on `main`.
- Docker Engine with `docker compose` support installed and running.
- `deploy/.env` configured per `deploy/.env.example` (at minimum, `TASKDECK_JWT_SECRET` must be set).
- No other services occupying the default proxy port (8080).
- `curl` or equivalent HTTP client available.

## Injection Method

### Option A: Missing Required Environment Variable

Remove or empty the required `TASKDECK_JWT_SECRET` from `deploy/.env`:

```bash
# Back up the env file
cp deploy/.env deploy/.env.bak

# Remove the JWT secret (compose will fail at render time)
sed -i 's/^TASKDECK_JWT_SECRET=.*/TASKDECK_JWT_SECRET=/' deploy/.env

# Attempt to start
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline up -d --build
```

### Option B: Invalid Database Path

Override the database connection string to point to a non-writable path inside the container:

```bash
# Start with an invalid DB path
TASKDECK_DB_PATH="/readonly/taskdeck.db" \
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline up -d --build

# Or override via environment in docker-compose.override.yml
```

### Option C: Port Conflict on Proxy

Occupy the proxy port before starting compose:

```bash
# Block port 8080
python -m http.server 8080 &
BLOCKER_PID=$!

# Attempt to start compose
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline up -d --build

# Clean up later
kill $BLOCKER_PID
```

### Option D: Corrupted Image Build

Introduce a build failure by temporarily modifying the Dockerfile:

```bash
# Back up
cp deploy/docker/backend.Dockerfile deploy/docker/backend.Dockerfile.bak

# Inject a failing step (add a bad RUN command)
echo "RUN exit 1" >> deploy/docker/backend.Dockerfile

# Attempt to build
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline up -d --build
```

## Expected Diagnosis Path

1. **Check container status**:
   ```bash
   docker compose -f deploy/docker-compose.yml --profile baseline ps
   ```
   Look for containers in `Exited`, `Restarting`, or `Created` (not `Running`) state.

2. **Check container logs**:
   ```bash
   docker compose -f deploy/docker-compose.yml --profile baseline logs api
   docker compose -f deploy/docker-compose.yml --profile baseline logs web
   docker compose -f deploy/docker-compose.yml --profile baseline logs proxy
   ```

3. **Test readiness through the proxy**:
   ```bash
   curl -s http://localhost:8080/health/live | jq .
   curl -s http://localhost:8080/health/ready | jq .
   ```
   If the proxy is up but the API is not, expect a `502 Bad Gateway` from nginx.

4. **Test the API container directly** (bypassing proxy):
   ```bash
   # Get the API container's internal port mapping
   docker compose -f deploy/docker-compose.yml --profile baseline port api 8080
   # Or exec into the proxy container
   docker compose -f deploy/docker-compose.yml --profile baseline exec proxy curl -s http://api:8080/health/ready
   ```

5. **Check build output** (if the build failed):
   ```bash
   docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline build 2>&1 | tail -30
   ```

6. **Verify environment variable injection**:
   ```bash
   docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline config | grep -A5 JWT
   ```

## Recovery Steps

### Missing Environment Variable

1. Restore the env file:
   ```bash
   mv deploy/.env.bak deploy/.env
   ```
2. Restart:
   ```bash
   docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline up -d
   ```

### Invalid Database Path

1. Fix the connection string in the environment configuration.
2. If the container already created a bad state, remove the volume and restart:
   ```bash
   docker compose -f deploy/docker-compose.yml --profile baseline down -v
   docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline up -d
   ```

### Port Conflict

1. Identify and stop the conflicting process.
2. Or change the proxy port:
   ```bash
   TASKDECK_PROXY_PORT=8081 docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline up -d
   ```

### Corrupted Dockerfile

1. Restore the original Dockerfile:
   ```bash
   mv deploy/docker/backend.Dockerfile.bak deploy/docker/backend.Dockerfile
   ```
2. Rebuild:
   ```bash
   docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline up -d --build
   ```

### Full Cleanup

```bash
docker compose -f deploy/docker-compose.yml --profile baseline down -v
```

## Evidence Checklist

- [ ] `docker compose ps` output showing container states
- [ ] Container logs for the failing service(s)
- [ ] Health endpoint responses (or connection errors if unreachable)
- [ ] `docker compose config` output showing effective environment (secrets redacted)
- [ ] Build output if the failure was at build time
- [ ] Commands used to diagnose the root cause
- [ ] Commands used to recover
- [ ] Verification of healthy deployment after recovery (`/health/live` and `/health/ready` both 200)
- [ ] Any findings about error clarity in container logs or compose output

## Related Documents

- `docs/ops/DEPLOYMENT_CONTAINERS.md` -- container deployment baseline
- `docs/ops/DEPLOYMENT_HARDENING_MATRIX.md` -- hardening verification matrix
- `deploy/docker-compose.yml` -- compose configuration
- `deploy/.env.example` -- required environment variables
