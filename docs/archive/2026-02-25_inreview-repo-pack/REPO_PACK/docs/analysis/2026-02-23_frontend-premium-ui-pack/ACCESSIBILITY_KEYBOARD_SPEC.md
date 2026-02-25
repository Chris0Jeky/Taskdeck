# Accessibility + Keyboard Spec (Premium UX Contract)
Date: 2026-02-23
Status: Draft

This spec is intended to become a “contract” you test and maintain.

---

## Accessibility baseline targets
- WCAG 2.2 AA where feasible for a solo product.
WCAG 2.2: https://www.w3.org/TR/WCAG22/

Key criteria relevant to Taskdeck:
- 2.4.11 Focus Not Obscured (AA)
- 2.5.7 Dragging Movements (AA)
- 2.5.8 Target Size (Minimum) (AA)
- 2.4.7 Focus Visible (AA)
Understanding target size: https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html

---

## Keyboard-first requirements
### Global
- `Ctrl/Cmd+K`: command palette (if you keep this convention)
- `?`: keyboard shortcuts help
- `Esc`: closes top-most overlay (escape stack)
- focus is restored after overlay closes

### Lists (Inbox, activity, logs)
- arrow or `j/k` navigation is optional, but must not break Tab order
- Enter opens item
- `Esc` closes details panel

### Board
- keyboard alternative for move between columns
- card modal is reachable and closable via keyboard
- drag-only operations must have a click/keyboard alternative (WCAG 2.5.7)

---

## ARIA patterns
Use WAI-ARIA Authoring Practices as the reference for complex widgets.
APG: https://wai-aria-practices.netlify.app/aria-practices/
Example index: https://www.w3.org/TR/2021/NOTE-wai-aria-practices-1.2-20211129/examples/

Guidance:
- Prefer semantic HTML first.
- Add ARIA only when necessary.
- For roving tabindex / list navigation, follow APG patterns.

---

## Focus management rules
- Modals trap focus, initial focus on the first meaningful field.
- Closing modal returns focus to the triggering control.
- Focus indicators must be clearly visible.
Focus appearance understanding: https://www.w3.org/WAI/WCAG22/Understanding/focus-appearance.html

---

## Target size and spacing
- Interactive targets should be at least 24x24 CSS px where possible.
WCAG target size guidance: https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html

Apply this strongly to:
- drag handles
- icon buttons
- small chips/tags
- close icons

---

## Reduced motion
- Respect `prefers-reduced-motion`
- Provide a setting toggle if you have complex animations

---

## Manual accessibility test script (add to MANUAL_TEST_CHECKLIST)
1) keyboard-only navigation through shell
2) open/close command palette, help overlay
3) open/close card modal, ensure focus restores
4) board move operation without drag
5) verify focus never becomes hidden behind overlay (2.4.11)
6) verify small targets are usable (2.5.8)

---

## Automated a11y testing (optional but recommended)
- Add basic a11y checks to component tests and E2E.
Common approach:
- axe-core integration in Playwright
(If you adopt it, define a baseline rule set and allow a small exception list with owner and expiry.)
