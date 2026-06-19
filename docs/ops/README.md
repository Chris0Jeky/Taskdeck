# Ops Docs

This folder contains deployment, observability, and human-operator runbooks.

## Deployment

- `DEPLOYMENT_WORKFLOW.md` — Staged blue/green + canary workflow (ADR-0028) — **parked by the archive pivot** (hosted/multi-instance; not the personal run path). The personal release path is the self-contained executable build + smoke in `.github/workflows/release-desktop.yml`.
- `RELEASE_CHECKLIST.md` — Smoke checklist for the OPS-09 staged (blue/green/canary) deployment — **parked by the archive pivot** (hosted/multi-instance; requires staging/prod URLs, container images, rollback slots — not the personal run path).
- `DEPLOYMENT_CONTAINERS.md` — Container baseline (Dockerfiles, compose, nginx)
- `DEPLOYMENT_HARDENING_MATRIX.md` — Container hardening verification matrix
- `DEPLOYMENT_TERRAFORM_BASELINE.md` — Terraform IaC baseline for AWS — **parked by the archive pivot** (cloud/multi-instance IaC; reference-only, not the personal run path)

## Operations

- `OBSERVABILITY_BASELINE.md`
- `OBSERVABILITY_SETUP.md`
- `ALERTING_RULES.md` — monitoring thresholds, alert priorities, escalation paths, and Grafana/CloudWatch/PagerDuty integration
- `SESSION_START_CHECKLIST.md`
- `TASKDECK_HUMAN_OPERATIONS.md`
- `GITHUB_LABEL_TAXONOMY.md`
- `DISASTER_RECOVERY_RUNBOOK.md`
- `INCIDENT_REHEARSAL_CADENCE.md`
- `SBOM_RELEASE_PROVENANCE.md`
