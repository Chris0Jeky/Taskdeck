# ci/ — Smart CI policy (ADR-0066)

Last Updated: 2026-08-30

| File | What it is |
| --- | --- |
| `policy.v1.json` | The versioned Smart CI policy: risk classes R0–R4, trust classes T0–T4, runner classes, **control paths** (changes that are R4/T2 and always qualify hosted), **path groups** (repository-relative globs → risk floor + lanes), **lanes** (each names the real check context of `ci-required.yml` so the gate can verify it), always-on lanes, label overrides (`ci:full`, `ci:hosted`, `ci:windows-full` — they only widen), and the full-escalation triggers. `mode: shadow` — nothing here changes which jobs run. |
| `schemas/ci-policy.v1.schema.json` | JSON Schema documenting the policy shape. The executable validation is `validatePolicy()` in `scripts/ci/smart-ci/lib/plan.mjs` (no schema library is used in CI yet — the same open decision as the processor manifest, CF-04). |
| `schemas/ci-plan.v1.schema.json` | JSON Schema of the planner receipt (`artifacts/ci-plan.json`). Executable validation: `validatePlan()`. |
| `schemas/ci-run.v1.schema.json` | JSON Schema of the gate receipt (`artifacts/ci-run.json`, the CI-12 ledger seed). |

Glob semantics are deliberately small (`scripts/ci/smart-ci/lib/glob.mjs`): `**` spans directories,
`*` stays inside one path segment, and a pattern matches the whole repository-relative path — so
`*.md` matches only root files and `**/*.md` any depth.

How a change is planned (`scripts/ci/smart-ci/plan.mjs`):

1. changed files (from the API, metadata only) are matched against `controlPaths` and `pathGroups`;
2. trust: tag push → T4; fork / bot / association outside `trustedAssociations` → T3; a control path
   touched → T2; else T1;
3. risk: the highest `riskFloor` among matched groups; any control path → R4;
4. escalation (full plan, hosted): unmapped path, control-path change, planner error, missing
   changed-file list or SHAs, `ci:full`;
5. selected lanes = always-lanes ∪ matched groups' lanes ∪ the risk class's required lanes (∪ the
   Windows family with `ci:windows-full`); everything else is listed as *skipped with a reason*;
6. runner per lane: `trustedOnly` lanes use their self-hosted class only when the effective
   execution mode is `hybrid`/`self-hosted` **and** the change is T1 and not R4/escalated;
   otherwise `hostedFallback`.

Edit the policy in a PR like any control change: it is itself a control path (R4/T2), and
`node --test scripts/ci/smart-ci/*.test.mjs` proves the fixtures. Enabling selection (leaving shadow
mode) is CI-03/CI-05/CI-07/CI-08 work behind the recall report — never a policy-only edit.
