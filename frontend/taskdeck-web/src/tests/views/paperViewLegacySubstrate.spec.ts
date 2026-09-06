import { describe, expect, it } from 'vitest'

import { PAPER_VIEW_ROOTS } from './paperRootInventory'

import activitySource from '../../views/ActivityView.vue?raw'
import agentRunDetailSource from '../../views/AgentRunDetailView.vue?raw'
import agentRunsSource from '../../views/AgentRunsView.vue?raw'
import agentsSource from '../../views/AgentsView.vue?raw'
import apiKeysSource from '../../views/ApiKeySettingsView.vue?raw'
import appearanceSource from '../../views/AppearanceSettingsView.vue?raw'
import archiveSource from '../../views/ArchiveView.vue?raw'
import automationChatSource from '../../views/AutomationChatView.vue?raw'
import automationQueueSource from '../../views/AutomationQueueView.vue?raw'
import boardAccessSource from '../../views/BoardAccessView.vue?raw'
import boardsSource from '../../views/BoardsListView.vue?raw'
import calendarSource from '../../views/CalendarView.vue?raw'
import devToolsSource from '../../views/DevToolsView.vue?raw'
import exportImportSource from '../../views/ExportImportView.vue?raw'
import integrationsSource from '../../views/IntegrationsView.vue?raw'
import metricsSource from '../../views/MetricsView.vue?raw'
import notFoundSource from '../../views/NotFoundView.vue?raw'
import notificationInboxSource from '../../views/NotificationInboxView.vue?raw'
import notificationPrefsSource from '../../views/NotificationPreferencesView.vue?raw'
import opsConsoleSource from '../../views/OpsConsoleView.vue?raw'
import profileSource from '../../views/ProfileSettingsView.vue?raw'
import savedViewsSource from '../../views/SavedViewsView.vue?raw'

/**
 * Legacy ("off") mode substrate guard for the #1769 Paper view restyle wave.
 *
 * Coverage was extended (#1815) from the four #1780 roots to every Paper-idiom
 * view root in the wave: the #1775 Saved Views root (#1813), the six Settings
 * roots from PR #1808, and the secondary roots from PR #1810.
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

type PaperViewName = (typeof PAPER_VIEW_ROOTS)[number]['view']

const VIEW_SOURCES: Readonly<Record<PaperViewName, string>> = {
  'ActivityView.vue': activitySource,
  'AgentRunDetailView.vue': agentRunDetailSource,
  'AgentRunsView.vue': agentRunsSource,
  'AgentsView.vue': agentsSource,
  'ApiKeySettingsView.vue': apiKeysSource,
  'AppearanceSettingsView.vue': appearanceSource,
  'ArchiveView.vue': archiveSource,
  'AutomationChatView.vue': automationChatSource,
  'AutomationQueueView.vue': automationQueueSource,
  'BoardAccessView.vue': boardAccessSource,
  'BoardsListView.vue': boardsSource,
  'CalendarView.vue': calendarSource,
  'DevToolsView.vue': devToolsSource,
  'ExportImportView.vue': exportImportSource,
  'IntegrationsView.vue': integrationsSource,
  'MetricsView.vue': metricsSource,
  'NotFoundView.vue': notFoundSource,
  'NotificationInboxView.vue': notificationInboxSource,
  'NotificationPreferencesView.vue': notificationPrefsSource,
  'OpsConsoleView.vue': opsConsoleSource,
  'ProfileSettingsView.vue': profileSource,
  'SavedViewsView.vue': savedViewsSource,
}

const VIEW_ROOTS = PAPER_VIEW_ROOTS.map(({ view, selector }) => ({
  view,
  selector,
  source: VIEW_SOURCES[view],
}))

/**
 * A root satisfies the invariant by painting ANY Paper substrate token, not
 * only `--paper`. Most roots are full-bleed pages and paint `--paper`; a few
 * (e.g. `.paper-not-found`) are self-contained card panels whose substrate is
 * `--paper-card`. Both leave the root's ink on a Paper-family ground in Legacy
 * mode, and the contrast assertion below is measured against whichever literal
 * fallback the root actually paints — so widening the token set does not weaken
 * the legibility guarantee. What is still forbidden is painting nothing.
 */
const SUBSTRATE = /background(?:-color)?:\s*var\(--paper(?:-card|-2)?,\s*(#[0-9a-fA-F]{3,8})\s*\)/

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
    expect(rule).toMatch(SUBSTRATE)
  })

  it.each(VIEW_ROOTS)(
    '$view $selector fallback ink clears WCAG AA on its fallback paper',
    ({ view, selector, source }) => {
      const rule = readRootRule(source, selector)
      const ink = rule.match(/color:\s*var\(--ink,\s*(#[0-9a-fA-F]{3,8})\s*\)/)?.[1]
      const paper = rule.match(SUBSTRATE)?.[1]
      expect(ink, `${view} ${selector} ink fallback`).toBeTruthy()
      expect(paper, `${view} ${selector} paper fallback`).toBeTruthy()

      expect(contrast(ink as string, paper as string)).toBeGreaterThanOrEqual(4.5)
    },
  )
})
