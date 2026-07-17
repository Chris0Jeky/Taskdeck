/**
 * Visual regression tests for the Review view.
 *
 * Review is the approval surface of the capture-review-apply loop. Its
 * empty state ("No proposals need review yet") is the default when a
 * freshly-registered user has not captured anything, making it a stable
 * baseline.
 */
import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from '../e2e/support/authSession'
import { prepareForScreenshot } from './visual-test-helpers'

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'visual-review', { theme: 'legacy' })
})

test('review view empty state', async ({ page }) => {
  await page.goto('/workspace/review')
  await expect(page.getByRole('heading', { name: 'Review', exact: true })).toBeVisible()
  // Wait for the empty state headline to settle — confirms proposal loading finished.
  await expect(page.getByRole('heading', { name: 'No proposals need review yet', exact: true })).toBeVisible()

  await prepareForScreenshot(page)

  await expect(page).toHaveScreenshot('review-empty.png')
})
