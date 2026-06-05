# ADR-0035: Promote Secret / Dependency / SAST Scans into the Required PR Merge Gate

- Status: Accepted
- Date: 2026-06-05
- Deciders: Repository maintainers
- Related: #1132, ADR-0031 (SAST Scanning with Semgrep), #871 (Gitleaks)

## Context

The required PR gate (`ci-required.yml`) previously ran no secret, dependency, or SAST scan.
Those scans existed only in label-gated `ci-extended`, nightly, and release lanes, so a PR
introducing a hardcoded secret or a high-severity vulnerable dependency could merge unblocked.
#1132 calls for these scans to block merge.

Making a scan merge-blocking is a security-posture and cross-cutting-convention change (per
`CLAUDE.md`, that warrants an ADR). It also has fail-closed availability implications: an enforced
whole-repo scan can block unrelated PRs when a new upstream CVE or analyzer rule lands.

## Decision

Add three scans to `ci-required.yml` as enforcing (fail-on-detect) jobs:

- **Secret Scan** — `reusable-gitleaks` in `pr` scan-mode (`fail-on-findings: true`), guarded to
  `pull_request` events (gitleaks PR mode is invalid on `push`/`merge_group`). PR-diff scoped, so it
  catches newly introduced secrets without tripping on pre-existing test fixtures.
- **Dependency Security** — `reusable-dependency-security-signals` (`enforce-findings: true`),
  scoped to **shipped (production) dependencies** (`frontend-omit-dev: true`, `frontend-audit-level:
  high`). Frontend dev-tooling vulnerabilities do not reach users and are often unfixable without
  major bumps; they remain visible (non-blocking) in the nightly dependency lane.
- **SAST Scan** — `reusable-sast-scanning` (Semgrep, `enforce-findings: true`). Extends ADR-0031's
  enforcement path into the required lane.

The frontend bundle-size budget is also promoted into the required lane (as a step in the existing
`Frontend Unit` job, reusing its build).

### Break-glass

The gates fail closed. Documented escape hatches when a scan blocks unrelated work:

- **Secrets:** `.gitleaks.toml` / `.gitleaksignore` (per-finding allowlist).
- **SAST:** `.semgrepignore` path excludes, or inline `# nosemgrep` comments.
- **Dependency:** remediate the advisory, or temporarily set `enforce-findings: false` on the job
  (a one-line, reviewable, tracked change) while remediating.

A richer per-advisory dependency allowlist consulted directly by the enforce step is a tracked
follow-up (the summarizer currently gates on aggregate high/critical counts).

### Branch protection

`ci-required.yml` has no aggregation gate job; each job publishes an independent check. A reusable
job's check context is `<caller job name> / <inner job name>` — i.e. `Secret Scan / Gitleaks Scan`,
`Dependency Security / Dependency Security Signals`, `SAST Scan / SAST Scan (Semgrep)`. These
contexts must be added to the protected-branch required-status-checks list (a repo-settings action)
for the scans to actually block merge; that step is tracked separately.

## Alternatives Considered

- **Keep scans non-blocking (advisory only).** Rejected: leaves the exact gap #1132 closes — a
  secret or vulnerable production dependency can merge.
- **Enforce on the full dependency tree (including dev deps).** Rejected: dev-tooling advisories
  create high-noise, frequently-unfixable blocks on unrelated PRs without protecting users.
- **One aggregation "gate" job that needs all scans.** Rejected for this change: the repo's existing
  branch protection lists individual reusable-job contexts; restructuring the gating model is a
  larger, riskier change out of scope here.

## Consequences

- Newly introduced secrets, high/critical production-dependency vulnerabilities, and Semgrep
  findings block PR merge once the check contexts are registered in branch protection.
- A new upstream production CVE can block unrelated PRs until remediated or the documented
  break-glass is used; the per-advisory allowlist follow-up will reduce this blast radius.
- The SAST tooling crash (setuptools 82 dropping `pkg_resources`) is fixed by pinning
  `setuptools<81`, so the gate scans rather than failing on a startup error.

## References

- #1132 — Security gate + config hardening
- ADR-0031 — SAST Scanning with Semgrep
- `docs/security/SECURITY_DEPENDENCY_VULNERABILITY_POLICY.md`
