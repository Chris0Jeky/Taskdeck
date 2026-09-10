# Feature capabilities and expectations

Last Updated: 2026-09-10

This is a user-facing contract inventory for the v0.4 QA programme, not a claim that v0.4 has
shipped or that its sessions passed. Baseline: the overhaul integrated through PR #2895 on main
`b344a5a74`; the overlapping-account reminder fix is in PR #2897 and remains a separate delivery
receipt until merged. Reconcile this inventory against the exact candidate before a QA session.

Use the [qualification plan](../testing/V04_QUALIFICATION_PLAN.md) for expected-result matrices
and the [overhaul guide](WORKSPACE_OVERHAUL.md) for detailed instructions. Delivery tests and their
limits live in the [validation ledger](WORKSPACE_OVERHAUL_VALIDATION.md).

## How to read status

| Status | Meaning |
| --- | --- |
| Implemented | A working integrated path exists, with the cited conditions and limits. This does not mean every device/provider combination is verified. |
| Experimental | Implemented behavior whose usefulness, presentation or policy still needs evaluation. It is not a simulated success path. |
| Optional / unconfigured | Real functionality requires explicit configuration, availability or consent. Disabled does not mean stubbed. |
| Test fixture / prototype | Simulated data or transport used to explore a design or prove a contract; it does not establish production-provider behavior. |
| Planned / deliberately deferred | An owning roadmap issue defines future work. Its milestone is a target, not evidence that implementation exists. |
| Outside the current contract | No current commitment here. This does not permanently reject the idea. |
| Stubbed | A visible behavior is simulated or incomplete in the product itself. Record the exact code and affected entry point before using this label. |

## Working product paths

| Feature and entry point | What to expect | Limits and interactions | Status / QA |
| --- | --- | --- | --- |
| Capture → Review → Apply | Saved input leads to a reviewable proposal. Inspect evidence, approve, then explicitly apply. The board changes at Apply. | Preview, experience changes and approval alone do not apply changes. Stale revisions and missing authority require recovery. UI, CLI and MCP must retain the same authority boundary. | Implemented; [QA-02](https://github.com/Chris0Jeky/Taskdeck/issues/2900). |
| Experience selector | Classic, Studio, Companion and Unified offer different entry points and navigation over the same task data. | Classic remains the default. Switches preserve the mounted route/open card and appropriate drafts; they do not change permissions, provider policy or proposal authority. | Implemented experiment; [QA-03](https://github.com/Chris0Jeky/Taskdeck/issues/2901). |
| Presentation and Appearance | Zen/Studio/Control adjust disclosure independently of experience. Grove/Grove Night add prototype-inspired palettes alongside Paper/Legacy and Auto. | Detail called Studio is separate from the Studio experience. Due/blocker/trust information must remain available. Visual preference and accessibility require direct evaluation. | Implemented; [QA-03](https://github.com/Chris0Jeky/Taskdeck/issues/2901), [QA-04](https://github.com/Chris0Jeky/Taskdeck/issues/2902). |
| Card → Open thinking deck | Ordered note, question, options, steps and thread layers, viewed as Stack or Path. | Shared board thinking is distinct from private answers. Saves use revisions. Creating real work from a step is an explicit action. | Implemented; [QA-05](https://github.com/Chris0Jeky/Taskdeck/issues/2903). |
| Create card from step | Choose title/destination and create a real linked card. Retrying reuses the saved link; displayed status comes from the card. | Removing a thinking layer never deletes its cards. WIP, archive and permission checks apply. A deleted target is unavailable, not silently recreated. | Implemented; [QA-05](https://github.com/Chris0Jeky/Taskdeck/issues/2903). |
| Explore dependencies | Explicit prerequisites appear in both directions; invalid cycles are rejected. | Same-board graph, separate revision, guarded import/remapping. Links do not automatically change deadlines, status or assignments. Broader typed work-item links remain a separate roadmap contract. | Implemented; [QA-05](https://github.com/Chris0Jeky/Taskdeck/issues/2903). |
| Studio Personal Plan / Focus | Private chosen cards and last-worked focus, with Board/List/Horizon views over the same references. | Make room and Plan tomorrow do not reschedule card due dates. Requires a connected backend; backend-less demos hide the private-plan controls. | Implemented; [QA-05](https://github.com/Chris0Jeky/Taskdeck/issues/2903). |
| Companion and source picker | Accountable chat uses explicitly chosen card/thinking/private-memory/original context, with persisted source receipts. | No implicit private retrieval. Changed sources or authority invalidate dispatch. Pending sends and unsaved thinking have continuity guards. Actual model output depends on the configured provider. | Implemented, provider-dependent; [QA-02](https://github.com/Chris0Jeky/Taskdeck/issues/2900), [QA-06](https://github.com/Chris0Jeky/Taskdeck/issues/2904). |
| Preview on board | A checked, read-only proposal layer marks affected saved objects in either board renderer; the authoritative diff describes proposed new objects. | Refresh checks the effective revision. Expiry, board changes or lost access remove stale markers. Open Review leads back to explicit approval and Apply. | Implemented; [QA-02](https://github.com/Chris0Jeky/Taskdeck/issues/2900). |
| Your private answer / Memory | Private answers, statements/assumptions/unknowns, corrections, originals and history. Archive/restore changes active visibility. | Board JSON excludes private answers. Account exports include private originals/history; erasure follows account ownership while preserving collaborators' records. Board-scoped private download is archival JSON, not an atomic backup/import. | Implemented; [QA-06](https://github.com/Chris0Jeky/Taskdeck/issues/2904), [QA-09](https://github.com/Chris0Jeky/Taskdeck/issues/2907). |
| Audio answer / original library | Explicit recording/file intake, immutable upload, playback/download and separate written versions. Confirmation creates a private answer. | Microphone access is requested through the browser. Original audio remains separate from written corrections. Retained archived/deleted-board originals do not reactivate a question or grant new answer authority. | Implemented; physical-device acceptance pending; [QA-07](https://github.com/Chris0Jeky/Taskdeck/issues/2905). |
| Transcription options | Inspect destination/model and consent to one request. Output remains provisional until reviewed and explicitly confirmed. Durable receipts support recovery. | Disabled by default, separate from chat provider. No automatic retry or local WhisperX pipeline is implied. Manual replay/write remains available when the route is unavailable. See [transcription policy](AUDIO_TRANSCRIPTION.md). | Optional experimental live-provider path; [QA-07](https://github.com/Chris0Jeky/Taskdeck/issues/2905). |
| Quiet insights → Analyze now | Explicit structural analysis of blocked work and unknown/needs-review memory, with dismiss/snooze/mute and revalidation. | Structural rules do not call a model, infer semantics or run global background scans. | Implemented; [QA-06](https://github.com/Chris0Jeky/Taskdeck/issues/2904). |
| Grounded questions | Select one card, preview the bounded excerpt, then request a few private questions backed by exact quotes. | Shares Chat quota/kill switch; stale source, denied access or unavailable provider causes a visible refusal. No board write or automatic resend. See [observation policy](GROUNDED_OBSERVATIONS.md). | Experimental, provider-dependent; [QA-07](https://github.com/Chris0Jeky/Taskdeck/issues/2905). |
| Reminder settings | Default-off links to existing revalidated questions, optionally restricted to weekly hours in a named time zone. | Quiet-state suppression and a shared allowance apply. These are in-app reminders while the board is active, not OS notifications or closed-browser scheduling. See [attention policy](WORKSPACE_ATTENTION.md). | Implemented experiment; [QA-08](https://github.com/Chris0Jeky/Taskdeck/issues/2906). |
| Experiences → comparison | Record scenario/outcome, optional ease and notes. Export/import observations across builds with identity and configuration attribution. | Observations are session-only until exported. Import is explicit. No telemetry, random assignment or statistical A/B claim. | Implemented descriptive comparison; [QA-03](https://github.com/Chris0Jeky/Taskdeck/issues/2901). |

## Numeric boundaries to recheck on the candidate

These are current defaults/contracts, not performance targets. Configuration may narrow availability.
Record the effective value in the session before testing the boundary and one value on each side.

| Surface | Current boundary | Source |
| --- | --- | --- |
| Thinking | 40 layers; 50 items per layer | [ThinkingDeck](../../backend/src/Taskdeck.Domain/Entities/ThinkingDeck.cs) |
| Private plan | 40 cards | [PersonalPlan](../../backend/src/Taskdeck.Domain/Entities/PersonalPlan.cs) |
| Dependencies | 500 same-board edges | [overhaul dependency contract](WORKSPACE_OVERHAUL.md) |
| Companion private sources | Five combined memories/originals | [overhaul source contract](WORKSPACE_OVERHAUL.md) |
| Audio originals | 60 seconds; 2 MiB; WebM/Ogg/WAV/MP3/MP4/M4A; up to 50 written versions per recording | [overhaul audio contract](WORKSPACE_OVERHAUL.md) |
| Transcription defaults | Five attempts and 10 MiB per owner/UTC day; 20 receipts per recording; two-minute publication deadline; 64 KiB response / 8,000 text characters | [settings](../../backend/src/Taskdeck.Application/Services/SpeechTranscriptionSettings.cs), [policy](AUDIO_TRANSCRIPTION.md) |
| Grounded questions | Up to three; categories next-step/outcome/dependency, at most one each; exact quote at most 400 characters; one-day freshness | [contract](../../backend/src/Taskdeck.Application/Services/WorkspaceObservationContract.cs), [policy](GROUNDED_OBSERVATIONS.md) |
| Reminder allowance | At most two claims per UTC day, at least two hours apart; visible/focused board polling no more often than five minutes | [attention policy](WORKSPACE_ATTENTION.md) |
| Reminder hours | Inclusive start, exclusive end; overnight belongs to its selected starting day in the saved IANA zone | [window](../../backend/src/Taskdeck.Domain/Entities/WorkspaceAttentionWindow.cs) |
| Comparison | Optional ease 1–5; notes 2,000 characters; 500 observations; 2 MiB import; v2/v3 accepted | [comparison store](../../frontend/taskdeck-web/src/store/workspaceExperimentStore.ts) |

## Planned, deferred and intentionally absent

| Area | Current expectation | Owning work / horizon |
| --- | --- | --- |
| Hosted open registration | Not established by a local build or the overhaul. Trusted hosting, threat model, identity, cost/abuse controls, backups and operational acceptance precede public registration. | [#2243](https://github.com/Chris0Jeky/Taskdeck/issues/2243), v0.4; [QA-10](https://github.com/Chris0Jeky/Taskdeck/issues/2908). Existing private-instance prerequisites retain their own milestones. |
| Expanded work model | Typed items/hierarchy, richer links, participants/assignments/estimates and custom fields must be checked against their own delivered slices. Existing card dependencies/private plans do not imply these are all complete. | [#2087](https://github.com/Chris0Jeky/Taskdeck/issues/2087), [#2092](https://github.com/Chris0Jeky/Taskdeck/issues/2092), [#2093](https://github.com/Chris0Jeky/Taskdeck/issues/2093), [#2094](https://github.com/Chris0Jeky/Taskdeck/issues/2094), v0.4. |
| Fabric foundations | Durable processing lifecycle, worker protocol/containment, representation migration and evidence anchors remain individually owned contracts. Native originals alone do not complete the general platform. | [#2254](https://github.com/Chris0Jeky/Taskdeck/issues/2254), children #2256–#2261/#2276, v0.4 foundation; [QA-11](https://github.com/Chris0Jeky/Taskdeck/issues/2909). |
| General semantic candidates and boardless recall | The selected-card grounded-question producer is not global/vector recall. | [#2262](https://github.com/Chris0Jeky/Taskdeck/issues/2262), [#2263](https://github.com/Chris0Jeky/Taskdeck/issues/2263), v0.5. |
| Packaged local speech / WhisperX / broad voice-note UX | Existing optional transcription does not supply one-click local speech or the general worker-based voice vertical. | [#2267](https://github.com/Chris0Jeky/Taskdeck/issues/2267), [#2268](https://github.com/Chris0Jeky/Taskdeck/issues/2268), [#2270](https://github.com/Chris0Jeky/Taskdeck/issues/2270), v0.5. |
| Policy routing, meeting understanding, runtime outcome dashboard and cloud speech benchmark | Separate later contracts; not inferred from presentation detail, selected context or manual comparison. | [#2264](https://github.com/Chris0Jeky/Taskdeck/issues/2264), [#2269](https://github.com/Chris0Jeky/Taskdeck/issues/2269), [#2271](https://github.com/Chris0Jeky/Taskdeck/issues/2271), [#2277](https://github.com/Chris0Jeky/Taskdeck/issues/2277), v0.6. |
| Source-storage import/restore | No general restore contract follows from private archival downloads. Use supported account/board exports and the actual instance backup runbook for their stated purposes. | Outside this overhaul contract; broader migration ownership includes [#2260](https://github.com/Chris0Jeky/Taskdeck/issues/2260). |
| Autonomous Apply / silent private retrieval / background question generation | Deliberately absent from these features. User intent and review-first authority remain explicit. | Delegated authority has its own separately gated [#2275](https://github.com/Chris0Jeky/Taskdeck/issues/2275); presentation controls do not enable it. |
| Randomized A/B platform, statistical winner, push reminders while closed | Not implemented or promised by the current comparison/reminder features. | Outside their current contract. |

The supplied HTML prototypes simulate local state and model interactions. Repository mock providers
and synthetic browser fixtures are also simulations. Neither is evidence of a hidden production
stub. This inventory has not established a product stub in the integrated overhaul; the broader
route audit in [QA-01](https://github.com/Chris0Jeky/Taskdeck/issues/2899) must name any actual stub
with its exact code and visible consequence. Existing cohort/Ollama and dead-surface questions in
[OUTSTANDING_TASKS](../../OUTSTANDING_TASKS.md) require current verification, not inference from old titles.

## What still needs actual use

Automated passing tests establish their exercised contracts. They do not establish microphone
quality, screen-reader usability, physical keyboard behavior, live-model usefulness, subjective
non-intrusion, a preferred layout or readiness to expose an instance publicly. Record those outcomes
in the QA issues. Human release, signing, hosting and participant decisions remain in
[OUTSTANDING_TASKS](../../OUTSTANDING_TASKS.md); no checkbox is satisfied merely by this guide.
