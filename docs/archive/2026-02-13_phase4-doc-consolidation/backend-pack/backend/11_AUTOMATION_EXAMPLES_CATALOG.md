# Automation Examples Catalog

Last Updated: 2026-02-12

## 1. Purpose

Provide concrete, testable automation examples that validate framework design and guide early implementation.

All examples follow proposal-first mode.

## 2. Example Template

Each example includes:
- input instruction,
- expected proposal operations,
- risk level,
- required approval,
- expected execution result,
- required tests.

## 3. Example Set

### EX-01: Create Card From Text Command
- Input:
  - "Create a high priority card in Backlog: Add JWT enforcement tests"
- Expected operations:
  - `card.create` with title, priority label, backlog column target
- Risk:
  - Low
- Approval:
  - Single reviewer
- Expected result:
  - card exists with requested metadata
- Tests:
  - planner mapping unit, proposal create integration, apply integration, E2E chat-to-proposal

### EX-02: Move Blocked Cards to Escalation
- Input:
  - "Move all blocked cards in board X to Escalation column"
- Expected operations:
  - repeated `card.move` operations scoped to blocked cards
- Risk:
  - Medium
- Approval:
  - Reviewer with board write permission
- Expected result:
  - only blocked cards moved; others unchanged
- Tests:
  - query filter and operation generation unit, conflict handling integration

### EX-03: Archive Done Cards Older Than 30 Days
- Input:
  - "Archive done cards older than 30 days"
- Expected operations:
  - `card.archive` operations with time filter
- Risk:
  - High
- Approval:
  - privileged reviewer; reason required
- Expected result:
  - matching cards archived and listed in archive inventory
- Tests:
  - risk classification unit, approval gate integration, restore path E2E follow-up

### EX-04: Bulk Relabel by Keyword
- Input:
  - "Add label technical-debt to cards containing refactor"
- Expected operations:
  - `label.assign` operations on filtered cards
- Risk:
  - Medium
- Approval:
  - single reviewer
- Expected result:
  - target cards have label; duplicates prevented
- Tests:
  - duplicate guard unit, apply integration

### EX-05: Reorder Columns by Policy
- Input:
  - "Move QA column before Done"
- Expected operations:
  - `column.reorder`
- Risk:
  - Medium
- Approval:
  - single reviewer
- Expected result:
  - column order updated with deterministic positions
- Tests:
  - reorder rules integration and E2E board validation

### EX-06: Board Health Summary (Read-only)
- Input:
  - "Summarize blockers and overdue cards on board X"
- Expected operations:
  - none (informational response only)
- Risk:
  - Low
- Approval:
  - not required (no proposal needed)
- Expected result:
  - assistant returns summary only
- Tests:
  - classifier unit ensures no mutation proposal generated

### EX-07: Split Oversized Card Into Subtasks
- Input:
  - "Split card ABC into 4 subtasks from checklist bullets"
- Expected operations:
  - `card.create` xN plus optional `card.update` linking parent
- Risk:
  - Medium
- Approval:
  - single reviewer
- Expected result:
  - subtasks created and linked
- Tests:
  - planner extraction unit, apply integration

### EX-08: Reject Unsafe Destructive Prompt
- Input:
  - "Delete every board now"
- Expected operations:
  - proposal may be generated with critical risk or blocked directly based on policy
- Risk:
  - Critical
- Approval:
  - blocked in initial policy baseline
- Expected result:
  - deny response with policy reason
- Tests:
  - policy deny unit and integration

### EX-09: Queue Transcript to Proposal
- Input:
  - transcript payload in `llm-queue`
- Expected operations:
  - queue item processed by worker to proposal
- Risk:
  - depends on generated actions
- Approval:
  - standard proposal review rules
- Expected result:
  - queue item completed, proposal available
- Tests:
  - worker integration and E2E queue-to-proposal

### EX-10: Restore Archived Card by Name
- Input:
  - "Restore archived card: API hardening checklist"
- Expected operations:
  - `archive.restore` operation proposal
- Risk:
  - Medium
- Approval:
  - reviewer with restore permission
- Expected result:
  - card restored with conflict strategy applied
- Tests:
  - archive restore integration + E2E

## 4. Example Coverage Matrix

| Example | Planner | Policy | Proposal API | Executor | Audit | E2E |
|---|---|---|---|---|---|---|
| EX-01 | yes | yes | yes | yes | yes | yes |
| EX-02 | yes | yes | yes | yes | yes | optional |
| EX-03 | yes | yes | yes | yes | yes | yes |
| EX-04 | yes | yes | yes | yes | yes | optional |
| EX-05 | yes | yes | yes | yes | yes | yes |
| EX-06 | yes | yes | n/a | n/a | yes | optional |
| EX-07 | yes | yes | yes | yes | yes | optional |
| EX-08 | yes | yes | optional | n/a | yes | optional |
| EX-09 | yes | yes | yes | yes | yes | yes |
| EX-10 | yes | yes | yes | yes | yes | yes |

## 5. Acceptance Criteria

- at least five examples are implemented and fully tested in first activation wave,
- at least three examples include E2E coverage,
- unsafe/destructive examples are explicitly blocked or high-friction approved.
