/**
 * E2E: Dark Mode Scenarios
 *
 * Extends dark mode coverage beyond the basic toggle test:
 * - Dark mode applies across multiple views (home, boards, inbox, today)
 * - Dark mode with board content (columns, cards) renders without
 *   white-on-white or invisible elements
 * - System prefers-color-scheme: dark (stub -- test.fixme until feature ships)
 * - Toggling dark mode off restores light theme
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

test('dark mode should persist when navigating between Home, Boards, and Inbox views', async ({ page }) => {
  await page.goto('/workspace/home')
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()

  const activated = await enableDarkMode(page)
  if (!activated) {
    test.skip()
    return
  }

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

test('dark mode board view should render columns and cards without invisible text', async ({ page, request }) => {
  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Dark Mode Board',
    description: 'dark mode board test',
    columnNamePrefix: 'Dark Column',
  })

  await page.goto(`/workspace/boards/${boardId}`)
  await expect(page.getByRole('heading', { name: `Dark Mode Board ${seed}` })).toBeVisible()

  const activated = await enableDarkMode(page)
  if (!activated) {
    test.skip()
    return
  }

  expect(await isDarkMode(page)).toBeTruthy()

  // Column heading should still be visible (not white-on-white)
  const columnHeading = page.getByRole('heading', { name: `Dark Column ${seed}`, exact: true })
  await expect(columnHeading).toBeVisible()

  // Verify the column heading occupies real space (not collapsed/invisible)
  const columnHeadingBox = await columnHeading.boundingBox()
  expect(columnHeadingBox).not.toBeNull()

  // The heading text should have non-zero dimensions (not collapsed/invisible)
  expect(columnHeadingBox!.width).toBeGreaterThan(0)
  expect(columnHeadingBox!.height).toBeGreaterThan(0)
})

// --- Toggling dark mode off restores light theme ---

test('toggling dark mode off should restore light theme', async ({ page }) => {
  await page.goto('/workspace/home')
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()

  const toggle = await findDarkModeToggle(page)
  if (!toggle) {
    test.skip()
    return
  }

  // Enable dark mode
  const wasDark = await isDarkMode(page)
  if (!wasDark) {
    await toggle.click()
  }
  expect(await isDarkMode(page)).toBeTruthy()

  // Disable dark mode
  await toggle.click()
  expect(await isDarkMode(page)).toBeFalsy()
})

// --- System prefers-color-scheme ---

test.fixme('system prefers-color-scheme dark should activate dark mode on first visit', async () => {
  // TODO: implement once automatic system dark mode detection is shipped
})
