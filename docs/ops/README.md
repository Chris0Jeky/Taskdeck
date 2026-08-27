# Ops Docs

This folder contains deployment, observability, and human-operator runbooks.

## Deployment

- `DEPLOYMENT_WORKFLOW.md` — Staged blue/green + canary workflow (ADR-0028) — **parked by the archive pivot** (hosted/multi-instance; not the personal run path). The beta release path is the self-contained executable build + smoke in `.github/workflows/release-desktop.yml`. `.github/workflows/cd-staging-gate.yml` is manual-dispatch-only after **#1228**; the stacked, unmerged **#1506** candidate replaces its `production` binding with a non-deploying summary and uses masked ephemeral Compose inputs. Run [30244212896](https://github.com/Chris0Jeky/Taskdeck/actions/runs/30244212896) proved build, real smoke/cleanup, environment `none`, deployment `no`, and no deployment API record at exact workflow/helper head `3efb7bd4`; this follow-up changes docs only. Post-retarget CodeQL still requires proof, while **#1504** activation and merge remain maintainer-owned.
- `RELEASE_CHECKLIST.md` — Smoke checklist for the OPS-09 staged (blue/green/canary) deployment — **parked by the archive pivot** (hosted/multi-instance; requires staging/prod URLs, container images, rollback slots — not the personal run path).
- `DEPLOYMENT_CONTAINERS.md` — Container baseline (Dockerfiles, compose, nginx)
- `DEPLOYMENT_HARDENING_MATRIX.md` — Container hardening verification matrix
- `DEPLOYMENT_TERRAFORM_BASELINE.md` — Terraform IaC baseline for AWS — **parked by the archive pivot** (cloud/multi-instance IaC; reference-only, not the personal run path)
- `RELEASE_TRUST_AND_DISTRIBUTION.md` — **active** Windows-first signing, installer, supply-chain evidence, direct-distribution, and private-cloud boundary programme
- `EXTERNAL_SERVICES_REGISTER.md` — **active, sanitized** vendor purpose, owner class, expiry/cost risk, and exit-path register; no account or credential evidence

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

## Cloud / Cost

Proposed ADR-0061 and `docs/platform/CLOUD_DEPLOYMENT_GUIDE.md` define the maximum boundary for a possible private shared-instance proof: one application instance, one SQLite volume, a few known users, private access, explicit cost/LLM ownership, and tested backup/restore. Deployment remains blocked until the maintainer records the decisions required by ADR-0061/#1772; this active planning seam grants no account, billing, or deployment authority and does **not** reactivate the older public-cloud, multi-instance, or SaaS architecture below. The files in this subsection remain **reference-only** and each carries its own de-scope banner.

- `CLOUD_REFERENCE_ARCHITECTURE.md` — cloud target topology + autoscaling reference architecture — **parked** (reference-only)
- `CLOUD_COST_OBSERVABILITY.md` — cloud cost observability (ADR-0026 companion) — **parked** (reference-only)
- `BUDGET_BREACH_RUNBOOK.md` — cloud budget-breach response (ADR-0026 companion) — **parked** (reference-only)
- `COST_HOTSPOT_REGISTRY.md` — cloud cost hotspot registry (ADR-0026 companion) — **parked** (reference-only)
