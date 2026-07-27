# Ops Docs

This folder contains deployment, observability, and human-operator runbooks.

## Deployment

- `DEPLOYMENT_WORKFLOW.md` — Staged blue/green + canary workflow (ADR-0028) — **parked by the archive pivot** (hosted/multi-instance; not the personal run path). The beta release path is the self-contained executable build + smoke in `.github/workflows/release-desktop.yml`. `.github/workflows/cd-staging-gate.yml` is manual-dispatch-only after **#1228**; the stacked, unmerged **#1506** candidate replaces its `production` binding with a non-deploying summary and uses masked ephemeral Compose inputs. Run [30242044922](https://github.com/Chris0Jeky/Taskdeck/actions/runs/30242044922) proved build, real smoke/cleanup, environment `none`, and deployment `no` at exact head `81cfbcea`; later review fixes and post-retarget CodeQL still require exact-head proof, while **#1504** activation and merge remain maintainer-owned.
- `RELEASE_CHECKLIST.md` — Smoke checklist for the OPS-09 staged (blue/green/canary) deployment — **parked by the archive pivot** (hosted/multi-instance; requires staging/prod URLs, container images, rollback slots — not the personal run path).
- `DEPLOYMENT_CONTAINERS.md` — Container baseline (Dockerfiles, compose, nginx)
- `DEPLOYMENT_HARDENING_MATRIX.md` — Container hardening verification matrix
- `DEPLOYMENT_TERRAFORM_BASELINE.md` — Terraform IaC baseline for AWS — **parked by the archive pivot** (cloud/multi-instance IaC; reference-only, not the personal run path)

## Operations

- `OBSERVABILITY_BASELINE.md`
- `OBSERVABILITY_SETUP.md`
- `ALERTING_RULES.md` — monitoring thresholds, alert priorities, escalation paths — the hosted/cloud thresholds (CloudWatch/PagerDuty) and its dependency on `CLOUD_REFERENCE_ARCHITECTURE.md` are **reference-only** under the archive pivot; only local/single-instance signals apply
- `SESSION_START_CHECKLIST.md`
- `TASKDECK_HUMAN_OPERATIONS.md`
- `GITHUB_LABEL_TAXONOMY.md`
- `DISASTER_RECOVERY_RUNBOOK.md`
- `INCIDENT_REHEARSAL_CADENCE.md`
- `SBOM_RELEASE_PROVENANCE.md`

## Cloud / Cost (parked by the archive pivot)

These document the de-scoped cloud / multi-instance track and are **reference-only** — Taskdeck is single-instance, SQLite, personal-use (never hosted/scaled out). Each file carries its own de-scope banner.

- `CLOUD_REFERENCE_ARCHITECTURE.md` — cloud target topology + autoscaling reference architecture — **parked** (reference-only)
- `CLOUD_COST_OBSERVABILITY.md` — cloud cost observability (ADR-0026 companion) — **parked** (reference-only)
- `BUDGET_BREACH_RUNBOOK.md` — cloud budget-breach response (ADR-0026 companion) — **parked** (reference-only)
- `COST_HOTSPOT_REGISTRY.md` — cloud cost hotspot registry (ADR-0026 companion) — **parked** (reference-only)
