# Downloadable v0.3.0 launch kit — `docs/product/LAUNCH_KIT.md` (#2242)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) and validated against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). Planning input, not authority: the live issue, the accepted ADRs and `docs/STATUS.md` win. Corrections to the bundle are listed in the last section.

## Outcome

One file the maintainer can post from, for the **downloadable** v0.3.0 release: Windows ZIP and Docker Compose, single-SQLite-file ownership, zero telemetry, review-first agents — every sentence traceable to a shipped receipt, every gap stated before a reader finds it. No hosted claims; those are #1310's.

## Live dependencies (verified 2026-09-02)

| Dependency | State | Note |
| --- | --- | --- |
| `docs/product/LAUNCH_KIT.md` | **absent** | `docs/product/` holds 11 documents; this is not one of them. AC1–AC4 unstarted |
| `docs/TELEMETRY.md` | **shipped** | The phone-home probe answer's target, and it already contains the destination table, the defaults, the self-check procedure and the v0.4 Option B statement |
| `LICENSING.md`, `LICENSE` (GPL-3.0, ADR-0050) | **shipped** | The licence probe answer's target |
| GitHub Discussions | **off** (`has_discussions: false`) | The 48-hour triage rule routes questions to Discussions. Either enable them (#1308 AC1, human-only) or route questions to issues and say so |
| `docs/releases/notes/v0.3.0-rc.1.md`, `UPGRADING.md` | **shipped** | Upgrade text already exists; the kit links it rather than restating it |
| `docs/ops/RELEASE_TRUST_AND_DISTRIBUTION.md`, `docs/ops/SBOM_RELEASE_PROVENANCE.md`, `docs/ops/RELEASE_CHECKLIST.md` | **shipped** | Checksums, provenance and SBOM location |
| Code signing (RT-1/RT-2/RT-3) | **not done** | SignPath route decided; publisher string, domain posture and enrolment pending. v0.2.0 shipped unsigned and signing gates no release before v0.3.x. The kit must state the SmartScreen consequence honestly |
| `docs/ops/DISASTER_RECOVERY_RUNBOOK.md` + the CLI recovery commands | **shipped** (PRs #2361, #2360) | A genuine claim the kit can make and the issue's gap list does not yet know about |
| Known-gap issues | open | #1429 artefact extraction unwired, #1653 TOTP seeds unencrypted at rest, #2243 no hosted instance, batch execute per-proposal |
| #1310 | open, v0.4 | Owns hosted demo, metrics baseline and hosted claims. Same file, later section |

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Startable before predecessors merge? |
| --- | --- | --- | --- |
| `KIT-D-1-claim-ledger` | Create `docs/product/LAUNCH_KIT.md` with a claim → evidence → owner → last-verified table covering review-first agents, local data ownership, zero telemetry, backup/restore, checksums/provenance | — | **Yes. This is the startable-now slice** and it creates the file every later slice edits |
| `KIT-D-2-probe-answers` | The three probe answers: phone-home (→ `docs/TELEMETRY.md`), licence permanence (GPL-3.0-only core, ADR-0050, copies already received under MIT keep those grants, not dual-licensed, monetization additive; plus the DCO structural argument), known gaps stated plainly | KIT-D-1 | Yes, immediately after |
| `KIT-D-3-posts` | r/selfhosted, Show HN + first comment, dev.to long-form, awesome-selfhosted submission text — maintainer voice, no roadmap in the present tense | KIT-D-2 | Yes |
| `KIT-D-4-presence` | 48-hour triage rule, known-issues pinned text, the same-day-fix promise boundary | Discussions (human) for the routing target | Partly — write it with the routing target marked TBD |
| `KIT-D-5-media` | Synthetic screenshots and a short demo script from seeded fixtures, at a consistent viewport | — | Yes, independent |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Zero-telemetry claim | `docs/TELEMETRY.md` + the appsettings defaults (`Sentry.Enabled=false`, `Telemetry.Enabled=false`, `Analytics.Enabled=false`) + production CSP `script-src 'self'; connect-src 'self'` | **exists** | The claim is defensible with a named file, not an assertion |
| Review-first claim | MCP write tools create proposals; `approve_proposal` does not exist; INV-01 scans `Application/Services` + `Api/Mcp` for direct mutation | **exists** | The strongest claim in the kit |
| Single-SQLite-file claim | one file, one volume | **exists** | Pair it with the honest ceiling: single-node, and the measured numbers must be real or the sentence goes |
| Recovery claim | `taskdeck --backup` / `--restore` / `--verify-connectors`, `deploy/docker/taskdeck-backup`, `docs/ops/DISASTER_RECOVERY_RUNBOOK.md` | **exists** | New since the issue was written; a real selling point for a self-hosting audience |
| Windows ZIP + packaged MCP | release-contract tests byte-verify the ZIP members, including the MCP guide and both stdio examples | **exists** | Clean-machine install is provable by the existing archive harness |
| Signed installer | — | **absent** | RT-1/2/3 pending. State it; an unsigned SmartScreen prompt discovered by a reader is worse than one disclosed |
| `docs/product/LAUNCH_KIT.md` | — | **new** | The deliverable |

## Implementation plan

**Preflight.** Read the issue body (there are no comments) and the RC deck q-6 ruling it cites: v0.3.0 ships as a tag when ready, the public launch is a separate human-dated event, the agent drafts and the maintainer posts. Then read `docs/TELEMETRY.md` and `LICENSING.md` before writing either probe answer — both have moved since the framing that produced AC4 on #1310.

**Write the ledger before the prose.** Every sentence in a post has to point at a row. A claim with no row does not get hedged language; it gets deleted or moved into the known-gaps list.

**Update the known-gaps list against `main`.** The issue's list is good and one item is now stale in the *helpful* direction: the recovery capability shipped. Add it as a claim and check whether anything else on the list moved.

**Never post.** The maintainer posts under their own accounts. This is the issue's own boundary and the harness's human-action law.

## Test plan

- [ ] `node scripts/check-docs-governance.mjs`
- [ ] One-shot markdown link check over `docs/product/LAUNCH_KIT.md` and everything it references
- [ ] Every claim row's evidence link resolves to a merged PR, a `docs/STATUS.md` line, or a named test; three spot-checked in review
- [ ] Clean-machine install walked end to end against the Windows ZIP quick start, and the Docker Compose one-liner run as written (`docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline up -d --build`)
- [ ] Checksum and provenance verification steps executed as written (`scripts/ci/verify-sha256.sh`)
- [ ] Upgrade path exercised per `UPGRADING.md`; data preserved
- [ ] Every screenshot is synthetic — no real board name, user name or e-mail
- [ ] No sentence describes an unshipped feature in the present tense — reviewed explicitly as a checklist item

## Edge cases

- A reader on Windows hitting an unsigned-binary SmartScreen prompt the kit did not warn about.
- awesome-selfhosted's release-age and activity criteria evaluated at submission, not at drafting.
- Questions arriving in a channel that does not exist because Discussions are off.
- A screenshot taken before a UI change and posted after.
- A reader conflating the downloadable release with a hosted service because a hosted sentence leaked in from #1310.
- The repository going private around the tag, breaking links inside the kit.
- The zero-telemetry claim read as "no network at all" — `docs/TELEMETRY.md` is careful about configured LLM providers, connectors, webhooks and operator-enabled Sentry/OTLP; the kit must be equally careful.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Docs draft | `docs/analysis/2026-08-30-acceleration-bundle/docs-drafts/LAUNCH_KIT_OUTLINE.md` | The claim-ledger columns, the probe-answer bank and the synthetic-workspace list | Written for a blended kit; take the downloadable half only, and drop its "Hosted availability" row entirely — that belongs to #1310 |
| Audit note | `.../audit-m4/TRACKER_DRIFT.md` §"Launch drift" | "Do not blend installation claims, availability promises or threat models" — the single most useful sentence the bundle wrote here | — |
| Blueprint | `.../architecture/HOSTED_BETA_READINESS_MODEL.md` §2 Stage 0 | The exact scope of a downloadable beta: user controls data and keys, release artifacts, upgrade and backup docs, **no hosted availability promise** | Read its validation preface |

## Corrections to the bundle

1. **Bundle pack:** "This remains distinct from v0.4 hosted launch #1310." **True and the most important thing it says.** **Consequence:** adopt it verbatim; the two kits share a file section but never a claim.
2. **Bundle pack residual:** "Upgrade/backup/restore and connector-key custody instructions." **True and now much cheaper:** `UPGRADING.md`, `docs/ops/DISASTER_RECOVERY_RUNBOOK.md` (321 lines, with the published RTO/RPO targets and the 0/1/2 exit table) and the shipped `--backup` / `--restore` / `--verify-connectors` commands all exist. **Consequence:** the kit **links** them; it must not restate a recovery procedure, or the two will drift.
3. **Bundle pack residual:** "Checksums, signatures/provenance, SBOM location and known gaps." **True with one correction:** *signatures* do not exist — RT-1/RT-2/RT-3 are open and v0.2.0 shipped unsigned. **Consequence:** the kit states the unsigned reality and the SmartScreen consequence; claiming signing would be a false claim in the first paragraph a security-minded reader checks.
4. **Bundle pack:** silent on the licence. **True:** the v0.3 kit's licence-permanence answer must make the **GPL-3.0** copyleft argument (ADR-0050), not the MIT one that the older REVIVAL-14 framing assumed. **Consequence:** the highest-risk probe answer is the one the pack does not mention.
5. **Bundle pack:** silent on Discussions. **True:** `has_discussions: false`. **Consequence:** the 48-hour triage rule in AC3 routes questions to a channel that does not exist. Enabling it is human-only; the kit must say which channel is live at posting time.
6. **Live issue known-gaps list:** written 2026-08-30. **True:** the backup/restore and connector-verification capability shipped afterwards (PRs #2361, #2360) and is currently absent from `docs/STATUS.md` too. **Consequence:** the kit should carry it as a claim, and #2235's SC-1 should land the STATUS line the claim points at.
