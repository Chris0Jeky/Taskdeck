# Taskdeck Next Work Checklist

Last Updated: 2026-03-07
Source of truth for issue-level execution is now:
- `docs/ISSUE_EXECUTION_GUIDE.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- Issue wave index: `#107`

This checklist is now a lightweight promotion checklist (not a duplicate issue catalog).

## Promotion Checklist (Required Before Moving to `Now`)

- [ ] Issue has exactly one priority label (`Priority I` to `Priority V`).
- [ ] Dependencies listed in issue body are complete or explicitly waived.
- [ ] Acceptance criteria are concrete and testable.
- [ ] Planned verification commands are known in advance.
- [ ] Required docs-update impact is identified (`STATUS`, `MASTERPLAN`, testing docs).

## Thesis Alignment Gate (Capture Realignment)

- [ ] Slice materially reduces maintenance overhead or capture friction.
- [ ] Slice preserves review-first trust model (no silent destructive apply).
- [ ] Slice includes provenance/error-policy implications where relevant.

## Current Priority I Completion Tranche

- [ ] Security/policy convergence: `#33`, `#34`, `#44`
- [ ] UX reliability tranche: `#35`, `#45`, `#36`, `#37`, `#38`, `#46`
- [ ] Automation/provider tranche: `#39`, `#40`, `#57`
- [ ] Starter-packs/debt blockers: `#47` to `#54`

## Future Expansion Waves (Seeded)

- [ ] Wave A/B foundation: `#67` to `#76` (`Priority II`)
- [ ] Wave C analytics/security/compliance: `#77` to `#83`, `#106`, `#110` (`Priority III`)
- [ ] Wave D/E platform/test/UX/docs maturity: `#84` to `#105`, `#111` (`Priority IV`)
- [ ] Wave F capture realignment: `#199` to `#213` (`Priority II` to `Priority IV`)
- [ ] Wave G testing harness guardrails: `#254` to `#260`
- [ ] Wave H outreach CRM deferred expansion: `#262` to `#268` (`Priority IV`)
- [ ] Meta index maintenance: `#107` (`Priority V`)

## MVP Expansion Productization (Wave I Seeded)

Detailed reconciliation:
- `docs/analysis/2026-03-07_mvp-expansion-gap-map.md`
- `docs/analysis/2026-03-07_mvp-expansion-source-coverage-audit.md`

- [ ] Wave I tracker: `#318`
- [ ] Batch A novice-first shell and entry clarity:
  - `#320` workspace modes + `Home` summary shell
  - `#322` `Review`-first routing + empty/help states + board selectors
- [ ] Batch B board-centered daily workflow:
  - `#324` `Today` agenda + onboarding path
  - `#326` proposal readability + board-centered action flow
- [ ] Batch C docs/help/testing coherence:
  - `#96` onboarding/contextual help (reused, reprioritized to `Priority II`)
  - `#100` user guides/tutorials/FAQ (reused, reprioritized to `Priority II`)
  - `#328` first-run smoke + launch-criteria guardrail
- [ ] Wave I implementation carry-forward:
  - `#320`: durable workspace-mode preference + product-shaped summary endpoint direction
  - `#324`: onboarding checklist plus first useful project/wizard flow
  - `#326`: application-layer proposal summaries + explicit board-aware action rail
  - `#96`: dismissible in-app help blocks/help-center direction
  - `#328`: `novice-first-first-run` scenario shape and launch-criteria sync
- [ ] Batch D agent substrate:
  - profiles
  - runs
  - run events
  - policies
  - first narrow template
- [ ] Batch E knowledge/integrations surface:
  - knowledge documents
  - SQLite FTS search
  - note/transcript/clip intake
  - integrations registry page

Related reuse anchors that stay outside the immediate Wave I execution core:
- [ ] `#93`, `#216`, `#77`, `#75`, `#97`, `#98`, `#218`, `#219`

Secondary deferred set preserved by the audit and still needing later disposition:
- [ ] `Demo Tools`, guided narrative/demo tour, nav badges, hero-board quality
- [ ] HTML report, snapshot/trace assertions, presets/soak, replay-from-trace, scenario composer
- [ ] saved views and broader import-surface follow-through

## Out-of-Code and Configuration Actions Coverage

- [x] Containerized runtime + reverse proxy + compression: `#69`
- [ ] Staged rollout strategy: `#101`
- [ ] Infrastructure as Code baseline: `#102`
- [ ] SBOM/provenance release policy: `#103`
- [ ] Cost guardrails and budget signals: `#104`
- [ ] Backup/restore disaster recovery: `#86`
- [ ] Observability baseline (metrics/traces/alerts): `#68`
- [ ] Performance/concurrency harness: `#70`
- [ ] Security hardening ops controls (headers, rate limits, dependency scanning): `#80`, `#81`, `#106`
- [ ] Secrets/configuration management baseline: `#110`
- [ ] Cloud topology/autoscaling ADR: `#111`

## WIP Discipline (Execution)

- [ ] Max 1 major issue in `Now`
- [ ] Max 1 issue in `Review`
- [ ] Resolve all `No Status` project items before release activities
