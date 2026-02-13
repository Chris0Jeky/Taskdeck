# Automation Proposal and Review Flow Specification

Last Updated: 2026-02-12
Status: Design complete, implementation pending

## 1. Purpose

Define a safe automation workflow where all automated changes are reviewable before apply.
This includes queue management, proposal editing, diff visibility, and approval controls.

## 2. Problem Statement

Current system has queue endpoints but no frontend for:
- request authoring and status monitoring,
- proposal review and interception,
- edit-before-apply controls,
- mutation diff visibility.

Personal objective requires ability to inject/intercept automation behavior from UI.

## 3. Current Backend Capability

Available now:
- `POST /api/llm-queue`
- `GET /api/llm-queue/user/{userId}`
- `GET /api/llm-queue/status/{status}`
- `POST /api/llm-queue/{requestId}/cancel?userId=...`
- `POST /api/llm-queue/process-next`
- `GET /api/llm-queue/stats`

Missing for full proposal flow:
- proposal CRUD and lifecycle endpoints
- diff preview endpoint
- approval token or policy gate endpoint

## 4. Target UX Surfaces

Primary routes:
- `/workspace/automations/queue`
- `/workspace/automations/proposals`

Primary panels/components:
- request composer
- queue table (status tabs)
- proposal list with filters
- proposal detail drawer
- diff preview viewer
- approve/reject/edit action bar

## 5. Core Data Contracts

Frontend types required:
- `AutomationRequest`
- `AutomationProposal`
- `ProposalMutationIntent`
- `ProposalDiff`
- `ProposalDecision`

Suggested proposal fields:
- `id`
- `origin` (`manual`, `voice`, `transcript`, `agent`)
- `boardId`
- `requestedBy`
- `status` (`pending-review`, `approved`, `rejected`, `applied`, `failed`)
- `createdAt`
- `updatedAt`
- `intents[]`
- `diffSummary`
- `riskLevel`

## 6. Workflow Definition

1. Submit request
- user submits automation request payload from composer
- queue item created (`Pending`)

2. Process to proposal
- queue processor or manual process action generates proposal
- proposal appears in `pending-review`

3. Review proposal
- user inspects mutation intents + diff preview
- user can edit proposal payload before decision

4. Decision
- `Approve`: applies mutation
- `Reject`: stores rejection reason
- `Edit + Approve`: applies revised mutation

5. Post-action visibility
- show resulting operation status
- write audit/log trace and refresh board state

## 7. Diff Model Requirements

Diff viewer must include:
- entity type and id
- field-level before/after values
- operation type (`create`, `update`, `move`, `delete`, `archive`, `restore`)
- risk tags (`destructive`, `cross-column`, `permission-sensitive`)

Minimum view modes:
- summary view
- expanded field diff view
- raw JSON intent view

## 8. Safety and Policy Controls

Mandatory policy defaults:
- no automatic apply by default
- destructive actions always require explicit approval
- board-level permission checks before apply
- invalid or stale proposal rejected with clear reason

Recommended policy settings in UI:
- require-review toggle (default on)
- auto-expire stale proposals
- approval role requirement per board

## 9. Injection and Interception Controls

Injection capabilities:
- create proposal manually from command text or structured form
- import transcript text and generate proposal draft

Interception capabilities:
- pause queue item before apply
- edit intent payload
- redirect target board/column when allowed

All interception actions must be audit-logged.

## 10. Required Backend Additions

To fully support this UX, add endpoints:
- `POST /api/automation/proposals`
- `GET /api/automation/proposals`
- `GET /api/automation/proposals/{id}`
- `POST /api/automation/proposals/{id}/approve`
- `POST /api/automation/proposals/{id}/reject`
- `POST /api/automation/proposals/{id}/edit`
- `GET /api/automation/proposals/{id}/diff`

## 11. Telemetry and Diagnostics

Track metrics:
- queue depth by status
- mean time to review
- approval rate vs rejection rate
- apply failure rate
- proposal edit frequency

Expose these in automation dashboard cards and charts.

## 12. Testing Requirements

Unit:
- proposal status transition rules
- diff renderer formatting and risk tagging

Integration:
- queue fetch + cancel + process interactions
- proposal decision calls and state updates

E2E:
- submit request -> process -> review -> approve
- edit proposal before approval
- reject destructive proposal with reason
- verify resulting board changes and activity trace

## 13. Definition of Done

Automation review slice is complete when:
- queue and proposal surfaces are functional,
- users can review/edit/approve/reject without leaving UI,
- diff view is available before any apply,
- policy defaults prevent unsupervised mutation,
- audit and logs capture every decision path.
