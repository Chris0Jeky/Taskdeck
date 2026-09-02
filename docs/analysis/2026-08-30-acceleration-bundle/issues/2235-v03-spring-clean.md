# v0.3 spring clean — reconcile docs, issues, milestones and strategy before the tag (#2235)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) and validated against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). Planning input, not authority: the live issue, the accepted ADRs and `docs/STATUS.md` win. Corrections to the bundle are listed in the last section.

## Outcome

At the v0.3.0 tag, no canonical document claims something that is not true on `main`, no open issue is milestone-less without a reason, and every ADR status matches its header. This is a truth-reconciliation issue, not a dead-code sweep.

## Live dependencies (verified 2026-09-02)

| Dependency | State | Note |
| --- | --- | --- |
| `docs/STATUS.md` | `Last Updated: 2026-08-30` | Does **not** mention #2238, #2239, PR #2361, PR #2360, `DISASTER_RECOVERY_RUNBOOK`, or the shipped backup/restore and connector-verification commands. Grepping `backup|restore|recovery|verify-connectors` in `docs/STATUS.md` returns only unrelated Context-Fabric and release lines. This is the largest single truth gap the tag would carry |
| `OUTSTANDING_TASKS.md` | 42 open rows at the 2026-08-30 reconciliation | Human-only rows RT-1/RT-2/RT-3, CL-1, BEN-1, DIST-1 stay intact and unchecked |
| Ten review-residual comments (2026-08-30) | open on this issue | Each names a file and a defect: `PaperBoardView.vue` duplicate phone-lane width, capture-draft stash restore keyed to no board, `compose-release-notes.test.mjs` invoked by no workflow, `RetiredGeminiProviderArchitectureTests` over-broad whitelist, two `windows_desktop_archive.py` validator narrowings, a `Program.cs` / `RetiredProviderEnvironmentConfiguration.cs` over-claim, and a PWA `registerType: 'prompt'` product ruling |
| #2193, #2185 | open with residuals | The checklist's "shipped by a merged PR but still open" class; curated separately in this directory |
| ADR statuses | ADR-0060 Accepted, ADR-0062 Accepted, ADR-0061 direction-only, ADR-0057 direction-only, ADR-0065 accepted under delegation | `docs/decisions/INDEX.md` must match the headers |
| Repository visibility | **public** (`private: false`, measured 2026-09-02) | The 2026-08-30 directive says the repository goes private for the v0.3.0 release. A human act, not this issue's |

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Startable before predecessors merge? |
| --- | --- | --- | --- |
| `SC-1-status-truth` | Add the shipped recovery/verification work to `docs/STATUS.md` (PRs #2361, #2360; the CLI commands; the container wrappers; `docs/ops/DISASTER_RECOVERY_RUNBOOK.md`; the smoke script), and section-read the head for any other claim that moved | — | **Yes — and it is the highest-value slice.** A tag that ships an undocumented recovery capability understates the release and leaves the operator without a pointer |
| `SC-2-issue-hygiene` | Every open issue in a milestone or carrying an explicit backlog reason in its last comment; the merged-but-open set (#2193, #2185, and siblings) either closed on evidence or with residuals re-filed; labels normalised | — | **Yes**, and it is pure GitHub work with no merge conflicts |
| `SC-3-strategy-ladder` | `docs/REVIVAL_PLAN.md` dateless ladder, hosted beta at v0.4, REVIVAL-12/14 rows re-pointed; `PRODUCT_DIRECTION.md` horizon table; README roadmap paragraph; `IMPLEMENTATION_MASTERPLAN.md` rows | — | Yes, but it is the widest diff — run it alone to avoid conflicting with every other lane |
| `SC-4-adr-index` | `docs/decisions/INDEX.md` statuses match headers; the ADR-0062 timing amendment for #2093 recorded | — | Yes, small and isolated |
| `SC-5-agent-docs` | `AGENTS.md`, `autodoc/AGENT_INDEX.md`, `.codex/memories/00_ACTIVE.md`, `docs/ISSUE_EXECUTION_GUIDE.md` against the shipped seams and `.agent-harness/tier.json` | — | Yes |
| `SC-6-links` | One-shot markdown link check across `docs/**`; fix or annotate every break | SC-1, SC-3 (run last so it checks the final text) | No |
| `SC-7-residual-triage` | Turn the ten tracked review comments into either a fix or a filed issue — one decision each, none silently dropped | — | **Yes.** Each is small and independently ownable |
| `SC-8-record` | One closing comment on #1947 with counts | all | No |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Docs governance gate | `node scripts/check-docs-governance.mjs` | **exists** | Every PR in this series runs it |
| Golden-principles gate | `node scripts/check-golden-principles.mjs` | **exists** | Needed when invariants text moves |
| GitHub-ops governance gate | `node scripts/check-github-ops-governance.mjs` | **exists** | Needed for `.github/ISSUE_TEMPLATE/**` and `AGENTS.md` project-ops changes |
| `Last Updated: YYYY-MM-DD` line | required in `docs/STATUS.md` and `docs/GOLDEN_PRINCIPLES.md` | **exists** | Exact format enforced; do not reformat it |
| Status history rotation | `docs/archive/status-history/` | **exists** | Where anything older than v0.2 goes (#1138 rest) |
| A markdown link checker in CI | — | **absent** | #1138 asks for it in nightly; this issue asks only for the one-shot run |
| `scripts/ci/compose-release-notes.test.mjs` in a workflow | — | **absent** | The MEDIUM CI gap recorded on this issue: 33 tests invoked by nothing |

## Implementation plan

**Preflight.** Read the ten comments — they are not commentary, they are the backlog. Then section-read `docs/STATUS.md`; never bulk-read it.

**Order matters.** SC-1 and SC-2 first: they are the two that change what a reader of the tag believes. SC-3 last among the doc lanes, because it touches the widest set of files and will conflict with anything else in flight. SC-6 after all text is final.

**The rule the issue states and this pass endorses:** never bulk-close. The 2026-07 bulk close bounced 13 of 25. Each issue is re-checked against `main` and closed with its evidence, or annotated with why it stays open.

**Do not** let this become a refactoring pass. Structural work is #2236's. The bundle's "dead-file/export/dependency inventory" framing is the wrong shape for this issue — see corrections.

**Do not** infer a maintainer check-off. `OUTSTANDING_TASKS.md` rows are pruned only on the maintainer's explicit check; the six human-only rows stay.

## Test plan

- [ ] `node scripts/check-docs-governance.mjs` on every PR in the series
- [ ] `node scripts/check-golden-principles.mjs` when invariant text moves
- [ ] `node scripts/check-github-ops-governance.mjs` when `AGENTS.md` project-ops or issue templates move
- [ ] Every `docs/STATUS.md` claim touched in SC-1 traced to a merged PR SHA or a named file on `main` — spot-check three at random in review
- [ ] The `Last Updated:` line matches the exact required format after every edit
- [ ] One-shot markdown link check over `docs/**` with zero unannotated breaks
- [ ] No behaviour change: the backend and frontend builds are untouched by the doc lanes (`git diff --stat` shows no `backend/src` or `frontend/*/src` file in SC-1/3/4/5/6)
- [ ] If SC-7 wires `compose-release-notes.test.mjs` into `ci-required.yml`: `node --test scripts/ci/compose-release-notes.test.mjs` locally first

## Edge cases

- A `docs/STATUS.md` claim that was true at v0.2 and is now false — rotate it into `docs/archive/status-history/` rather than deleting it, so the decision trail survives.
- An issue that a merged PR closed in substance but whose residual is real (#2193, #2185) — closing it loses the residual; leaving it open without a comment loses the reader.
- A link that resolves on `main` but breaks when the repository goes private.
- The milestone rename (v0.3 *Accountable Agents + Downloadable Beta*, v0.4 *Hosted Open Beta + …*) leaving stale titles quoted inside issue bodies and docs.
- A tracked review comment that turns out to be already fixed by a later merge — record that it was checked, do not silently drop it.
- Two doc lanes touching `docs/REVIVAL_PLAN.md` at once.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Audit note | `docs/analysis/2026-08-30-acceleration-bundle/audit-m4/TRACKER_DRIFT.md` | Two findings that belong in SC-2: work-model version drift (issue bodies still saying v0.3 or "blocked on ADR" after ADR-0060/0062 were accepted) and launch drift (#2242 downloadable vs #1310 hosted must not blend) | The rest of the file is per-issue and covered by the sibling curated files |
| Reconciliation | `../RECONCILIATION.md` §"Live-state corrections to the received snapshot" | A worked example of the evidence standard this issue's doc edits should meet | Dated 2026-08-30; `main` has moved since |

## Corrections to the bundle

1. **Bundle pack recommended state:** `after-cutover-measurement`. **True:** the live issue's own direction is *"more tidying up and spring cleaning and reconciliation needs to be done **by v0.3 release**"*. **Consequence:** the pack defers to after the cutover the work the maintainer scheduled before it, and inverts the issue's purpose.
2. **Bundle pack residual:** "Run dead-file/export/dependency and docs-link inventories" / "Delete only after consumer and release-path proof" / "dead export scanner" / "release bundle diff". **True:** the live checklist has eight items and **none** is about dead code, exports or dependencies; all eight are documentation, issue, milestone, ADR and link reconciliation. **Consequence:** the pack describes a different issue. Following it would produce deletion PRs during a release cutover — the one thing the live issue's "release-safe" framing warns against.
3. **Bundle pack:** "Move deeper structural refactors to #2236." **True and worth keeping** — #2236 (REF-0) owns measurement tooling and structural work. **Consequence:** the one pack bullet to adopt.
4. **Bundle pack:** silent on the ten tracked review-residual comments. **True:** they are the majority of this issue's live content, each with a named file. **Consequence:** the pack's residual list misses the concrete, immediately actionable backlog.
5. **Bundle pack:** silent on `docs/STATUS.md`. **True:** STATUS is checklist item 2 and it currently omits the entire #2238/#2239 recovery capability shipped by PRs #2361 and #2360. **Consequence:** the highest-value slice in the issue is absent from the pack.
6. **Bundle pack test evidence:** "Build/test/package before and after". **True:** for a documentation-reconciliation series the proving checks are the three governance scripts plus a link run; a full build/package before-and-after would be ceremony against a diff that touches no source. **Consequence:** right instinct for a deletion PR, wrong gate for this issue.
