# Partial transcript dates — residual coverage after the primary fix (#2193)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) and validated against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). Planning input, not authority: the live issue, the accepted ADRs and `docs/STATUS.md` win. Corrections to the bundle are listed in the last section.

## Outcome

A partial date spoken in a transcript resolves to the right year or to nothing, and the reference it resolves against is the one the *user* would name — the capture's own day, not whatever UTC day the server happened to be on when the job ran.

## Live dependencies (verified 2026-09-02)

| Dependency | State | Note |
| --- | --- | --- |
| Primary fix | **merged**, PR **#2206** (`08ac35505`) | Not PR #2214 — see corrections |
| Reference date in the prompt | **shipped** | `LlmCaptureTriagePrompt.ReferenceDatePlaceholder` (`{REF_DATE}`) rendered by `BuildSystemPrompt(DateOnly)`; resolution rule is "the first such date on or after the reference" |
| Plausibility window | **shipped** | `CaptureTriageOutputContract.MaxDueDateYearsBeforeReference = 2`, `MaxDueDateYearsAfterReference = 5`, enforced by `IsWithinDueDatePlausibilityWindow` at validation (`CaptureTriageContracts.cs:323`) |
| `ReviewDueDateHint` | **shipped** | An implausible or unparseable hint is dropped with an honest note; one bad date costs the hint, not the extraction |
| December-boundary tests | **already exist** | `LlmCaptureTriagePromptTests.cs:105` `TryParseTasks_ShouldKeepANextYearResolvedDate_FromADecemberCaptureDay` and `CaptureTriageOutputContractTests.cs:241` `ReviewDueDateHint_ShouldKeepANextYearResolution_FromADecemberCaptureDay`, both anchored at `2026-12-31`. Added by PR #2206's round-2 review |
| **#2210** reference-date semantics | **open — the real residual** | `LlmCaptureTriagePrompt.CurrentReferenceDate => DateOnly.FromDateTime(DateTime.UtcNow)`. The reference is the server's UTC day at triage, not the capture row's `CreatedAt` and not the user's local calendar day. A far-west-timezone capture mentioning its own local day resolves a year late; a retry re-enqueue (`CaptureService.cs:342`) can push the reference days later |
| **#2211** prompt-version bump | open | Derivation semantics changed without moving past `PromptVersionLlmV2` (`llm-triage.v2`). No `llm-triage.v3` exists anywhere in `backend/` or `docs/`. `docs/STATUS.md:55` scopes the bump at 14 files across backend, the frontend provenance classifier, `scripts/ci/windows_desktop_archive.py:949` and four docs |

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Startable before predecessors merge? |
| --- | --- | --- | --- |
| `PD-1-jan-boundary` | The mirror of the existing December cases: a January-1 reference resolving a December day/month, and a same-day reference (the "on or after" boundary itself) | — | **Yes.** The startable-now slice: two test methods in files that already exist, no production change |
| `PD-2-culture-and-clock` | Pin the rendered reference date and the parsed output under a non-invariant culture (`de-DE`, `ar-SA` with a non-Gregorian default calendar) and prove `DateOnly` round-trips unaffected | — | **Yes**, independent |
| `PD-3-false-positives` | Fixtures where a transcript contains a version string, an RFC-822 header or package metadata and no due date is produced | — | **Yes**, but see the correction — verify the extractor is even reachable by that text before asserting a behaviour |
| `PD-4-reference-source` (**#2210**) | Resolve against the capture's own recorded day rather than the server's UTC day at triage; decide whether "the capture's day" is `CreatedAt` UTC or a user-local day the client supplies | — | **No — this is #2210's issue, not this one.** Do not implement it here; #2193 closes on tests |
| `PD-5-version-bump` (**#2211**) | `llm-triage.v3` across the 14 files | PD-4 (bump once, after the semantics settle) | No |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Reference-date rendering | `LlmCaptureTriagePrompt.BuildSystemPrompt(DateOnly referenceDate)` | **exists** | Formats with `"yyyy-MM-dd"` and `CultureInfo.InvariantCulture` at line 113 — the culture risk is already mitigated at the render seam |
| Reference-date source | `LlmCaptureTriagePrompt.CurrentReferenceDate` | **exists — and is the defect** | `DateOnly.FromDateTime(DateTime.UtcNow)`. `TryParseTasks(content, CurrentReferenceDate, out …)` bakes it in at line 122 |
| Plausibility validation | `CaptureTriageOutputContract.ValidateV2(…, DateOnly? referenceDate)` | **exists** | The window is applied only when a reference is supplied; a null reference skips the check |
| Honest note on a dropped hint | `ReviewDueDateHint` (`CaptureTriageContracts.cs:391`) | **exists** | Emits a note naming the capture date; `MaxDueDateHintNoteLength` bounds the echoed hint |
| Prompt versions | `PromptVersionV1`, `PromptVersionLlmV1`, `PromptVersionLlmV2` | **exists** | The v2 validator hard-requires `llm-triage.v2` (line 252); the separate v1 path validates against `KnownPromptVersions` |
| Notes surfaced to the reviewer | — | **absent** | The notes the parser produces are returned at the contract boundary and shown nowhere; tracked on #2210 |
| A capture-local calendar day | — | **new** | Nothing records the user's local day at capture time |

## Implementation plan

**Preflight.** Read the single comment: it is the delivery receipt and it names both residual issues. Then read `docs/STATUS.md:55`, which is the most precise statement of what shipped and what did not — including the two "Not verified" clauses.

**This issue's remaining scope is tests.** The behaviour question is #2210's and the version bump is #2211's. Adding either here re-opens a merged design.

**Sequence.** PD-1 and PD-2 are two small additions to `LlmCaptureTriagePromptTests` and `CaptureTriageOutputContractTests`. Both are pure and both run in seconds. PD-3 needs a reality check first (below). Then close #2193 against the merged PR plus the new tests, and leave #2210/#2211 carrying the rest — which is exactly what #2235 asks for on issues shipped by a merged PR but still open.

**Do not** redesign date extraction. The bundle says so and it is right.

## Test plan

- [ ] `TryParseTasks` with a `2027-01-01` reference and a "31 December" mention resolves forward to `2027-12-31`, not backward to `2026-12-31` — `dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~LlmCaptureTriagePrompt"`
- [ ] Same-day boundary: a reference of `2026-09-01` and a "1 September" mention resolves to `2026-09-01` (on-or-after includes the day itself)
- [ ] The rendered `{REF_DATE}` is identical under `de-DE`, `fr-FR` and a thread culture with a non-Gregorian default calendar
- [ ] `IsWithinDueDatePlausibilityWindow` boundary: exactly 2 years before and exactly 5 years after the reference are inside; one day beyond each is outside
- [ ] A null reference date skips the window check without throwing, and the caller records that no plausibility check ran
- [ ] `ReviewDueDateHint` drops an implausible hint, returns the honest note, and does not truncate a hint below `MaxDueDateHintNoteLength` — `--filter "FullyQualifiedName~CaptureTriageOutputContract"`
- [ ] Regression fixture from the issue: "Monday 1 September" with a `2026-08-29` reference → `2026-09-01`
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- A reference of 29 February and a partial date; a partial "29 February" in a non-leap resolution year.
- A transcript that states its own date differing from the triage day — the case #2210 owns.
- A retry re-enqueue days after capture, moving the reference forward under the user's feet.
- A model returning a fully-qualified but implausible date (the original defect: `2023-09-01`), which the window now catches.
- A model returning `null` (the shipped `gpt-5.6-luna` behaviour) — the absence of a date is correct, not a degradation.
- A transcript containing several partial dates where only one is implausible: one bad date must cost one hint, not the extraction.
- Culture-sensitive parsing on a machine whose default calendar is not Gregorian.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Audit note | `docs/analysis/2026-08-30-acceleration-bundle/audit-m4/TRACKER_DRIFT.md` §"#2193 partial dates" | "Do not redesign date extraction unless the residual tests expose a defect" — correct and worth keeping | Attributes the fix to PR #2214 |
| Audit note | `.../audit-m4/HIGH_LEVERAGE_RESIDUALS.md` §"Tracker closure pair" | Framing this as a low-conflict test-only task independent of #2185 | Accurate |

## Corrections to the bundle

1. **Bundle pack and `TRACKER_DRIFT.md`:** "PR #2214 added a reference date, future-date resolution and plausibility filtering." **True:** the fix is **PR #2206**, merge `08ac35505`. **PR #2214 does not exist in this repository.** **Consequence:** the pack points an agent at nothing; the 2026-08-30 RECONCILIATION recorded this and the archived pack still says #2214.
2. **Bundle pack residual:** "Add clock-controlled tests for Dec/Jan rollover." **True:** the **December** half already exists in two tests, both anchored at `2026-12-31`, added by PR #2206's own round-2 review. **Consequence:** only the January-reference mirror and the same-day boundary are genuinely missing. Claiming the whole rollover is uncovered overstates the residual by half.
3. **Bundle pack residual:** "Cover RFC822 headers, README/package metadata and unrelated version/date strings." **True:** nothing in the live issue, the merged PR or `docs/STATUS.md` mentions metadata false positives, and the triage extractor consumes transcript-source captures rather than files with RFC-822 headers. **Consequence:** this looks imported from a generic date-parsing checklist. Verify the surface is reachable before writing a test that asserts a behaviour nobody designed.
4. **Bundle pack residual:** "Verify normalization and null behavior are stable across local/CI timezone/culture." **True and half-mitigated:** the render seam already uses `CultureInfo.InvariantCulture` (`LlmCaptureTriagePrompt.cs:113`), and the existing tests format the expected value the same way. **Consequence:** the culture test is cheap insurance, not a gap; the *timezone* half is not a test gap at all — it is #2210's behaviour defect.
5. **Bundle pack:** silent on #2210 and #2211. **True:** the single live comment names both as the reason #2193 stays open, and `docs/STATUS.md:55` records both with their exact scope. **Consequence:** the pack's residual list omits the two things that actually keep this issue open.
6. **Bundle pack:** implies #2193 closes when the tests land. **True:** the live comment says "Kept open behind #2210". **Consequence:** confirm with the owner whether #2193 closes on the tests with #2210/#2211 carrying the behaviour, or waits — #2235's issue-hygiene checklist names exactly this class of decision.
