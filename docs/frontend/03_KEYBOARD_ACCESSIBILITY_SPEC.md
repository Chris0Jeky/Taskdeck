# Keyboard-First and Accessibility Specification

Last Updated: 2026-02-12
Status: Decision-complete specification for redesign implementation

## 1. Purpose

This specification defines keyboard interaction contracts and accessibility baselines for the frontend overhaul.

It addresses explicit personal-note requirements:
- navigate create/edit flows without mouse,
- move between fields (including future checklist surfaces) with keyboard only,
- keep typing focus reliable in editor areas.

## 2. Core Principles

1. Keyboard parity
- every primary action available by mouse must be keyboard-accessible.

2. Predictable focus
- focus transitions are deterministic and visible.

3. Scoped shortcuts
- shortcut handlers are context-aware and do not hijack typing inputs.

4. Escape stack
- `Escape` closes only the top-most active layer.

5. Accessibility baseline
- interactions must satisfy WCAG-oriented keyboard and focus requirements.

## 3. Shortcut Context Model

Define explicit contexts:
- `global-shell`
- `board-canvas`
- `card-editor`
- `column-editor`
- `project-editor` (future)
- `checklist-editor` (future)
- `modal/drawer`
- `ops-console`

Resolution rule:
- active child context overrides parent context shortcuts, except global emergency shortcuts (`Escape`, help toggle).

## 4. Baseline Shortcut Set

Global:
- `Ctrl/Cmd+K`: open command palette
- `?`: open keyboard help
- `Escape`: close top-most surface

Board navigation:
- `h`/`ArrowLeft`: previous column
- `l`/`ArrowRight`: next column
- `j`/`ArrowDown`: next card in active column
- `k`/`ArrowUp`: previous card in active column
- `Enter`: open selected card editor
- `n`: new card in selected column
- `Shift+N`: new column

Editor actions:
- `Ctrl/Cmd+S`: save section
- `Ctrl/Cmd+Enter`: save and close
- `Alt+1`: jump to title
- `Alt+2`: jump to description
- `Alt+3`: jump to checklist section (future)
- `Alt+4`: jump to labels
- `Alt+5`: jump to due date/blocking

Ops surface:
- `Ctrl/Cmd+Enter`: run selected command/request
- `Ctrl/Cmd+L`: focus logs filter

## 5. Focus Graph Contracts

## 5.1 Card Editor Focus Graph

Required tab order:
1. title
2. description
3. checklist block (placeholder while feature pending)
4. labels
5. due date
6. blocked toggle
7. block reason (conditional)
8. metadata region
9. save/cancel actions

Checklist placeholder rule:
- before checklist feature exists, keep a visible disabled focus target with explanatory text to preserve future navigation contract.

## 5.2 Create/Edit Workflow Without Mouse

For board, column, and card forms:
- open form by shortcut
- first editable field receives focus automatically
- submit by `Ctrl/Cmd+Enter` or primary button with keyboard focus
- on success, focus returns to logical origin (new item card/column row)
- on failure, focus moves to first invalid field

## 6. Typing Safety Rules

Shortcut guard rules:
- while focus is in input/textarea/contenteditable, navigation shortcuts are suppressed.
- exceptions: `Escape`, explicit save shortcuts, and palette shortcut.
- prevent accidental board navigation while typing in description/checklist fields.

## 7. Visual Focus Requirements

Must implement:
- high-contrast focus rings
- visible selected-card and selected-column states
- no focus loss on async refresh
- no hidden focus in off-screen containers

## 8. Accessibility Requirements

Minimum implementation baseline:
- semantic landmark structure (`header`, `nav`, `main`, `aside`)
- correct button/input labeling
- ARIA for dialogs/drawers/tabs where applicable
- `aria-live` region for toast messages and async updates
- keyboard trap within modal/drawer while open
- restore focus to invoker on close
- color contrast checks for status and label chips

## 9. Screen Reader Interaction Baseline

Required announcements:
- board change
- column/card selection change
- mutation success/failure
- queue status updates in automation center
- command run completion in ops console

## 10. Testing Specification

Unit tests:
- shortcut registry scope resolution
- typing guard behavior
- escape stack behavior

Integration tests:
- focus graph traversal for card editor
- save error focus handling
- context switching between board and modal

E2E tests:
- full keyboard-only flow: create board -> create column -> create card -> edit description -> save
- jump from description to checklist placeholder and back without mouse
- permission-denied action surfaces keyboard accessible explanation
- ops console run via keyboard shortcuts

## 11. Definition of Done

Keyboard/accessibility slice is complete when:
- each primary action has a documented shortcut and working keyboard path,
- focus graphs are implemented for all active editors,
- no critical flow requires mouse usage,
- E2E keyboard-only scripts pass,
- accessibility checks pass for focus/labels/dialog behavior.
