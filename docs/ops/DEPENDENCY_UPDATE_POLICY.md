# Dependency Update Policy

Last Updated: 2026-06-13
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
- Security PRs are distinguishable by the security advisory reference in the PR body. GitHub may also auto-apply a `security` label if one exists in the repo. Note: the PR title format is the same as regular Dependabot PRs ("Bump X from Y to Z"), so rely on the body and labels rather than the title to identify security updates.
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

## Pinned / version-capped dependencies

Some dependencies are intentionally held below their latest major via `ignore`
rules in `.github/dependabot.yml`. These must be removed deliberately (as a
coordinated migration), not incidentally.

| Package(s) | Cap | Reason | Remove when |
|---|---|---|---|
| `Microsoft.EntityFrameworkCore`, `.Sqlite`, `.Design`, `.Tools` | major bumps blocked (stay on 8.x) | The project pins the runtime EF Core stack to 8.x (#760/#767). Dependabot otherwise bumps the core package to 9.x while the providers stay on 8.x, desyncing them and reintroducing an ambiguous `ExecuteDeleteAsync` compile break (#1102, #1106). `Microsoft.EntityFrameworkCore.Tools` is now capped here too: although design-time-only (`PrivateAssets`), on its 10.x line it dragged EF Core 9.x in transitively, so it is pinned to 8.0.x with the rest of the EF stack (#1127, ADR-0039). | The runtime EF Core stack is migrated to 9.x+ together, in one PR. |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | major bumps blocked (stay on 8.x) | Must track the EF Core 8.x family it builds on. CPM aligned it down from 9.0.4 to 8.0.11 (#1127, ADR-0039); without the cap Dependabot re-proposes 9.x and re-desyncs it from EF Core. | The runtime EF Core stack is migrated to 9.x+ together, in one PR (with the EF Core packages above). |
| `Microsoft.AspNetCore.SignalR.Client`, `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Http.Polly`, `Microsoft.Extensions.Logging.Abstractions` | major bumps blocked (stay on 8.x) | These had drifted to 10.0.8 while the backend targets `net8.0`. CPM aligned them to the 8.0.x family (#1127, ADR-0039); without major caps the next weekly Dependabot run re-proposes 9.x/10.x and reverts the alignment. | The backend is migrated off `net8.0` as one coordinated migration. |
| `FluentAssertions` | major bumps blocked (stay on 7.x) | FluentAssertions 8.x+ requires a paid commercial license (Xceed); 7.x is the last free line. Maintainer decision on #1088. | The project purchases the Xceed license, or migrates to a free assertion library. |
| `StackExchange.Redis`, `Microsoft.AspNetCore.SignalR.StackExchangeRedis` | major bumps blocked | Both direct Redis packages back the SignalR backplane (ADR-0025) and the optional Redis cache service (`RedisCacheService`), both config-gated and **dormant** in the single-instance default (`Cache:Provider=InMemory`, empty `SignalR:Redis` connection string). Under the archive pivot a major bump carries breaking-change risk on never-exercised runtime paths with no benefit (PR #1224 closed for proposing `StackExchange.Redis` 3.0.0); the backplane package is capped alongside the client so its own major can't reopen the same churn (#1225). | Redis is ever deliberately activated (multi-instance scale-out or Redis cache), as a coordinated change. |

## Central Package Management (backend NuGet)

The backend uses **Central Package Management (CPM)**. Every NuGet version is declared
once in **`backend/Directory.Packages.props`** as a `<PackageVersion>` entry
(`ManagePackageVersionsCentrally = true`). Individual `.csproj` files reference packages
with `<PackageReference Include="..." />` and **must not** carry a `Version=` attribute —
adding one fails restore with **NU1008**.

Practical implications for dependency updates:

- **To bump a version, edit `backend/Directory.Packages.props`, not the `.csproj`.**
  Dependabot edits this central file, so a single bump applies uniformly across all 11
  backend projects (no more per-project version drift).
- The version caps in the table above are enforced via `.github/dependabot.yml` `ignore`
  rules, which act on the package name regardless of where the version is declared, so CPM
  and the caps work together.
- See ADR-0039 for the full rationale (CPM adoption, the 8.x alignment, and the SDK pin).

## .NET SDK pin (`global.json`)

A `global.json` at the **repository root** pins the .NET SDK to **8.0.415** with
**`rollForward: latestFeature`** and `allowPrerelease: false`. The file is at the repo
root (not under `backend/`) because the .NET muxer discovers `global.json` by walking
**upward** from the working directory, and the documented commands and all CI jobs run
`dotnet` from the repo root.

`rollForward` is `latestFeature` (not `latestPatch`): the pin still locks the **8.0**
major.minor line but tolerates feature-band rollovers. CI (`setup-dotnet` with
`dotnet-version: 8.0.x`) and the Docker builds (`mcr.microsoft.com/dotnet/sdk:8.0`) carry
only the newest 8.0.x feature band, so `latestPatch` would break every CI job and Docker
build simultaneously the day Microsoft ships only `8.0.5xx` SDKs. See ADR-0039.
