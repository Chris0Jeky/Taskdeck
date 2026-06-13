# Ops Docs

This folder contains deployment, observability, and human-operator runbooks.

## Deployment

- `DEPLOYMENT_WORKFLOW.md` — Staged blue/green + canary workflow (ADR-0028) — **parked by the archive pivot** (hosted/multi-instance; not the personal run path). The personal release path is the self-contained exe + `RELEASE_CHECKLIST.md` smoke.
- `RELEASE_CHECKLIST.md` — Smoke verification checklist tied to release transitions
- `DEPLOYMENT_CONTAINERS.md` — Container baseline (Dockerfiles, compose, nginx)
- `DEPLOYMENT_HARDENING_MATRIX.md` — Container hardening verification matrix
- `DEPLOYMENT_TERRAFORM_BASELINE.md` — Terraform IaC baseline for AWS

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
