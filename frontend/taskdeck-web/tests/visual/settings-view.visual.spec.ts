/**
 * Visual regression tests for the Settings (Profile) view.
 *
 * The profile view surfaces user-specific data (username, email, user id)
 * which varies per test run. We mask those values with the standard
 * Playwright `mask` option so the screenshot captures layout but not
 * identity — the baseline remains stable across freshly-registered users.
 */
import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from '../e2e/support/authSession'
import { prepareForScreenshot } from './visual-test-helpers'

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'visual-settings', { theme: 'legacy' })
})

test('profile settings default view', async ({ page }) => {
  await page.goto('/workspace/settings/profile')
  await expect(page.getByRole('heading', { name: 'Settings', exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Profile', exact: true })).toBeVisible()

  await prepareForScreenshot(page)

  // Mask the dynamic user-identity fields (username, email, user id, role)
  // so we screenshot layout only. These values are newly generated per run.
  await expect(page).toHaveScreenshot('settings-profile.png', {
    mask: [page.locator('.td-info-value')],
  })
})
