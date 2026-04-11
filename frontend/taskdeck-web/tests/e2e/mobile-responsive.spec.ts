import { expect, test, type Page } from '@playwright/test'
import { registerAndAttachSession } from './support/authSession'
import { addCard, addColumn, createBoard } from './support/boardUiHelpers'

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

async function openMobileNavigation(page: Page) {
  const menuButton = page.getByRole('button', { name: 'Open navigation menu' })
  await expect(menuButton).toBeVisible()
  await menuButton.click()

  const navigation = page.getByRole('navigation', { name: 'Main navigation' })
  await expect(navigation).toBeVisible()
  return navigation
}

async function navigateWithMobileMenu(
  page: Page,
  destination: 'Boards' | 'Inbox',
  urlPattern: RegExp,
) {
  const navigation = await openMobileNavigation(page)
  const href =
    destination === 'Boards'
      ? '/workspace/boards'
      : '/workspace/inbox'
  await navigation.locator(`a[href="${href}"]`).click()
  await expect(page).toHaveURL(urlPattern)
}

function captureLauncher(page: Page) {
  return page
    .getByRole('button', { name: 'Open capture modal to add a new inbox item' })
    .first()
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

  // Click the card title area to avoid the drag-handle intercepting the tap.
  const card = page.locator('[data-card-id]').filter({ hasText: cardTitle }).first()
  await card.getByRole('heading', { name: cardTitle, exact: true }).click()

  const editHeading = page.getByRole('heading', { name: 'Edit Card', exact: true })
  await expect(editHeading).toBeVisible()

  // The edit modal should be within the viewport bounds
  const viewportSize = page.viewportSize()
  expect(viewportSize).not.toBeNull()

  const modal = page.getByRole('dialog', { name: 'Edit Card' })
  await expect(modal).toBeVisible()
  const modalBox = await modal.boundingBox()
  expect(modalBox).not.toBeNull()
  // Modal should not exceed viewport width
  expect(modalBox!.x + modalBox!.width).toBeLessThanOrEqual(viewportSize!.width + 2)
  // Modal should have a reasonable minimum width on mobile
  expect(modalBox!.width).toBeGreaterThan(200)

  // Close the modal
  await page.keyboard.press('Escape')
  await expect(editHeading).not.toBeVisible()
})

test('@mobile workspace views should render correctly on small screen', async ({ page }) => {
  await page.goto('/workspace/home')
  await expect(page).toHaveURL(/\/workspace\/home$/)

  const viewportSize = page.viewportSize()
  expect(viewportSize).not.toBeNull()
  expect(viewportSize!.width).toBeLessThan(500)

  // Home heading should be visible
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()

  // Navigate using the mobile hamburger menu rather than bypassing the UI.
  await navigateWithMobileMenu(page, 'Boards', /\/workspace\/boards$/)
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()

  await navigateWithMobileMenu(page, 'Inbox', /\/workspace\/inbox$/)
  await expect(captureLauncher(page)).toBeVisible()

  // Each workspace view should render its primary content within viewport
  const body = page.locator('body')
  const bodyBox = await body.boundingBox()
  expect(bodyBox).not.toBeNull()
  // Body should not be wider than the viewport (no horizontal overflow forcing scroll)
  // Allow small tolerance for scrollbar
  expect(bodyBox!.width).toBeLessThanOrEqual(viewportSize!.width + 20)
})

test('@mobile capture modal should be usable on small screen', async ({ page }) => {
  await page.goto('/workspace/home')
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()
  await navigateWithMobileMenu(page, 'Inbox', /\/workspace\/inbox$/)

  const captureText = `Mobile capture ${Date.now()}`

  await captureLauncher(page).click()
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

  // Type and submit through the actual mobile-visible action button.
  await captureInput.fill(captureText)
  await captureModal.getByRole('button', { name: 'Save Capture' }).click()

  // Inbox should stay visible and show the newly created capture.
  await expect(page).toHaveURL(/\/workspace\/inbox$/)
  await expect(page.locator('.td-inbox-row__excerpt').first()).toContainText(captureText)
})
