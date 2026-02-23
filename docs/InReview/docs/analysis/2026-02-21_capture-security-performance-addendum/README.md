# 2026-02-21 Capture + Security + Performance Addendum

This folder contains an “extra explicit” addendum pack for Taskdeck.

## Files

1) `OPENAPI_capture_inbox_v1.yaml`  
Draft OpenAPI spec for `/api/capture/*` endpoints.

2) `BACKEND_SLICE_SKELETON_capture_inbox_v1.md`  
File-path-level backend implementation skeleton for a Capture Inbox MVP.

3) `SECURITY_TRUSTWORTHINESS_PLAYBOOK.md`  
Practical security + trust roadmap, aligned with Taskdeck’s current architecture (proposals, worker, gating).

4) `PERFORMANCE_RESPONSIVENESS_PLAYBOOK.md`  
Practical performance and responsiveness plan for API + worker + UI.

5) `ISSUE_SEEDS_capture_security_performance.md`  
Ready-to-copy issue drafts.

## Intended use

- Copy this folder into your repo under `docs/analysis/`.
- Create issues from `ISSUE_SEEDS_*`.
- Treat the skeleton doc as the canonical implementation map for the Capture Inbox MVP.

## Notes

- The skeleton intentionally reuses `LlmRequest` as the capture persistence layer to minimize schema surface area for MVP.
- If you later decide to split capture into dedicated tables, keep the `/api/capture/*` contract stable and swap the backend storage behind it.
