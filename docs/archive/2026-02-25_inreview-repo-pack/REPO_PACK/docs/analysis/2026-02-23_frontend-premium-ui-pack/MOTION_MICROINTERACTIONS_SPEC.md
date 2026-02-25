# Motion + Micro-interactions Spec
Date: 2026-02-23
Status: Draft

Premium feel often comes from consistent, subtle motion and crisp feedback.

---

## Motion goals
- reinforce causality (“I clicked this → that happened”)
- guide attention without being distracting
- avoid dropped frames (no heavy layout thrash)
- respect reduced-motion settings

Material guidance suggests desktop animations are often fastest (150–200ms) to remain responsive.  
https://m1.material.io/motion/duration-easing.html

---

## Motion tokens (recommended)
Add to design tokens:
- durations:
  - `--td-duration-1: 100ms`
  - `--td-duration-2: 150ms`
  - `--td-duration-3: 200ms`
  - `--td-duration-4: 300ms`
- easing:
  - `--td-ease-standard: cubic-bezier(...)`
  - `--td-ease-emphasized: ...`

Material motion tokens reference:
- https://m3.material.io/styles/motion/easing-and-duration/tokens-specs

---

## Where motion is allowed
- hover state transitions (color, shadow): 100–150ms
- open/close overlays (dialog/drawer): 150–250ms
- toast entrance/exit: 150–250ms
- drag “lift” feedback: immediate + subtle shadow/scale

Avoid:
- long animations
- layout-affecting transitions on large containers

---

## Reduced motion
Implement:
```css
@media (prefers-reduced-motion: reduce) {
  * { transition-duration: 0.01ms !important; animation-duration: 0.01ms !important; }
}
```
Or provide component-level conditional motion.

---

## Motion acceptance criteria
- no visible stutter during modal open/close
- drag feels immediate (no delayed drag-start)
- no animation blocks user input
- motion is consistent across components (same durations)
