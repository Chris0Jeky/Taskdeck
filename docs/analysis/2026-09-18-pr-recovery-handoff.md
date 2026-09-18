# Pull-request recovery handoff

**Repository:** `Chris0Jeky/Taskdeck`  
**Prepared:** 2026-09-18  
**Archival branch base:** `88bb3b4498e975eda17f64dfbd4a975373aa5890`

## Purpose

Preserve the outputs of the 2026-09-17 pull-request recovery pass inside GitHub and record what was
actually submitted after the repository moved. This document is a provenance and handoff record; it
does not replace the live pull-request state, CI results, review threads, `STATUS.md`, or the product
masterplan.

## Remote submission state

### Frontmatter scalar validation

The original local proposal is preserved at:

- `docs/analysis/recovery/2026-09-18-pr-3134-review-fixes.patch`

The clean replacement PR, #3134, subsequently evolved beyond that proposal and was merged. Its
merged implementation is authoritative. It includes the loader-aligned YAML implicit-scalar
recognition, control-character handling, Unicode validation, regressions, and supporting analysis.
The archived patch is retained only to preserve the investigation trail and must not be replayed over
`main`.

### Friends-and-family beta runbook

The original local proposal is preserved at:

- `docs/analysis/recovery/2026-09-18-pr-3098-review-fixes.patch`

The applicable corrections were reconciled against the live #3098 branch rather than applying the
stale patch wholesale. The following remote commits were added to
`docs/1325-friends-family-beta`:

- `6451b6706225429695b7d432fec74d445b5bbb39` — authenticated egress-disclosure procedure,
  sole-owner deletion preflight, recurring-backup completion gate, and updated runbook date;
- `dae1c6f7ff0227a1eeab3d79f075295d500e0bee` — read-only SQLite inventory flags and a local
  registration-closure probe that cannot be intercepted by Cloudflare Access.

Pre-existing fixes on the branch, including recurring backup prerequisites and explicit Windows
board selection before capture, were preserved.

## Archived source material

- `docs/analysis/recovery/2026-09-17-pr-triage-original.md` is the exact original triage snapshot.
  It records the repository state observed during the first pass and is intentionally historical.
- `docs/analysis/recovery/2026-09-18-pr-3134-review-fixes.patch` is the exact original #3134 patch.
- `docs/analysis/recovery/2026-09-18-pr-3098-review-fixes.patch` is the exact original #3098 patch.

## Integrity

```text
62f8a3a10aa9dd2880c41830e22a51d58c6305bc7dd19d210e13a564d4fc8db7  2026-09-17-pr-triage-original.md
99990659664381713f5baab4df4fb96c0e0912486c08f832d2744278c09f2df8  2026-09-18-pr-3134-review-fixes.patch
3469208b85c41253c70ac2eae3652abc2eb4a6b38df043e22c726d64bf2f9f92  2026-09-18-pr-3098-review-fixes.patch
```

The hashes above are SHA-256 values of the original sandbox files before upload.

## Verification boundary

The GitHub commits and file contents are remotely durable once this archival PR is opened. Hosted CI
and fresh code review remain the authority for merge readiness. No claim is made here that an
in-progress workflow has passed, that #3098 has been merged, or that the human participant
walkthrough required by GH-1325 has occurred.
