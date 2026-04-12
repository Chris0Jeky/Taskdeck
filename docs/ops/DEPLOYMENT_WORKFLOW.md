# Staged Deployment Workflow

Last Updated: 2026-04-09
Issue: `#101` OPS-09 staged deployment with blue/green and canary release policy
ADR: `ADR-0028`

This document is the canonical reference for Taskdeck's staging-to-production deployment workflow. All release processes must follow this workflow unless an emergency hotfix override is explicitly authorized by the release owner.

## Overview

Taskdeck uses a **blue/green deployment model with canary verification gates**. Two identical environments (blue and green) exist behind a traffic router. New releases are deployed to the idle environment, verified through automated and manual gates, then promoted by switching traffic. The previous environment remains available for instant rollback.

```
┌─────────────┐     ┌──────────────┐     ┌───────────────┐     ┌─────────────┐
│   CI Build  │────>│   Staging    │────>│    Canary      │────>│  Production │
│   & Test    │     │  Deployment  │     │  Verification  │     │  Promotion  │
└─────────────┘     └──────────────┘     └───────────────┘     └─────────────┘
       │                   │                     │                     │
   ci-release.yml    Deploy to idle       10% traffic for         Switch 100%
   SBOM/provenance   environment          15 min window           traffic
```

## Prerequisites

- Docker Engine with `docker compose` support on the target host
- SSH access to the target host (key-based, no password auth)
- AWS CLI configured with permissions for SSM parameter access (if using Terraform topology)
- `scripts/deploy/smoke-test.sh` available on the deployment host
- Release owner identified and available for the duration of the deployment

## Environments

| Environment | Purpose | Traffic | Lifecycle |
|---|---|---|---|
| **Staging** | Pre-production validation | Internal only | Receives every release candidate |
| **Blue** | Production slot A | 0% or 100% | Alternates between live and idle |
| **Green** | Production slot B | 0% or 100% | Alternates between live and idle |

The staging environment is a separate instance (Terraform environment `staging`) used for integration testing before any production slot receives the release.

## Phase 1: Build and Artifact Preparation

**Owner**: CI automation (triggered by tag push or release event)

1. Tag the release commit: `git tag v<MAJOR>.<MINOR>.<PATCH>`
2. Push the tag: `git push origin v<MAJOR>.<MINOR>.<PATCH>`
3. CI triggers automatically:
   - `ci-release.yml` runs release build verification
   - `reusable-sbom-provenance.yml` generates SBOM and provenance attestation
   - `reusable-container-images.yml` builds and exports container image artifacts
4. Verify all CI jobs pass before proceeding.

**Artifacts produced**:
- `taskdeck-api:<tag>` container image
- `taskdeck-web:<tag>` container image
- SBOM (CycloneDX JSON)
- Provenance attestation (SHA256 checksums)

**Gate**: All CI jobs must be green. Any failure blocks proceeding to Phase 2.

## Phase 2: Staging Deployment

**Owner**: Release owner

1. Identify the staging host from Terraform outputs:
   ```bash
   cd deploy/terraform/aws/environments/staging
   terraform output ssh_command
   ```

2. SSH to the staging host and pull the new images:
   ```bash
   docker pull <registry>/taskdeck-api:<tag>
   docker pull <registry>/taskdeck-web:<tag>
   ```

3. Update the compose configuration to use the new image tags:
   ```bash
   # On staging host at /opt/taskdeck
   sed -i "s|image: .*taskdeck-api:.*|image: <registry>/taskdeck-api:<tag>|" docker-compose.yml
   sed -i "s|image: .*taskdeck-web:.*|image: <registry>/taskdeck-web:<tag>|" docker-compose.yml
   ```

4. Deploy to staging:
   ```bash
   cd /opt/taskdeck
   docker compose --env-file .env up -d
   ```

5. Run the staging smoke test:
   ```bash
   bash scripts/deploy/smoke-test.sh http://localhost:<port>
   ```

6. Perform manual exploratory testing against the staging URL (see `docs/ops/RELEASE_CHECKLIST.md` for the full checklist).

**Gate**: All smoke tests pass AND manual validation confirms no regressions. Any failure blocks proceeding to Phase 3.

## Phase 3: Production Canary Deployment

**Owner**: Release owner

### Single-Node Topology (nginx upstream switching)

The single-node topology runs blue and green as separate Docker Compose projects on the same host, with nginx upstream groups controlling traffic distribution.

1. Identify the currently live slot:
   ```bash
   # On production host
   cat /opt/taskdeck/active-slot
   # Returns "blue" or "green"
   ```

2. Determine the idle slot:
   ```bash
   ACTIVE=$(cat /opt/taskdeck/active-slot)
   IDLE=$([[ "$ACTIVE" == "blue" ]] && echo "green" || echo "blue")
   echo "Deploying to idle slot: $IDLE"
   ```

3. Deploy the new release to the idle slot:
   ```bash
   cd /opt/taskdeck/$IDLE
   docker compose --env-file ../.env pull
   docker compose --env-file ../.env up -d
   ```

4. Verify the idle slot is healthy:
   ```bash
   bash scripts/deploy/smoke-test.sh http://localhost:<idle-port>
   ```

5. Enable canary traffic (10% to new slot):
   ```bash
   # Update nginx upstream weights
   cat > /opt/taskdeck/nginx/upstream.conf <<UPSTREAM
   upstream taskdeck_backend {
       server ${IDLE}_api:8080 weight=1;
       server ${ACTIVE}_api:8080 weight=9;
   }
   upstream taskdeck_frontend {
       server ${IDLE}_web:8080 weight=1;
       server ${ACTIVE}_web:8080 weight=9;
   }
   UPSTREAM
   nginx -s reload
   ```

6. Monitor the canary window (default: 15 minutes):
   - Poll `/health/ready` on both slots every 30 seconds
   - Compare error rates between slots
   - Watch application logs for exceptions or panics

**Gate**: Canary window completes with all health checks passing and no rollback triggers (see Rollback Criteria below).

## Phase 4: Production Promotion

**Owner**: Release owner (manual approval required)

1. After the canary window passes, switch 100% traffic to the new slot:
   ```bash
   cat > /opt/taskdeck/nginx/upstream.conf <<UPSTREAM
   upstream taskdeck_backend {
       server ${IDLE}_api:8080;
   }
   upstream taskdeck_frontend {
       server ${IDLE}_web:8080;
   }
   UPSTREAM
   nginx -s reload
   ```

2. Update the active slot marker:
   ```bash
   echo "$IDLE" > /opt/taskdeck/active-slot
   ```

3. Run the full smoke test against the production URL:
   ```bash
   bash scripts/deploy/smoke-test.sh https://<production-url>
   ```

4. Keep the previous slot running for at least 1 hour as a rollback target.

5. Record the deployment in the release log:
   ```bash
   echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) v<tag> deployed to $IDLE by <release-owner>" >> /opt/taskdeck/release-log.txt
   ```

**Gate**: Full smoke test passes against production. If it fails, execute immediate rollback.

## Rollback Procedure

Rollback can be triggered at any phase. The procedure depends on which phase the deployment has reached.

### Rollback from Canary (Phase 3)

Remove the canary traffic split and restore 100% to the active slot:

```bash
ACTIVE=$(cat /opt/taskdeck/active-slot)
cat > /opt/taskdeck/nginx/upstream.conf <<UPSTREAM
upstream taskdeck_backend {
    server ${ACTIVE}_api:8080;
}
upstream taskdeck_frontend {
    server ${ACTIVE}_web:8080;
}
UPSTREAM
nginx -s reload
```

Stop the idle slot:

```bash
IDLE=$([[ "$ACTIVE" == "blue" ]] && echo "green" || echo "blue")
cd /opt/taskdeck/$IDLE
docker compose --env-file ../.env down
```

### Rollback from Production (Phase 4)

Switch traffic back to the previous slot:

```bash
CURRENT=$(cat /opt/taskdeck/active-slot)
PREVIOUS=$([[ "$CURRENT" == "blue" ]] && echo "green" || echo "blue")

cat > /opt/taskdeck/nginx/upstream.conf <<UPSTREAM
upstream taskdeck_backend {
    server ${PREVIOUS}_api:8080;
}
upstream taskdeck_frontend {
    server ${PREVIOUS}_web:8080;
}
UPSTREAM
nginx -s reload

echo "$PREVIOUS" > /opt/taskdeck/active-slot
echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) ROLLBACK from $CURRENT to $PREVIOUS by <operator>" >> /opt/taskdeck/release-log.txt
```

### Rollback Time Targets

| Phase | Target Rollback Time | Method |
|---|---|---|
| Canary (Phase 3) | < 30 seconds | Remove canary upstream weight |
| Production (Phase 4) | < 30 seconds | Switch upstream to previous slot |
| Staging (Phase 2) | N/A | Staging is non-user-facing; redeploy previous version |

## Rollback Criteria

Any of the following triggers an immediate rollback:

| Criterion | Threshold | Detection |
|---|---|---|
| Health check failure | 3 consecutive non-200 responses from `/health/ready` | Automated polling (30s interval) |
| Error rate | > 5% of requests return 5xx | Application logs or metrics |
| Response latency | P95 > 2x baseline from live environment | Smoke test comparison |
| Smoke test failure | Any assertion fails | `scripts/deploy/smoke-test.sh` |
| Manual abort | Release owner or on-call decision | Human judgment |
| Database migration failure | Migration throws or leaves schema inconsistent | Startup logs |

## Database Migration Safety

Taskdeck uses EF Core auto-migration on API startup. When blue and green slots share the same SQLite database volume, migrations must be **forward-compatible**:

- **Additive-only migrations**: Add new columns with defaults, add new tables. Never remove or rename columns that the current live version depends on.
- **Two-phase migration pattern**: If a breaking schema change is required, split it across two releases: (1) add the new schema alongside the old, (2) remove the old schema after the previous version is fully retired.
- **Migration order**: The idle slot starts first and applies any pending migrations. If the migration fails, the idle slot will not become healthy and the deployment is blocked at Phase 3 step 4 (health check).
- **Shared database risk**: During the canary window, both slots read/write the same database. If the new migration alters behavior for existing data, ensure the active slot can still function with the migrated schema.

If blue and green are deployed on separate hosts with separate databases (e.g., in a future multi-node topology), migration safety is simplified since each slot has its own schema lifecycle.

## Emergency Hotfix Override

For critical production incidents where the standard workflow would take too long:

1. The release owner or on-call responder can skip the canary phase.
2. Deploy directly to the idle slot and immediately switch traffic.
3. Document the override reason in the release log.
4. Run the full smoke test immediately after traffic switch.
5. File a post-incident review within 24 hours.

Emergency overrides must still pass Phase 1 (CI build) and Phase 2 (staging smoke). Skipping CI entirely is never permitted.

## GitHub Actions Integration

The `cd-staging-gate.yml` workflow automates Phase 1 and Phase 2 gates:

- Triggers on release publish or manual dispatch
- Builds and verifies container images
- Runs the smoke test suite against a CI-hosted staging environment
- Requires manual approval (GitHub environment protection) before Phase 3 can proceed
- See `.github/workflows/cd-staging-gate.yml` for the workflow definition

## Ownership and Escalation

| Role | Responsibility |
|---|---|
| **Release owner** | Initiates deployment, monitors canary, approves promotion or triggers rollback |
| **On-call responder** | Fallback authority for rollback if release owner is unavailable |
| **Platform team** | Maintains deployment tooling, scripts, and Terraform modules |

Escalation path: Release owner (15 min) -> On-call responder (30 min) -> Platform team lead.

## References

- ADR-0028: Staged Deployment — Blue/Green with Canary Verification
- `docs/ops/RELEASE_CHECKLIST.md` — smoke verification checklist
- `docs/ops/DEPLOYMENT_CONTAINERS.md` — container baseline
- `docs/ops/DEPLOYMENT_TERRAFORM_BASELINE.md` — Terraform baseline
- `docs/ops/DISASTER_RECOVERY_RUNBOOK.md` — disaster recovery procedures
- `docs/ops/INCIDENT_REHEARSAL_CADENCE.md` — incident rehearsal schedule
- `.github/workflows/cd-staging-gate.yml` — staging gate workflow
- `scripts/deploy/smoke-test.sh` — portable smoke test script
