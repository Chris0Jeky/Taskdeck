# v0.5 candidate code — reference only

Last Updated: 2026-09-02

Archived verbatim from `taskdeck-milestone-6-acceleration-bundle-2026-08-30`, whose GitHub grounding
failed (0 issues captured). The code is issue-agnostic and namespace-free; `../CANDIDATE_MAP.md`
supplies the mapping to live v0.5 issues and the validation against current source that the
generator could not perform. None of it is part of a Taskdeck build.

Verified 2026-09-02: all 8 C# files build together in an isolated `net8.0` class library with
`Nullable` enabled (0 warnings, 0 errors); the 3 TypeScript files pass `tsc --strict --noEmit`; the
Python utilities (`canary_report.py`, `score_fixtures.py`) run against `../fixtures/`. Internal
consistency only, not repository integration. `dependency_planner.py` and the bundle-handoff schemas
were dropped as bundle tooling.

The admission contract in `../../v0.6/candidates/README.md` applies unchanged.
