/**
 * E2E: Dark Mode Scenarios
 *
 * All tests are marked test.fixme (#1129) because the selectors are stale:
 * they check for a `dark` CSS class but the Paper theme uses `paper-night`
 * via PaperSidebar's theme toggle. Update selectors to match Paper, then
 * convert back to test(...):
 * - Dark mode persists when navigating between Home, Boards, Inbox, and Today views
 * - Dark mode board view renders column headings visible with non-zero dimensions
 * - Toggling dark mode off restores light theme
 * - System prefers-color-scheme: dark
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

async function findDarkModeToggle(page: Page) {
  const toggle = page
    .getByRole('button', { name: /dark mode|theme|light|dark/i })
    .or(page.getByLabel(/dark mode|toggle theme/i))
    .first()

  if (await toggle.isVisible({ timeout: 5_000 }).catch(() => false)) {
    return toggle
  }
  return null
}

async function isDarkMode(page: Page): Promise<boolean> {
  return page.evaluate(() => {
    return (
      document.documentElement.classList.contains('dark') ||
      document.documentElement.dataset.theme === 'dark' ||
      document.body.classList.contains('dark') ||
      document.body.dataset.theme === 'dark'
    )
  })
}

async function enableDarkMode(page: Page): Promise<boolean> {
  const toggle = await findDarkModeToggle(page)
  if (!toggle) {
    return false
  }

  const alreadyDark = await isDarkMode(page)
  if (!alreadyDark) {
    await toggle.click()
  }
  return true
}

// --- Dark mode across multiple views ---
// FIXME(#1129): test selectors are stale — they check for a `dark` CSS class
// but the Paper theme uses `paper-night` via PaperSidebar's theme toggle.
// Update selectors to match the Paper design system.

test.fixme('dark mode should persist when navigating between Home, Boards, and Inbox views', async ({ page }) => {
  await page.goto('/workspace/home')
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()

  const activated = await enableDarkMode(page)
  expect(activated).toBeTruthy()

  expect(await isDarkMode(page)).toBeTruthy()

  // Navigate to Boards
  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()
  expect(await isDarkMode(page)).toBeTruthy()

  // Navigate to Inbox
  await page.goto('/workspace/inbox')
  await expect(page.getByRole('heading', { name: 'Inbox', exact: true })).toBeVisible()
  expect(await isDarkMode(page)).toBeTruthy()

  // Navigate to Today
  await page.goto('/workspace/today')
  await expect(page.getByRole('heading', { name: 'Today', exact: true })).toBeVisible()
  expect(await isDarkMode(page)).toBeTruthy()
})

// --- Dark mode with board content ---
// FIXME(#1129): stale selectors — check for `dark` class but Paper uses `paper-night`.

test.fixme('dark mode board view should render columns and cards without invisible text', async ({ page, request }) => {
  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Dark Mode Board',
    description: 'dark mode board test',
    columnNamePrefix: 'Dark Column',
  })

  await page.goto(`/workspace/boards/${boardId}`)
  await expect(page.getByRole('heading', { name: `Dark Mode Board ${seed}` })).toBeVisible()

  const activated = await enableDarkMode(page)
  expect(activated).toBeTruthy()

  expect(await isDarkMode(page)).toBeTruthy()

  // Column heading should still be visible in dark mode
  const columnHeading = page.getByRole('heading', { name: `Dark Column ${seed}`, exact: true })
  await expect(columnHeading).toBeVisible()

  // Verify the column heading occupies real space (not collapsed or zero-size)
  const columnHeadingBox = await columnHeading.boundingBox()
  expect(columnHeadingBox).not.toBeNull()
  expect(columnHeadingBox!.width).toBeGreaterThan(0)
  expect(columnHeadingBox!.height).toBeGreaterThan(0)
})

// --- Toggling dark mode off restores light theme ---
// FIXME(#1129): stale selectors — check for `dark` class but Paper uses `paper-night`.

test.fixme('toggling dark mode off should restore light theme', async ({ page }) => {
  await page.goto('/workspace/home')
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()

  const toggle = await findDarkModeToggle(page)
  expect(toggle).not.toBeNull()

  // Enable dark mode
  const wasDark = await isDarkMode(page)
  if (!wasDark) {
    await toggle!.click()
  }
  expect(await isDarkMode(page)).toBeTruthy()

  // Disable dark mode
  await toggle!.click()
  expect(await isDarkMode(page)).toBeFalsy()
})

// --- System prefers-color-scheme ---

test.fixme('system prefers-color-scheme dark should activate dark mode on first visit', async () => {
  // TODO: implement once automatic system dark mode detection is shipped
})
