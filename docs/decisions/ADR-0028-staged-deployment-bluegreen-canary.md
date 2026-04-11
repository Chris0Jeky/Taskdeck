# ADR-0028: Staged Deployment Workflow — Blue/Green with Canary Verification

- **Status**: Accepted
- **Date**: 2026-04-09
- **Deciders**: Project maintainers

## Context

Taskdeck's production deployment currently follows a direct-replace model: a single Docker Compose stack is updated in place on the target host. This approach provides no mechanism for staged rollout, traffic-shifted verification, or instant rollback without downtime. As Taskdeck moves toward cloud hosting (v0.2.0 in the platform expansion strategy, ADR-0014), a formal deployment strategy is needed to:

1. Prevent bad releases from reaching all users simultaneously.
2. Provide deterministic rollback within seconds, not minutes.
3. Gate production promotion on automated smoke verification.
4. Support the review-first trust model by treating deployment as a reviewable, auditable process.

The deployment model must work for both the current single-node Terraform/Compose topology and future multi-node or container-orchestrated environments.

## Decision

Adopt a **blue/green deployment model with canary verification gates** for staging-to-production promotion.

### Blue/Green Core

- **Two identical environments** (blue and green) exist behind a traffic router (nginx upstream groups on single-node; ALB target groups on AWS).
- At any time, one environment is "live" (serving production traffic) and the other is "idle" (available for the next release).
- A new release is deployed to the idle environment. After passing all verification gates, the traffic router is switched to point at the newly deployed environment.
- The previous live environment remains running as an instant rollback target until the next release cycle.

### Canary Verification Phase

Before full traffic cutover, a canary phase routes a small percentage of traffic (10% default) to the new environment for a configurable observation window (default: 15 minutes). During this window:

- Automated smoke tests run continuously against the canary endpoint.
- Health endpoint (`/health/ready`) is polled at 30-second intervals.
- Error rate and response latency are compared against the live environment baseline.
- If any gate fails, traffic is automatically reverted to the live environment (rollback).

### Promotion Gates (All Must Pass)

1. **Build verification**: CI release pipeline (`ci-release.yml`) passes.
2. **Container image integrity**: SBOM and provenance artifacts generated (`reusable-sbom-provenance.yml`).
3. **Staging smoke**: Automated smoke test suite passes against staging environment.
4. **Canary health**: Canary receives traffic for the observation window with zero critical failures.
5. **Manual approval**: A designated release owner explicitly approves the production promotion (GitHub Actions environment protection rule).

### Rollback Criteria (Any Triggers Rollback)

- Health endpoint returns non-200 for 3 consecutive checks.
- Error rate exceeds 5% of requests during canary window.
- P95 response latency exceeds 2x the baseline measured from the live environment.
- Any smoke test assertion fails.
- Release owner issues manual rollback command.

### Ownership

- **Release owner**: The engineer who initiated the deployment. Responsible for monitoring the canary window and approving or aborting promotion.
- **On-call responder**: Fallback authority to trigger rollback if the release owner is unavailable. Follows the incident rehearsal cadence defined in `docs/ops/INCIDENT_REHEARSAL_CADENCE.md`.

## Alternatives Considered

- **Rolling update (Kubernetes-style)**: Requires container orchestration (ECS/K8s). Taskdeck's current single-node topology does not support this natively. Rolling updates also make rollback slower since old containers are terminated incrementally. Rejected for current phase; may be revisited at v0.4.0+ when multi-node is in scope.

- **Feature flags only (no environment separation)**: Feature flags gate functionality but do not protect against infrastructure-level failures (broken images, migration errors, config drift). Flags complement but do not replace environment-level deployment safety. Rejected as sole strategy.

- **Recreate (stop-then-start)**: Simplest but guarantees downtime during every deployment. Unacceptable for any environment with active users. Rejected.

- **Full canary without blue/green**: Canary alone requires sophisticated traffic splitting at the application layer. Blue/green provides a simpler implementation path for the current infrastructure, with canary as an additive verification phase rather than the primary mechanism. Rejected as standalone.

## Consequences

- **Positive**: Zero-downtime deployments; instant rollback capability; automated safety gates prevent bad releases from reaching all users; deployment process is auditable and reviewable.
- **Positive**: Aligns with review-first trust model — deployments are proposals that must pass gates before being applied.
- **Negative**: Doubles infrastructure cost for the production tier (two environments running simultaneously). Mitigated by using the idle environment for staging/preview workloads between releases.
- **Negative**: Adds operational complexity (traffic routing, health monitoring, promotion scripts). Mitigated by automation and documented runbooks.
- **Neutral**: Single-node topology uses nginx upstream switching; future multi-node uses ALB target groups. The workflow documents are topology-aware.

## References

- Issue: #101 (OPS-09: Staged deployment workflow)
- ADR-0013: CI Topology — Reusable Workflow Decomposition
- ADR-0014: Platform Expansion — Four Pillars
- `docs/ops/DEPLOYMENT_WORKFLOW.md` — canonical workflow document
- `docs/ops/RELEASE_CHECKLIST.md` — smoke verification checklist
- `docs/ops/DEPLOYMENT_CONTAINERS.md` — container baseline
- `docs/ops/DEPLOYMENT_TERRAFORM_BASELINE.md` — Terraform baseline
- `docs/ops/DISASTER_RECOVERY_RUNBOOK.md` — DR procedures
