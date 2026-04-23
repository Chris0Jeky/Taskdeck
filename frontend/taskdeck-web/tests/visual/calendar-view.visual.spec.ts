/**
 * Visual regression tests for the Calendar / planning view.
 *
 * Captures the default calendar grid in its empty state (no due cards).
 * The timeline toggle and grid layout are both load-bearing UI so regressions
 * in either should be caught by this baseline.
 */
import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from '../e2e/support/authSession'
import { prepareForScreenshot } from './visual-test-helpers'

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'visual-calendar')
})

test('calendar view default state', async ({ page }) => {
  await page.goto('/workspace/calendar')
  await expect(page.getByRole('heading', { name: 'Calendar', exact: true })).toBeVisible()

  await prepareForScreenshot(page)

  await expect(page).toHaveScreenshot('calendar-default')
})
