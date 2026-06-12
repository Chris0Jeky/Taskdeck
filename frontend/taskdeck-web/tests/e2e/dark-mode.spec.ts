/**
 * E2E: Dark Mode (Paper night theme) Scenarios
 *
 * Dark mode ships as the Paper night theme: PaperSidebar exposes a theme
 * toggle ("Switch to dark Paper theme") and paperThemeStore applies the
 * `paper-night` class to <body>, persisting the choice in localStorage
 * under `td.paper.mode`. Paper mode must be ON for the toggle to exist,
 * so tests seed `td.paper.mode = 'paper'` before app load (same pattern
 * as paper-night.spec.ts / paper-board-drag.spec.ts).
 *
 * Covered:
 * - Night theme persists when navigating between Home, Boards, Inbox, and Today views
 * - Night theme board view renders column headings visible with non-zero dimensions
 * - Toggling the night theme off restores the light Paper theme
 *
 * Still pending (test.fixme):
 * - System prefers-color-scheme on first visit (default mode is 'off'; the
 *   opt-in 'auto' mode exists but automatic first-visit detection is not shipped)
 */

import type { Page } from '@playwright/test'
import { expect, test } from '@playwright/test'
import { registerAndAttachSession, type AuthResult } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'

let auth: AuthResult

test.beforeEach(async ({ page, request }) => {
  auth = await registerAndAttachSession(page, request, 'dark-mode')
})

// --- Helpers ---

const PAPER_NIGHT_CLASS = /(^|\s)paper-night(\s|$)/
const PAPER_LIGHT_CLASS = /(^|\s)paper(\s|$)/

/**
 * Seed Paper mode before app load so PaperSidebar (and its theme toggle)
 * renders. Guarded so it does not clobber a night-mode value persisted by
 * the app between in-test navigations/reloads — `addInitScript` re-runs on
 * every document load.
 */
async function enablePaperMode(page: Page) {
  await page.addInitScript(() => {
    if (!window.localStorage.getItem('td.paper.mode')) {
      window.localStorage.setItem('td.paper.mode', 'paper')
    }
  })
}

function nightToggle(page: Page) {
  return page.getByRole('button', { name: 'Switch to dark Paper theme' })
}

function lightToggle(page: Page) {
  return page.getByRole('button', { name: 'Switch to light Paper theme' })
}

// --- Dark mode across multiple views ---

test('night theme should persist when navigating between Home, Boards, Inbox, and Today views', async ({ page }) => {
  await enablePaperMode(page)

  await page.goto('/workspace/home')
  await expect(page.getByTestId('paper-home-greeting')).toBeVisible()

  await nightToggle(page).click()
  await expect(page.locator('body')).toHaveClass(PAPER_NIGHT_CLASS)

  // Navigate to Boards (full page load — persists via td.paper.mode)
  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()
  await expect(page.locator('body')).toHaveClass(PAPER_NIGHT_CLASS)

  // Navigate to Inbox
  await page.goto('/workspace/inbox')
  await expect(page.getByRole('heading', { name: /what.s on your mind/i })).toBeVisible()
  await expect(page.locator('body')).toHaveClass(PAPER_NIGHT_CLASS)

  // Navigate to Today
  await page.goto('/workspace/today')
  await expect(page.locator('[data-paper-today]')).toBeVisible()
  await expect(page.locator('body')).toHaveClass(PAPER_NIGHT_CLASS)
})

// --- Dark mode with board content ---

test('night theme board view should render columns without invisible text', async ({ page, request }) => {
  await enablePaperMode(page)

  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Dark Mode Board',
    description: 'dark mode board test',
    columnNamePrefix: 'Dark Column',
  })

  await page.goto(`/workspace/boards/${boardId}`)
  await expect(page.getByRole('heading', { name: `Dark Mode Board ${seed}` })).toBeVisible()

  await nightToggle(page).click()
  await expect(page.locator('body')).toHaveClass(PAPER_NIGHT_CLASS)

  // Column heading should still be visible in the night theme
  const columnHeading = page.getByRole('heading', { name: `Dark Column ${seed}`, exact: true })
  await expect(columnHeading).toBeVisible()

  // Verify the column heading occupies real space (not collapsed or zero-size)
  const columnHeadingBox = await columnHeading.boundingBox()
  expect(columnHeadingBox).not.toBeNull()
  expect(columnHeadingBox!.width).toBeGreaterThan(0)
  expect(columnHeadingBox!.height).toBeGreaterThan(0)
})

// --- Toggling the night theme off restores the light Paper theme ---

test('toggling the night theme off should restore the light Paper theme', async ({ page }) => {
  await enablePaperMode(page)

  await page.goto('/workspace/home')
  await expect(page.getByTestId('paper-home-greeting')).toBeVisible()

  // Enable the night theme
  await nightToggle(page).click()
  await expect(page.locator('body')).toHaveClass(PAPER_NIGHT_CLASS)

  // Disable it again — the toggle label flips once night mode is active
  await lightToggle(page).click()
  await expect(page.locator('body')).toHaveClass(PAPER_LIGHT_CLASS)
  await expect(page.locator('body')).not.toHaveClass(PAPER_NIGHT_CLASS)
})

// --- System prefers-color-scheme ---
// FIXME(#1129): automatic first-visit dark-mode detection is not shipped.
// paperThemeStore supports an opt-in 'auto' mode (follows prefers-color-scheme)
// but the default mode is 'off' — nothing reacts to the OS scheme on first
// visit. Implement first-visit detection (or default to 'auto'), then enable.

test.fixme('system prefers-color-scheme dark should activate dark mode on first visit', async () => {
  // TODO: implement once automatic system dark mode detection is shipped
})
