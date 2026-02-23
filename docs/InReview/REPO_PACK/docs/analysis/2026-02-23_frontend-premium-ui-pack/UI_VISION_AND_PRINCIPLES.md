# UI Vision and Principles (Premium Feel)
Date: 2026-02-23
Status: Draft

## The UX thesis
Taskdeck should feel like a **calm execution cockpit**:
- fast capture
- low-noise planning
- safe automation with visibility and control
- keyboard-first without being hostile to mouse users

## What “premium feel” means operationally
Premium is not “rounded corners”.
Premium is:

### 1) Cohesion
- every interactive element looks like it belongs to the same product
- spacing and typography follow consistent scales (tokens)
- a small number of visual surfaces and elevations

### 2) Predictability / trust
- users can predict outcomes
- system status is always visible (loading/progress/result)
- automation changes are reviewable and explainable
NNGroup: visibility of system status builds trust.  
https://www.nngroup.com/articles/visibility-system-status/

### 3) Speed
- capture modal opens instantly
- board interactions do not stutter
- expensive work is backgrounded (triage jobs, proposal generation)

### 4) Accessibility (as polish)
- large enough click targets (WCAG 2.5.8 Target Size Minimum)
- keyboard focus never disappears (WCAG 2.4.11 Focus Not Obscured; 2.4.7 Focus Visible)
- drag actions must have non-drag alternatives (WCAG 2.5.7 Dragging Movements)
WCAG 2.2: https://www.w3.org/TR/WCAG22/

### 5) Minimalism without ambiguity
NNGroup heuristic #8 (aesthetic/minimalist) + #4 (consistency) apply strongly here.  
https://www.nngroup.com/articles/ten-usability-heuristics/

## Design constraints for Taskdeck specifically
- Data density matters (boards, logs, ops outputs) → design for “dense but readable”
- Many surfaces (boards, proposals, ops console, archive, inbox) → primitives must be shared
- Keyboard-first is a differentiator → make it discoverable (help overlay, command palette)

## Interaction contracts (non-negotiable)
These should become “UI contracts” you test:
- escape stack behavior (Esc closes the topmost surface)
- focus restoration (closing modal returns focus)
- consistent toast + aria-live behavior
- drag handles do not conflict with click-to-open
- “proposal-first” UX: nothing large applies without explicit approval

## “Screens” that must feel world-class
1) AppShell (navigation, command palette, shortcuts help)
2) Board (cards, filters, quick actions, drag/move)
3) Inbox (capture + triage + proposal link)
4) Proposal review (summary + diff + approve/apply)
5) Ops console (permissions clarity + safe running)
