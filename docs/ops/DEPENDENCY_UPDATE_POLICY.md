# Dependency Update Policy

Last Updated: 2026-03-29
Owner: Repository maintainers
Linked issue: `#148` (OPS-18)

## Purpose

This document defines the dependency update automation policy and security-advisory triage workflow for Taskdeck. It complements the existing vulnerability management policy (`docs/security/SECURITY_DEPENDENCY_VULNERABILITY_POLICY.md`) by adding proactive update automation and structured triage ownership.

Related docs:

- `docs/security/SECURITY_DEPENDENCY_VULNERABILITY_POLICY.md` — vulnerability scan cadence, severity policy, exception process
- `.github/dependabot.yml` — Dependabot automation configuration

## Automation Tool

Taskdeck uses **GitHub Dependabot** for automated dependency update PRs across three ecosystems:

| Ecosystem      | Config directory          | Schedule        | Grouping strategy                          |
|----------------|---------------------------|-----------------|--------------------------------------------|
| NuGet (backend)| `/backend`                | Weekly (Monday) | Minor/patch grouped; major individual      |
| npm (frontend) | `/frontend/taskdeck-web`  | Weekly (Monday) | Minor/patch grouped; major individual      |
| GitHub Actions | `/`                       | Weekly (Monday) | All update types grouped                   |

## Update Categories

### Routine updates (minor and patch)

- Grouped into single PRs per ecosystem to reduce noise.
- Expected to pass CI without intervention in most cases.
- Maintainer review is required before merge; no auto-merge is enabled.
- If CI passes and the changelog shows no breaking changes, merge promptly.

### Major version updates

- Arrive as individual PRs for explicit review.
- Require maintainer assessment of breaking changes, migration guides, and downstream impact.
- Should be tested locally when the changelog indicates API surface changes.
- Target resolution within one development cycle (1 to 2 weeks) unless blocked.

### Security updates

- Dependabot security updates are enabled by default on GitHub and create PRs independently of the weekly schedule.
- Security PRs are clearly distinguishable by the security advisory reference in the PR body and `[security]` prefix in the PR title. GitHub may also auto-apply a `security` label if one exists in the repo.
- Security updates follow the severity-based SLA targets defined below.

### Lock-file-only updates

- Dependabot may propose lock-file-only changes when transitive dependencies have updates.
- These follow the same review process as routine updates.
- Pay attention to transitive security fixes surfaced through lock-file changes.

## PR Verification Expectations

All Dependabot PRs must pass the `ci-required.yml` gate before merge. This includes:

- Backend build and unit tests (domain, application, CLI, API integration)
- Frontend typecheck, lint, unit tests, and E2E smoke
- Architecture boundary tests
- Docs governance checks

Additional verification for dependency PRs:

- Review the Dependabot PR body for changelog links and compatibility notes.
- For NuGet major bumps: verify `dotnet build` succeeds locally and check for deprecation warnings.
- For npm major bumps: verify `npm run build` and `npx vitest --run` succeed locally.
- For GitHub Actions bumps: verify the referenced action version exists and check for breaking changes in the action's release notes.

## Security-Advisory Triage Workflow

### Trigger sources

1. **Dependabot security alerts** — GitHub surfaces these on the Security tab and may auto-create PRs.
2. **Dependabot security update PRs** — automated fix PRs for known advisories.
3. **Nightly dependency security signals** — `nightly-quality.yml` runs vulnerability scans on schedule.
4. **Manual scan** — operators can run `dotnet list ... --vulnerable` and `npm audit` locally (see commands in the vulnerability policy doc).

### Triage ownership

- The maintainer who sees the alert first owns initial triage classification.
- Classification means: confirm severity, determine if the package is runtime/test/build-only, and assign a remediation owner.
- If no maintainer is available within the SLA window, the alert must be escalated (GitHub issue with `Priority I` or `Priority II` label).

### Severity-based response targets

These targets align with the existing vulnerability policy (`docs/security/SECURITY_DEPENDENCY_VULNERABILITY_POLICY.md`):

| Severity | Triage SLA         | Remediation target                                    |
|----------|---------------------|-------------------------------------------------------|
| Critical | Same business day   | Merge fix within 1 business day; block releases       |
| High     | 1 business day      | Merge fix within 3 business days; block releases      |
| Moderate | 5 business days     | Schedule in normal backlog; does not block release     |
| Low/Info | 10 business days    | Batch with routine dependency hygiene                  |

### Triage checklist

For each security advisory or Dependabot security PR:

1. Confirm the advisory applies to Taskdeck's usage of the package (not just presence in the dependency tree).
2. Classify exposure: `runtime`, `test-only`, `build-only`, `local-dev-only`.
3. Check if Dependabot has already opened a fix PR. If yes, prioritize review.
4. If no automated fix exists, create a GitHub issue with the appropriate priority label.
5. If remediation is blocked (no upstream fix available), follow the exception process in the vulnerability policy doc.
6. Post triage outcome (fixed, excepted, or false positive) as a comment on the alert or PR.

### Escalation

- Critical/High findings with no available fix: create a GitHub issue with `Priority I` label and document compensating controls.
- Findings that affect the release pipeline: notify maintainers and block release candidates per the vulnerability policy enforcement rules.

## Policy Boundaries

### What this policy does NOT cover

- Auto-merge: all dependency PRs require human review. Auto-merge may be considered in the future for patch-only grouped updates with passing CI, but is not enabled now.
- Renovate: the project uses Dependabot only. Renovate may be evaluated if Dependabot proves insufficient.
- SBOM generation: tracked separately in `#103`.
- Stricter required-PR blocking for vulnerability findings: tracked as a follow-up in the vulnerability policy doc.

### Review cadence

- Maintainers should review open Dependabot PRs at least weekly (aligned with the Monday generation schedule).
- Stale Dependabot PRs older than 30 days should be investigated: either the update is blocked (needs an issue) or it was overlooked.
