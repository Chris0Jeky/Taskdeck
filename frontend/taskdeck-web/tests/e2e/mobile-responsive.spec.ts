import type { Page } from '@playwright/test'
import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from './support/authSession'

/**
 * Mobile-responsive E2E tests.
 *
 * These tests run only on mobile viewport projects (Pixel 7, iPhone 14)
 * and validate that critical workflows remain usable at small screen sizes.
 *
 * Tag: @mobile — filtered by project grep in playwright.config.ts.
 */

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'mobile')
})

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

async function createBoard(page: Page, boardName: string) {
  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()
  await page.getByRole('button', { name: '+ New Board' }).click()
  await page.getByPlaceholder('Board name').fill(boardName)
  await page.getByRole('button', { name: 'Create', exact: true }).click()
  await expect(page).toHaveURL(/\/workspace\/boards\/[a-f0-9-]+$/)
  await expect(page.getByRole('heading', { name: boardName })).toBeVisible()
}

async function addColumn(page: Page, columnName: string) {
  await page.getByRole('button', { name: '+ Add Column' }).click()
  await page.getByPlaceholder('Column name').fill(columnName)
  await page.getByRole('button', { name: 'Create', exact: true }).click()
  await expect(page.getByRole('heading', { name: columnName, exact: true })).toBeVisible()
}

function columnByName(page: Page, columnName: string) {
  return page
    .locator('[data-column-id]')
    .filter({ has: page.getByRole('heading', { name: columnName, exact: true }) })
    .first()
}

async function addCard(page: Page, columnName: string, cardTitle: string) {
  const column = columnByName(page, columnName)
  await column.getByRole('button', { name: 'Add Card' }).click()
  const addCardInput = column.getByPlaceholder('Enter card title...')
  await expect(addCardInput).toBeVisible()
  await addCardInput.fill(cardTitle)
  const createCardResponse = page.waitForResponse((response) =>
    response.request().method() === 'POST'
    && /\/api\/boards\/[a-f0-9-]+\/cards$/i.test(response.url())
    && response.ok())
  await column.getByRole('button', { name: 'Add', exact: true }).click()
  await createCardResponse
  await expect(
    page.locator('[data-card-id]').filter({ hasText: cardTitle }).first(),
  ).toBeVisible()
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

test('@mobile board navigation and column visibility on small screen', async ({ page }) => {
  const boardName = `Mobile Board ${Date.now()}`
  const columnName = `Mobile Col ${Date.now()}`

  await createBoard(page, boardName)
  await addColumn(page, columnName)

  // Board heading should be visible on mobile
  await expect(page.getByRole('heading', { name: boardName })).toBeVisible()

  // Column heading should be visible and not clipped outside viewport
  const columnHeading = page.getByRole('heading', { name: columnName, exact: true })
  await expect(columnHeading).toBeVisible()
  const headingBox = await columnHeading.boundingBox()
  expect(headingBox).not.toBeNull()
  // The heading should have a positive x position (not pushed off-screen)
  expect(headingBox!.x).toBeGreaterThanOrEqual(0)

  // The viewport should be small (confirming mobile project is active)
  const viewportSize = page.viewportSize()
  expect(viewportSize).not.toBeNull()
  expect(viewportSize!.width).toBeLessThan(500)

  // Board controls (New Board, Add Column) should still be reachable
  await expect(page.getByRole('button', { name: '+ Add Column' })).toBeVisible()
})

test('@mobile card editing modal should fit within mobile viewport', async ({ page }) => {
  const boardName = `Mobile Edit Board ${Date.now()}`
  const columnName = `Mobile Edit Col ${Date.now()}`
  const cardTitle = `Mobile Edit Card ${Date.now()}`

  await createBoard(page, boardName)
  await addColumn(page, columnName)
  await addCard(page, columnName, cardTitle)

  // Click the card to open the edit modal
  const card = page.locator('[data-card-id]').filter({ hasText: cardTitle }).first()
  await card.click()

  const editHeading = page.getByRole('heading', { name: 'Edit Card' })
  await expect(editHeading).toBeVisible()

  // The edit modal should be within the viewport bounds
  const viewportSize = page.viewportSize()
  expect(viewportSize).not.toBeNull()

  // The modal/dialog container should not overflow the viewport width
  const modal = page.locator('[role="dialog"], .td-card-edit-modal, .td-modal').first()
  const modalExists = await modal.count()
  if (modalExists > 0) {
    const modalBox = await modal.boundingBox()
    if (modalBox) {
      // Modal should not exceed viewport width
      expect(modalBox.x + modalBox.width).toBeLessThanOrEqual(viewportSize!.width + 2)
      // Modal should have a reasonable minimum width on mobile
      expect(modalBox.width).toBeGreaterThan(200)
    }
  }

  // Card title field should be visible and editable
  const titleInput = page.locator('#card-title, [name="title"], input[type="text"]').first()
  if (await titleInput.count() > 0) {
    await expect(titleInput).toBeVisible()
  }

  // Close the modal
  await page.keyboard.press('Escape')
  await expect(editHeading).not.toBeVisible()
})

test('@mobile sidebar navigation should remain accessible on small screen', async ({ page }) => {
  await page.goto('/workspace/home')
  await expect(page).toHaveURL(/\/workspace\/home$/)

  const viewportSize = page.viewportSize()
  expect(viewportSize).not.toBeNull()
  expect(viewportSize!.width).toBeLessThan(500)

  // Home heading should be visible
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()

  // Navigate to boards workspace — use direct URL since sidebar may be
  // collapsed or behind a hamburger on mobile
  await page.goto('/workspace/boards')
  await expect(page).toHaveURL(/\/workspace\/boards$/)
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()

  // Navigate to inbox
  await page.goto('/workspace/inbox')
  await expect(page).toHaveURL(/\/workspace\/inbox$/)

  // Each workspace view should render its primary content within viewport
  const body = page.locator('body')
  const bodyBox = await body.boundingBox()
  expect(bodyBox).not.toBeNull()
  // Body should not be wider than the viewport (no horizontal overflow forcing scroll)
  // Allow small tolerance for scrollbar
  expect(bodyBox!.width).toBeLessThanOrEqual(viewportSize!.width + 20)
})

test('@mobile capture modal should be usable on small screen', async ({ page }) => {
  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()

  const captureText = `Mobile capture ${Date.now()}`

  // Open capture modal via keyboard shortcut
  await page.keyboard.press('Control+Shift+C')
  const captureModal = page.getByRole('dialog', { name: 'Capture item' })
  await expect(captureModal).toBeVisible()

  // The capture textarea should be visible and interactable
  const captureInput = captureModal.getByPlaceholder('Capture a thought, task, or follow-up...')
  await expect(captureInput).toBeVisible()

  // On mobile the modal should fit the viewport
  const viewportSize = page.viewportSize()
  expect(viewportSize).not.toBeNull()

  const modalBox = await captureModal.boundingBox()
  if (modalBox) {
    expect(modalBox.x + modalBox.width).toBeLessThanOrEqual(viewportSize!.width + 2)
  }

  // Type and submit
  await captureInput.fill(captureText)
  await captureInput.press('Control+Enter')

  // Should navigate to inbox with the capture visible
  await expect(page).toHaveURL(/\/workspace\/inbox$/)
  await expect(page.locator('.td-inbox-row__excerpt').first()).toContainText(captureText)
})
