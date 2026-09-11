# Product and trust reconciliation, 2026-09-11

Last Updated: 2026-09-11

This is the bounded assessment requested in [#2984](https://github.com/Chris0Jeky/Taskdeck/issues/2984).
Source was inspected at main `02abedfe983b700eb3619ad2d161382820f54918`;
GitHub delivery, reviews, milestones and leases were refreshed on September 11.
It supersedes the September 10-11 Alpha checkpoint for the facts below, not the repository's
release ruling or other lanes' plans. Historical test receipts are attributed, not rerun here.

## Delivery and residual acceptance

| Surface | Confirmed delivery | Residual and consequence |
| --- | --- | --- |
| True archive/restore | [#2932](https://github.com/Chris0Jeky/Taskdeck/pull/2932), merge `19dc823c3`; [lifecycle interaction](../product/CARD_HIERARCHY.md#archive-and-delete) | Preserve legacy Block semantics. Cumulative multi-restore preview remains [#2926](https://github.com/Chris0Jeky/Taskdeck/issues/2926). |
| Task, Epic, Spike | [#2949](https://github.com/Chris0Jeky/Taskdeck/pull/2949), merge `7695211a0`; [contract](../product/CARD_WORK_ITEM_TYPES.md) | Types do not infer capture classification, recurrence or authority. Follow-ups [#2950](https://github.com/Chris0Jeky/Taskdeck/issues/2950), [#2952](https://github.com/Chris0Jeky/Taskdeck/issues/2952). |
| Same-board hierarchy | [#2965](https://github.com/Chris0Jeky/Taskdeck/pull/2965), merge `86c6f1bdf`; [#2087](https://github.com/Chris0Jeky/Taskdeck/issues/2087) closed; [contract](../product/CARD_HIERARCHY.md) | Three links/four levels. Archive/delete detaches direct children, including archived children; restore does not reattach. No cascade UI or cross-board hierarchy. |
| Active-card calculations | [#2951](https://github.com/Chris0Jeky/Taskdeck/pull/2951), merge `02abedfe9`, and [#2957](https://github.com/Chris0Jeky/Taskdeck/pull/2957), merge `93ca1cd21` | WIP/import/conflict and observation calculations exclude archived cards. This is not proof that every future count/query handles archive correctly. |
| Inbox long-running triage | [#2945](https://github.com/Chris0Jeky/Taskdeck/pull/2945), merge `fad49351f`; [#1585](https://github.com/Chris0Jeky/Taskdeck/issues/1585) closed | Combined status/detail ordering is still open in [#2959](https://github.com/Chris0Jeky/Taskdeck/pull/2959); uncached body residual [#2960](https://github.com/Chris0Jeky/Taskdeck/issues/2960). |
| Review and Apply legibility | [#2942](https://github.com/Chris0Jeky/Taskdeck/pull/2942), merge `e9316fd82`; [#2948](https://github.com/Chris0Jeky/Taskdeck/pull/2948), merge `be0e1349f` | [#2961](https://github.com/Chris0Jeky/Taskdeck/pull/2961) still open. [#2930](https://github.com/Chris0Jeky/Taskdeck/issues/2930) must remain open for delayed hash-pin success announcement even after that PR lands. |
| Archived dependency controls | [#2955](https://github.com/Chris0Jeky/Taskdeck/pull/2955) open, head `38321394c` | Local/current-base review recorded, hosted checks green at this snapshot. Not merged. Permission-refresh residual [#2958](https://github.com/Chris0Jeky/Taskdeck/issues/2958). |
| Multiple assignments | [#2977](https://github.com/Chris0Jeky/Taskdeck/pull/2977) parked, head `29ead8fdf` | [#2240](https://github.com/Chris0Jeky/Taskdeck/issues/2240) is Blocked, Priority II, v0.4. HIGH [#2981](https://github.com/Chris0Jeky/Taskdeck/issues/2981) prevents merge regardless of CI. |
| Estimates/current-state totals | [#2093](https://github.com/Chris0Jeky/Taskdeck/issues/2093) Next, Priority II, v0.4 | Unit decided; no implementation started. Shared-path sequencing follows assignment repair/delivery. |
| Richer typed links | [#2092](https://github.com/Chris0Jeky/Taskdeck/issues/2092) Pending, v0.4 | Existing dependency graph is shipped. Migrate it under the recorded compatibility contract; do not build a competing graph. |

The three unmerged delivery heads are `38321394c` (#2955), `150a1fc63` (#2959) and
`85c085119` (#2961), all qualified locally against main `02abedfe9`. The latter two and #2977
still had pending hosted checks at the snapshot. Check live head, base, CI and review threads
before delivery; this document grants no merge eligibility.

Hierarchy review residuals remain separately tracked in
[#2966](https://github.com/Chris0Jeky/Taskdeck/issues/2966),
[#2967](https://github.com/Chris0Jeky/Taskdeck/issues/2967),
[#2968](https://github.com/Chris0Jeky/Taskdeck/issues/2968),
[#2969](https://github.com/Chris0Jeky/Taskdeck/issues/2969) and
[#2974](https://github.com/Chris0Jeky/Taskdeck/issues/2974).
Their existence does not reopen the delivered parent acceptance or justify a broad fix cascade.

## Decisions, design choices and boundaries

**Maintainer decisions:** true archive/restore; three parent-child links/four levels;
[explicit mapping of imported assignees](https://github.com/Chris0Jeky/Taskdeck/issues/2240#issuecomment-5626095831)
to eligible destination participants before Apply;
effort time stored in minutes and displayed as hours/minutes. The first two are already in
[ADR-0060](../decisions/ADR-0060-canonical-work-model-and-compatibility-path.md).
The [estimate ruling](https://github.com/Chris0Jeky/Taskdeck/issues/2093#issuecomment-5627296488)
complements [ADR-0062](../decisions/ADR-0062-custom-fields-aggregates-and-threshold-rules.md).
None remains an unanswered product question.

**Assignment implementation contract, still unmerged:** reuse active User identities and the
owner-or-board-access union. An owner need not have a BoardAccess row; an eligible Viewer may
be assigned without receiving write access. Revoke/erasure detaches assignments atomically with
audit and post-commit notification, including archived cards. New-board import belongs to the
importer, so the initial choices are Me/importer or explicit Unassigned. No name matching,
source identity resurrection, membership grant or invitation follows from import.

**Estimate implementation choices, not additional maintainer rulings:** the
[residual contract](https://github.com/Chris0Jeky/Taskdeck/issues/2093#issuecomment-5627326866)
keeps null distinct from explicit zero, derives active-card totals on read and counts each card
once per board. A multi-assigned card contributes its full estimate to each participant, so
person totals overlap and cannot be added to recover the board total. Parent estimates are
independent; summing or overwriting them from descendants is not implied. Numeric bounds,
overflow behavior and final UI placement still need explicit implementation notes and tests.

**Typed-link design assumptions, not new accepted decisions:** the
[source audit](https://github.com/Chris0Jeky/Taskdeck/issues/2092#issuecomment-5627271725)
proposes directed duplicates/spawned-from and canonical symmetric relates-to edges. Confirm
the exact vocabulary/directions and per-kind cycle rules against issue acceptance before code.
Accepted same-board scope and independent hierarchy/type semantics remain binding.

No new ADR is needed to re-ratify these decisions. The existing ADRs receive dated clarifications.
New Project/WorkItem placement or identity tables, cross-board containment, WorkLog and stages
4-5 still require their own existing evidence/amendment gates. Optional cascade archive is
permitted direction, not a delivered behavior or an invitation to extend the hierarchy PR.

## Architecture implications

The shipped stage-2 model remains Board → Column → Card. Type, parent and archive state are
orthogonal card properties, not new identities. This lets capture and proposals target existing
card IDs while progressively adding useful work semantics. It also makes lifecycle correctness
a cross-cutting obligation: counts, participant views, imports, graphs, history and receipts must
each declare whether they include archived work.

[CardHierarchy](../../backend/src/Taskdeck.Domain/Entities/CardHierarchy.cs) validates the complete
graph, including archived descendants. [Hierarchy mutations](../../backend/src/Taskdeck.Application/Services/CardService.Hierarchy.cs)
pin child IDs, state and versions in a detach fingerprint. The board concurrency token protects
graph decisions without changing metadata recency. Later estimates/assignments must preserve
these version/conflict semantics; a new scalar does not justify bypassing concurrent lifecycle checks.

[Dependency storage](../../backend/src/Taskdeck.Infrastructure/Repositories/BoardDependencyRepository.cs)
and [lifecycle invalidation](../../backend/src/Taskdeck.Infrastructure/Repositories/CardRepository.cs)
retain a separate graph revision, including an empty graph. The active projection can hide
archived endpoints while retaining their edges. For #2092, old `A dependsOn B` means `B blocks A`;
the legacy full-graph adapter must replace only its active dependency subset and preserve archived
edges and other relation kinds. One canonical edge store plus revision metadata is sufficient.

Migration is a data contract, not just an additive schema script. Existing JSON can contain
references hidden after hard deletion. Typed-link Up needs an explicit handling policy; Down
must reconstruct the current dependency subset, not restore a stale pre-migration JSON copy
that resurrects deleted edges. Non-dependency kinds cannot be losslessly converted to the old
format. Record that loss boundary before shipping.

Board JSON versions are separate from database migration versions. Plain/v2 imports remain
supported; hierarchy exports require v3 and fresh ID remapping. Assignment exports propose v4
with explicit participant mapping. Account exports remain scoped exports, not a general restore
mechanism. Test the combined archive/type/parent/assignment graph, not just isolated new fields.

## Risk assessment and proof obligations

| Risk and current evidence | Consequence | Owner / next proof |
| --- | --- | --- |
| **HIGH, confirmed source path:** a pending assignment PUT outlives Discard changes; unmount ignores its eventual receipt | UI claims discard while server commits. Cancellation of response handling is not rollback | [#2981](https://github.com/Chris0Jeky/Taskdeck/issues/2981), repair existing #2977. Reproduce delayed success/failure through modal, inspector, Escape/backdrop/header and navigation, then prove Paper/Legacy behavior with the real API. One fix/verification round remains. |
| **Compatibility:** legacy archive means Block; lifecycle archive is distinct | Reinterpreting saved operations changes the user's approved action | Preserve exact operation names in producers, previews, receipts and documentation; lifecycle regressions belong to every new producer. |
| **Confirmed downgrade loss:** Down removes archive/type/parent metadata | Reapply can make archived cards active, types Task and parents null. Schema reversal is not data recovery | Existing migration contracts plus #2984 UPGRADING packet to #2977. Preserve backup/export before developer rollback; do not claim supported arbitrary app downgrade. |
| **Concurrency and stale receipts:** newer card/session/navigation state can supersede an old completion | Saved state or authorization can be falsely displayed, even when server checks are correct | Preserve request generations and server versions. Delayed success, failure, auth loss and independent draft tests are more valuable than another happy-path count. |
| **Authority and attribution:** participant display is not authorization; requester and applier differ | Import or a secondary receipt can misrepresent who may act or who acted | Assignment tests use distinct owner/editor/viewer and author/applier identities. [#2978](https://github.com/Chris0Jeky/Taskdeck/issues/2978) tracks secondary-history attribution; [#2982](https://github.com/Chris0Jeky/Taskdeck/issues/2982) tracks denied-save recovery. Server checks remain authoritative. |
| **Projection and migration:** hidden archived edges and empty revisions are durable state | A naive graph replacement or Down can erase history or revive deleted links | #2092 migration/adapter tests include empty graphs, archived endpoints, deletes after Up, old-client writes and concurrent lifecycle changes. |
| **Aggregate meaning:** unknown/zero and overlapping assignees are distinct | Totals imply precision, capacity or additive person allocation that does not exist | #2093 proves null/zero, active/archive, multiple assignees, parent independence, safe arithmetic and permission-scoped reads. Label current-state estimates explicitly. |
| **Remaining MEDIUM assignment gaps:** invalid webhook card ID and differing labels for one source key | External consumers or import explanation can be misleading without an unauthorized write | [#2979](https://github.com/Chris0Jeky/Taskdeck/issues/2979), [#2980](https://github.com/Chris0Jeky/Taskdeck/issues/2980); keep tracked and separate from the bounded HIGH repair. |
| **Evidence gaps:** full suite totals did not expose #2981; old Linux coverage worker crashed | A green aggregate does not prove the missing human journey or explain a failed run | Reproduce the changed interaction. Old SIGSEGV is not established flaky; Windows two-worker coverage success is not equivalent Linux proof. |

## Development sequence and release implications

1. Resume the existing assignment PR and its exact HIGH restart contract. Finish already
   implemented Alpha delivery debt under current-base review/CI. Do not open competing writers
   simply because those PRs are inconvenient.
2. Build #2093 as a hard product vertical after shared schema/editor/proposal/portability leases
   are released. This order avoids integration conflicts; a scalar estimate has no intrinsic
   assignment dependency, while per-participant totals do. Keep authorization, audit, portable
   state and readable roll-ups in the same acceptance slice.
3. Admit #2092 from its audited residual, reusing the shipped dependency graph. Serialize schema,
   proposal vocabulary and import changes. Avoid a simultaneous broad model rewrite.
4. Run the prepared [v0.4 qualification sessions](../testing/V04_QUALIFICATION_PLAN.md) against a
   pinned combined candidate. Delivery tests do not change the 44 prepared cases from NOT RUN.
   Prioritize archive/restore + detach + assignments + import, and delayed writes + navigation.

This follows the maintainer's hard-work preference; [#2940](https://github.com/Chris0Jeky/Taskdeck/issues/2940)
remains preserved without a PR, not a convenience next task. Complexity is not itself value:
finish one coherent human workflow before adding another large surface.

For **v0.3**, these merges improve beta trust but do not establish release readiness. The latest
published final is v0.2.0; v0.3.0-rc.1 is a prerelease. The
[September 6 ruling](https://github.com/Chris0Jeky/Taskdeck/issues/2240#issuecomment-5556351206)
moved #2240 to v0.4, superseding the old v0.3 exception. Do not derive a release blocker from milestone
membership alone or infer a release go/no-go from this sweep.

For **v0.4**, stronger card semantics make the hosted product more useful; they do not establish
trusted hosting, backup restore, public registration or the platform threat-model gates. Keep
the existing A/B/C/D release gates and public registration last.

For **v0.5**, semantic candidates and Universal Capture can propose types, parents, assignments
and estimates once their destination contracts ship. Evidence selection, confidence and risk
ordering still do not authorize writes. Voice and evidence playback need explicit save/apply
receipts and uncertain-write recovery, not a new meaning for silence or cancellation.

For **v0.6**, these receipts can inform policy presentation and user control. They do not prove
delegated authority safe or satisfy [#2275](https://github.com/Chris0Jeky/Taskdeck/issues/2275)'s
separate evidence gate. No new autonomy or private retrieval is admitted here.

## Verification record and document integration

Historical receipt scope: hierarchy's final Required run
[34540544226](https://github.com/Chris0Jeky/Taskdeck/actions/runs/34540544226) passed; WIP's
[34542612225](https://github.com/Chris0Jeky/Taskdeck/actions/runs/34542612225) passed. Assignment's
[park receipt](https://github.com/Chris0Jeky/Taskdeck/issues/2240#issuecomment-5627379239)
records 238 Application, 53 API and full frontend coverage of 6,530 passing/three skipped tests,
plus two earlier real-API journeys. These tests missed #2981. Earlier broad backend failures
have scoped superseding checks, not a newly claimed fully green invocation.

This sweep checks documentation links/governance and source/delivery consistency. It does not
rerun application suites, migrations, browsers, live providers, screen readers, physical-device
acceptance or release qualification. No prepared QA case is marked passed.

Shared-document integration remains explicit:

- [#2947](https://github.com/Chris0Jeky/Taskdeck/pull/2947) owns STATUS/readiness: replace its opening
  future-hierarchy sentence with delivered three-link/four-level hierarchy and child-detach/no-reattach
  facts; retain #2926. Add the delivery/open distinction above, not an assignment completion claim.
- [#2931](https://github.com/Chris0Jeky/Taskdeck/pull/2931) owns TESTING_GUIDE: link this dated proof
  map, retain historical counts as scoped evidence, add the missing delayed-save/discard journey.
  Its mutation-runtime proposal is separate from application and release acceptance.
- [#2977](https://github.com/Chris0Jeky/Taskdeck/pull/2977) owns UPGRADING: add archive migration
  `20260910165817_AddCardArchiveLifecycle` before type/parent/assignment notes; Down drops IsArchived,
  and reapply makes retained cards active. Preserve assignment's explicit v4 mapping and loss note.

The [human-action file](../../OUTSTANDING_TASKS.md) retains 41 open items. Release/cutover,
private-instance values, legal/licensing, real device/usefulness checks and historical review
acknowledgements remain with their recorded owners. No checkbox or subjective acceptance was
inferred. [STATUS](../STATUS.md) remains the shipped-state home; this dated report is the factual
integration packet while its lease is open. Live GitHub outranks this snapshot.
