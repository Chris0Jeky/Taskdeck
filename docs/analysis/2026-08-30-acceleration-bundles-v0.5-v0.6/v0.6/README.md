# v0.6 — Under Your Rules: curated acceleration material

Last Updated: 2026-09-02

Start with `ARCHITECTURE.md` (thesis, live dependency graph, waves, gates, risks, ownership fences),
then the file for the issue you are admitting. Every file names what it corrected in the bundle.

| Issue | File | Class | Startable now |
| --- | --- | --- | --- |
| CF-10 `#2264` | `CF-10-processing-profiles-router.md` | core policy | contract slice only |
| CF-11 `#2265` | `CF-11-processing-cache-escalation.md` | hardening | cache-key canonicalizer only |
| CF-24B `#2277` | `CF-24B-runtime-metrics-dashboard.md` | core measurement | metric dictionary only |
| CF-15 `#2269` | `CF-15-cloud-speech-adapter.md` | optional breadth | no |
| CF-17 `#2271` | `CF-17-meeting-understanding.md` | optional breadth | no |
| CF-18 `#2272` | `CF-18-local-ocr-sidecar.md` | optional breadth | no |
| CF-22 `#2275` | `CF-22-authority-shadow.md` | gated stretch, shadow-only | record contract only; execution never without the maintainer go |

Supporting material: `schemas/` + `fixtures/` (contract drafts, checked by
`scripts/context_fabric/check_contract_drafts.py` through `../contracts.manifest.json`),
`candidates/` (reference code, see its README), `diagrams/` (`.dot` sources and `.svg` renders:
architecture, dependencies, router sequence, cache escalation, cloud speech boundary, local OCR path,
meeting understanding, runtime metrics flow, authority safety ladder, agent integration train).
