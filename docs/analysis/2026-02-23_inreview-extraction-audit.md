# 2026-02-23 InReview Extraction Audit

Status: Non-authoritative audit record.

Purpose:
- verify that useful content from `docs/InReview` is either:
  - promoted into active docs, or
  - converted into dependency-mapped GitHub issues, or
  - explicitly deferred with issue tracking.

## Coverage Summary

- Capture automation/security/performance packs: extracted and issue-seeded (`#199` to `#213`, `#218` to `#220`).
- Human thesis/positioning pack: promoted into active docs and extended backlog (`#216`, `#217`; plus scope updates to `#77` and `#100`).
- No high-value untracked items remain in `docs/InReview` as of this audit.

## Source-to-Target Matrix

| Source | Key signal | Target location(s) | Status |
|---|---|---|---|
| `docs/InReview/HUMAN/00_OVERVIEW.md` | overall direction framing | `README.md`, `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md` | promoted |
| `docs/InReview/HUMAN/01_PRODUCT_THESIS.md` | maintenance-overhead thesis | `README.md`, `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/TaskdeckNextWorkChecklist.md`, `docs/SESSION_START_CHECKLIST.md` | promoted |
| `docs/InReview/HUMAN/02_MARKET_AND_VALUE.md` | value proposition and wedge | `README.md`, issue `#216` | promoted + seeded |
| `docs/InReview/HUMAN/03_EXECUTION_ROADMAP.md` | phased execution model | `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/ISSUE_EXECUTION_GUIDE.md`, issues `#199` to `#213` | promoted + seeded |
| `docs/InReview/HUMAN/04_GTM_AND_MARKETING.md` | demo/landing/beta ops | issue `#216`, wave index `#107` | seeded |
| `docs/InReview/HUMAN/05_USER_RESEARCH_PLAYBOOK.md` | interviews/usability validation | issue `#217`, scope extension in `#77` | seeded |
| `docs/InReview/HUMAN/06_INTERVIEW_STORY.md` | communication narrative for thesis/engineering story | issue `#216` and docs baseline (`README.md`) | partially promoted (execution issue open) |
| `docs/InReview/REPO_PACK/.../PRODUCT_BRIEF.md` | capture MVP product definition | issue tracker `#199`, execution issues `#200` to `#211` | seeded |
| `docs/InReview/REPO_PACK/.../AUTONOMY_TRUST_MODEL.md` | trust model and autonomy boundaries | `README.md`, `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md` | promoted |
| `docs/InReview/REPO_PACK/.../CAPTURE_PIPELINE_SPEC.md` | domain/workflow model | `#200`, `#201`, `#203`, `#204` | seeded |
| `docs/InReview/REPO_PACK/.../API_SPEC_CAPTURE.md` | capture API contract | `#201` | seeded |
| `docs/InReview/REPO_PACK/.../LLM_TRIAGE_CONTRACT.md` | strict output schema and validation | `#205` | seeded |
| `docs/InReview/REPO_PACK/.../UX_SPEC.md` | inbox/capture/provenance UX | `#206` to `#209` | seeded |
| `docs/InReview/REPO_PACK/.../TESTING_AND_VERIFICATION_PLAN.md` | test/E2E/manual requirements | `#210`, `#211`, `docs/TESTING_GUIDE.md`, `docs/MANUAL_TEST_CHECKLIST.md` | promoted + seeded |
| `docs/InReview/REPO_PACK/.../SECURITY_PRIVACY.md` | capture privacy and trust controls | `#81`, `#156`, `#212` | seeded |
| `docs/InReview/REPO_PACK/.../ISSUE_SEEDS.md` | capture issue decomposition | `#199` to `#211`, `#218` to `#220` | seeded |
| `docs/InReview/REPO_PACK/.../PROMOTION_CHECKLIST.md` | canonical doc promotion rules | `#211`, active docs update discipline | promoted + seeded |
| `docs/InReview/REPO_PACK/.github/ISSUE_TEMPLATE/feature.yml` | feature issue structure | `.github/ISSUE_TEMPLATE/feature.md` | adapted/promoted |
| `docs/InReview/docs/.../BACKEND_SLICE_SKELETON_capture_inbox_v1.md` | implementation shape options and routing details | `#200` to `#204` | seeded |
| `docs/InReview/docs/.../SECURITY_TRUSTWORTHINESS_PLAYBOOK.md` | security roadmap and redaction/rate-limit priorities | `#81`, `#156`, `#212` | seeded |
| `docs/InReview/docs/.../PERFORMANCE_RESPONSIVENESS_PLAYBOOK.md` | responsiveness/virtualization/perf expectations | `#213`, `#77` | seeded |
| `docs/InReview/docs/.../ISSUE_SEEDS_capture_security_performance.md` | capture+security+perf issue list | `#81`, `#201` to `#205`, `#212`, `#213` | seeded |
| `docs/InReview/docs/.../OPENAPI_capture_inbox_v1.yaml` | detailed API schema draft | `#201` implementation contract reference | seeded |

## Explicit Deferrals

Deferred in backlog (not immediate delivery):
- `#218` transcript capture source
- `#219` voice capture/transcription
- `#220` batch triage and suggestion editing

These are intentionally parked in Priority IV until capture MVP retention/trust baseline is proven.
