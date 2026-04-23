/**
 * Visual regression tests for the Today view.
 *
 * Today is the novice-first daily agenda. Its layout stability matters
 * because it is the first workspace view many users see. We capture the
 * empty-state rendering (no onboarding progress, no pending proposals)
 * which remains constant across a freshly-registered account.
 */
import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from '../e2e/support/authSession'
import { prepareForScreenshot } from './visual-test-helpers'

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'visual-today')
})

test('today view default state', async ({ page }) => {
  await page.goto('/workspace/today')
  await expect(page.getByRole('heading', { name: 'Today', exact: true })).toBeVisible()

  await prepareForScreenshot(page)

  await expect(page).toHaveScreenshot('today-default')
})
