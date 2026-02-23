# Taskdeck Next Work Checklist

Last Updated: 2026-02-18
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
- [ ] Meta index maintenance: `#107` (`Priority V`)

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
