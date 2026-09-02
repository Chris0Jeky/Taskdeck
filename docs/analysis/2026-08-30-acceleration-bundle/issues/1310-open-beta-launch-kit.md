# REVIVAL-14 — Open-beta launch kit for the hosted v0.4 beta (#1310)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) and validated against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). Planning input, not authority: the live issue, the accepted ADRs and `docs/STATUS.md` win. Corrections to the bundle are listed in the last section.

## Outcome

When the maintainer chooses a launch day, everything except the posting already exists and every sentence in it is traceable to a shipped receipt. This issue owns the **hosted** kit (v0.4): the demo, the hosted-availability claims and the metrics baseline. The **downloadable** v0.3 kit is #2242 and the two must not blend.

## Live dependencies (verified 2026-09-02)

| Dependency | State | Note |
| --- | --- | --- |
| #2242 downloadable v0.3 kit | open | Owns `docs/product/LAUNCH_KIT.md` first. This issue extends it with hosted material rather than forking a second file |
| `docs/product/LAUNCH_KIT.md` | **absent** | `docs/product/` contains 11 documents; no `LAUNCH_KIT.md`. AC3 unstarted |
| #1308 REVIVAL-12 (AC1 prerequisite) | open, partly landed | `docs/TELEMETRY.md` **now exists**, so the phone-home probe answer has a target. Discussions remain `has_discussions: false`, so the feedback channel is not live and the participants metric still has no source |
| #1309 REVIVAL-13 (AC1 prerequisite) | open, substantially landed | `docs/MCP_SERVER.md` and `docs/MCP_TOOLING_GUIDE.md` exist; API-key scopes shipped (`Taskdeck.Domain/Enums/ApiKeyScope.cs`); packaged desktop + Docker MCP examples shipped. The residual is AC3/AC5, not documentation |
| #2243 hosted open beta epic | open | Gates the *hosted demo* and every availability claim. Stage 4 in the gate ladder |
| Licence | **GPL-3.0**, ADR-0050 | AC4's licence-permanence answer must make the copyleft argument, not the MIT one. Recorded 2026-08-23 and still uncorrected in the issue body |
| Launch date | **scrapped** | RC deck q-6 = A: "we ship when the release is ready". No date gate exists to decide |

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Startable before predecessors merge? |
| --- | --- | --- | --- |
| `KIT-H-0-baseline` | Capture the pre-launch metrics baseline: stars, Release download counts, issues-to-stars ratio, and the participants metric marked explicitly unavailable until Discussions exist | — | **Yes — and it is the only time-sensitive slice.** A baseline captured after a launch is worthless. It costs one `gh api` read and a dated table |
| `KIT-H-1-hosted-claims` | A hosted section in `docs/product/LAUNCH_KIT.md`: claim→evidence ledger rows for availability, cost, backup/restore RTO, telemetry, isolation; every row carries a receipt link or the word *unproven* | #2242 creates the file | No — write it as a follow-on so the two kits share one file and one voice |
| `KIT-H-2-demo` | Deploy the backend-less demo (`VITE_API_BASE_URL=''` → seeded client-side fixtures) as a static site with a "data is fake and local" banner; link from README | — | **Yes for the build, no for the deploy.** Building and proving the demo bundle is agent work; publishing it is a maintainer act |
| `KIT-H-3-probe-answers` | Rewrite AC4's three probe answers against current reality: phone-home → `docs/TELEMETRY.md`; licence → GPL-3.0 / ADR-0050 / DCO; known gaps → the honest v0.4 list | KIT-H-1 | Yes |
| `KIT-H-4-presence` | 48-hour triage plan, known-issues pinned Discussion text, close-registration and incident templates | #1308 TLM-0 for the Discussions target | Partly — the text is writable now, the pinning is not |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Backend-less demo mode | `VITE_API_BASE_URL` empty → seeded client-side fixtures | **exists** | The mechanism the AC2 demo relies on; verify it still seeds before promising a demo site |
| Telemetry disclosure target | `docs/TELEMETRY.md` | **exists** | The probe answer's landing page |
| MCP disclosure target | `docs/MCP_SERVER.md`, `docs/MCP_TOOLING_GUIDE.md` | **exists** | AC1's REVIVAL-13 docs prerequisite is met even though #1309 is open |
| Licence | `LICENSE` = GPL-3.0, ADR-0050 | **exists** | AC4's stale MIT framing is the correction |
| Gate ladder | `docs/analysis/.../architecture/HOSTED_BETA_READINESS_MODEL.md` §2 | blueprint | Stage 4 "launch claims linked to receipts" is exactly this issue |
| `docs/product/LAUNCH_KIT.md`, `docs/product/claims/**`, a demo deployment | — | **new** | None exists |

## Implementation plan

**Preflight.** Read the three comments; the 2026-08-29 q-6 comment splits this issue in two and moves it to v0.4. Do not start any hosted claim before #2242's file exists, and never write a hosted-availability sentence that #2243 has not proven.

**The one thing to do now** is `KIT-H-0-baseline`. Everything else is either gated on #2242's file or on human acts (deploy, post, enable Discussions). Record the baseline as a dated table inside the eventual kit or in `docs/product/evidence/`, and state plainly that the Discussions-participants row has no source yet.

**Claim discipline.** Every row is *claim | evidence link | owner | last verified | allowed wording*. A claim with no receipt does not get softened language — it gets deleted or moved to the known-gaps list.

**Never:** an agent posts externally. The issue's own trap says so twice, and the RC deck ruling makes the posting a separate human-dated event.

## Test plan

- [ ] Docs: `node scripts/check-docs-governance.mjs`
- [ ] Links: a one-shot markdown link check over the kit and everything it references (the same run #2235 asks for)
- [ ] Demo: cold load measured against the AC's <3 s target on a throttled profile, with the number recorded rather than asserted
- [ ] Demo: every screenshot and fixture is synthetic — assert no real board name, user name or e-mail appears in the seeded fixture file
- [ ] Claims: each row's evidence link resolves to a merged PR, a `docs/STATUS.md` line, or a named test — a reviewer spot-checks three at random
- [ ] Baseline: the captured numbers are reproducible from the same `gh api` reads on the same day

## Edge cases

- The demo's API is unavailable or the fixtures drift after a UI change — a stale screenshot is a false claim.
- Discussions are enabled *after* the baseline is captured, so the participants row starts mid-stream; say so rather than backfilling.
- awesome-selfhosted's release-age and activity criteria are checked at submission time, not at drafting time.
- A cost spike or an incident during the 48-hour window — the close-registration and status text must already be written.
- Support volume exceeds the maintainer's capacity; the kit needs a documented "what we will not answer within 48h" boundary.
- A claim that was true at drafting becomes false by launch — every row carries *last verified* for exactly this.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Docs draft | `docs/analysis/2026-08-30-acceleration-bundle/docs-drafts/LAUNCH_KIT_OUTLINE.md` | The claim-ledger table shape, the probe-answer bank and the synthetic-workspace list | Written for one blended kit; split it across #2242 (downloadable) and this issue (hosted). Its "Hosted availability" row must stay *beta expectation, not SLA* |
| Blueprint | `.../architecture/HOSTED_BETA_READINESS_MODEL.md` §2 Stage 4, §6 scorecard | The evidence a launch claim is allowed to rest on | Read its validation preface first |
| Diagram | `.../diagrams/hosted-beta-gates.svg` | One picture of why the launch post comes last | Explanatory only |

## Corrections to the bundle

1. **Bundle pack:** "The body still contains downloadable-v0.3 assumptions. The milestone now owns a hosted v0.4 beta." **True and already actioned:** the 2026-08-29 comment split the downloadable kit out as **#2242** and moved this issue to v0.4. **Consequence:** the pack's "please amend the framing" instruction is done; the residual is to stop the two kits from re-merging.
2. **Bundle pack decision list:** "Canonical launch date gate". **True:** RC deck q-6 = A scrapped the dates — "we ship when the release is ready". **Consequence:** there is no date decision to receive; remove it from the blocker list.
3. **Bundle pack:** does not mention the licence drift. **True:** AC4's licence-permanence answer was written against an MIT-forever commitment; `LICENSE` on `main` is **GPL-3.0** under ADR-0050. **Consequence:** the probe answer is wrong as written and is the highest-value correction in the issue.
4. **Bundle pack AC1 framing:** treats #1308 and #1309 as wholesale prerequisites. **True:** `docs/TELEMETRY.md`, `docs/MCP_SERVER.md` and `docs/MCP_TOOLING_GUIDE.md` all exist; API-key scopes shipped. **Consequence:** the *documentation* prerequisites are met; what remains is Discussions being off and #1309's AC3/AC5.
5. **Bundle pack file ownership:** `frontend/demo/**`. **True:** no such directory; the demo is the existing frontend under an empty `VITE_API_BASE_URL`. **Consequence:** the fence names a path that does not exist.
6. **Bundle pack KIT-4:** "Capture pre-launch metrics and produce day-1/day-7/day-30 templates" is listed fourth. **True:** it is the only slice that becomes impossible if delayed. **Consequence:** promote it to first, as the 2026-08-23 comment already argued.
