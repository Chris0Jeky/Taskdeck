/**
 * Visual regression test for the Shell Sidebar.
 *
 * The sidebar is present on every workspace view, so a dedicated baseline
 * protects against navigation regressions that a full-page screenshot
 * might absorb into noise. We land on the Home view (stable, empty
 * state) and screenshot only the sidebar locator.
 */
import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from '../e2e/support/authSession'
import { prepareForScreenshot } from './visual-test-helpers'

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'visual-sidebar')
})

test('shell sidebar default', async ({ page }) => {
  await page.goto('/workspace/home')
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()

  const sidebar = page.locator('.td-sidebar').first()
  await expect(sidebar).toBeVisible()

  await prepareForScreenshot(page)

  await expect(sidebar).toHaveScreenshot('shell-sidebar.png')
})
