/**
 * Visual regression tests for public authentication views.
 *
 * Login and Register are the first surfaces a new user encounters, so
 * their layout stability is important. Both views are public (no session
 * setup needed) and render synchronously from local state — the only
 * async work is an optional OIDC provider fetch, which we allow to settle
 * via network-idle before capturing.
 */
import { expect, test } from '@playwright/test'
import { prepareForScreenshot } from './visual-test-helpers'

test('login view default state', async ({ page }) => {
  await page.goto('/login')
  await expect(page.getByRole('heading', { name: 'Sign in to Taskdeck' })).toBeVisible()

  await prepareForScreenshot(page)

  await expect(page).toHaveScreenshot('login-default')
})

test('register view default state', async ({ page }) => {
  await page.goto('/register')
  await expect(page.getByRole('heading', { name: 'Create an account' })).toBeVisible()

  await prepareForScreenshot(page)

  await expect(page).toHaveScreenshot('register-default')
})
