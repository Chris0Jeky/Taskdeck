/**
 * Visual regression test for the global Capture modal.
 *
 * The capture modal is reachable via the Ctrl+Shift+C shortcut from any
 * workspace view. It is the entry point for typed and transcript captures.
 * We open it on the Home view to keep the underlying page state
 * deterministic (same as home-view.visual.spec.ts baseline).
 */
import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from '../e2e/support/authSession'
import { prepareForScreenshot } from './visual-test-helpers'

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'visual-capture-modal')
})

test('capture modal typed mode default', async ({ page }) => {
  await page.goto('/workspace/home')
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()

  await page.keyboard.press('Control+Shift+C')
  await expect(page.getByRole('dialog', { name: 'Capture item' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Quick Capture', exact: true })).toBeVisible()

  await prepareForScreenshot(page)

  await expect(page).toHaveScreenshot('capture-modal-typed.png')
})
