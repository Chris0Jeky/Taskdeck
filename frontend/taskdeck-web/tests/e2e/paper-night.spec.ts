/**
 * E2E: Paper at Night dark theme verification (PAPER-12 / #1008)
 *
 * Verifies the foundation + shell of the Paper & Graphite, Ember Edition
 * overhaul render correctly in `paper-night` mode. Surface-level coverage
 * (Home, Board, Review, Inbox, Today, Misc) is added in follow-ups once
 * those surface PRs merge.
 *
 * Routes covered:
 * - `/styleguide/paper` — public, renders every primitive + every utility
 *   class in both light and night frames.
 *
 * Strategy:
 * - Set `paperThemeStore` to `paper-night` via localStorage before navigation
 *   so the body class is set on first paint (no FOUC race).
 * - Capture console errors during the run; assert none from app-origin sources
 *   (we ignore transient resource failures like Google Fonts, which can fail
 *   on offline/restricted-network CI lanes without invalidating the night
 *   render check).
 * - Assert the body has the `paper-night` class (anchored, no substring leak).
 * - Assert at least one element computed style uses the night ember.
 *
 * Surface follow-ups (post-merge of #1013 #1014 #1025 #1026 #1027 #1028):
 *   /workspace/home, /workspace/boards/:id, /workspace/review,
 *   /workspace/inbox, /workspace/today, /workspace/cards/:id
 */

import { expect, test, type ConsoleMessage } from '@playwright/test'

const NIGHT_EMBER = 'rgb(217, 106, 62)' // #d96a3e
const PAPER_NIGHT_CLASS = /(^|\s)paper-night(\s|$)/
const PAPER_LIGHT_CLASS = /(^|\s)paper(\s|$)/

/**
 * Filter to console errors that indicate an app-side problem.  Resource-
 * loading failures (Google Fonts in `paper-tokens.css`, fonts.gstatic.com,
 * etc.) routinely fire `console.error` in restricted-network CI lanes and
 * do NOT invalidate the night-render correctness signal.  Keep only errors
 * that look like JS exceptions or app-origin warnings.
 */
function isAppRelevantError(text: string): boolean {
  const lowered = text.toLowerCase()
  // Resource-load failures we explicitly tolerate
  if (lowered.includes('failed to load resource')) return false
  if (lowered.includes('net::err_')) return false
  if (lowered.includes('fonts.googleapis.com')) return false
  if (lowered.includes('fonts.gstatic.com')) return false
  return true
}

test.describe('Paper at Night — foundation + shell', () => {
  test('styleguide renders without console errors and uses night ember', async ({
    page,
  }) => {
    const consoleErrors: string[] = []
    page.on('console', (msg: ConsoleMessage) => {
      if (msg.type() === 'error') consoleErrors.push(msg.text())
    })

    await page.addInitScript(() => {
      window.localStorage.setItem('td.paper.mode', 'paper-night')
    })

    await page.goto('/styleguide/paper')
    await expect(page.locator('body')).toHaveClass(PAPER_NIGHT_CLASS)

    // Find an ember-toned utility (the styleguide always renders a tagstamp).
    const ember = page.locator('.tagstamp', { hasText: 'PROPOSED' }).first()
    await expect(ember).toBeVisible()
    const color = await ember.evaluate((el) =>
      window.getComputedStyle(el as HTMLElement).color,
    )
    expect(color).toBe(NIGHT_EMBER)

    const appErrors = consoleErrors.filter(isAppRelevantError)
    expect(
      appErrors,
      `App-relevant console errors during paper-night styleguide render: ${appErrors.join('\n')}`,
    ).toEqual([])
  })

  test('styleguide light mode does not leak night ember', async ({ page }) => {
    await page.addInitScript(() => {
      window.localStorage.setItem('td.paper.mode', 'paper')
    })
    await page.goto('/styleguide/paper')
    await expect(page.locator('body')).toHaveClass(PAPER_LIGHT_CLASS)
    const ember = page.locator('.tagstamp', { hasText: 'PROPOSED' }).first()
    const color = await ember.evaluate((el) =>
      window.getComputedStyle(el as HTMLElement).color,
    )
    expect(color).not.toBe(NIGHT_EMBER) // should be #a8421f light ember
  })
})
