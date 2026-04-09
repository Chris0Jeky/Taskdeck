/**
 * Visual regression tests for the Inbox / Capture view.
 *
 * Captures the inbox in its empty state. This is a key entry point in
 * the capture-review-execute loop and its layout stability is important
 * for the novice-first experience.
 */
import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from '../e2e/support/authSession'
import { prepareForScreenshot } from './visual-test-helpers'

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'visual-inbox')
})

test('inbox view empty state', async ({ page }) => {
  await page.goto('/workspace/inbox')
  await page.waitForLoadState('networkidle')

  await prepareForScreenshot(page)

  await expect(page).toHaveScreenshot('inbox-empty.png')
})
