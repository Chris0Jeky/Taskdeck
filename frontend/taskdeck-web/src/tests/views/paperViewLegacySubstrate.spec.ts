import { describe, expect, it } from 'vitest'

import activitySource from '../../views/ActivityView.vue?raw'
import boardsSource from '../../views/BoardsListView.vue?raw'
import calendarSource from '../../views/CalendarView.vue?raw'
import metricsSource from '../../views/MetricsView.vue?raw'

/**
 * Legacy ("off") mode substrate guard for the #1780 Paper view restyles.
 *
 * `src/paper-tokens.css` scopes every Paper variable under `.paper` /
 * `.paper-night` — they DO NOT apply at `:root`. With Appearance set to
 * "Off (Legacy / Obsidian)" the body carries neither class
 * (`paperThemeStore.resolveBodyClass('off')` returns `null`), so a restyled
 * view's `color: var(--ink, #1a1814)` resolves to the near-black literal
 * fallback while `AppShell`'s `.td-content` still paints the Obsidian
 * `--td-surface-base` (#131313) — the `.td-shell--paper .td-content` cream
 * repaint is gated on `paperTheme.isOn`. These routes have no Paper/Legacy
 * component switch, so one component renders in both modes.
 *
 * The fix, and the invariant this test pins: a view root that sets the Paper
 * ink MUST also paint the Paper substrate. It is a no-op under
 * `.paper`/`.paper-night` (`.td-content` already paints `var(--paper)` there),
 * and in Legacy mode the two literal fallbacks land together at AA contrast.
 *
 * Sources are pulled in with Vite's `?raw` rather than `node:fs`: this spec is
 * type-checked by `tsconfig.vitest.json`, whose `types` deliberately omits
 * "node", and its quarantine list may only shrink.
 */

const VIEW_ROOTS: ReadonlyArray<{ view: string; selector: string; source: string }> = [
  { view: 'MetricsView.vue', selector: '.paper-metrics', source: metricsSource },
  { view: 'ActivityView.vue', selector: '.paper-activity', source: activitySource },
  { view: 'CalendarView.vue', selector: '.paper-calendar', source: calendarSource },
  { view: 'BoardsListView.vue', selector: '.paper-boards', source: boardsSource },
]

/** Read the first top-level rule body for `selector` (these blocks contain no nested braces). */
function readRootRule(source: string, selector: string): string {
  const pattern = new RegExp(`^\\${selector}\\s*\\{([\\s\\S]*?)\\}`, 'm')
  const match = source.match(pattern)
  if (!match) throw new Error(`Could not locate the ${selector} rule`)
  return match[1]
}

function hexToRgb(hex: string): [number, number, number] {
  let h = hex.replace('#', '')
  if (h.length === 3) h = h.split('').map((c) => c + c).join('')
  return [parseInt(h.slice(0, 2), 16), parseInt(h.slice(2, 4), 16), parseInt(h.slice(4, 6), 16)]
}

function relativeLuminance([r, g, b]: [number, number, number]): number {
  const lin = (c: number) => {
    const s = c / 255
    return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4)
  }
  return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b)
}

function contrast(a: string, b: string): number {
  const la = relativeLuminance(hexToRgb(a))
  const lb = relativeLuminance(hexToRgb(b))
  const [hi, lo] = la >= lb ? [la, lb] : [lb, la]
  return (hi + 0.05) / (lo + 0.05)
}

describe('Paper view roots stay legible in Legacy mode', () => {
  it.each(VIEW_ROOTS)('$view $selector paints --paper wherever it sets --ink', ({ selector, source }) => {
    const rule = readRootRule(source, selector)

    // Guard the guard: if the ink declaration is ever dropped or renamed, the
    // substrate assertion below would otherwise pass vacuously.
    expect(rule).toMatch(/color:\s*var\(--ink,\s*#[0-9a-fA-F]{3,8}\s*\)/)
    expect(rule).toMatch(/background(-color)?:\s*var\(--paper,\s*#[0-9a-fA-F]{3,8}\s*\)/)
  })

  it.each(VIEW_ROOTS)(
    '$view $selector fallback ink clears WCAG AA on its fallback paper',
    ({ view, selector, source }) => {
      const rule = readRootRule(source, selector)
      const ink = rule.match(/color:\s*var\(--ink,\s*(#[0-9a-fA-F]{3,8})\s*\)/)?.[1]
      const paper = rule.match(/background(?:-color)?:\s*var\(--paper,\s*(#[0-9a-fA-F]{3,8})\s*\)/)?.[1]
      expect(ink, `${view} ${selector} ink fallback`).toBeTruthy()
      expect(paper, `${view} ${selector} paper fallback`).toBeTruthy()

      expect(contrast(ink as string, paper as string)).toBeGreaterThanOrEqual(4.5)
    },
  )
})
