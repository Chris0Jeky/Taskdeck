/**
 * Visual regression tests for the Notification Inbox view.
 *
 * Captures the notifications screen in its default state for a
 * freshly-registered user (no notifications yet). The layout stability
 * here matters because this view is visible from the shell's notifications
 * bell entry point.
 */
import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from '../e2e/support/authSession'
import { prepareForScreenshot } from './visual-test-helpers'

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'visual-notifications')
})

test('notification inbox empty state', async ({ page }) => {
  await page.goto('/workspace/notifications')
  await expect(page.getByRole('heading', { name: 'Notifications', exact: true })).toBeVisible()
  await page.waitForLoadState('networkidle')

  await prepareForScreenshot(page)

  await expect(page).toHaveScreenshot('notifications-empty')
})
