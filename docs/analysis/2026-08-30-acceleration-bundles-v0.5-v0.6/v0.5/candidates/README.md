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

## Known defects (archived verbatim; fix on adoption, never here)

| File | Defect |
| --- | --- |
| `csharp/AudioCaptureSession.cs` | keeps accepting chunks after the client marked chunk *N* final; a re-registered chunk is only hash-checked against the first |
| `csharp/RetentionPlanner.cs` | emits `Delete` decisions (must be an `IBlobStore` reference release); an unknown `RetentionSubject.Kind` is not failed closed |
| `csharp/ProcessingPolicySnapshot.cs` | `ComputeDigest` uses default `JsonSerializerOptions` — not canonical, so a field reorder silently changes every stored digest |
| `csharp/ProcessorRouteEvaluator.cs` | cost-orders candidates, which is scoring by another name (forbidden before the CF-24A corpus) |
| `csharp/SemanticCandidate.cs` | discards its evidence anchors, has no `CaptureId`, and its state enum drops `Corrected` / `Dismissed` |
| `csharp/AutomationSafetyGate.cs`, `python/canary_report.py` | hard-code the struck-through ≥50 / ≤10 % / zero-reversal numbers as a `safeToExpand` verdict and omit the maintainer go (ADR-0065 amendment 10) — rejected |

The admission contract in `../../v0.6/candidates/README.md` applies unchanged.
