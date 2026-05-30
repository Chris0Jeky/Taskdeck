/**
 * Visual regression tests for the Metrics view.
 *
 * Captures the metrics shell when no board is selected (placeholder state).
 * This is deterministic — no charts render until a user picks a board — so
 * it is safe as a baseline even without seeded data. The board select shows
 * the "Select a board" disabled placeholder.
 */
import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from '../e2e/support/authSession'
import { prepareForScreenshot } from './visual-test-helpers'

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'visual-metrics')
})

test('metrics view empty placeholder', async ({ page }) => {
  await page.goto('/workspace/metrics')
  await expect(page.getByRole('heading', { name: 'Board Metrics', exact: true })).toBeVisible()

  await prepareForScreenshot(page)

  await expect(page).toHaveScreenshot('metrics-placeholder.png')
})
