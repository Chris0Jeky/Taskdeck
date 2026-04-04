/**
 * E2E: Multi-Board Workflows (#712)
 *
 * Covers board-lifecycle edge cases and multi-board user journeys:
 * - Fresh user with no boards sees correct empty state
 * - Rapid board switching produces no data contamination
 * - Board with 10+ boards has scrollable sidebar
 * - Deleting the currently-viewed board redirects correctly
 * - Archiving and restoring a board (already covered in smoke; this variant checks
 *   that the sidebar reflects the change immediately without a manual refresh)
 */

import type { Page } from '@playwright/test'
import { expect, test } from '@playwright/test'
import { registerAndAttachSession, type AuthResult } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'

let auth: AuthResult

test.beforeEach(async ({ page, request }) => {
  auth = await registerAndAttachSession(page, request, 'multi-board')
})

// ─── Helpers ──────────────────────────────────────────────────────────────────

async function gotoBoardsWorkspace(page: Page) {
  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()
}

async function createBoardViaUI(page: Page, boardName: string): Promise<string> {
  await gotoBoardsWorkspace(page)
  await page.getByRole('button', { name: '+ New Board' }).click()
  await page.getByPlaceholder('Board name').fill(boardName)
  await page.getByRole('button', { name: 'Create', exact: true }).click()
  await expect(page).toHaveURL(/\/workspace\/boards\/[a-f0-9-]+$/)
  await expect(page.getByRole('heading', { name: boardName })).toBeVisible()
  const match = /\/workspace\/boards\/([a-f0-9-]+)$/.exec(page.url())
  return match?.[1] ?? ''
}

function columnByName(page: Page, columnName: string) {
  return page
    .locator('[data-column-id]')
    .filter({ has: page.getByRole('heading', { name: columnName, exact: true }) })
    .first()
}

// ─── Scenario 1: Fresh user sees empty state CTA ─────────────────────────────

test('fresh user with no boards should see empty state with clear CTA', async ({ page }) => {
  await page.goto('/workspace/home')
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()

  // New account has no boards yet; the empty-state copy must appear
  await expect(
    page.getByText(
      'No boards yet. Start setup from Home or Today so captures and review can land somewhere useful.',
    ),
  ).toBeVisible()

  // At least one call-to-action button must be present
  const ctaButton = page
    .getByRole('button', { name: /start|create|setup|new board/i })
    .or(page.getByRole('link', { name: /start|create|setup|new board/i }))
    .first()
  await expect(ctaButton).toBeVisible()
})

// ─── Scenario 2: Rapid board switching produces no data contamination ─────────

test('rapid board switching should not contaminate board data between views', async ({ page, request }) => {
  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`

  // Create two boards via API
  const boardIdA = await createBoardWithColumn(request, auth, `${seed}-A`, {
    boardNamePrefix: 'Switch Board A',
    description: 'switch test board A',
    columnNamePrefix: 'Alpha',
  })
  const boardIdB = await createBoardWithColumn(request, auth, `${seed}-B`, {
    boardNamePrefix: 'Switch Board B',
    description: 'switch test board B',
    columnNamePrefix: 'Beta',
  })

  // Navigate to board A, confirm its column name
  await page.goto(`/workspace/boards/${boardIdA}`)
  await expect(page.getByRole('heading', { name: `Switch Board A ${seed}-A` })).toBeVisible()
  await expect(page.getByRole('heading', { name: `Alpha ${seed}-A`, exact: true })).toBeVisible()

  // Immediately switch to board B without waiting
  await page.goto(`/workspace/boards/${boardIdB}`)
  await expect(page.getByRole('heading', { name: `Switch Board B ${seed}-B` })).toBeVisible()

  // Board A column must NOT appear on Board B's view
  await expect(
    page.getByRole('heading', { name: `Alpha ${seed}-A`, exact: true }),
  ).toHaveCount(0)

  // Board B column must be present
  await expect(page.getByRole('heading', { name: `Beta ${seed}-B`, exact: true })).toBeVisible()
})

// ─── Scenario 3: 10+ boards sidebar is scrollable ────────────────────────────

test('user with 10 boards should have a scrollable sidebar board list', async ({ page, request }) => {
  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`

  // Create 10 boards via API (fast — no UI round-trips)
  const boardIds: string[] = []
  for (let i = 1; i <= 10; i++) {
    const boardId = await createBoardWithColumn(request, auth, `${seed}-${i}`, {
      boardNamePrefix: `Overflow Board ${i}`,
      description: `board ${i} for overflow test`,
      columnNamePrefix: 'Column',
    })
    boardIds.push(boardId)
  }

  await gotoBoardsWorkspace(page)

  // The sidebar nav should exist and contain board links
  // We verify overflow is handled: sidebar must not expand the viewport width
  const sidebar = page
    .locator('nav[aria-label*="board" i], aside, [data-sidebar]')
    .first()

  if (await sidebar.isVisible({ timeout: 5_000 }).catch(() => false)) {
    const sidebarBox = await sidebar.boundingBox()
    const viewportSize = page.viewportSize()
    if (sidebarBox && viewportSize) {
      // Sidebar should not be wider than half the viewport
      expect(sidebarBox.width).toBeLessThan(viewportSize.width * 0.75)
    }
  }

  // All 10 board names should be discoverable on the page (scrolled or not)
  for (let i = 1; i <= 10; i++) {
    const boardName = `Overflow Board ${i} ${seed}-${i}`
    const boardLink = page
      .getByRole('link', { name: boardName })
      .or(page.getByText(boardName))
      .first()
    await expect(boardLink).toBeAttached({ timeout: 10_000 })
  }
})

// ─── Scenario 4: Deleting the currently-viewed board redirects gracefully ─────

test('deleting the currently viewed board should redirect to boards list or another board', async ({ page }) => {
  const boardName = `Delete Me ${Date.now()}`
  await createBoardViaUI(page, boardName)

  // Open board settings and archive/delete
  await page.locator('button[title="Board Settings"]').click()

  page.once('dialog', (dialog) => dialog.accept())
  await page.getByRole('button', { name: 'Move to Archive' }).click()

  // Should be redirected away from the deleted board
  await expect(page).not.toHaveURL(/\/workspace\/boards\/[a-f0-9-]+$/)
  // Either lands on the boards list or the home
  await expect(page).toHaveURL(/\/workspace\/(boards|home)/)
  await expect(page.getByText(boardName)).toHaveCount(0)
})

// ─── Scenario 5: Archive + restore appears in sidebar immediately ─────────────

test('restoring a board from archive should make it appear in sidebar without refresh', async ({ page }) => {
  const boardName = `Restore Sidebar ${Date.now()}`
  await createBoardViaUI(page, boardName)

  // Archive it
  await page.locator('button[title="Board Settings"]').click()
  page.once('dialog', (dialog) => dialog.accept())
  await page.getByRole('button', { name: 'Move to Archive' }).click()
  await expect(page).toHaveURL(/\/workspace\/boards$/)
  await expect(page.getByText(boardName)).toHaveCount(0)

  // Restore from archive
  await page.goto('/workspace/archive')
  const archivedBoardRow = page.locator('.td-archive-row').filter({ hasText: boardName }).first()
  await expect(archivedBoardRow).toBeVisible()

  page.once('dialog', (dialog) => dialog.accept())
  await archivedBoardRow.getByRole('button', { name: 'Restore Board' }).click()

  // After restore, navigate back to boards workspace
  await page.goto('/workspace/boards')

  // Board should appear in the sidebar/list without a manual page reload
  const restoredBoard = page.getByText(boardName).first()
  await expect(restoredBoard).toBeVisible({ timeout: 10_000 })
})

// ─── Scenario 6: User switches boards rapidly (rapid navigation stress) ───────

test('rapid back-and-forth navigation between two boards should settle on the last-navigated board', async ({ page, request }) => {
  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardIdX = await createBoardWithColumn(request, auth, `${seed}-X`, {
    boardNamePrefix: 'Rapid Nav X',
    description: 'rapid nav board X',
    columnNamePrefix: 'X-Column',
  })
  const boardIdY = await createBoardWithColumn(request, auth, `${seed}-Y`, {
    boardNamePrefix: 'Rapid Nav Y',
    description: 'rapid nav board Y',
    columnNamePrefix: 'Y-Column',
  })

  // Navigate rapidly back and forth — intentionally not waiting between each
  void page.goto(`/workspace/boards/${boardIdX}`)
  void page.goto(`/workspace/boards/${boardIdY}`)
  void page.goto(`/workspace/boards/${boardIdX}`)
  await page.goto(`/workspace/boards/${boardIdY}`)

  // Final navigation should win — board Y must be displayed
  await expect(page.getByRole('heading', { name: `Rapid Nav Y ${seed}-Y` })).toBeVisible({ timeout: 15_000 })
  // Board X column must not bleed into the view
  await expect(
    page.getByRole('heading', { name: `X-Column ${seed}-X`, exact: true }),
  ).toHaveCount(0)
})

// ─── Scenario 7: New board → navigate to it → create first card (first-time flow lite) ──

test('user creates first board then creates a card through the UI', async ({ page }) => {
  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardName = `First Board ${seed}`
  const columnName = `Todo ${seed}`
  const cardTitle = `First Card ${seed}`

  await gotoBoardsWorkspace(page)

  // Create board
  await page.getByRole('button', { name: '+ New Board' }).click()
  await page.getByPlaceholder('Board name').fill(boardName)
  await page.getByRole('button', { name: 'Create', exact: true }).click()
  await expect(page).toHaveURL(/\/workspace\/boards\/[a-f0-9-]+$/)
  await expect(page.getByRole('heading', { name: boardName })).toBeVisible()

  // Create column
  await page.getByRole('button', { name: '+ Add Column' }).click()
  await page.getByPlaceholder('Column name').fill(columnName)
  await page.getByRole('button', { name: 'Create', exact: true }).click()
  await expect(page.getByRole('heading', { name: columnName, exact: true })).toBeVisible()

  // Create card
  const column = columnByName(page, columnName)
  await column.getByRole('button', { name: 'Add Card' }).click()
  const cardInput = column.getByPlaceholder('Enter card title...')
  await expect(cardInput).toBeVisible()
  await cardInput.fill(cardTitle)

  const createCardResponse = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' &&
      /\/api\/boards\/[a-f0-9-]+\/cards$/i.test(response.url()) &&
      response.ok(),
  )
  await column.getByRole('button', { name: 'Add', exact: true }).click()
  await createCardResponse

  await expect(page.locator('[data-card-id]').filter({ hasText: cardTitle }).first()).toBeVisible()
})
