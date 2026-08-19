# ADR-0051: Autonomous Backlog Admission and Agent-Executable Merge Authority

- **Status:** Accepted
- **Date:** 2026-08-18
- **Decision owner:** Maintainer
- **Related:** `#1269`, ADR-0044, ADR-0046, `.agent-harness/tier.json`
- **Supersedes:** the archive-era LIGHT/FULL review proposal in `#1269`, the blanket human-review rule for dependency PRs, and ADR-0013's claim that `CODEOWNERS` itself enforces review

## Context

On 2026-08-18 the maintainer explicitly directed Taskdeck to remove blanket human merge gates and
promote substantially more of the existing backlog. The live authority declaration already said
`push: free` and `merge: free`, but several operational documents still reserved dependency or
workflow review to a maintainer. The active revival plan also allowed only pre-ratified wave work,
which left the Project with no issues in `Now` or `Next` despite a large tracked correctness,
security, reliability, test, and product backlog.

Those restrictions mixed two different concerns:

1. the evidence required to make a change safe; and
2. the identity of the person who performs or approves the merge.

Taskdeck needs the first. Requiring the second for every PR created an owner-click bottleneck
without adding a distinct technical check. The repository already has an exact-head required CI
gate, risk-calibrated review, DCO enforcement, narrow-slice guidance, and explicit boundaries for
external or irreversible actions.

## Decision

1. **PR merging is agent-executable across all file and dependency classes.** An authorized agent
   may merge a PR, including a dependency, workflow, governance, or major-version PR, when the
   explicit task scope permits it and the exact head satisfies `ci-required.yml`, DCO, the global
   `review-and-ship` pipeline, and any seam-specific evidence. No separate maintainer approval or
   owner click is required merely because of the changed path or package category.
2. **`CODEOWNERS` is advisory routing, not merge eligibility.** A requested owner review remains
   useful feedback, but its absence does not block a merge unless live repository protection is
   later changed to require it. Any such repository-setting change remains separately scoped.
3. **This is not platform auto-merge.** A merge still has an accountable coordinator that reads the
   diff, classifies feedback, verifies the exact head and base, and makes the merge decision. A bot
   rule that blindly merges on check completion is neither enabled nor authorized by this ADR.
4. **Existing tracked backlog may be admitted autonomously.** The coordinator may promote an open
   issue from `Pending` to `Next` or `Now` without a new owner decision when it has clear acceptance
   criteria and proving commands, its dependencies and ownership are known, it advances the active
   product direction or correctness/security/reliability substrate, and it does not cross a human-
   only boundary below. The issue need not already be named in a historical REVIVAL or GEN table.
5. **The live queue is finite but ambitious.** `Now` may contain at most four issue items and `Next`
   at most eight. A coordinator replenishes those slots after merges or deliberate parks, finishes
   or parks existing WIP before opening another conflicting lane, and keeps one writer per checkout.
   Promotion of an existing issue does not consume the separate new-issue creation allowance.
6. **New product surface still needs product authority.** A new table, endpoint, mutation path,
   connector type, top-level view, security posture, or other architectural surprise must already
   be authorized by the revival plan or an Accepted ADR/plan amendment. This prevents a larger queue
   from becoming permission for speculative scope.
7. **Human-only boundaries remain narrow and explicit.** Credentials or private data, production
   mutation/deployment, release tags, legal/licensing/trademark decisions, repository or environment
   protection settings, destructive work-loss operations, and subjective dogfooding/beta judgments
   still require their stated human action or separate explicit scope. Their presence on one issue
   does not prevent work on unrelated admitted issues.
8. **Dependency PRs use risk evidence, not a human category gate.** Routine updates use the normal
   exact-head gate. Major updates add compatibility/release-note review and targeted local evidence
   for the changed ecosystem. Security updates add exposure and advisory triage. An unresolved
   licensing, production, or breaking-product decision parks the PR; the word “major” alone does not.

## Alternatives considered

- **Keep pre-ratified waves and ask for each expansion:** rejected because it recreated the empty
  queue that prompted this decision and made safe tracked work depend on coordinator availability.
- **Enable blind GitHub auto-merge for green PRs:** rejected because check completion is not a
  substitute for diff review, feedback disposition, dependency ordering, or exact-base reasoning.
- **Remove all WIP limits:** rejected because parallelism beyond clear ownership lanes increases
  collision and review debt. Four active issues plus eight staged successors is enough to sustain an
  ambitious run while remaining inspectable.
- **Remove the external-action gates too:** rejected because they protect credentials, production,
  legal posture, irreversible history, or genuinely subjective decisions rather than reserving an
  ordinary merge click.

## Consequences

- The standing backlog can feed continuous autonomous delivery instead of stopping when a fixed wave
  empties. The 2026-08-18 snapshot found 122 open issues and roughly 89 plausible agent-executable
  candidates after obvious external and dependency holds; this is an estimate, not blanket admission.
- Four already-green dependency PRs can enter the canonical merge pipeline without waiting for a
  human review category gate. They still require current-base, exact-head proof before merge.
- Review quality remains risk-calibrated and evidence-backed. Removing a mandatory human identity
  does not weaken CI, review, DCO, architecture, security, or product trust invariants.
- The Project board becomes the durable near-horizon queue: `Now` identifies active ownership and
  `Next` identifies the bounded replenishment set; everything else remains `Pending` or `Blocked`.

## References

- `.agent-harness/tier.json`
- `docs/REVIVAL_PLAN.md`
- `docs/GITHUB_PROJECT_AUTOMATION.md`
- `docs/ops/DEPENDENCY_UPDATE_POLICY.md`
- `docs/agentic/OVERNIGHT_LOOP.codex.md`
- `docs/decisions/ADR-0013-ci-topology-reusable-workflows.md`
