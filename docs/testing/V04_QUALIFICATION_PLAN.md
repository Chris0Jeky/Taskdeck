# v0.4 qualification programme

Last Updated: 2026-09-10

Tracker: [#2898](https://github.com/Chris0Jeky/Taskdeck/issues/2898). This document prepares future sessions;
all seeded outcome rows are NOT RUN.

Read [feature capabilities](../product/FEATURE_CAPABILITIES.md) and use the
[session template](V04_SESSION_TEMPLATE.md). The [outcome ledger](v04-outcomes.csv) starts
with 44 cases and must be copied or versioned per candidate/session; do not overwrite failed evidence.

## Outcome

Prepare and execute a source-backed qualification programme for v0.4. A release reader should know what each feature actually does, how to use it, expected outcomes, tested combinations, limitations, and which promises are experimental, unavailable, stubbed, planned or deliberately deferred.

This is new QA/documentation work requested on 2026-09-10, separate from implementation delivery #2808. Creating issues or writing matrices does not mean the sessions passed. Existing release authority and the four v0.4 gates remain unchanged; milestone placement alone does not declare a new release blocker.

## Shared execution contract

Every case records: case ID; full commit/tag and artifact digest; deployment/configuration; browser/OS/device; synthetic fixture revision; account/role; experience/detail/theme; prerequisites; numbered actions; expected UI and persisted state; expected request/side effects (including zero writes/egress where applicable); actual result; PASS/FAIL/BLOCKED/NOT RUN/NOT APPLICABLE; evidence; defect link and retest SHA. NOT APPLICABLE needs a stated contract reason. Unrun cells never count as passes.

Fixtures use owner A, collaborating editor B, viewer C and unrelated account D; active, empty and archived boards; normal/due/blocked/completed cards; a pending proposal; private memory/originals; and a named reset procedure. Do not use production data or publish participant recordings, tokens or private answers. Counts, revisions and source hashes are observed from the fixture and recorded before each scenario.

## Sequence

1. Freeze the feature contract and fixture/case inventory on the candidate.
2. Run deterministic API/component/browser invariants and failure-path tests.
3. Run cross-browser/device/accessibility and moderated UX sessions on the same candidate.
4. Exercise upgrade/restore, hosted isolation/operations and relevant Fabric/work-model integrations as their implementations land.
5. Reconcile observed outcomes, defects, remaining human acceptance and release claims. Retest changed seams; reuse evidence only when its input/contract is unchanged.

## Existing owners reused

- #2237 owns performance measurements/threshold derivation; consume its baseline instead of inventing timing promises.
- #1363/#2764 own visual baseline infrastructure/residuals; #1949 owns affordance guards.
- #2243 owns hosted-beta implementation; #1644 and the beta threat model own browser authentication posture.
- #2334 owns clean-from-tag release qualification; #1271 owns actual 10-working-day personal dogfooding and #1325 the friends/family beta path. No agent report substitutes for human use.
- #1308 owns opt-in telemetry; comparison sessions add no implicit collection.

## Completion

- [ ] Linked session issues have executable matrices, fixture/reset instructions and source-linked expected values.
- [ ] Capability catalogue includes implemented, experimental, unavailable/unconfigured, verified stubs, planned/deferred and outside-current-contract categories without conflating them.
- [ ] Candidate result ledger links actual evidence for every required case and explicitly dispositions every uncovered combination.
- [ ] Confirmed trust/data-loss/security defects are fixed and retested or prevent the affected release claim; remaining findings have owners and explicit disposition under existing release policy.
- [ ] Actual participant/device/provider/hosted results are distinguished from synthetic proof; maintainer release acceptance remains explicit.

## Session backlog

- [ ] #2899 — [v0.4 QA-01] Publish the feature contract and capability status catalogue
- [ ] #2900 — [v0.4 QA-02] Rehearse capture, review and explicit apply across UI, CLI and MCP
- [ ] #2901 — [v0.4 QA-03] Compare Classic, Studio, Companion and Unified through task-based UX sessions
- [ ] #2902 — [v0.4 QA-04] Run keyboard, screen-reader, mobile and theme accessibility sessions
- [ ] #2903 — [v0.4 QA-05] Validate thinking, Studio continuity and work-model interactions
- [ ] #2904 — [v0.4 QA-06] Verify private answers, memory, original sources and account lifecycle isolation
- [ ] #2905 — [v0.4 QA-07] Qualify audio intake, transcription consent and grounded-question usefulness
- [ ] #2906 — [v0.4 QA-08] Exercise reminders, quiet states and overlapping request recovery
- [ ] #2907 — [v0.4 QA-09] Rehearse upgrades, backup restore and portable data compatibility
- [ ] #2908 — [v0.4 QA-10] Run hosted-beta isolation, onboarding and operations acceptance drills
- [ ] #2909 — [v0.4 QA-11] Qualify Fabric processing and evidence contracts as v0.4 slices land

 All new qualification issues target milestone v0.4; existing v0.3 prerequisites and v0.5/v0.6 feature work keep their current milestones.

## Initial session matrices

### [v0.4 QA-01] Publish the feature contract and capability status catalogue

Issue: [#2899](https://github.com/Chris0Jeky/Taskdeck/issues/2899).

#### Outcome matrix

| Case | Setup and action | Expected outcome |
| --- | --- | --- |
| CAT-01 | Walk each advertised route/control with its documented prerequisites | A real consequence or explicit unavailable/disabled reason; no inert enabled control. Record the request and persisted consequence. |
| CAT-02 | Compare guide claims with code, feature flags and latest delivery receipts | Each claim has a source, candidate SHA, availability condition, known limit and proving case. |
| CAT-03 | Inspect disabled providers, demo builds and experimental features | Unconfigured is distinct from stubbed; synthetic transport is test evidence, not a production provider. Mark a stub only with exact code evidence. |
| CAT-04 | Reconcile v0.4 work-model/Fabric and later voice/semantic roadmap | Planned work retains its owning issue and actual milestone; no earlier release promise is inferred from shared terminology. |

#### Session design and limits

Current verified stub to catalogue: /workspace/metrics/cohorts (newAutomation enabled by default) calls AutomationMetricsController.GetCohortMetrics, which validates date ranges but returns an empty cohort list until the service exists (source owner #1142; dead-surface disposition #1276). Do not treat that empty list as actual measured inactivity.

Deliver docs/product/FEATURE_CAPABILITIES.md with user entry points, examples, expected state changes, permission/privacy boundaries, persistence/export behavior, errors/recovery, limits, confidence/evidence, dependencies and planned/not-planned disposition. Audit current documentation claims without copying historical suite counts or QA maturity scores as present evidence. “Not planned” means no current committed scope, not a permanent product rejection.

Use the parent’s synthetic A/B/C/D account fixture and evidence contract. Record exact candidate/configuration, baseline counts/revisions, ordered actions, actual UI/request/persisted effects and the case result. Expected values above are acceptance targets grounded in named contracts; revalidate constants against the candidate and explicitly document any approved contract change.


### [v0.4 QA-02] Rehearse capture, review and explicit apply across UI, CLI and MCP

Issue: [#2900](https://github.com/Chris0Jeky/Taskdeck/issues/2900).

#### Outcome matrix

| Case | Setup and action | Expected outcome |
| --- | --- | --- |
| CORE-01 | Capture one known task through the UI/API proposal flow and request a proposal | Capture is durable; proposal is reviewable; board card count remains N until explicit Apply. |
| CORE-02 | In UI/API, preview, approve, then explicitly apply the one-create proposal | Preview and approval leave N cards; successful explicit Apply gives N+1 with correct text/destination and linked audit/provenance. |
| CORE-03 | Reject, lose permission, archive board or change proposal revision before apply | No unauthorized/stale board mutation; visible recovery explains refresh/review; no silent reapproval or resend. |
| CORE-04 | Repeat UI/API apply after a lost receipt; inspect the available CLI and MCP commands separately | UI/API retry resolves the saved outcome without a second created card. MCP proposal tools expose get/list/dismiss, not approve/apply. CLI card add/move directly call application services and require their own permission/retry expectations; do not replay an invented Apply operation. |

#### Session design and limits

Use the same synthetic task where interfaces support it, with separate expected side effects: UI/API proposal approval and apply; MCP proposal reads/dismiss and supported proposal-producing tools with UI/API review handoff; CLI card add/move direct mutations, with claims-first/fresh-machine hardening tracked in #1131. Do not describe CLI authority parity as already proven; include credential absent/invalid/expired and scoped-key read/write boundaries. Check keyboard review focus, stale deep links, readable decision feedback and return to the exact proposal. Inventory actual command names from the current CLI/MCP docs rather than inventing parallel APIs. Consumption of #1940/#2215 fixes is a dependency, not duplicate implementation.

Use the parent’s synthetic A/B/C/D account fixture and evidence contract. Record exact candidate/configuration, baseline counts/revisions, ordered actions, actual UI/request/persisted effects and the case result. Expected values above are acceptance targets grounded in named contracts; revalidate constants against the candidate and explicitly document any approved contract change.


### [v0.4 QA-03] Compare Classic, Studio, Companion and Unified through task-based UX sessions

Issue: [#2901](https://github.com/Chris0Jeky/Taskdeck/issues/2901).

#### Outcome matrix

| Case | Setup and action | Expected outcome |
| --- | --- | --- |
| UX-01 | Switch through all four experiences with an open card and unsaved capture/chat/thinking | Same account, route, card IDs and appropriate unsaved draft remain; zero proposal approval/apply or provider request solely from switching. |
| UX-02 | Repeat one capture-think-review-resume scenario in each experience | Expected task is completed with correct saved result; record time, misclicks, assistance, recovery attempts and participant explanation. |
| UX-03 | Change Zen/Studio/Control and Grove/Grove Night, including Auto appearance | Permission, due/blocker and trust information remain accessible; selected and resolved appearance attribution are accurate. |
| UX-04 | Record, export and import the same comparison observations twice | Stable IDs and configuration/build attribution survive; exact duplicates are skipped; conflicting IDs reject the batch rather than silently overwrite. |

#### Session design and limits

Cover all 4×3×2 named experience/detail/Grove combinations for deterministic shell invariants; cover supported Paper/Legacy renderer combinations and Auto resolution separately. Use representative deeper journeys plus documented pairwise choices instead of asserting every browser/data Cartesian product was tested. Rotate experience order across moderated sessions to reduce practice bias; report raw individual outcomes, no statistical significance or randomized-product A/B claim. Start with a pilot session, then reuse the corrected script with the agreed beta cohort (#1325). Participant numbers and recruitment remain recorded choices, not invented research evidence.

Use the parent’s synthetic A/B/C/D account fixture and evidence contract. Record exact candidate/configuration, baseline counts/revisions, ordered actions, actual UI/request/persisted effects and the case result. Expected values above are acceptance targets grounded in named contracts; revalidate constants against the candidate and explicitly document any approved contract change.


### [v0.4 QA-04] Run keyboard, screen-reader, mobile and theme accessibility sessions

Issue: [#2902](https://github.com/Chris0Jeky/Taskdeck/issues/2902).

#### Outcome matrix

| Case | Setup and action | Expected outcome |
| --- | --- | --- |
| A11Y-01 | Keyboard-only capture, card thinking, review, settings and nested confirmation | Every intended control is reachable and named; focus is visible; no trap; Escape/close returns focus to the invoking control where contracted. |
| A11Y-02 | Use a real screen reader for saving, validation, stale results and proposal decisions | Names/roles/state changes and errors are announced meaningfully; recovery does not depend solely on color or visual location. |
| A11Y-03 | Open card/nested confirmation with a physical mobile keyboard visible; rotate and zoom | Focused fields and primary actions remain reachable; no obscured confirmation or lost draft; inspect real iOS Safari and Android Chrome separately from emulation. |
| A11Y-04 | Audit Grove/Night, Paper/Legacy and reduced-motion/high-zoom states | Measure the applicable WCAG 2.2 AA criteria (4.5:1 normal text, 3:1 large text/non-text where applicable); record exceptions and actual measurements, not a blanket certification. |

#### Session design and limits

Record browser, OS, assistive technology, physical device, viewport, zoom, font size and input method. Check meaningful loading/empty/disabled/error states, touch targets, scroll containment, shortcut collisions and theme changes during dialogs. Automated axe and reviewed visual baselines support the session but do not replace it. Proposed AA acceptance is a QA target requiring measured disposition, not an assertion of current conformance.

Use the parent’s synthetic A/B/C/D account fixture and evidence contract. Record exact candidate/configuration, baseline counts/revisions, ordered actions, actual UI/request/persisted effects and the case result. Expected values above are acceptance targets grounded in named contracts; revalidate constants against the candidate and explicitly document any approved contract change.


### [v0.4 QA-05] Validate thinking, Studio continuity and work-model interactions

Issue: [#2903](https://github.com/Chris0Jeky/Taskdeck/issues/2903).

#### Outcome matrix

| Case | Setup and action | Expected outcome |
| --- | --- | --- |
| WORK-01 | Save a steps layer, create one linked card, retry and remove the original step | Exactly one card/link on retry; current status comes from the real card; removing the layer does not delete that card. |
| WORK-02 | Add a prerequisite, attempt a reverse edge/cycle, then export/import | Valid edge appears in both directions; invalid cycle causes zero graph mutation; import remaps endpoints within the new board. |
| WORK-03 | Choose private plan/Focus, switch Board/List/Horizon, use Make room, reload | Same real card references; plan/focus persists for its owner; Make room does not change card due dates or collaborators’ private plan. |
| WORK-04 | Exercise typed items/parent hierarchy/assignments/custom fields on a candidate that includes their owning implementation | Apply each owning issue’s exact contract, ID/permission/archive/import rules and migrations; otherwise record BLOCKED on that issue, not PASS or stubbed. |

#### Session design and limits

Include WIP-full destinations, read-only/archived boards, deleted child targets, two-tab revisions, lost save receipts and empty plans. Snapshot card count, IDs, due dates, graph revision and per-user plan before/after. The existing dependency graph limit is 500 edges; verify boundary behavior against the candidate constant before seeding 499/500/501 cases. Keep dependency edges distinct from the broader typed-link feature #2092.

Use the parent’s synthetic A/B/C/D account fixture and evidence contract. Record exact candidate/configuration, baseline counts/revisions, ordered actions, actual UI/request/persisted effects and the case result. Expected values above are acceptance targets grounded in named contracts; revalidate constants against the candidate and explicitly document any approved contract change.


### [v0.4 QA-06] Verify private answers, memory, original sources and account lifecycle isolation

Issue: [#2904](https://github.com/Chris0Jeky/Taskdeck/issues/2904).

#### Outcome matrix

| Case | Setup and action | Expected outcome |
| --- | --- | --- |
| PRIV-01 | A and B answer the same shared question; C/D inspect board routes and exports | Shared question may be visible by board permission; private answers/history never appear in another user’s response, board JSON or UI. |
| PRIV-02 | Correct/archive/restore memory and preserve an older original | Current value changes only as requested; immutable original and correction lineage remain readable by the owner; historical originals are clearly labelled. |
| PRIV-03 | Explicitly select a memory/original for Companion, then change source or permission before send | Only previewed selected content is eligible; stale/unauthorized content is rejected; no implicit private retrieval or model resend. |
| PRIV-04 | Export account, erase A in an isolated fixture during pending reads/writes | Owner’s private source graph, representations and receipts follow erasure policy; late work cannot resurrect it; B’s records survive. |

#### Session design and limits

Test account-switch stale success and failure responses, logout, board physical deletion versus archive, native and legacy preserved originals, both account export formats and the board-scoped private download. A board-scoped multi-read download is not an atomic backup or a supported restore format. Record before/after ownership counts and blob/source lineage; sanitize all evidence.

Use the parent’s synthetic A/B/C/D account fixture and evidence contract. Record exact candidate/configuration, baseline counts/revisions, ordered actions, actual UI/request/persisted effects and the case result. Expected values above are acceptance targets grounded in named contracts; revalidate constants against the candidate and explicitly document any approved contract change.


### [v0.4 QA-07] Qualify audio intake, transcription consent and grounded-question usefulness

Issue: [#2905](https://github.com/Chris0Jeky/Taskdeck/issues/2905).

#### Outcome matrix

| Case | Setup and action | Expected outcome |
| --- | --- | --- |
| AI-01 | Record/upload valid audio, deny microphone access, use text fallback and retry a lost upload receipt | Original playback/download is usable; permission failure offers an actionable path; stable retry does not duplicate the original. |
| AI-02 | Inspect destination/model, consent once, receive provisional transcript, review and confirm | No provider request before consent; source bytes and configured destination match; provisional text is not an answer until explicit confirmation; original remains unchanged. |
| AI-03 | Preview one card excerpt and request grounded questions | The model user content equals the previewed excerpt; bounded questions contain valid quoted evidence; stale evidence/permission/budget failures are explicit and do not auto-resend. |
| AI-04 | Run an agreed synthetic/redacted corpus through an explicitly configured live route | Record transcription errors, unsupported claims, useful/duplicate questions, corrections, latency and usage. Report actual outcomes and limitations; deterministic mocks alone cannot pass this row. |

#### Session design and limits

Include silence/noise, short/long supported clips, unsupported size/type, provider unavailable, timeout, cancellation, reload and account erasure interleavings. The browser microphone recorder stops at 60 seconds; selected files have supported-MIME and 2 MiB bounds but no server duration check. A valid longer low-bitrate upload is not a duration defect. Read the remaining model-budget/configuration limits from the candidate and record exact boundary values before execution. Grounded analysis currently produces up to three questions with one-day freshness; include 0/1/3/over-limit and stale cases. No credentials, purchases or private recordings are assumed. General local STT/WhisperX/semantic recall/cloud benchmark roadmap stays with its v0.5/v0.6 owners; existing optional remote transcription is not a stub for those broader promises.

Use the parent’s synthetic A/B/C/D account fixture and evidence contract. Record exact candidate/configuration, baseline counts/revisions, ordered actions, actual UI/request/persisted effects and the case result. Expected values above are acceptance targets grounded in named contracts; revalidate constants against the candidate and explicitly document any approved contract change.


### [v0.4 QA-08] Exercise reminders, quiet states and overlapping request recovery

Issue: [#2906](https://github.com/Chris0Jeky/Taskdeck/issues/2906).

#### Outcome matrix

| Case | Setup and action | Expected outcome |
| --- | --- | --- |
| ATT-01 | Use a new account and eligible existing question; leave reminders disabled | Zero reminder delivery and zero automatic model analysis. Opt-in is explicit. |
| ATT-02 | Enable reminders with typing/dialog/Focus/Zen/hidden-tab suppression; advance controlled clock | No interruption in quiet states; at most two deliveries per UTC day and at least two hours between eligible deliveries across sessions. |
| ATT-03 | Save named-zone weekday/overnight hours and test DST, UTC rollover and enable-only toggles | Window uses the configured IANA zone and starting day; toggles preserve saved window and unsaved edit draft; editing hours does not reset usage allowance. |
| ATT-04 | A save pending → switch to B → B edits/saves → A succeeds or fails → B finishes | A’s completion cannot clear B’s guard; B’s draft remains; an explicit hours save uses B’s current revision exactly once. |

#### Session design and limits

Also test confirmed validation rejection versus uncertain/lost response: a confirmed validation failure retains editable values; uncertain state requires explicit read/reload and does not silently replay the write. Include both settlement orders, multiple tabs, disable/mute/snooze, source deletion and permission loss. Record subjective annoyance/usefulness in actual sessions separately from eligibility-rule correctness; no claim of non-intrusion based on mocked timers.

Use the parent’s synthetic A/B/C/D account fixture and evidence contract. Record exact candidate/configuration, baseline counts/revisions, ordered actions, actual UI/request/persisted effects and the case result. Expected values above are acceptance targets grounded in named contracts; revalidate constants against the candidate and explicitly document any approved contract change.


### [v0.4 QA-09] Rehearse upgrades, backup restore and portable data compatibility

Issue: [#2907](https://github.com/Chris0Jeky/Taskdeck/issues/2907).

#### Outcome matrix

| Case | Setup and action | Expected outcome |
| --- | --- | --- |
| DATA-01 | Upgrade a copied supported prior-release fixture using UPGRADING.md | Migrations succeed or fail safely; card IDs/counts, private histories, links, plans, originals and receipts reconcile to the baseline. |
| DATA-02 | Restore a backup into an isolated instance using the existing runbook | Restored counts/content and decryptability match; measured RPO/RTO and artifact hashes are recorded; original instance is untouched. |
| DATA-03 | Import board exports with linked thinking/dependencies and comparison v2/v3 files | References remap within the new board; private answers stay excluded; duplicates/conflicting IDs follow the documented policy; unsupported formats fail visibly. |
| DATA-04 | Attempt corrupt/truncated/unsupported imports and interrupted migration/restore | No partial unauthorized graph or silent data discard; recovery follows the documented transaction/backup boundary. |

#### Session design and limits

Inventory supported predecessor tags and export schema versions from current compatibility docs; do not guess universal backwards compatibility. Include enabled and disabled optional providers, empty and representative large fixtures, audio blobs, archived/deleted source cases and account export formats. Board-scoped memory JSON is not atomic backup/import; source-storage restore not currently defined must be explicitly NOT APPLICABLE with the limitation cited. Measure performance through #2237, not a made-up release threshold.

Use the parent’s synthetic A/B/C/D account fixture and evidence contract. Record exact candidate/configuration, baseline counts/revisions, ordered actions, actual UI/request/persisted effects and the case result. Expected values above are acceptance targets grounded in named contracts; revalidate constants against the candidate and explicitly document any approved contract change.


### [v0.4 QA-10] Run hosted-beta isolation, onboarding and operations acceptance drills

Issue: [#2908](https://github.com/Chris0Jeky/Taskdeck/issues/2908).

#### Outcome matrix

| Case | Setup and action | Expected outcome |
| --- | --- | --- |
| HOST-01 | Fresh browser follows the enabled registration/invitation mode and first capture-review-apply | The documented user path works with clear setup/egress/cost disclosure; closed/invite-only modes reject unauthorized registration. |
| HOST-02 | A/B/D attempt cross-account object reads/writes, stale sessions and reconnect | No cross-account private data or authority leakage; expired/revoked credentials fail visibly; reconnect does not duplicate writes. |
| HOST-03 | Exercise configured rate/cost/size ceilings and provider/storage outage | Admission fails before prohibited work/charges; operator and user receive bounded useful signals; secrets and private payloads are absent from logs. |
| HOST-04 | Restore the isolated deployment and rehearse an incident/runbook handoff | Documented recovery succeeds within measured/accepted objectives; status/contact/ownership and rollback evidence are real. |

#### Session design and limits

Dependency gates are #2243 and its security/host/backup/identity prerequisites, including #1644 and the beta threat model; this issue does not implement or bypass them. Run privately with synthetic accounts first. Public registration, production data mutation, external spending and credentials require their existing explicit scope. Gate order remains Fabric persistence → processor containment → trusted hosted instance → public hosted beta. Include reverse-proxy HTTPS, browser reload, two-device sessions, security headers/cookies and accessible error states according to the current deployment contract.

Use the parent’s synthetic A/B/C/D account fixture and evidence contract. Record exact candidate/configuration, baseline counts/revisions, ordered actions, actual UI/request/persisted effects and the case result. Expected values above are acceptance targets grounded in named contracts; revalidate constants against the candidate and explicitly document any approved contract change.


### [v0.4 QA-11] Qualify Fabric processing and evidence contracts as v0.4 slices land

Issue: [#2909](https://github.com/Chris0Jeky/Taskdeck/issues/2909).

#### Outcome matrix

| Case | Setup and action | Expected outcome |
| --- | --- | --- |
| FAB-01 | Submit a supported source and execute the existing deterministic capability path | Capture/source/job/run/representation lineage is durable and queryable under the owning contract; duplicate admission does not create unintended duplicate work. |
| FAB-02 | Restart/timeout/cancel a processor; expire a lease; replay a receipt | State transitions, retry eligibility and visible recovery match the queue/worker contract; no ambiguous “success” or duplicate committed outcome. |
| FAB-03 | Feed maliciously oversized/compressed/unsupported input to the isolated processor harness | Configured resource/capability limits stop processing; no uncontrolled child process or forbidden filesystem/network effect. |
| FAB-04 | Inspect/export evidence anchors after representation migration or source deletion | Anchors resolve to the intended typed source/version or clearly report unavailability; private/account boundaries and documented retention remain intact. |

#### Session design and limits

Resolve exact limits, expected job states, exit codes and lease timing from CF-02..CF-07/CF-23 implementations at the pinned candidate. Missing implementations block their scenarios explicitly. Use the existing conformance harness, not a parallel worker architecture. IBlobStore abstraction does not imply object-store hosting is enabled; semantic candidates/resolver, local transcription, meeting understanding and policy routing keep their later milestones. Include compatibility consumers from capture, chat and evidence preview, while preserving explicit Review/Approve/Apply.

Use the parent’s synthetic A/B/C/D account fixture and evidence contract. Record exact candidate/configuration, baseline counts/revisions, ordered actions, actual UI/request/persisted effects and the case result. Expected values above are acceptance targets grounded in named contracts; revalidate constants against the candidate and explicitly document any approved contract change.


