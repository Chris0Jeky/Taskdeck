/**
 * Visual regression tests for the Archive view.
 *
 * Captures the archive screen in its empty state (no archived items).
 * Testing the populated state would require archiving a board first,
 * which is covered in separate E2E tests; the visual baseline here
 * ensures the empty-state layout remains stable.
 */
import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from '../e2e/support/authSession'
import { prepareForScreenshot } from './visual-test-helpers'

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'visual-archive')
})

test('archive view empty state', async ({ page }) => {
  await page.goto('/workspace/archive')
  await page.waitForLoadState('networkidle')

  await prepareForScreenshot(page)

  await expect(page).toHaveScreenshot('archive-empty.png')
})
