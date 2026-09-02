# Source ledger — provenance of the bundle

Verbatim: `MANIFEST.json`, `SHA256SUMS.txt`, `SNAPSHOT.md`, `VALIDATION_REPORT.md`, `bundle-summary.json`,
`EVIDENCE.md`, `snapshot.json`, and the bundle's own `README.md` (as `BUNDLE_README.md`). They describe the
bundle at `221aa88c8` and its generator's self-validation. They do not validate any later Taskdeck state.

Re-validation on 2026-09-02 (this machine, the re-added copy): see `../HEAD_START.md` — the copy is missing
`01_MILESTONE_5/issue-packs/2094.md` (checksum line fails), the Python suite is 11/12 with the documented
Windows path failure, and the C# candidates build clean in isolation.
