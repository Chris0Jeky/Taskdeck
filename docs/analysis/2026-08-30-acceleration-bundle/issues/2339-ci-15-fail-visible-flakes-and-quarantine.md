# CI-15 — Fail-visible flakes and expiring quarantine (#2339)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) and validated against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). Planning input, not authority: the live issue, ADR-0066 invariant 9 and `docs/ci/SMART_CI.md` §1.9 win. Corrections to the bundle are in the last section.

## Outcome

A rerun may *classify* a failure; it may never erase one. Every temporary quarantine is owned,
justified, expiring, and visible in the weekly report — and an expired entry turns the governance
check red rather than quietly extending itself.

## Live dependencies (verified 2026-09-02)

| Issue | State | Must deliver first | Unblocks |
| --- | --- | --- | --- |
| CI-12 `#2336` receipts | open | The rerun/attempt/first-failure fields in `ci/schemas/ci-run.v1.schema.json`. Without an attempt-aware receipt there is nowhere to retain attempt one — this is the issue's stated dependency and it is real | 02, 04 |
| CI-03 `#2327` gate registration | open | While the gate is advisory, "the required gate treats quarantined tests as advisory only" has no required gate to be advisory *within* | 01 (semantics), 02 |
| CI-10 `#2334` nightly coordinator | open | Every quarantine entry names a *compensating nightly lane*; today `ci-nightly.yml` runs a fixed job list with no way to bind a lane to an entry | 01, 03 |

Slice 01 (schema + governance checker) has **no** blocking predecessor: it is a data file and a
validator, and it is the one thing that makes the other three honest.

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Mode | Startable before predecessors merge? |
| --- | --- | --- | --- | --- |
| `CI15-1-governance` | `ci/quarantine.v1.json` + `ci/schemas/quarantine.v1.schema.json` + an expiry/ownership checker with tests, run in the R4 control lane | — | tooling + control-plane | **Yes — start here.** An empty-but-valid quarantine file plus a red-on-expiry checker is a complete, useful slice on its own |
| `CI15-2-rerun-ledger` | One diagnostic rerun maximum; attempt one preserved in the receipt; a rerun-to-green surfaced as `flaky: <test>` in the gate summary | CI-12 `#2336` | control-plane | No — the retention of attempt one lives in CI-12's receipt fields |
| `CI15-3-cohort` | Fix or quarantine `#2157`, `#2159`, `#2161` (Windows `dev-up`) — fix preferred, and the quarantine records the decision either way | 01 | implementation | **Partly** — fixing them is startable now and is the preferred outcome; *quarantining* them needs 01 |
| `CI15-4-reporting` | Flake rate, quarantine age and inventory in the CI-12 weekly report; repeated flakes update an owned finding | 01, CI-12 `#2336` | tooling | No |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| The invariant | ADR-0066 invariant 9 + `docs/ci/SMART_CI.md` §1.9 — *"Flakes are defects. A rerun may classify, never silently turn a failed required run green."* | **exists as policy** | Accepted; nothing enforces it in code |
| Retry-to-green | ADR-0066 §Alternatives Considered — "Retry-to-green for flaky lanes… Rejected" | **exists as policy** | The rejection is already recorded; this issue implements it |
| Rerun measurement | `docs/ci/CI_BASELINE.md` | **exists** | 58 runs re-run / 62 extra attempts in the 31-day window; `CI` required alone had 55 re-runs and **386 cancelled (32%)**. Cancellation is not flake — the classifier must separate them |
| Windows `dev-up` cohort | `scripts/ci/dev-up.test.mjs`, issues `#2157` / `#2159` / `#2161` | **exists** | The named first-cohort candidates |
| Known red lanes | `#2180` (nightly mobile-safari, red since 2026-08-24 — 7 nightly failures measured), `#1872` (E2E smoke apt timeout, CI-08), `#1210` (ci-extended `startup_failure`) | **exists** | `#2180` is CI-10's ownership per `#2334`; if it is quarantined instead of fixed, the entry belongs here |
| Runner-context evidence | `scripts/ci/collect_runner_context.py` | **exists** | The infrastructure-vs-product classifier's input; do not invent a second collector |
| Per-test timing/results | `scripts/ci/summarize_trx_timing.py` | **exists** | TRX parsing already ships; the flake ledger consumes it |
| `ci/quarantine.v1.json` + schema | — | **new** | Nothing in `ci/` today; `ci/` holds `policy.v1.json`, `README.md` and three schemas |
| Governance checker | — | **new** | Must run in the R4 hosted-control lane so a PR cannot skip it |

**Renewal, not expiry, is the loophole.** An entry with `created_on` and `expires_on` and a 30-day
cap on the interval can be renewed forever by advancing *both* dates. Add an immutable
`first_quarantined_on` and a `renewals` count, make the checker warn at the first renewal and fail at
the second without a maintainer exception that names evidence. Otherwise "expiring quarantine" means
"quarantine with extra typing".

**Test identity must be normalized once.** A parameterized case's TRX display name embeds its
arguments and drifts whenever a fixture changes. Fix the identity to `Namespace.Class.Method`
(arguments excluded) in slice 01 and make both the quarantine file and the flake ledger use it, or
the two ledgers will disagree about whether a test is the same test.

## Implementation plan

**Preflight.** Decide the identity normalization and the timezone rule before writing the schema;
both are load-bearing and both are cheap to get wrong. Read the three `dev-up` issues and attempt the
fix first — the issue says fix is preferred, and three fixes are a better outcome than three entries.

**Producer-owned paths:** `ci/quarantine.v1.json`, `ci/schemas/quarantine.v1.schema.json`,
`scripts/ci/smart-ci/quarantine.mjs` + `quarantine.test.mjs` (match the shipped `node --test` lane —
the bundle's validator is Python).

**Integration-owner seams:** `ci/policy.v1.json` (registering the governance lane),
`.github/workflows/smart-ci-self-test.yml` or the control lane that runs it, `ci/README.md`,
`docs/ci/SMART_CI.md` §1/§8, `docs/STATUS.md` §CI Status.

**Rollout / rollback.** Land the schema and checker with an **empty** entries array first: the
checker is then trivially green and its red paths are proven only by fixtures. Add real entries only
after CI15-3 decides fix-versus-quarantine per test. Rollback is emptying the file — never deleting
the checker, because a deleted checker is indistinguishable from a passing one.

**Definition of done.** An expired entry is red in a test, not in principle. A rerun-to-green appears
as `flaky: <test>` in the gate summary *and* in the receipt. The three Windows `dev-up` issues each
have a recorded disposition. `docs/STATUS.md` §CI Status states the retry policy so no future agent
adds a retry action.

## Test plan

- [ ] Schema/checker: an entry missing any of `test`, `issue`, `owner`, `reason`, `created_on`, `expires_on`, `compensating_coverage` is red, naming the field once (not twice) — `node --test scripts/ci/smart-ci/quarantine.test.mjs`
- [ ] Checker: `expires_on` before today (evaluated in **UTC**) is red; an entry expiring today is still valid until the UTC day ends (correction 2)
- [ ] Checker: `expires_on - created_on > 30 days` without a maintainer exception is red; the default when unspecified is 14 days
- [ ] Checker: an entry whose `first_quarantined_on` is more than the hard maximum before `expires_on` is red regardless of `created_on` — the renewal loophole (correction 3)
- [ ] Checker: a wildcard `test` pattern without `maximum_matches` is red, **and** a pattern matching more than `maximum_matches` real tests is red (correction 4)
- [ ] Checker: a duplicate `test` entry is red
- [ ] Checker: an entry whose linked issue is closed produces a warning with the issue number (correction 5)
- [ ] Checker: an empty `entries: []` document is green
- [ ] Rerun ledger: a job that failed on attempt 1 and passed on attempt 2 is reported `flaky: <test>` and attempt 1's failure is present in the receipt
- [ ] Rerun ledger: a second rerun of the same job is refused — one diagnostic rerun maximum
- [ ] Rerun ledger: a **cancelled** attempt followed by a green attempt is not a flake (386 of 1,205 required runs were cancelled in the baseline window)
- [ ] Rerun ledger: an infrastructure failure (runner context shows the provisioning failure) is classified as infrastructure, and the classifier's low-confidence path defaults to **product** (fail visible)
- [ ] Identity: a `[Theory]` case and its class normalize to the same `Namespace.Class.Method` in both ledgers
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- Parameterized display-name drift after a fixture change — normalized identity absorbs it; an un-normalized one silently un-quarantines the test.
- A quarantined test is renamed: the entry no longer matches anything. The checker must flag an entry matching **zero** tests as stale, not treat it as satisfied.
- Expiry timezone: `expires_on` is a bare date; a runner in UTC and a maintainer in local time disagree by up to a day.
- The linked issue is closed while the entry lives on — warning, and it must reach the weekly report.
- An infrastructure outage causes a mass flake: dozens of "flaky" classifications in one night must not each open a finding. Rate-limit finding creation per run.
- Two genuinely independent failures in one job — the classifier must not attribute both to one flake.
- A quarantined test is the *only* coverage of a boundary: `compensating_coverage` must name a lane that actually runs, and the checker should verify the lane name exists in `ci/policy.v1.json`.
- A rerun launched by a human through the UI rather than the workflow — the one-rerun cap cannot be enforced technically, so the ledger must record and report it instead of pretending.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Schema | `docs/analysis/2026-08-30-acceleration-bundle/candidates/smart-ci/quarantine.v1.schema.json` | A clean, tight entry shape: `additionalProperties: false`, `reason` `minLength: 12`, `compensating_coverage` `minLength: 5`, `maximum_matches` `minimum: 1`, `maintainer_exception` as a `minLength: 8` **string** (evidence, not a boolean) | No `first_quarantined_on` / renewal counter; `format: date` is documentation-only in most validators |
| Validator | `.../candidates/smart-ci/validate_quarantine.py` | The full rule set in ~100 readable lines — dates, duplicates, issue-prefix check, wildcard guard, 30-day cap | `dt.date.today()` is **local** time; the cap is measured `created_on → expires_on` so renewals evade it; treats `maintainer_exception` as truthy (its own schema says string); double-reports a missing field; Python, while the shipped lane is `node --test` |
| Test vector | `.../testing/test-vectors/quarantine.sample.json` | A realistic first-cohort entry for `#2157` with a 14-day expiry and `nightly/windows-dev-up-full` coverage | Its `test` value `Taskdeck.DevUp.Tests.HighVolumeMarkerAcceptanceTests` is illustrative — there is no `Taskdeck.DevUp.Tests` assembly; the `dev-up` checks are `scripts/ci/dev-up.test.mjs` |
| Blueprint | `.../architecture/SMART_CI_DEPTH_BLUEPRINT.md` §Flake governance | The six-line governance contract, adopted verbatim by the live issue | See its validation preface |
| Docs draft | `.../docs-drafts/CI_WEEKLY_REPORT_TEMPLATE.md` | Its "Flakes/quarantine" table is the CI15-4 output shape | Add an "age / renewals" column |

## Corrections to the bundle

1. **Bundle:** "The governance model is clear" and lists it as new. **True:** ADR-0066 invariant 9,
   `docs/ci/SMART_CI.md` §1.9 and the ADR's explicit rejection of retry-to-green are already
   accepted policy on `main`. **Consequence:** this issue is enforcement, not decision. Do not
   re-litigate the model in the PR; cite the ADR.
2. **Bundle validator:** `today = today or dt.date.today()`. **True:** that is the *local* date of
   whatever machine runs it, while the pack's own edge-case list names "expiry timezone".
   **Consequence:** an entry can be valid on a UTC runner and expired on a maintainer's laptop, or
   vice versa. Fix the rule to UTC in the schema text, not just the code.
3. **Bundle validator:** enforces `hard_max_days` over `expires_on - created_on`. **True:** both
   dates are author-supplied, so advancing them together renews indefinitely — the bundle's own
   "avoid: silent expiry extension" is unimplemented. **Consequence:** add an immutable
   `first_quarantined_on` plus a renewal count and cap the *total* quarantined age.
4. **Bundle validator:** requires `maximum_matches` for a wildcard entry but never counts real
   matches. **True:** the pack's stated risk is "wildcard hiding large suite". **Consequence:** the
   check is decorative until it is run against the test inventory — which is the same inventory
   CI-06 `#2330` slice 01 must build. Note the cross-dependency.
5. **Bundle validator:** checks that `issue` starts with `#` or a GitHub URL. **True:** the pack's
   own edge case is "issue closed while entry remains", which a prefix check cannot detect.
   **Consequence:** the checker needs one read-only API call per entry, or the weekly report must
   carry the open/closed state.
6. **Bundle validator:** reports `entry[i].test:required` *and* `entry[i].test:invalid` for one
   missing field. **Consequence:** noisy output on the exact path a maintainer reads under
   time pressure; report each field once.
7. **Bundle:** `maintainer_exception` is read as truthy in `validate_quarantine.py` while
   `quarantine.v1.schema.json` types it as a `minLength: 8` string. **True:** the schema is right and
   the code is wrong — the schema's intent is a recorded justification. **Consequence:** as coded,
   `"maintainer_exception": true` bypasses the 30-day ceiling with no evidence, from inside the same
   file an agent edits. Keep the schema's string form and require it to name an issue or a comment URL.
8. **Bundle:** language is Python. **True:** the shipped Smart CI lane is `.mjs` with
   `node --test scripts/ci/smart-ci/*.test.mjs` as the repository's declared proving check.
   **Consequence:** port the rules; a second toolchain in the control plane is a maintenance tax the
   ADR's "do not fork the control plane" rule is meant to prevent.
