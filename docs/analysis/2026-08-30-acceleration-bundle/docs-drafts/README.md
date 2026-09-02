# Docs drafts — source material, never canonical

Archived verbatim from `08_DOCS_DRAFTS`. Each draft is raw material for one canonical document and must be
fact-checked against current code before any sentence moves. None is linked from `docs/INDEX.md`.

| Draft | Canonical home when adopted | Owning issue | Disposition (2026-09-02) |
| --- | --- | --- | --- |
| `BACKUP_RESTORE_RUNBOOK.md` | `docs/ops/` | `#2238` (closed) → `#1772` | **Largely superseded**: PR `#2361` shipped the encrypted backup/restore commands and rewrote `docs/ops/DISASTER_RECOVERY_RUNBOOK.md`; use this draft only for the restore-drill timing template |
| `HOSTED_BETA_RUNBOOK.md` | `docs/ops/` | `#2243` | Source material; the gate ladder must match `../architecture/HOSTED_BETA_READINESS_MODEL.md` as validated |
| `LAUNCH_KIT_OUTLINE.md` | `docs/product/LAUNCH_KIT.md` | `#2242` (v0.3 downloadable) / `#1310` (v0.4 hosted) | Source material for both; keep the two kits distinct |
| `TELEMETRY.md` | `docs/TELEMETRY.md` | `#1308` | Option B (opt-in) ruled 2026-08-29 (q-5) and `docs/TELEMETRY.md` ships on `main`; blocked on endpoint ownership, retention and aggregate-publication values — no transport is authorized |
| `CI_WEEKLY_REPORT_TEMPLATE.md` | `docs/ci/` | `#2336` | Source material; must consume the shipped Smart CI receipts, not the bundle's schema |
| `PERFORMANCE_BASELINE_TEMPLATE.md` | `docs/analysis/performance/` | `#2237` | Source material; the authoritative baseline waits for the final `v0.3.0` tag |
| `PROCESSOR_CONFORMANCE_CHECKLIST.md` | `docs/architecture/` | `#2258` | Source material; Worker Protocol v1-alpha on `main` is the contract, not the draft |
