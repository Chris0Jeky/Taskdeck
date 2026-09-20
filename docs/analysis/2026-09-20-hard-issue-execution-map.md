# Hard-issue execution map — 2026-09-20

## Evidence boundary

This is an engineering execution map, not a new roadmap, release acceptance, or exhaustive audit of every open issue. The pass inspected the 100 oldest open issues, the open Priority I search, current open PRs, and the relevant implementation and test seams. Difficulty below is a qualitative judgement about invariants, concurrency, trust boundaries, migration risk, and the evidence needed to falsify a design; it is not a restatement of GitHub priority labels.

Authoritative starting point: `main` at `ecaccb0d2090c19a93908ee874b4600c0a6f6582`. The supplied source archive identifies `6818072c413609fd2b9a9e37c778e866998d5b1e`. GitHub's comparison found five subsequent commits affecting only `.agent-harness/delegation.json` and `docs/STATUS.md`; the runtime and test files examined in the archive are unchanged at the live starting point. Future passes must repeat the live reconciliation rather than treating this statement as permanent.

Existing work is not reimplemented merely because an umbrella issue remains open. In particular, the open CI, proposal-evidence, frontend-race, and capture-triage PRs were checked for overlap before the quota lane was claimed. No visibility, billing, branch-protection, runner-association, publication, credential, or security-policy setting is changed by this map.

## Difficulty map

| Rank | Issue family | Hard part | Smallest useful next proof | Boundary that must not be crossed silently |
| --- | --- | --- | --- | --- |
| 1 | [#1429](https://github.com/Chris0Jeky/Taskdeck/issues/1429), extraction worker containment; [#2258](https://github.com/Chris0Jeky/Taskdeck/issues/2258), worker protocol | OS-enforced memory containment, process lifetime, IPC limits, timeout versus memory-kill classification, and parity between Windows Job Objects and container/cgroup execution | A single-parse worker killed by a bounded fixture, with exactly one content-free failure result and the host remaining healthy | In-process decoder limits are defence in depth, not proof of whole-process containment. Keep the worker path default-off until the supported paths are proved; upstream disclosure remains human-owned |
| 2 | [#3170](https://github.com/Chris0Jeky/Taskdeck/issues/3170), [#2326](https://github.com/Chris0Jeky/Taskdeck/issues/2326), [#2327](https://github.com/Chris0Jeky/Taskdeck/issues/2327), trusted CI and private rehearsal | Trusted base policy versus untrusted PR code, complete reusable-workflow reachability, exact-head receipts, fail-closed selection, and non-vacuous Linux-only evidence | Inventory every Windows selector and its call path; prove malformed, missing, or head-controlled mode inputs cannot suppress required evidence | [#2337](https://github.com/Chris0Jeky/Taskdeck/issues/2337) owns the actual cutover. No agent infers private-mode observation, runner isolation, budget approval, or settings changes from unit tests |
| 3 | [#1435](https://github.com/Chris0Jeky/Taskdeck/issues/1435), quota reservation cold-start concurrency | Distinguishing a genuine database atomicity failure from multiple test hosts, differing database identities, time-window errors, or provider configuration differences | Capture each contender's physical database identity before interpreting admission counts; then exercise the real repository on fresh closed-and-reopened file-backed databases | Historical WAL-visibility explanations are hypotheses until the experiment proves a single database. Do not add startup warmers, global locks, or a counter migration merely to make a faulty harness green |
| 4 | [#1453](https://github.com/Chris0Jeky/Taskdeck/issues/1453), [#1465](https://github.com/Chris0Jeky/Taskdeck/issues/1465), [#1467](https://github.com/Chris0Jeky/Taskdeck/issues/1467), effective revision identity and bounded snapshots | Preserving exactly the revision a decision observed; distinguishing legacy unpinned data from an intentional original-payload decision; returning at most one revision per proposal without drifting from the shared dispatcher | Design the reject-time pin and its legacy/original discriminator first; then parity-test SQL narrowing against the shared resolver across every status and a long history | A constructor timestamp is not commit visibility. Dismissal is filing, not a new decision. Do not duplicate the resolver's rules independently in list, detail, and related-evidence reads |
| 5 | [#1399](https://github.com/Chris0Jeky/Taskdeck/issues/1399), streaming extraction-history batching | Reducing N+1 reads while keeping materialisation bounded even when one artefact has arbitrarily many history rows; preserving caller artefact order and SQLite's timestamp/ID-as-TEXT order | A row-limited, owner-scoped batch cursor with long-history, cross-user, empty-history, ordering, cancellation, and SQL-count tests | A fixed number of artefact IDs does not bound their histories. The current 64 KiB JSON buffer argument is an initial capacity, not a hard memory ceiling |
| 6 | [#1512](https://github.com/Chris0Jeky/Taskdeck/issues/1512), [#1521](https://github.com/Chris0Jeky/Taskdeck/issues/1521), CI-only concurrency failures | Recovering the first causal server exception under real load without mistaking runner slowness, host lifecycle, or SQLite contention for each other | Content-free correlated diagnostics followed by the issue's repeated Windows/Linux reproduction matrix | A green rerun is not a root cause. Do not retry mutations, swallow HubException/HTTP 500, or quarantine away the contract |
| 7 | [#1653](https://github.com/Chris0Jeky/Taskdeck/issues/1653), [#1644](https://github.com/Chris0Jeky/Taskdeck/issues/1644), hosted identity and secret custody | Key custody, plaintext migration, rotation, backup/restore, refresh-session/CSRF semantics, and desktop compatibility | A reviewed design with synthetic raw-database and recovery/rotation acceptance fixtures | The local-first risk treatment does not automatically authorize hosted deployment. Ratification and operational key ownership are separate from implementing encryption |

The first two ranks combine difficult implementation with platform or maintainer evidence. The next three are the strongest deterministic engineering targets that can be progressed in isolated code/test PRs without choosing hosting or commercial policy.

## Execution order for this pass and its continuation

1. **Quota experiment integrity (#1435).** Finish the negative control, repair only the proven seam, restore the quarantined boundary contracts when justified, and add repository-level fresh-file evidence. Keep this independent of CI-control and frontend PRs.
2. **Bounded export cursor (#1399).** Establish the bounded persistence primitive before changing the streaming consumer. Keep the buffered export's existing full-history method and its size guard unchanged. A primitive-only PR must say explicitly that the export N+1 is not yet closed.
3. **Decision identity (#1453 + #1465), then bounded reads (#1467).** Review one schema/semantic decision before one query-translation decision. This order avoids cementing the rejected timestamp heuristic into a new SQL selector.
4. **Containment (#1429) and CI trust (#3170/#2327).** Proceed in independently reviewable protocol, host, platform-proof, and integration slices, respecting existing lane ownership. Do not collapse these into a single cross-platform/control-plane megacommit.

This order selects non-overlapping evidence-producing work; it does not change release milestone membership or the maintainer's accepted priority queue.

## Quota lane: falsifiable plan

Owned seam: `LlmQuotaReservationConcurrencyTests`, a focused cold-start diagnostic, the actual `LlmUsageRecordRepository` test surface, and an evidence note. The initial draft is [PR #3280](https://github.com/Chris0Jeky/Taskdeck/pull/3280).

The archive shows that the repeated-burst tests first access `WebApplicationFactory.Services` from competing tasks, while `TestWebApplicationFactory.ConfigureWebHost` allocates a new database pathname per host configuration. That supplies a concrete alternative hypothesis to stale WAL visibility: competing first accesses may build different test hosts and therefore different files. This is not yet a claimed runtime finding in this map.

The experiment has two independent assertions: all contenders use one database; exactly one contender reserves the single available slot. A failure of the first invalidates the quota experiment rather than proving an enforcement defect. A passing run does not by itself falsify a nondeterministic host race.

After the cause is observed, the permanent tests must keep host construction outside the burst and retain concurrent independent contexts/connections. Separate direct-repository tests must close setup connections before racing the first reservation against one fresh file, cover request, per-user token, and shared-budget limits, and assert persisted reservation rows as well as returned decisions. No eager production keep-alive is needed merely to construct a test host correctly.

The first diagnostic commit omitted `using Xunit` and failed compilation. That authoring error is not negative-control evidence. The corrected diagnostic head is `b6593896b1ac80af15028d6af47733117f6f2f6c`; its current Actions results, later commits, and eventual disposition belong in the PR rather than being guessed here.

## Export lane: design constraints before implementation

The current streaming consumer is `DataExportService.WriteArtefactsTailAsync` / `WriteExtractionHistoryAsync`; the existing buffered batch method is `ArtefactExtractionRepository.GetByArtefactsForUserAsync`. Reusing the latter unmodified would materialise all selected histories before returning.

The proposed bounded primitive must accept an ordered, bounded ID window, bind the requesting user in the database join, and put a hard row `LIMIT` in every payload query. De-duplicate IDs without changing their first-occurrence order; enforce the raw input cap before de-duplication. Preserve each artefact's database ordering rather than sorting GUIDs in .NET. A page/cursor must not keep a database reader or write transaction open while the destination stream applies backpressure.

A 50-row page is a row-count bound, not a process-RSS guarantee. The entity's text limit, UTF-16 string storage, warning metadata, transient overlapping allocations, and escaped JSON must be included in any claimed byte budget. Cancellation and destination failures must dispose the cursor without changing stored history. Concurrent edits do not become a whole-export snapshot merely because queries are batched; any stronger snapshot claim needs its own transaction and operational review.

Required negative controls: more than 50 history rows for one artefact; many artefacts with no history; interleaved caller order; identical timestamps crossing a page boundary; a requested foreign-user ID; duplicate IDs; an oversized raw list; cancellation before the first query; and cancellation while advancing. The consumer-switch PR must additionally compare the old and new emitted bytes and count actual SQL reader commands. Unit-level list sizes alone do not establish query count or ownership.

## Revision lane: decision package

Keep one semantic authority. `AutomationProposalService` now delegates effective-revision selection to the shared `ProposalEffectiveOperationsResolver`; the issue's older private-dispatcher wording is not permission to introduce another authority.

The reject-pin design must explicitly answer what a null pin means for a new rejection of original operations versus legacy rejected rows whose decision-time payload can only be approximated. Prefer an additive, versioned representation with a documented backfill rather than silently changing the meaning of existing `ApprovedRevisionId`. The same decided identity must survive dismissal. A later bounded query may narrow candidates only when parity fixtures prove it returns the resolver's winner, including intentionally original decisions, missing legacy pins, rejected cutoffs, dismissed states, and long histories.

Approval/application safety and rejected-display consistency are distinct acceptance claims. Do not describe a display-only fix as preventing a demonstrated wrong execution when the issue explicitly says the approval pin already protects execution.

## Verification and handoff rules

The supplied workspace has Node and Python but no .NET SDK. One official SDK metadata request failed DNS; no equivalent GitHub network workaround was attempted. C# compilation and API integration evidence therefore come from GitHub Actions at the exact PR head and its recorded synthetic merge. Local SQL experiments, static checks, or an earlier green head do not substitute for that evidence.

For each code PR, record the negative-control result, changed invariant, final source SHA, workflow/run/job identity, checked platforms, and any residual. Keep drafts while checks or review findings remain unresolved. Do not claim an issue closed merely because a preparatory primitive or diagnostic exists. Re-read live main and open PRs before the next lane, preserve concurrent work, and leave human-owned acceptance boxes unchanged.
