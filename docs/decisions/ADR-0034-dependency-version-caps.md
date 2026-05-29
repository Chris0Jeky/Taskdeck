# ADR-0034: Dependency Version Caps via Dependabot Ignore Rules (EF Core 8.x, FluentAssertions 7.x)

**Status:** Accepted

**Date:** 2026-05-29

**Deciders:** Repository maintainers

## Context

Dependabot's grouped NuGet updates repeatedly proposed major-version bumps that the
project does not want, creating recurring, self-reinflating churn:

1. **Entity Framework Core.** The runtime EF stack is intentionally pinned to the **8.x**
   line (see #760/#767). Dependabot's `dotnet-minor-patch` group kept bumping
   `Microsoft.EntityFrameworkCore` to **9.x** while leaving the `.Sqlite` and `.Design`
   providers on 8.x. That version split desynchronizes the providers and reintroduces an
   ambiguous-overload compile error:
   `CS0121: ambiguous between EntityFrameworkQueryableExtensions.ExecuteDeleteAsync and
   RelationalQueryableExtensions.ExecuteDeleteAsync`. It was fixed once in #1102 and
   recurred within the same day on #1106 — each time requiring a manual per-PR pin.

2. **FluentAssertions.** Version **8.x+ requires a paid commercial license** (Xceed);
   **7.x is the last free line**. After the project moved to FluentAssertions 7.2.2
   (#1088, maintainer decision), dependabot immediately re-proposed 8.10.0 (#1117).

Per-PR pins/closes do not stop recurrence: dependabot re-proposes the same majors every
cycle. A durable, declarative cap is needed.

## Decision

Cap these dependencies below their next major via `ignore` rules in
`.github/dependabot.yml`, scoped to `version-update:semver-major` only (minor/patch
updates continue to flow):

- `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Sqlite`,
  `Microsoft.EntityFrameworkCore.Design` — stay on 8.x until the runtime EF stack is
  migrated together in one deliberate PR. `Microsoft.EntityFrameworkCore.Tools` is a
  design-time-only package (`PrivateAssets`) tracked separately on its 10.x line and is
  intentionally **not** capped (it does not affect the runtime compile).
- `FluentAssertions` — stay on the free 7.x line until the project either purchases the
  Xceed license or migrates to a free assertion library.

Each cap is recorded in the "Pinned / version-capped dependencies" table in
`docs/ops/DEPENDENCY_UPDATE_POLICY.md` with its reason and removal condition, so the cap
is discoverable and removed deliberately rather than incidentally.

## Alternatives

- **Per-PR pins/closes (status quo).** Rejected: dependabot re-proposes every cycle;
  unbounded manual toil and a standing risk of an accidental merge.
- **Central Package Management (`Directory.Packages.props`).** A larger refactor; would
  centralize versions but does not by itself stop dependabot proposing a major. Out of
  scope for this decision; the `ignore` rule is the minimal durable fix.
- **Adopt the bumps.** Rejected: EF 9.x is a deliberate non-goal right now; FluentAssertions
  8.x carries a paid-license cost the project chose not to take on.

## Consequences

- Dependabot stops re-proposing EF Core 9.x and FluentAssertions 8.x; the recurring
  `ExecuteDeleteAsync` break and paid-license PRs no longer recur.
- Security/bugfix updates within 8.x (EF) and 7.x (FluentAssertions) still flow normally.
- The caps must be removed **deliberately and atomically** when the project chooses to
  migrate — the EF runtime packages must move to 9.x+ together, and FluentAssertions only
  after a license/library decision. The removal conditions are tracked in the dependency
  policy doc.
- Caps are intentionally narrow (named packages, major-only) to avoid masking other
  legitimate updates.

## References

- `.github/dependabot.yml` (the `ignore` rules)
- `docs/ops/DEPENDENCY_UPDATE_POLICY.md` (Pinned / version-capped dependencies table)
- PRs: #1102, #1106 (EF pins), #1112 (EF ignore rule), #1088 (FluentAssertions 7.x), #1118 (FluentAssertions ignore rule); #1117 (closed v8 bump)
- Prior EF pin rationale: #760/#767
- `docs/agentic/FAILURE_LEDGER.md` (2026-05-29 entries)
